const cp = require('child_process');
const fs = require('fs');
const path = require('path');
const vscode = require('vscode');

let activeClient = null;
let lintDiagnostics = null;

const selector = [{ language: 'cfgs', scheme: 'file' }, { language: 'cfgs', scheme: 'untitled' }];
const semanticTokenLegend = new vscode.SemanticTokensLegend(
  ['namespace', 'class', 'interface', 'enum', 'enumMember', 'function', 'method', 'parameter', 'variable'],
  ['declaration', 'readonly', 'static', 'async', 'defaultLibrary']
);

function activate(context) {
  const output = vscode.window.createOutputChannel('CFGS');
  const lspDiagnostics = vscode.languages.createDiagnosticCollection('cfgs');
  lintDiagnostics = vscode.languages.createDiagnosticCollection('cfgs-lint');
  const client = new CfgsLanguageClient(context, output, lspDiagnostics);
  activeClient = client;

  context.subscriptions.push(output, lspDiagnostics, lintDiagnostics, client);
  context.subscriptions.push(
    vscode.commands.registerCommand('cfgs.restartServer', async () => {
      await client.restart();
    }),
    vscode.commands.registerCommand('cfgs.lintFile', async () => {
      await lintCurrentFile(output);
    }),
    vscode.commands.registerCommand('cfgs.lintWorkspace', async () => {
      await lintWorkspace(output);
    }),
    vscode.commands.registerCommand('cfgs.formatFile', async () => {
      const editor = vscode.window.activeTextEditor;
      if (editor && isCfgsDocument(editor.document)) {
        await vscode.commands.executeCommand('editor.action.formatDocument');
      }
    }),
    vscode.workspace.onDidSaveTextDocument(async (document) => {
      if (!isCfgsDocument(document)) {
        return;
      }

      const config = vscode.workspace.getConfiguration('cfgs');
      if (config.get('lint.fixOnSave', false)) {
        await runCfgsCli(['-lint', document.uri.fsPath, '--fix', '--json'], output);
      }
      if (config.get('lint.onSave', false) || config.get('lint.fixOnSave', false)) {
        await lintFile(document.uri, output);
      }
    })
  );

  client.start().catch((error) => {
    output.appendLine(`Failed to start CFGS language server: ${error.message}`);
    vscode.window.showErrorMessage(`Failed to start CFGS language server: ${error.message}`);
  });
}

function deactivate() {
  const client = activeClient;
  activeClient = null;
  return client ? client.dispose() : undefined;
}

class CfgsLanguageClient {
  constructor(context, output, diagnostics) {
    this.context = context;
    this.output = output;
    this.diagnostics = diagnostics;
    this.process = null;
    this.buffer = Buffer.alloc(0);
    this.requestId = 0;
    this.pending = new Map();
    this.subscriptions = [];
  }

  async start() {
    const server = this.resolveServerLaunch();
    this.output.appendLine(`Starting CFGS language server: ${server.command} ${server.args.join(' ')}`);

    this.process = cp.spawn(server.command, server.args, {
      cwd: server.cwd,
      stdio: ['pipe', 'pipe', 'pipe']
    });

    this.process.stdout.on('data', (chunk) => this.handleStdout(chunk));
    this.process.stderr.on('data', (chunk) => this.output.append(chunk.toString()));
    this.process.on('exit', (code, signal) => {
      this.output.appendLine(`CFGS language server exited code=${code} signal=${signal}`);
      this.failPendingRequests(new Error('CFGS language server stopped.'));
    });
    this.process.on('error', (error) => {
      this.output.appendLine(`CFGS language server failed: ${error.message}`);
      this.failPendingRequests(error);
    });

    await this.sendRequest('initialize', {
      processId: process.pid,
      rootUri: getRootUri(),
      capabilities: {}
    });
    this.sendNotification('initialized', {});

    this.registerDocumentSync();
    this.registerProviders();

    for (const document of vscode.workspace.textDocuments) {
      if (isCfgsDocument(document)) {
        this.didOpen(document);
      }
    }
  }

  async restart() {
    await this.dispose();
    this.buffer = Buffer.alloc(0);
    this.requestId = 0;
    this.pending = new Map();
    this.subscriptions = [];
    await this.start();
  }

