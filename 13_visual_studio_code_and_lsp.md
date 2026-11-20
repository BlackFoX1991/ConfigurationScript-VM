# Visual Studio Code and LSP

The CFGS Language Tools extension adds editing support for `.cfs` files in Visual Studio Code. It includes the language server needed for diagnostics, completion, navigation, and formatting.

If you are creating your first script, follow [Your First CFGS Project](00_first_project.md) first. The extension edits `.cfs` files; the CFGS CLI runs them.

## What you can do in VS Code

- See syntax, compile, and lint diagnostics while editing.
- Get code completion for CFGS symbols, import paths, plugins, and supported native .NET types, plus signature help for calls.
- Jump to definitions, find references, and rename symbols across files in the open project.
- Use hover information, document symbols, highlights, semantic colors, code lenses, call hierarchy, and inlay hints to explore code.
- Apply quick fixes for supported local import problems.
- Format a file or selection, lint the current file or workspace, and configure linting on save.

## Install the VS Code Extension

The packaged `cfgs-vscode-<version>.vsix` contains the language server and CLI tooling used by the editor. It works with any CFGS project folder.

In VS Code, open **Extensions**, choose **Install from VSIX…** from the Extensions view menu, and select the package file. This repository includes `vscode-cfgs/cfgs-vscode-0.12.3.vsix`. The packaged extension needs the .NET 10 runtime on your machine.

Open **the CFGS project folder** in VS Code, then open `main.cfs`. The extension starts its bundled language server when a CFGS file is opened. The language mode should be **CFGS** in the status bar. In the Command Palette, try `CFGS: Lint Current File` or `CFGS: Format Current File`. Run scripts with your installed `CFGS_VM.exe`.

For a comfortable starting configuration, put this in the project's `.vscode/settings.json`:

```json
{
  "[cfgs]": {
    "editor.defaultFormatter": "blackfox1991.cfgs-vscode",
    "editor.formatOnSave": true,
    "editor.insertSpaces": true,
    "editor.tabSize": 4
  },
  "cfgs.lint.live": true
}
```

The extension already supplies the default formatter and enables live linting; this file makes the preferred indentation and format-on-save behavior explicit for everyone who opens the project. If the project intentionally uses tabs, set `editor.insertSpaces` to `false` and `cfgs.lint.allowTabs` to `true` instead. This prevents the `CFGS8002` warning.

If the editor still shows an old import diagnostic after files change, run `CFGS: Restart Language Server` from the Command Palette. Check that the imported path is relative to the importing script and that you opened the intended project folder.

## Optional VS Code Settings

The extension exposes these settings if you want to launch the server differently:

- `cfgs.server.command`
- `cfgs.server.args`
- `cfgs.server.cwd`
- `cfgs.cli.command`
- `cfgs.cli.args`
- `cfgs.lint.live`
- `cfgs.lint.onSave`
- `cfgs.lint.fixOnSave`
- `cfgs.lint.allowTabs` (set to `true` when the formatter uses tabs)

## If the editor does not behave as expected

- If a `.cfs` file appears as plain text, check that CFGS Language Tools is installed and select **CFGS** as the language mode in the VS Code status bar.
- If an import is reported as missing, open the actual project folder in VS Code and check the path relative to the importing file. Then run `CFGS: Restart Language Server` from the Command Palette.
- If the server does not start, check that the .NET 10 runtime is installed and restart VS Code.
- If the linter reports `CFGS8002` for tabs, use spaces or enable `cfgs.lint.allowTabs` and set `editor.insertSpaces` to `false`.
