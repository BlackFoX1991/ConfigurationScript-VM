# Getting Started and Running Scripts

## What This Project Is

ConfigurationScript, or CFGS, is implemented in this repository as a bytecode VM with its own lexer, parser, compiler, and runtime. Scripts are not executed directly from the AST. They are parsed first, compiled next, and then executed inside the VM.

In practice this means two things. First, the language is clearly formalized. Second, the documentation should be aligned with the current implementation instead of historical material. That is exactly what this folder does.

If you want the codebase-level structure behind that implementation, continue with [Internal Architecture](14_internal_architecture.md). If you want the normative language rules, use [CFGS Language Specification](18_language_specification.md).

## Important Project Folders

- `CFGS_NE` contains the language frontend, compiler, runtime, and CLI.
- `CFGS.StandardLibrary` contains the standard builtins and intrinsics.
- `CFGS.Web.Http` contains the `http` namespace and the built in HTTP server handle.
- `CFGS.Microsoft.SQL` contains SQL support.
- `Documentation` contains the German documentation.
- `documentation_en` contains the English documentation.
- `CFGS_NE/Samples` and `CFGS_NE/_edgecases` are strong references for real language usage.
- `14_internal_architecture.md` documents the productive parser, compiler, VM, and LSP architecture.

## Building the Project

You can build the full repository through the solution.

```powershell
dotnet build CFGS_VM.sln
```

The plugins and the standard library are built as normal .NET assemblies. The sample scripts typically import their DLLs from `dist/Debug/net10.0`.

## Running a Script

A CFGS script normally uses the `.cfs` extension.

```powershell
dotnet run --project CFGS_NE -- .\my_script.cfs
```

If you need script arguments, pass them after `-p` or `-params`.

```powershell
dotnet run --project CFGS_NE -- .\my_script.cfs -p alpha beta gamma
```

Inside the script you can access them through `cmdArgs()`.

## Starting the REPL

If you start the CLI without a script file, it opens the interactive mode.

```powershell
dotnet run --project CFGS_NE
```

On startup, the REPL can automatically load plugins listed in `repl_plugins.json` next to the application. Bare DLL names are loaded from the application `plugins` directory, relative paths are resolved from the application directory, and invalid entries are skipped with a warning. This autoload behavior is only active in REPL mode.

```json
{
  "plugins": [
    "CFGS.StandardLibrary.dll",
    "plugins/CustomPlugin.dll"
  ]
}
```

The REPL supports these `$` commands. The prefix keeps REPL commands separate from normal script input.

- `$help` shows the help screen.
- `$exit` or `$quit` ends the session.
- `$clear` or `$cls` clears the console.
- `$debug` toggles debug mode.
- `$ansi` toggles ANSI output.
- `$buffer:len` sets the debug buffer size.

The built in REPL help also describes these interactions.

- `Ctrl+Enter` runs the current multiline buffer.
- `Ctrl+Backspace` clears the current buffer.
- `$L <line> <content>` edits a specific line.
- Up and down arrow keys help with line editing.

## Important CLI Options

- `-d` or `-debug` enables debug mode before execution starts.
- `-di`, `-debugger`, or `--debugger` starts the interactive VM debugger.
- `-check <file.cfs|file.cfb>`, `--check <file.cfs|file.cfb>`, or `check <file.cfs|file.cfb>` validates a source or CFB file without running it.
- `--json` with `-check` emits diagnostics as a JSON array for tools and CI.
- `init` creates a `cfgs.json` package manifest in the current project.
- `install <plugin.dll>` adds a local plugin dependency and restores it.
- `restore` reads `cfgs.json`, copies plugin artifacts into `.cfgs/plugins`, and updates `cfgs.lock.json`.
- `deps list` lists manifest plugins and lock status.
- `-c <file.cfs>` or `-compile <file.cfs>` compiles a source script to `.cfb`.
- `-o <file.cfb>` or `-out <file.cfb>` sets the output path for `-c` or `-compile`.
- `-s buffer <number>` sets the debug buffer size.
- `-s ansi 0` or `-s ansi 1` disables or enables ANSI output.
- `-p` or `-params` separates script arguments from CLI arguments.

In debug mode the CLI writes a `log_file.log` into the current working directory after execution.
In interactive debugger mode the VM stops before instructions and accepts commands such as `step`, `next`, `continue`, `break`, `locals`, `globals`, `stack`, `callstack`, and `print`. For details, see [Debugging CFGS Scripts](17_debugging.md).
For diagnostic codes, source spans, and machine-readable check output, see [Diagnostics and Error Codes](19_diagnostics.md).
For `cfgs.json`, lockfiles, and restore/install workflows, see [Package and Dependency Management](20_package_dependency_management.md).

## Working Directory During Execution

When you run a script file, the CLI changes the current working directory to the directory of that script. This matters for relative imports, file operations, and plugin DLLs loaded through relative paths.

Import resolution then searches in this order.

1. The script directory.
2. The current working directory.
3. The directory of the running CLI executable.

## File Types

- `.cfs` is the normal source format.
- `.dll` can be imported as a plugin.
- `.cfb` is a compiled CFGS bytecode artifact that can be executed directly by the CLI.

You can compile a script like this.

```powershell
dotnet run --project CFGS_NE -- -c .\my_script.cfs -o .\my_script.cfb
```

Then run the compiled artifact directly.

```powershell
dotnet run --project CFGS_NE -- .\my_script.cfb
```

The `.cfb` format stores bytecode, function metadata, source hash, compiler version, auto-main metadata, and plugin DLL names discovered during compilation. It does not store local source or plugin file paths.

## The Smallest Reasonable Starting Point

In most practical scripts you start by importing the standard library.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";

func main() {
    print("CFGS is running");
}
```

## Top Level Behavior in Short

Top level code in CFGS now has two practical modes. Directly executed scripts stay compatibility-friendly, but imported `.cfs` modules and namespace bodies are declaration-oriented. In those declaration-oriented contexts, imports and declarations belong at top level, while control flow and free executable statements belong inside functions or methods.

That usually leads to a simple pattern like this.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";

func main() {
    if (true) {
        print("all good");
    }
}
```

When a directly executed script is purely declarative at top level and defines `main`, the CLI invokes `main` automatically. Older scripts that still call `main();` or `var _ = await main();` at top level continue to work.

If you want to understand why certain constructs are not allowed at top level, continue with [Language and Data](02_language_and_data.md) and [Control Flow and Errors](03_control_flow_and_errors.md).