  async dispose() {
    for (const subscription of this.subscriptions) {
      subscription.dispose();
    }
    this.subscriptions = [];
    this.failPendingRequests(new Error('CFGS language client disposed.'));
    if (this.process) {
      try {
        this.sendNotification('shutdown', null);
      } catch (_) {
      }
      this.process.kill();
      this.process = null;
    }
  }

  registerDocumentSync() {
    this.subscriptions.push(
      vscode.workspace.onDidOpenTextDocument((document) => {
        if (isCfgsDocument(document)) {
          this.didOpen(document);
        }
      }),
      vscode.workspace.onDidChangeTextDocument((event) => {
        if (isCfgsDocument(event.document)) {
          this.didChange(event.document);
        }
      }),
      vscode.workspace.onDidCloseTextDocument((document) => {
        if (isCfgsDocument(document)) {
          this.diagnostics.delete(document.uri);
          if (lintDiagnostics) {
            lintDiagnostics.delete(document.uri);
          }
          this.didClose(document);
        }
      })
    );
  }

  registerProviders() {
    this.subscriptions.push(
      vscode.languages.registerDocumentFormattingEditProvider(selector, {
        provideDocumentFormattingEdits: async (document, options) => {
          const result = await this.sendRequest('textDocument/formatting', {
            textDocument: { uri: document.uri.toString() },
            options: { tabSize: options.tabSize, insertSpaces: options.insertSpaces }
          });
          return Array.isArray(result) ? result.map(toTextEdit) : [];
        }
      }),
      vscode.languages.registerCompletionItemProvider(selector, {
        provideCompletionItems: async (document, position) => {
          const result = await this.sendRequest('textDocument/completion', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });
          return Array.isArray(result) ? result.map(toCompletionItem) : [];
        }
      }, '.', '"', "'", '/', '\\', '{', ','),
      vscode.languages.registerHoverProvider(selector, {
        provideHover: async (document, position) => {
          const result = await this.sendRequest('textDocument/hover', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });
          if (!result || !result.contents || typeof result.contents.value !== 'string') {
            return undefined;
          }
          return new vscode.Hover(new vscode.MarkdownString(result.contents.value), result.range ? toRange(result.range) : undefined);
        }
      }),
      vscode.languages.registerDefinitionProvider(selector, {
        provideDefinition: async (document, position) => {
          const result = await this.sendRequest('textDocument/definition', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });
          return Array.isArray(result) ? result.map(toLocation) : undefined;
        }
      }),
      vscode.languages.registerTypeDefinitionProvider(selector, {
        provideTypeDefinition: async (document, position) => {
          const result = await this.sendRequest('textDocument/typeDefinition', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });
          return Array.isArray(result) ? result.map(toLocation) : undefined;
        }
      }),
      vscode.languages.registerImplementationProvider(selector, {
        provideImplementation: async (document, position) => {
          const result = await this.sendRequest('textDocument/implementation', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });
          return Array.isArray(result) ? result.map(toLocation) : undefined;
        }
      }),
      vscode.languages.registerReferenceProvider(selector, {
        provideReferences: async (document, position, context) => {
          const result = await this.sendRequest('textDocument/references', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position),
            context: { includeDeclaration: Boolean(context.includeDeclaration) }
          });
          return Array.isArray(result) ? result.map(toLocation) : [];
        }
      }),
      vscode.languages.registerDocumentHighlightProvider(selector, {
        provideDocumentHighlights: async (document, position) => {
          const result = await this.sendRequest('textDocument/documentHighlight', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });
          return Array.isArray(result) ? result.map(toDocumentHighlight) : [];
        }
      }),
      vscode.languages.registerRenameProvider(selector, {
        provideRenameEdits: async (document, position, newName) => {
          const result = await this.sendRequest('textDocument/rename', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position),
            newName
          });
          return result ? toWorkspaceEdit(result) : undefined;
        },
        prepareRename: async (document, position) => {
          const result = await this.sendRequest('textDocument/prepareRename', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });
          return result && result.range ? toRange(result.range) : undefined;
        }
      }),
      vscode.languages.registerDocumentSymbolProvider(selector, {
        provideDocumentSymbols: async (document) => {
          const result = await this.sendRequest('textDocument/documentSymbol', {
            textDocument: { uri: document.uri.toString() }
          });
          return Array.isArray(result) ? result.map(toDocumentSymbol) : [];
        }
      }),
      vscode.languages.registerWorkspaceSymbolProvider({
        provideWorkspaceSymbols: async (query) => {
          const result = await this.sendRequest('workspace/symbol', { query: query || '' });
          return Array.isArray(result) ? result.map(toSymbolInformation) : [];
        }
      }),
      vscode.languages.registerCodeActionsProvider(selector, {
        provideCodeActions: async (document, range, context) => {
          const result = await this.sendRequest('textDocument/codeAction', {
            textDocument: { uri: document.uri.toString() },
            range: toLspRange(range),
            context: { diagnostics: Array.isArray(context.diagnostics) ? context.diagnostics.map(toLspDiagnostic) : [] }
          });
          return Array.isArray(result) ? result.map(toCodeAction) : [];
        }
      }, { providedCodeActionKinds: [vscode.CodeActionKind.QuickFix, vscode.CodeActionKind.Refactor] }),
      vscode.languages.registerDocumentLinkProvider(selector, {
        provideDocumentLinks: async (document) => {
          const result = await this.sendRequest('textDocument/documentLink', {
            textDocument: { uri: document.uri.toString() }
          });
          return Array.isArray(result) ? result.map(toDocumentLink) : [];
        }
      }),
      vscode.languages.registerSelectionRangeProvider(selector, {
        provideSelectionRanges: async (document, positions) => {
          const result = await this.sendRequest('textDocument/selectionRange', {
            textDocument: { uri: document.uri.toString() },
            positions: positions.map(toLspPosition)
          });
          return Array.isArray(result) ? result.map(toSelectionRange) : [];
        }
      }),
      vscode.languages.registerCodeLensProvider(selector, {
        provideCodeLenses: async (document) => {
          const result = await this.sendRequest('textDocument/codeLens', {
            textDocument: { uri: document.uri.toString() }
          });
          return Array.isArray(result) ? result.map(toCodeLens) : [];
        }
      }),
      vscode.languages.registerInlayHintsProvider(selector, {
        provideInlayHints: async (document, range) => {
          const result = await this.sendRequest('textDocument/inlayHint', {
            textDocument: { uri: document.uri.toString() },
            range: toLspRange(range)
          });
          return Array.isArray(result) ? result.map(toInlayHint) : [];
        }
      }),
      vscode.languages.registerCallHierarchyProvider(selector, {
        prepareCallHierarchy: async (document, position) => {
          const result = await this.sendRequest('textDocument/prepareCallHierarchy', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });
          return Array.isArray(result) && result.length > 0 ? result.map(toCallHierarchyItem) : undefined;
        },
        provideCallHierarchyIncomingCalls: async (item) => {
          const result = await this.sendRequest('callHierarchy/incomingCalls', {
            item: fromCallHierarchyItem(item)
          });
          return Array.isArray(result) ? result.map(toIncomingCall) : [];
        },
        provideCallHierarchyOutgoingCalls: async (item) => {
          const result = await this.sendRequest('callHierarchy/outgoingCalls', {
            item: fromCallHierarchyItem(item)
          });
          return Array.isArray(result) ? result.map(toOutgoingCall) : [];
        }
      }),
      vscode.languages.registerDocumentSemanticTokensProvider(selector, {
        provideDocumentSemanticTokens: async (document) => {
          const result = await this.sendRequest('textDocument/semanticTokens/full', {
            textDocument: { uri: document.uri.toString() }
          });
          return new vscode.SemanticTokens(new Uint32Array(result && Array.isArray(result.data) ? result.data : []));
        }
      }, semanticTokenLegend),
      vscode.languages.registerSignatureHelpProvider(selector, {
        provideSignatureHelp: async (document, position) => {
          const result = await this.sendRequest('textDocument/signatureHelp', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });
          return result && Array.isArray(result.signatures) ? toSignatureHelp(result) : undefined;
        }
      }, '(', ',')
    );
  }

  didOpen(document) {
    this.sendNotification('textDocument/didOpen', {
      textDocument: {
        uri: document.uri.toString(),
        languageId: 'cfgs',
        version: document.version,
        text: document.getText()
      }
    });
  }

  didChange(document) {
    this.sendNotification('textDocument/didChange', {
      textDocument: { uri: document.uri.toString(), version: document.version },
      contentChanges: [{ text: document.getText() }]
    });
  }

  didClose(document) {
    this.sendNotification('textDocument/didClose', {
      textDocument: { uri: document.uri.toString() }
    });
  }

  handleStdout(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    while (true) {
      const headerEnd = this.buffer.indexOf('\r\n\r\n');
      if (headerEnd < 0) {
        return;
      }
      const header = this.buffer.slice(0, headerEnd).toString('ascii');
      const match = /Content-Length:\s*(\d+)/i.exec(header);
      if (!match) {
        this.buffer = Buffer.alloc(0);
        return;
      }
      const bodyLength = Number(match[1]);
      const frameLength = headerEnd + 4 + bodyLength;
      if (this.buffer.length < frameLength) {
        return;
      }
      const payload = this.buffer.slice(headerEnd + 4, frameLength).toString('utf8');
      this.buffer = this.buffer.slice(frameLength);
      try {
        this.handleMessage(JSON.parse(payload));
      } catch (error) {
        this.output.appendLine(`Failed to parse CFGS server message: ${error.message}`);
      }
    }
  }

  handleMessage(message) {
    if (Object.prototype.hasOwnProperty.call(message, 'id')) {
      const pending = this.pending.get(message.id);
      if (!pending) {
        return;
      }
      this.pending.delete(message.id);
      if (message.error) {
        pending.reject(new Error(message.error.message || 'Unknown CFGS server error.'));
      } else {
        pending.resolve(message.result);
      }
      return;
    }

    if (message.method === 'textDocument/publishDiagnostics' && message.params) {
      const uri = vscode.Uri.parse(message.params.uri);
      const diagnostics = Array.isArray(message.params.diagnostics)
        ? message.params.diagnostics.map(toDiagnostic)
        : [];
      this.diagnostics.set(uri, diagnostics);
    }
  }

  sendRequest(method, params) {
    const id = ++this.requestId;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.writeMessage({ jsonrpc: '2.0', id, method, params });
    });
  }

  sendNotification(method, params) {
    this.writeMessage({ jsonrpc: '2.0', method, params });
  }

  writeMessage(message) {
    if (!this.process || !this.process.stdin.writable) {
      return;
    }
    const json = JSON.stringify(message);
    const body = Buffer.from(json, 'utf8');
    const header = Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, 'ascii');
    this.process.stdin.write(Buffer.concat([header, body]));
  }

  failPendingRequests(error) {
    for (const pending of this.pending.values()) {
      pending.reject(error);
    }
    this.pending.clear();
  }

  resolveServerLaunch() {
    const config = vscode.workspace.getConfiguration('cfgs');
    const command = config.get('server.command', '').trim();
    const args = config.get('server.args', []);
    const cwdOverride = config.get('server.cwd', '').trim();
    const workspaceFolder = getWorkspaceFolder();

    if (command) {
      return { command, args: Array.isArray(args) ? args : [], cwd: cwdOverride || workspaceFolder || this.context.extensionPath };
    }

    const bundledServerDll = path.join(this.context.extensionPath, 'server', 'CFGS.Lsp.dll');
    if (fs.existsSync(bundledServerDll)) {
      return { command: 'dotnet', args: [bundledServerDll], cwd: cwdOverride || workspaceFolder || this.context.extensionPath };
    }

    if (workspaceFolder) {
      const serverDll = path.join(workspaceFolder, 'dist', 'Debug', 'net10.0', 'CFGS.Lsp.dll');
      if (fs.existsSync(serverDll)) {
        return { command: 'dotnet', args: [serverDll], cwd: cwdOverride || workspaceFolder };
      }

      const serverProject = path.join(workspaceFolder, 'CFGS.Lsp', 'CFGS.Lsp.csproj');
      if (fs.existsSync(serverProject)) {
        return { command: 'dotnet', args: ['run', '--project', serverProject], cwd: cwdOverride || workspaceFolder };
      }
    }

    throw new Error('Could not locate CFGS.Lsp.dll. Configure cfgs.server.command.');
  }
}

