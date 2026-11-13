const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

let settings = {};
const vscode = {
  SemanticTokensLegend: class {},
  Range: class {
    constructor(...args) { this.args = args; }
  },
  Diagnostic: class {
    constructor(range, message, severity) {
      this.range = range;
      this.message = message;
      this.severity = severity;
    }
  },
  DiagnosticSeverity: { Error: 0, Warning: 1, Information: 2 },
  Uri: { file: (file) => ({ fsPath: file, toString: () => file }) },
  workspace: {
    getConfiguration: () => ({ get: (key, fallback) => settings[key] ?? fallback })
  }
};

const sandbox = {
  require: (name) => name === 'vscode' ? vscode : require(name),
  module: { exports: {} },
  Buffer,
  process,
  console
};
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(__dirname, '..', 'extension.js'), 'utf8'), sandbox);

const output = { appendLine() {} };
const document = { languageId: 'cfgs', fileName: 'sample.cfs', uri: { scheme: 'file', fsPath: 'sample.cfs' } };
const calls = [];
sandbox.runCfgsCli = async (args) => { calls.push(`fix:${args.join(' ')}`); return { code: 0, stdout: '[]' }; };
sandbox.lintFile = async (uri) => { calls.push(`lint:${uri.fsPath}`); };

async function checkSave(config, doc, expected) {
  settings = config;
  calls.length = 0;
  await sandbox.handleSavedDocument(doc, output);
  assert.deepEqual(calls, expected);
}

async function main() {
  await checkSave({ 'lint.onSave': true, 'lint.live': false }, document, ['lint:sample.cfs']);
  await checkSave({ 'lint.onSave': true, 'lint.live': true }, document, []);
  await checkSave({ 'lint.onSave': false, 'lint.live': false }, document, []);
  await checkSave(
    { 'lint.onSave': true, 'lint.live': false, 'lint.fixOnSave': true },
    document,
    ['fix:-lint sample.cfs --fix --json', 'lint:sample.cfs']
  );
  await checkSave(
    { 'lint.fixOnSave': true, 'lint.allowTabs': true },
    document,
    ['fix:-lint sample.cfs --fix --allow-tabs --json']
  );
  settings = { 'lint.allowTabs': true };
  assert.deepEqual(Array.from(sandbox.createLintArgs('sample.cfs')), ['-lint', 'sample.cfs', '--allow-tabs', '--json']);
  settings = {};
  assert.deepEqual(Array.from(sandbox.createLintArgs('sample.cfs')), ['-lint', 'sample.cfs', '--json']);
  await checkSave(
    { 'lint.fixOnSave': true },
    { ...document, uri: { scheme: 'untitled', fsPath: 'sample.cfs' } },
    []
  );
  await checkSave({ 'lint.onSave': true, 'lint.live': false }, { ...document, languageId: 'plaintext', fileName: 'sample.txt' }, []);

  const recorded = [];
  sandbox.testDiagnostics = { set: (uri, diagnostics) => recorded.push([uri.fsPath, diagnostics]) };
  vm.runInContext('lintDiagnostics = testDiagnostics', sandbox);
  sandbox.applyLintDiagnostics(JSON.stringify([
    { source: 'imported.cfs', code: 'CFGS8004', message: 'style', severity: 'warning', line: 1, column: 1 }
  ]), output, document.uri);
  assert.equal(recorded.length, 2);
  assert.equal(recorded[0][0], 'sample.cfs');
  assert.equal(recorded[0][1].length, 0);
  assert.equal(recorded[1][0], 'imported.cfs');
  assert.equal(recorded[1][1].length, 1);

  console.log('VS_CODE_SAVE_LINT_TEST:OK');
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
