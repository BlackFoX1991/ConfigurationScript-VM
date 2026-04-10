# CFGS Language Tools

Visual Studio Code support for ConfigurationScript (`.cfs`) with the bundled CFGS language server and syntax highlighting.

## Features

- Diagnostics
- Document Symbols
- Go to Definition
- Type Definition
- Go to Implementation
- Hover
- Document Highlights
- References
- Rename
- Prepare Rename
- Completion
- Signature Help
- Semantic Tokens
- Workspace Symbols
- Formatting
- Document Links
- Selection Ranges
- Code Lenses
- Inlay Hints
- Call Hierarchy
- Syntax highlighting for the current CFGS keyword and receiver set
- Quick Fixes for common local import errors

## Notes

- The packaged extension starts the bundled CFGS language server with `dotnet`.
- The VSIX bundles `CFGS.Lsp.dll` under `server/`, so no repo checkout is needed after installation.
- If you want to override server startup, use the settings `cfgs.server.command`, `cfgs.server.args`, and `cfgs.server.cwd`.
