# Getting Started and Running Scripts

## What This Project Is

ConfigurationScript, or CFGS, is implemented in this repository as a bytecode VM with its own lexer, parser, compiler, and runtime. Scripts are not executed directly from the AST. They are parsed first, compiled next, and then executed inside the VM.

In practice this means two things. First, the language is clearly formalized. Second, the documentation should be aligned with the current implementation instead of historical material. That is exactly what this folder does.

If you want the codebase-level structure behind that implementation, continue with [Internal Architecture](14_internal_architecture.md).

## Important Project Folders

- `CFGS_NE` contains the language frontend, compiler, runtime, and CLI.
- `CFGS.StandardLibrary` contains the standard builtins and intrinsics.
- `CFGS.Web.Http` contains HTTP builtins and the built in HTTP server handle.
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

The REPL supports these commands.

- `help` shows the help screen.
- `exit` or `quit` ends the session.
- `clear` or `cls` clears the console.
- `debug` toggles debug mode.
- `ansi` toggles ANSI output.
- `buffer:len` sets the debug buffer size.

The built in REPL help also describes these interactions.

- `Ctrl+Enter` runs the current multiline buffer.
- `Ctrl+Backspace` clears the current buffer.
- `$L <line> <content>` edits a specific line.
- Up and down arrow keys help with line editing.

## Important CLI Options

- `-d` or `-debug` enables debug mode before execution starts.
- `-s buffer <number>` sets the debug buffer size.
- `-s ansi 0` or `-s ansi 1` disables or enables ANSI output.
- `-p` or `-params` separates script arguments from CLI arguments.

In debug mode the CLI writes a `log_file.log` into the current working directory after execution.

## Working Directory During Execution

When you run a script file, the CLI changes the current working directory to the directory of that script. This matters for relative imports, file operations, and plugin DLLs loaded through relative paths.

Import resolution then searches in this order.

1. The script directory.
2. The current working directory.
3. The directory of the running CLI executable.

## File Types

- `.cfs` is the normal source format.
- `.dll` can be imported as a plugin.
- `.cfb` is no longer supported. Binary support has been removed.

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
