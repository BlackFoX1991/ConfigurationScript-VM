const cp = require('child_process');
const fs = require('fs');
const path = require('path');
const vscode = require('vscode');

let activeClient = null;
const semanticTokenLegend = new vscode.SemanticTokensLegend(
  ['namespace', 'class', 'enum', 'enumMember', 'function', 'method', 'parameter', 'variable'],
  ['declaration', 'readonly']
);

function activate(context) {
  const output = vscode.window.createOutputChannel('CFGS');
  const diagnostics = vscode.languages.createDiagnosticCollection('cfgs');
  const client = new CfgsLanguageClient(context, output, diagnostics);
  activeClient = client;

  context.subscriptions.push(output, diagnostics, client);

  return client.start();
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
    this.started = false;
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
      this.output.appendLine(`CFGS language server exited with code=${code} signal=${signal}`);
      this.failPendingRequests(new Error('CFGS language server stopped.'));
    });
    this.process.on('error', (error) => {
      this.output.appendLine(`CFGS language server failed: ${error.message}`);
      this.failPendingRequests(error);
      vscode.window.showErrorMessage(`CFGS language server failed: ${error.message}`);
    });

    const rootUri = this.getRootUri();
    await this.sendRequest('initialize', {
      processId: process.pid,
      rootUri,
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

    this.started = true;
  }

  dispose() {
    for (const subscription of this.subscriptions) {
      subscription.dispose();
    }

    this.failPendingRequests(new Error('CFGS language client disposed.'));

    if (this.process) {
      try {
        this.sendNotification('shutdown', null);
      } catch (_) {
      }

      this.process.kill();
      this.process = null;
    }

    this.started = false;
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
          this.didClose(document);
        }
      })
    );
  }

  registerProviders() {
    const selector = [{ language: 'cfgs', scheme: 'file' }, { language: 'cfgs', scheme: 'untitled' }];

    this.subscriptions.push(
      vscode.languages.registerDocumentSymbolProvider(selector, {
        provideDocumentSymbols: async (document) => {
          const result = await this.sendRequest('textDocument/documentSymbol', {
            textDocument: { uri: document.uri.toString() }
          });
          return Array.isArray(result) ? result.map(toDocumentSymbol) : [];
        }
      }),
      vscode.languages.registerDefinitionProvider(selector, {
        provideDefinition: async (document, position) => {
          const result = await this.sendRequest('textDocument/definition', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });

          if (!Array.isArray(result)) {
            return undefined;
          }

          return result.map(toLocation);
        }
      }),
      vscode.languages.registerHoverProvider(selector, {
        provideHover: async (document, position) => {
          const result = await this.sendRequest('textDocument/hover', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });

          if (!result || !result.contents || typeof result.contents.value !== 'string') {
            return undefined;
          }

          const markdown = new vscode.MarkdownString(result.contents.value);
          markdown.isTrusted = false;
          return new vscode.Hover(markdown, result.range ? toRange(result.range) : undefined);
        }
      }),
      vscode.languages.registerDocumentHighlightProvider(selector, {
        provideDocumentHighlights: async (document, position) => {
          const result = await this.sendRequest('textDocument/documentHighlight', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });

          if (!Array.isArray(result)) {
            return [];
          }

          return result.map((item) => new vscode.DocumentHighlight(
            toRange(item.range),
            toDocumentHighlightKind(item.kind)
          ));
        }
      }),
      vscode.languages.registerReferenceProvider(selector, {
        provideReferences: async (document, position, context) => {
          const result = await this.sendRequest('textDocument/references', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position),
            context: {
              includeDeclaration: !!context.includeDeclaration
            }
          });

          if (!Array.isArray(result)) {
            return [];
          }

          return result.map(toLocation);
        }
      }),
      vscode.languages.registerCodeActionsProvider(selector, {
        provideCodeActions: async (document, range, context) => {
          const result = await this.sendRequest('textDocument/codeAction', {
            textDocument: { uri: document.uri.toString() },
            range: toLspRange(range),
            context: {
              diagnostics: Array.isArray(context.diagnostics)
                ? context.diagnostics.map(toLspDiagnostic)
                : []
            }
          });

          if (!Array.isArray(result)) {
            return [];
          }

          return result.map(toCodeAction);
        }
      }, {
        providedCodeActionKinds: [vscode.CodeActionKind.QuickFix]
      }),
      vscode.languages.registerRenameProvider(selector, {
        prepareRename: async (document, position) => {
          const result = await this.sendRequest('textDocument/prepareRename', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });

          if (!result || !result.range) {
            return undefined;
          }

          return {
            range: toRange(result.range),
            placeholder: typeof result.placeholder === 'string' ? result.placeholder : document.getText(toRange(result.range))
          };
        },
        provideRenameEdits: async (document, position, newName) => {
          const result = await this.sendRequest('textDocument/rename', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position),
            newName
          });

          if (!result || !result.changes) {
            return undefined;
          }

          return toWorkspaceEdit(result);
        }
      }),
      vscode.languages.registerSignatureHelpProvider(selector, {
        provideSignatureHelp: async (document, position) => {
          const result = await this.sendRequest('textDocument/signatureHelp', {
            textDocument: { uri: document.uri.toString() },
            position: toLspPosition(position)
          });

          if (!result || !Array.isArray(result.signatures)) {
            return undefined;
          }

          return toSignatureHelp(result);
        }
      }, '(', ','),
      vscode.languages.registerCompletionItemProvider(selector, {
        provideCompletionItems: async (document) => {
          const result = await this.sendRequest('textDocument/completion', {
            textDocument: { uri: document.uri.toString() }
          });

          if (!Array.isArray(result)) {
            return [];
          }

          return result.map(toCompletionItem);
        }
      }),
      vscode.languages.registerDocumentSemanticTokensProvider(selector, {
        provideDocumentSemanticTokens: async (document) => {
          const result = await this.sendRequest('textDocument/semanticTokens/full', {
            textDocument: { uri: document.uri.toString() }
          });

          if (!result || !Array.isArray(result.data)) {
            return new vscode.SemanticTokens(new Uint32Array());
          }

          return new vscode.SemanticTokens(new Uint32Array(result.data));
        }
      }, semanticTokenLegend)
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
      textDocument: {
        uri: document.uri.toString(),
        version: document.version
      },
      contentChanges: [
        {
          text: document.getText()
        }
      ]
    });
  }

  didClose(document) {
    this.diagnostics.delete(document.uri);
    this.sendNotification('textDocument/didClose', {
      textDocument: {
        uri: document.uri.toString()
      }
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
      this.applyDiagnostics(message.params);
    }
  }

  applyDiagnostics(params) {
    const uri = vscode.Uri.parse(params.uri);
    const diagnostics = Array.isArray(params.diagnostics)
      ? params.diagnostics.map(toDiagnostic)
      : [];
    this.diagnostics.set(uri, diagnostics);
  }

  sendRequest(method, params) {
    const id = ++this.requestId;
    const payload = {
      jsonrpc: '2.0',
      id,
      method,
      params
    };

    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.writeMessage(payload);
    });
  }

  sendNotification(method, params) {
    this.writeMessage({
      jsonrpc: '2.0',
      method,
      params
    });
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
    const workspaceFolder = vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0
      ? vscode.workspace.workspaceFolders[0].uri.fsPath
      : '';

    if (command) {
      return {
        command,
        args: Array.isArray(args) ? args : [],
        cwd: cwdOverride || workspaceFolder || this.context.extensionPath
      };
    }

    const bundledServerDll = path.join(this.context.extensionPath, 'server', 'CFGS.Lsp.dll');
    if (fs.existsSync(bundledServerDll)) {
      return {
        command: 'dotnet',
        args: [bundledServerDll],
        cwd: cwdOverride || workspaceFolder || this.context.extensionPath
      };
    }

    if (!workspaceFolder) {
      throw new Error('Open the CFGS repository as a workspace or configure cfgs.server.command.');
    }

    const serverDll = path.join(workspaceFolder, 'dist', 'Debug', 'net10.0', 'CFGS.Lsp.dll');
    if (fs.existsSync(serverDll)) {
      return {
        command: 'dotnet',
        args: [serverDll],
        cwd: cwdOverride || workspaceFolder
      };
    }

    const serverProject = path.join(workspaceFolder, 'CFGS.Lsp', 'CFGS.Lsp.csproj');
    return {
      command: 'dotnet',
      args: ['run', '--project', serverProject],
      cwd: cwdOverride || workspaceFolder
    };
  }

  getRootUri() {
    const folder = vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0
      ? vscode.workspace.workspaceFolders[0]
      : null;
    return folder ? folder.uri.toString() : null;
  }
}

