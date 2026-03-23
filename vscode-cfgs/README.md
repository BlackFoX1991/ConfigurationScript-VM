# CFGS Language Tools

Visual Studio Code support for ConfigurationScript (`.cfs`) with the bundled CFGS language server.

## Features

- Diagnostics
- Go to Definition
- Hover
- References
- Rename
- Completion
- Signature Help
- Semantic Tokens
- Quick Fixes for common local import errors

## Notes

- The packaged extension starts the bundled CFGS language server with `dotnet`.
- If you want to override server startup, use the settings `cfgs.server.command`, `cfgs.server.args`, and `cfgs.server.cwd`.
