const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');

function readJson(relativePath) {
  const fullPath = path.join(root, relativePath);
  return JSON.parse(fs.readFileSync(fullPath, 'utf8'));
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function assertFile(relativePath) {
  const fullPath = path.join(root, relativePath);
  assert(fs.existsSync(fullPath), `missing file: ${relativePath}`);
  return fullPath;
}

const manifest = readJson('package.json');
assert(manifest.name === 'cfgs-vscode', 'unexpected extension name');
assert(manifest.version === '0.7.0', 'unexpected extension version');
assert(manifest.main === './extension.js', 'unexpected extension entry point');

const extensionSource = fs.readFileSync(assertFile('extension.js'), 'utf8');
assert(extensionSource.includes('class CfgsLanguageClient'), 'extension client class missing');
assert(extensionSource.includes('textDocument/completion'), 'completion request wiring missing');
assert(extensionSource.includes('textDocument/formatting'), 'formatting request wiring missing');
assert(extensionSource.includes('textDocument/publishDiagnostics'), 'diagnostic notification wiring missing');
assert(extensionSource.includes('registerDocumentRangeSemanticTokensProvider'), 'range semantic tokens provider missing');
assert(extensionSource.includes('resolveCodeLens'), 'code lens resolve wiring missing');
assert(extensionSource.includes('editor.action.showReferences'), 'reference navigation command wiring missing');
assert(extensionSource.includes('contentChanges: changes'), 'incremental text sync wiring missing');
assert(extensionSource.includes("findBundledServerFile(extensionRoot, 'CFGS_VM.dll')"), 'bundled CLI fallback missing');

const packageScriptSource = fs.readFileSync(assertFile('scripts/package-vsix.js'), 'utf8');
assert(packageScriptSource.includes('CFGS_VM.deps.json'), 'bundled CFGS_VM deps packaging missing');
assert(packageScriptSource.includes('CFGS_VM.runtimeconfig.json'), 'bundled CFGS_VM runtimeconfig packaging missing');

const contributes = manifest.contributes || {};
assert(Array.isArray(contributes.languages), 'languages contribution missing');
assert(contributes.languages.some((language) => language.id === 'cfgs'), 'cfgs language contribution missing');

const commands = new Set((contributes.commands || []).map((command) => command.command));
for (const command of ['cfgs.restartServer', 'cfgs.lintFile', 'cfgs.lintWorkspace', 'cfgs.formatFile']) {
  assert(commands.has(command), `missing command contribution: ${command}`);
}

const activationEvents = new Set(manifest.activationEvents || []);
for (const activation of ['onLanguage:cfgs', 'onCommand:cfgs.restartServer', 'onCommand:cfgs.lintFile']) {
  assert(activationEvents.has(activation), `missing activation event: ${activation}`);
}

const properties = contributes.configuration && contributes.configuration.properties;
assert(properties && properties['cfgs.server.command'], 'cfgs.server.command setting missing');
assert(properties && properties['cfgs.cli.command'], 'cfgs.cli.command setting missing');
assert(properties && properties['cfgs.lint.onSave'], 'cfgs.lint.onSave setting missing');
assert(properties && properties['cfgs.lint.fixOnSave'], 'cfgs.lint.fixOnSave setting missing');

assertFile('language-configuration.json');
const grammar = readJson('syntaxes/cfgs.tmGrammar.json');
assert(grammar.scopeName === 'source.cfgs', 'unexpected CFGS grammar scope');
const stringPatterns = (((grammar.repository || {}).strings || {}).patterns || []);
assert(stringPatterns.some((pattern) => String(pattern.begin || '').includes('\\$"')), 'interpolated string grammar missing');
assert(stringPatterns.some((pattern) => String(pattern.begin || '').includes('@"')), 'verbatim string grammar missing');
assert(stringPatterns.some((pattern) => String(pattern.begin || '').includes('\\$@') || String(pattern.begin || '').includes('@\\$')), 'verbatim interpolated string grammar missing');
const declarationPatterns = (((grammar.repository || {}).declarations || {}).patterns || []);
assert(declarationPatterns.some((pattern) => String(pattern.match || '').includes('(\\\\.)') || String(pattern.match || '').includes('(\\.)')), 'explicit interface method declaration grammar missing');
const keywordPatterns = (((grammar.repository || {}).keywords || {}).patterns || []);
assert(keywordPatterns.some((pattern) => String(pattern.match || '').includes('native') && String(pattern.match || '').includes('init')), 'current CFGS declaration keywords missing from grammar');

assert(Array.isArray(contributes.semanticTokenScopes), 'semanticTokenScopes contribution missing');
const cfgsSemanticScopes = contributes.semanticTokenScopes.find((entry) => entry.language === 'cfgs');
assert(cfgsSemanticScopes && cfgsSemanticScopes.scopes && cfgsSemanticScopes.scopes.class, 'class semantic token scope mapping missing');

console.log('VS_CODE_EXTENSION_SMOKE:OK');