async function lintCurrentFile(output) {
  const editor = vscode.window.activeTextEditor;
  if (!editor || !isCfgsDocument(editor.document) || editor.document.uri.scheme !== 'file') {
    vscode.window.showWarningMessage('Open a CFGS file first.');
    return;
  }
  await lintFile(editor.document.uri, output);
}

async function lintWorkspace(output) {
  const workspaceFolder = getWorkspaceFolder();
  if (!workspaceFolder) {
    vscode.window.showWarningMessage('Open a workspace first.');
    return;
  }
  const result = await runCfgsCli(['-lint', workspaceFolder, '--json'], output);
  if (lintDiagnostics) {
    lintDiagnostics.clear();
  }
  applyLintDiagnostics(result.stdout, output);
}

async function lintFile(uri, output) {
  const result = await runCfgsCli(['-lint', uri.fsPath, '--json'], output);
  applyLintDiagnostics(result.stdout, output, uri);
}

function applyLintDiagnostics(stdout, output, fallbackUri) {
  if (!lintDiagnostics) {
    return;
  }

  let items = [];
  try {
    items = JSON.parse(stdout || '[]');
  } catch (error) {
    output.appendLine(`Could not parse CFGS lint JSON: ${error.message}`);
    return;
  }

  const grouped = new Map();
  for (const item of Array.isArray(items) ? items : []) {
    if (!item || !item.source) {
      continue;
    }
    const uri = vscode.Uri.file(item.source);
    const key = uri.toString();
    if (!grouped.has(key)) {
      grouped.set(key, { uri, diagnostics: [] });
    }
    grouped.get(key).diagnostics.push(toToolDiagnostic(item));
  }

  if (fallbackUri && grouped.size === 0) {
    lintDiagnostics.set(fallbackUri, []);
    return;
  }

  for (const group of grouped.values()) {
    lintDiagnostics.set(group.uri, group.diagnostics);
  }
}

