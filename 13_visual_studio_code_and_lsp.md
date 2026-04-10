# Visual Studio Code and LSP

This repository now contains a basic CFGS language server and a matching Visual Studio Code extension.

## Included Components

- `CFGS.Lsp`
  A dependency-free Language Server Protocol implementation in C#.
- `vscode-cfgs`
  A small Visual Studio Code extension that starts the server over `stdio`.

## Current Features

- Syntax and compile diagnostics while editing `.cfs` files
- Document symbols
- Go to definition for indexed top-level symbols, local bindings, class members reached through `this`, `type`, and `super`, and simple chained member access inferred from obvious constructors and aliases
- Hover for indexed top-level symbols, local bindings, class members reached through `this`, `type`, and `super`, and simple chained member access inferred from obvious constructors and aliases
- Document highlights for symbol occurrences inside the current file
- Find references for indexed top-level symbols, local bindings, class members reached through `this`, `type`, and `super`, and simple chained member access inferred from obvious constructors and aliases
- Rename for indexed top-level symbols, local bindings, class members reached through `this`, `type`, and `super`, and simple chained member access inferred from obvious constructors and aliases
- Prepare rename support so Visual Studio Code can validate rename targets before applying edits
- Workspace-wide references and rename for stable imported symbols across `.cfs` files inside the current workspace root
- Cross-file definition and hover stay aligned with open unsaved imported files by using in-memory document overlays during analysis
- Completion and signature help keep working during broken intermediate edits by falling back to the last successful analysis for the current document
- Semantic tokens for namespaces, classes, enums, enum members, functions, methods, parameters, variables, and readonly constants
- Quick fixes for common local import diagnostics such as missing `from` strings, missing local module files, and missing exports in local imported files
- Signature help for functions and constructors
- Keyword and symbol completion
- Basic syntax highlighting and editor language configuration

## Current Analysis Path

The LSP does not keep a second parser or semantic fork. The productive analysis path is:

1. `LspServer` manages open documents and overlay content.
2. `CfgsAnalyzer` builds analysis input for the requested file and its imports.
3. `Lexer`, `Parser`, `SyntaxLowerer`, and `Compiler` run in the same order as normal CFGS execution.
4. Diagnostics, navigation, hover, rename, semantic tokens, and quick fixes are derived from that shared result.

For the broader codebase split behind this path, see [Internal Architecture](14_internal_architecture.md).

## Build the Server

From the repository root:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .config).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet build -v minimal CFGS.Lsp\CFGS.Lsp.csproj
```

The server output is written to:

```text
dist/Debug/net10.0/CFGS.Lsp.dll
```

The main CFGS executable can also forward into LSP mode:

```powershell
.\dist\Debug\net10.0\CFGS_VM.exe -lsp
```

For release builds produced by `build_release.bat`, the same switch works with the shipped `plugins\CFGS.Lsp.dll`:

```powershell
.\dist_release\CFGS_VM.exe -lsp
```

## Use the VS Code Extension

1. Open this repository in Visual Studio Code.
2. Open the `vscode-cfgs` folder as an extension project or package it later as a normal VS Code extension.
3. Make sure the language server has been built once.
4. Start the extension host.

By default the extension resolves the server in this order:

1. Bundled `server/CFGS.Lsp.dll` inside the installed extension
2. `dist/Debug/net10.0/CFGS.Lsp.dll`
3. `dotnet run --project CFGS.Lsp/CFGS.Lsp.csproj`

## Build a VSIX Package

After building the language server once, create an installable `.vsix` package with:

```powershell
powershell -ExecutionPolicy Bypass -File vscode-cfgs\scripts\package-vsix.ps1
```

The package is written to:

```text
artifacts/blackfox1991.cfgs-vscode-0.3.3.vsix
```

The packaged extension includes the built CFGS language server under `extension/server`, so it can start directly after installation as long as `dotnet` is available on the machine.

## Optional VS Code Settings

The extension exposes these settings if you want to launch the server differently:

- `cfgs.server.command`
- `cfgs.server.args`
- `cfgs.server.cwd`

## Notes

- Deeper chained instance analysis can still be expanded later. The current semantic layer already covers local bindings, top-level names, constructors, `using` bindings, implicit class-member lookup inside methods, direct class-context member access through `this`, `type`, `super`, and `outer`, namespace-import aliases, callable value aliases including rebinding flows such as `var f = add; f = other; f(1)`, control-flow merges such as `if (flag) f = other; f(1)`, loop merges such as `while (flag) { f = other; } f(1)`, `for`/`foreach` variants that may execute zero times, `do/while` flows that execute at least once, multi-iteration loop fixpoints such as `picked = current; current = other;` where a second or later pass changes the callable seen after the loop, completion-aware loop control with `break`, `continue`, and early `return`, `try/catch/finally` flows including `finally`-forwarded callables plus `finally`-mediated break/continue propagation and `finally` return overrides, returned callables such as `var f = choose(flag); f(1)`, returned containers such as `var box = chooseBox(flag); box["fn"](1)`, array/container slot flows such as `handlers[0] = add; handlers[0](1)` and `[add, other][0](1)` after aliasing/returns, returned dict literals such as `return {"fn": add};`, instance-specific member rebinding such as `a.fn = add; b.fn = other`, dynamic container aliases like `bag["fn"] = add`, nested forwarded containers like `root["inner"]["fn"]`, qualified static paths, and simple chained member access such as `this.helper.run()` or `foo.helper.run()` when the intermediate value can be inferred from obvious constructors or aliases. Call hierarchy, signature help, and inlay hints now all consume the same recorded call-site resolution rather than re-deriving targets from overlapping token occurrences.
- Workspace-wide rename intentionally targets stable declaration symbols. Purely local bindings still stay document-local.
- Cross-file analysis prefers open editor content over on-disk file content for imported `.cfs` files, so navigation and hover do not lag behind unsaved edits.
- Completion and signature help intentionally prefer the latest successful symbol model over an empty failed re-analysis, so they remain usable while the current file is temporarily syntactically broken.
- Document highlights are intentionally document-local; workspace-wide symbol navigation still belongs to references and rename.
- Semantic tokens reuse the same symbol model as navigation features, so semantic highlighting tracks imported and unsaved symbols consistently.
- Quick fixes are intentionally conservative and currently target only reliable local import problems, not arbitrary compiler errors or remote/http imports.
- The extension does not require `npm install` because the client is plain JavaScript and uses only the built-in `vscode` API.