function isCfgsDocument(document) {
  return document.languageId === 'cfgs' || document.fileName.endsWith('.cfs');
}

function toLspPosition(position) {
  return {
    line: position.line,
    character: position.character
  };
}

function toRange(range) {
  return new vscode.Range(
    new vscode.Position(range.start.line, range.start.character),
    new vscode.Position(range.end.line, range.end.character)
  );
}

function toLspRange(range) {
  return {
    start: toLspPosition(range.start),
    end: toLspPosition(range.end)
  };
}

function toLocation(location) {
  return new vscode.Location(vscode.Uri.parse(location.uri), toRange(location.range));
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

function toCompletionItem(item) {
  const completion = new vscode.CompletionItem(item.label, toCompletionItemKind(item.kind));
  completion.detail = item.detail || '';
  return completion;
}

function toDiagnostic(item) {
  const diagnostic = new vscode.Diagnostic(
    toRange(item.range),
    item.message || 'Unknown CFGS diagnostic.',
    toDiagnosticSeverity(item.severity)
  );

  diagnostic.source = item.source || 'cfgs';
  if (item.code) {
    diagnostic.code = item.code;
  }

  return diagnostic;
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

function toWorkspaceEdit(item) {
  const edit = new vscode.WorkspaceEdit();
  if (Array.isArray(item.documentChanges)) {
    for (const change of item.documentChanges) {
      if (!change) {
        continue;
      }

      if (change.kind === 'create' && typeof change.uri === 'string') {
        edit.createFile(vscode.Uri.parse(change.uri), change.options || { ignoreIfExists: true });
        continue;
      }

      if (!change.textDocument || typeof change.textDocument.uri !== 'string' || !Array.isArray(change.edits)) {
        continue;
      }

      const uri = vscode.Uri.parse(change.textDocument.uri);
      for (const textEdit of change.edits) {
        edit.replace(uri, toRange(textEdit.range), textEdit.newText || '');
      }
    }
    return edit;
  }

  for (const [uriString, edits] of Object.entries(item.changes || {})) {
    const uri = vscode.Uri.parse(uriString);
    for (const textEdit of edits) {
      edit.replace(uri, toRange(textEdit.range), textEdit.newText || '');
    }
  }
  return edit;
}

function toCodeAction(item) {
  const action = new vscode.CodeAction(
    item.title || 'CFGS quick fix',
    toCodeActionKind(item.kind)
  );

  if (item.edit) {
    action.edit = toWorkspaceEdit(item.edit);
  }

  if (Array.isArray(item.diagnostics)) {
    action.diagnostics = item.diagnostics.map(toDiagnostic);
  }

  return action;
}

function toLspDiagnostic(diagnostic) {
  return {
    range: toLspRange(diagnostic.range),
    severity: fromDiagnosticSeverity(diagnostic.severity),
    source: diagnostic.source,
    code: diagnostic.code,
    message: diagnostic.message
  };
}

function toDocumentHighlightKind(kind) {
  switch (kind) {
    case 2:
      return vscode.DocumentHighlightKind.Write;
    case 3:
      return vscode.DocumentHighlightKind.Text;
    default:
      return vscode.DocumentHighlightKind.Read;
  }
}

function toDiagnosticSeverity(severity) {
  switch (severity) {
    case 1:
      return vscode.DiagnosticSeverity.Error;
    case 2:
      return vscode.DiagnosticSeverity.Warning;
    case 3:
      return vscode.DiagnosticSeverity.Information;
    case 4:
      return vscode.DiagnosticSeverity.Hint;
    default:
      return vscode.DiagnosticSeverity.Error;
  }
}

function fromDiagnosticSeverity(severity) {
  switch (severity) {
    case vscode.DiagnosticSeverity.Error:
      return 1;
    case vscode.DiagnosticSeverity.Warning:
      return 2;
    case vscode.DiagnosticSeverity.Information:
      return 3;
    case vscode.DiagnosticSeverity.Hint:
      return 4;
    default:
      return 1;
  }
}

function toCodeActionKind(kind) {
  if (kind === 'quickfix') {
    return vscode.CodeActionKind.QuickFix;
  }

  return vscode.CodeActionKind.Empty;
}

function toSymbolKind(kind) {
  switch (kind) {
    case 3:
      return vscode.SymbolKind.Namespace;
    case 5:
      return vscode.SymbolKind.Class;
    case 6:
      return vscode.SymbolKind.Method;
    case 10:
      return vscode.SymbolKind.Enum;
    case 12:
      return vscode.SymbolKind.Function;
    case 13:
      return vscode.SymbolKind.Variable;
    case 14:
      return vscode.SymbolKind.Constant;
    case 22:
      return vscode.SymbolKind.EnumMember;
    default:
      return vscode.SymbolKind.Object;
  }
}

function toCompletionItemKind(kind) {
  switch (kind) {
    case 2:
      return vscode.CompletionItemKind.Method;
    case 3:
      return vscode.CompletionItemKind.Function;
    case 6:
      return vscode.CompletionItemKind.Variable;
    case 7:
      return vscode.CompletionItemKind.Class;
    case 13:
      return vscode.CompletionItemKind.Enum;
    case 14:
      return vscode.CompletionItemKind.Keyword;
    case 20:
      return vscode.CompletionItemKind.EnumMember;
    case 21:
      return vscode.CompletionItemKind.Constant;
    default:
      return vscode.CompletionItemKind.Text;
  }
}

module.exports = {
  activate,
  deactivate
};
