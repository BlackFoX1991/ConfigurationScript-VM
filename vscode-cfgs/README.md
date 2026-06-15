# CFGS Language Tools

Visual Studio Code support for ConfigurationScript.

Features:

- `.cfs` language registration and syntax highlighting
- CFGS LSP integration
- diagnostics, hover, completion, definitions, references, rename, symbols, semantic tokens, inlay hints, code lenses, call hierarchy, document links, selection ranges, and formatting
- completion for import paths, named `.cfs` exports, native .NET interop aliases, plugin namespace metadata, and object/class/namespace members
- commands for `CFGS: Lint Current File`, `CFGS: Lint Workspace`, `CFGS: Format Current File`, and `CFGS: Restart Language Server`
- optional `cfgs.lint.onSave` and `cfgs.lint.fixOnSave`

The extension auto-detects a bundled server under `server/CFGS.Lsp.dll` or a workspace build under `dist/Debug/net10.0/CFGS.Lsp.dll`. Override `cfgs.server.command` and `cfgs.server.args` when needed.

Build an installable package from this folder with:

```powershell
npm run package
```

The generated package is `cfgs-vscode-0.5.0.vsix`.