function runCfgsCli(args, output) {
  const launch = resolveCliLaunch(args);
  output.appendLine(`Running CFGS CLI: ${launch.command} ${launch.args.join(' ')}`);
  return new Promise((resolve) => {
    cp.execFile(launch.command, launch.args, { cwd: launch.cwd, windowsHide: true }, (error, stdout, stderr) => {
      if (stderr) {
        output.append(stderr);
      }
      if (error && stdout.trim().length === 0) {
        output.appendLine(error.message);
      }
      resolve({ code: error && typeof error.code === 'number' ? error.code : 0, stdout, stderr });
    });
  });
}

function resolveCliLaunch(toolArgs) {
  const config = vscode.workspace.getConfiguration('cfgs');
  const command = config.get('cli.command', '').trim();
  const baseArgs = config.get('cli.args', []);
  const workspaceFolder = getWorkspaceFolder();

  if (command) {
    return { command, args: [...(Array.isArray(baseArgs) ? baseArgs : []), ...toolArgs], cwd: workspaceFolder || undefined };
  }

  if (workspaceFolder) {
    const vmDll = path.join(workspaceFolder, 'dist', 'Debug', 'net10.0', 'CFGS_VM.dll');
    if (fs.existsSync(vmDll)) {
      return { command: 'dotnet', args: [vmDll, ...toolArgs], cwd: workspaceFolder };
    }

    const vmProject = path.join(workspaceFolder, 'CFGS_NE', 'CFGS_VM.csproj');
    if (fs.existsSync(vmProject)) {
      return { command: 'dotnet', args: ['run', '--project', vmProject, '--', ...toolArgs], cwd: workspaceFolder };
    }
  }

  return { command: 'dotnet', args: ['CFGS_VM.dll', ...toolArgs], cwd: workspaceFolder || undefined };
}

