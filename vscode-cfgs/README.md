# CFGS Language Tools

Visual Studio Code support for ConfigurationScript.

Features:

- `.cfs` language registration and syntax highlighting
- grammar support for interpolated strings, verbatim strings, explicit interface member declarations, file-scoped namespaces, and current accessor keywords
- CFGS LSP integration
- diagnostics, hover, completion, definitions, references, rename, symbols, semantic tokens, inlay hints, code lenses, call hierarchy, document links, selection ranges, and formatting
- incremental document sync so normal typing errors no longer force full-document resend from the VS Code side
- full and range semantic tokens wired to the CFGS LSP server
- completion for import paths, named `.cfs` exports, native .NET interop aliases, plugin namespace metadata, and object/class/namespace members
- commands for `CFGS: Lint Current File`, `CFGS: Lint Workspace`, `CFGS: Format Current File`, and `CFGS: Restart Language Server`
- optional `cfgs.lint.onSave` and `cfgs.lint.fixOnSave`
- auto-detected server and CLI binaries from workspace builds under `dist/Debug/net10.0`, `dist/Release/net10.0`, `dist/net10.0`, or the bundled `server` payload in the packaged extension

The extension auto-detects the CFGS language server and CLI in this order:

- explicit `cfgs.server.command` or `cfgs.cli.command`
- workspace builds in `dist/Debug/net10.0`, `dist/Release/net10.0`, or `dist/net10.0`
- workspace project fallback via `dotnet run` when the CFGS repo is open
- bundled binaries inside the packaged extension

Build an installable package from this folder with:

```powershell
npm run package
```

The generated package is `cfgs-vscode-0.8.3.vsix`.
