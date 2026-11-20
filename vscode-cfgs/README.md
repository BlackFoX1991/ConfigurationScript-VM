# CFGS Language Tools

Visual Studio Code support for ConfigurationScript.

New to CFGS? Follow [Your First CFGS Project](../00_first_project.md) with an already compiled `CFGS_VM.exe`. It shows how to create `cfgs.json` and `main.cfs`, run the script, and use this extension in VS Code. The extension supplies editor support; project creation and script execution use the CFGS CLI.

Features:

- `.cfs` language registration and syntax highlighting
- grammar support for interpolated strings, verbatim strings, explicit interface member declarations, file-scoped namespaces, and current accessor keywords
- CFGS LSP integration
- diagnostics, hover, completion, definitions, references, rename, symbols, semantic tokens, inlay hints, code lenses, call hierarchy, document links, selection ranges, and formatting
- incremental document sync so normal typing errors no longer force full-document resend from the VS Code side
- full and range semantic tokens wired to the CFGS LSP server
- completion for import paths, named `.cfs` exports, native .NET interop aliases, plugin namespace metadata, and object/class/namespace members
- commands for `CFGS: Lint Current File`, `CFGS: Lint Workspace`, `CFGS: Format Current File`, and `CFGS: Restart Language Server`
- optional `cfgs.lint.onSave` when live lint is disabled, and `cfgs.lint.fixOnSave`
- `cfgs.lint.allowTabs` for projects that intentionally use tab indentation; set it to `true` alongside `editor.insertSpaces: false` to keep the formatter and linter consistent
- bundled language server and CLI tooling in the installable extension package

## Install and use

Install `cfgs-vscode-0.12.3.vsix` through VS Code's **Extensions → Install from VSIX…** menu. The extension package includes the language server and CLI tooling and needs the .NET 10 runtime. Open your CFGS project folder, then open a `.cfs` file. The language mode should be **CFGS** in the status bar.

Use **View → Command Palette** for `CFGS: Lint Current File`, `CFGS: Format Current File`, and `CFGS: Restart Language Server`. To run a script, use the already installed `CFGS_VM.exe` as shown in [Your First CFGS Project](../00_first_project.md).

For editor settings and troubleshooting, see [Visual Studio Code and LSP](../13_visual_studio_code_and_lsp.md).