function getWorkspaceFolder() {
  return vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0
    ? vscode.workspace.workspaceFolders[0].uri.fsPath
    : '';
}

function getRootUri() {
  const folder = vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0
    ? vscode.workspace.workspaceFolders[0]
    : null;
  return folder ? folder.uri.toString() : null;
}

function isCfgsDocument(document) {
  return document.languageId === 'cfgs' || document.fileName.endsWith('.cfs');
}

function toLspPosition(position) {
  return { line: position.line, character: position.character };
}

function toLspRange(range) {
  return { start: toLspPosition(range.start), end: toLspPosition(range.end) };
}

function toRange(range) {
  return new vscode.Range(
    new vscode.Position(range.start.line, range.start.character),
    new vscode.Position(range.end.line, range.end.character)
  );
}

function toLocation(location) {
  return new vscode.Location(vscode.Uri.parse(location.uri), toRange(location.range));
}

function toDocumentHighlight(item) {
  return new vscode.DocumentHighlight(toRange(item.range), item.kind || vscode.DocumentHighlightKind.Text);
}

function toTextEdit(edit) {
  return new vscode.TextEdit(toRange(edit.range), edit.newText || '');
}

function toDiagnostic(item) {
  const diagnostic = new vscode.Diagnostic(
    toRange(item.range),
    item.message || 'Unknown CFGS diagnostic.',
    toDiagnosticSeverity(item.severity)
  );
  diagnostic.source = item.source || 'cfgs';
  diagnostic.code = item.code;
  return diagnostic;
}

function toWorkspaceEdit(item) {
  const edit = new vscode.WorkspaceEdit();
  for (const [uriString, edits] of Object.entries(item.changes || {})) {
    const uri = vscode.Uri.parse(uriString);
    for (const textEdit of edits) {
      edit.replace(uri, toRange(textEdit.range), textEdit.newText || '');
    }
  }
  return edit;
}

function toToolDiagnostic(item) {
  const line = Math.max(0, (item.line || 1) - 1);
  const column = Math.max(0, (item.column || 1) - 1);
  const span = Math.max(1, item.spanLength || 1);
  const diagnostic = new vscode.Diagnostic(
    new vscode.Range(line, column, line, column + span),
    item.message || 'Unknown CFGS lint diagnostic.',
    toToolSeverity(item.severity)
  );
  diagnostic.source = 'cfgs-lint';
  diagnostic.code = item.code;
  return diagnostic;
}

function toDiagnosticSeverity(severity) {
  switch (severity) {
    case 1: return vscode.DiagnosticSeverity.Error;
    case 2: return vscode.DiagnosticSeverity.Warning;
    case 3: return vscode.DiagnosticSeverity.Information;
    case 4: return vscode.DiagnosticSeverity.Hint;
    default: return vscode.DiagnosticSeverity.Error;
  }
}

function toToolSeverity(severity) {
  switch (String(severity || '').toLowerCase()) {
    case 'error': return vscode.DiagnosticSeverity.Error;
    case 'warning': return vscode.DiagnosticSeverity.Warning;
    case 'info': return vscode.DiagnosticSeverity.Information;
    default: return vscode.DiagnosticSeverity.Information;
  }
}

function toLspDiagnostic(diagnostic) {
  return {
    range: toLspRange(diagnostic.range),
    severity: diagnostic.severity + 1,
    source: diagnostic.source,
    code: diagnostic.code,
    message: diagnostic.message
  };
}

function toCompletionItem(item) {
  const completion = new vscode.CompletionItem(item.label, toCompletionItemKind(item.kind));
  completion.detail = item.detail || '';
  if (item.insertTextFormat === 2 && item.insertText) {
    completion.insertText = new vscode.SnippetString(item.insertText);
  } else if (item.insertText) {
    completion.insertText = item.insertText;
  }
  return completion;
}

function toDocumentSymbol(item) {
  const symbol = new vscode.DocumentSymbol(
    item.name,
    item.detail || '',
    toSymbolKind(item.kind),
    toRange(item.range),
    toRange(item.selectionRange)
  );
  if (Array.isArray(item.children)) {
    symbol.children = item.children.map(toDocumentSymbol);
  }
  return symbol;
}

function toSymbolInformation(item) {
  const location = item.location ? toLocation(item.location) : new vscode.Location(vscode.Uri.file(''), new vscode.Position(0, 0));
  return new vscode.SymbolInformation(item.name || '', toSymbolKind(item.kind), '', location);
}

function toCodeAction(item) {
  const action = new vscode.CodeAction(item.title || 'CFGS quick fix', item.kind === 'quickfix' ? vscode.CodeActionKind.QuickFix : vscode.CodeActionKind.Refactor);
  if (item.edit) {
    const edit = new vscode.WorkspaceEdit();
    for (const [uriString, edits] of Object.entries(item.edit.changes || {})) {
      const uri = vscode.Uri.parse(uriString);
      for (const textEdit of edits) {
        edit.replace(uri, toRange(textEdit.range), textEdit.newText || '');
      }
    }
    action.edit = edit;
  }
  if (Array.isArray(item.diagnostics)) {
    action.diagnostics = item.diagnostics.map(toDiagnostic);
  }
  return action;
}

function toDocumentLink(item) {
  return new vscode.DocumentLink(toRange(item.range), item.target ? vscode.Uri.parse(item.target) : undefined);
}

function toSelectionRange(item) {
  return new vscode.SelectionRange(toRange(item.range), item.parent ? toSelectionRange(item.parent) : undefined);
}

function toCodeLens(item) {
  const lens = new vscode.CodeLens(toRange(item.range));
  if (item.command && item.command.title) {
    lens.command = {
      title: item.command.title,
      command: item.command.command || ''
    };
  }
  return lens;
}

function toInlayHint(item) {
  const hint = new vscode.InlayHint(
    new vscode.Position(item.position.line, item.position.character),
    item.label || '',
    item.kind === 2 ? vscode.InlayHintKind.Type : vscode.InlayHintKind.Parameter
  );
  hint.paddingRight = Boolean(item.paddingRight);
  hint.paddingLeft = Boolean(item.paddingLeft);
  return hint;
}

function toCallHierarchyItem(item) {
  const callItem = new vscode.CallHierarchyItem(
    toSymbolKind(item.kind),
    item.name || '',
    item.detail || '',
    vscode.Uri.parse(item.uri),
    toRange(item.range),
    toRange(item.selectionRange)
  );
  callItem._cfgsLspItem = item;
  return callItem;
}

function fromCallHierarchyItem(item) {
  return item._cfgsLspItem || {
    name: item.name,
    kind: fromSymbolKind(item.kind),
    uri: item.uri.toString(),
    range: toLspRange(item.range),
    selectionRange: toLspRange(item.selectionRange),
    detail: item.detail || ''
  };
}

function toIncomingCall(item) {
  return new vscode.CallHierarchyIncomingCall(
    toCallHierarchyItem(item.from),
    Array.isArray(item.fromRanges) ? item.fromRanges.map(toRange) : []
  );
}

function toOutgoingCall(item) {
  return new vscode.CallHierarchyOutgoingCall(
    toCallHierarchyItem(item.to),
    Array.isArray(item.fromRanges) ? item.fromRanges.map(toRange) : []
  );
}

function toSignatureHelp(item) {
  const help = new vscode.SignatureHelp();
  help.activeSignature = typeof item.activeSignature === 'number' ? item.activeSignature : 0;
  help.activeParameter = typeof item.activeParameter === 'number' ? item.activeParameter : 0;
  help.signatures = item.signatures.map((signature) => {
    const info = new vscode.SignatureInformation(signature.label || '');
    info.parameters = Array.isArray(signature.parameters)
      ? signature.parameters.map((parameter) => new vscode.ParameterInformation(parameter.label || ''))
      : [];
    return info;
  });
  return help;
}

function toSymbolKind(kind) {
  switch (kind) {
    case 3: return vscode.SymbolKind.Namespace;
    case 5: return vscode.SymbolKind.Class;
    case 6: return vscode.SymbolKind.Method;
    case 10: return vscode.SymbolKind.Enum;
    case 12: return vscode.SymbolKind.Function;
    case 13: return vscode.SymbolKind.Variable;
    case 14: return vscode.SymbolKind.Constant;
    case 22: return vscode.SymbolKind.EnumMember;
    default: return vscode.SymbolKind.Object;
  }
}

function fromSymbolKind(kind) {
  switch (kind) {
    case vscode.SymbolKind.Namespace: return 3;
    case vscode.SymbolKind.Class: return 5;
    case vscode.SymbolKind.Method: return 6;
    case vscode.SymbolKind.Enum: return 10;
    case vscode.SymbolKind.Function: return 12;
    case vscode.SymbolKind.Variable: return 13;
    case vscode.SymbolKind.Constant: return 14;
    case vscode.SymbolKind.EnumMember: return 22;
    default: return 19;
  }
}

function toCompletionItemKind(kind) {
  switch (kind) {
    case 2: return vscode.CompletionItemKind.Method;
    case 3: return vscode.CompletionItemKind.Function;
    case 5: return vscode.CompletionItemKind.Field;
    case 6: return vscode.CompletionItemKind.Variable;
    case 7: return vscode.CompletionItemKind.Class;
    case 8: return vscode.CompletionItemKind.Interface;
    case 9: return vscode.CompletionItemKind.Module;
    case 10: return vscode.CompletionItemKind.Property;
    case 13: return vscode.CompletionItemKind.Enum;
    case 14: return vscode.CompletionItemKind.Keyword;
    case 15: return vscode.CompletionItemKind.Snippet;
    case 17: return vscode.CompletionItemKind.File;
    case 20: return vscode.CompletionItemKind.EnumMember;
    case 21: return vscode.CompletionItemKind.Constant;
    case 23: return vscode.CompletionItemKind.Event;
    default: return vscode.CompletionItemKind.Text;
  }
}

module.exports = { activate, deactivate };
