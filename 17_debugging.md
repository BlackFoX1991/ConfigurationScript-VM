# Debugging CFGS Scripts

CFGS has two debugging modes.

- `-d` or `-debug` enables the existing debug log mode.
- `-di`, `-debugger`, or `--debugger` starts the interactive VM debugger.

Use debug log mode when you want a complete instruction trace after execution. Use interactive debugger mode when you want to stop while the program is running, step through bytecode, inspect variables, and continue from breakpoints.

## Debug Log Mode

Run a script with `-d`.

```powershell
dotnet run --project CFGS_NE -- -d .\script.cfs
```

The CLI prints the compiled instruction list before execution starts. After execution it writes `log_file.log` into the script working directory. That log contains VM trace data such as instruction pointer, stack snapshot, scope count, and call stack depth.

This mode does not pause execution.

## Interactive Debugger Mode

Run a script with `-di`.

```powershell
dotnet run --project CFGS_NE -- -di .\script.cfs
```

You can also use the already built runtime host.

```powershell
dotnet .\dist\Debug\net10.0\CFGS_VM.dll -di .\script.cfs
```

The VM stops before executing instructions and shows a prompt.

```text
dbg>
```

Each stop prints the current instruction pointer, opcode, source location, stack size, scope count, and call stack depth.

```text
[dbg] ip=12 LOAD_VAR total @ script.cfs:8:12 stack=0 scopes=2 calls=1
dbg>
```

## Stepping

`step` executes exactly one VM instruction.

```text
step
s
```

Pressing Enter on an empty debugger prompt also behaves like `step`.

`next` continues until the current call depth is reached again. In practice this is useful for stepping over nested function or method calls.

```text
next
n
```

`continue` runs until the next breakpoint or program end.

```text
continue
cont
c
```

`quit` stops the current execution.

```text
quit
exit
q
```

## Breakpoints

Breakpoints can target source lines or VM instruction pointers.

Break at a line in the current source file.

```text
break 12
b 12
```

Break at a line in a specific file.

```text
break script.cfs:12
break C:\work\script.cfs:12
```

Break at an instruction pointer.

```text
break ip 42
```

List breakpoints.

```text
breaks
breakpoints
```

Clear breakpoints.

```text
clear all
clear ip 42
```

Line breakpoints match by full path when possible and by file name as a fallback. This keeps breakpoints practical when scripts are executed from generated or copied runtime folders.

## Inspecting State

Show variables in the current local scope.

```text
locals
```

Show variables in the global scope.

```text
globals
```

Show the VM operand stack.

```text
stack
```

Show the call stack.

```text
callstack
where
bt
```

Print a visible variable by name.

```text
print total
p total
```

Show the current instruction pointer.

```text
ip
```

Show debugger help.

```text
help
h
?
```

## Example Session

Given this script:

```cfs
var total = 0;

func add(a, b) {
    var result = a + b;
    return result;
}

total = add(2, 3);
```

Start the debugger.

```powershell
dotnet run --project CFGS_NE -- -di .\sample.cfs
```

Set a breakpoint and continue.

```text
dbg> break 4
breakpoint added at *:4
dbg> continue
[dbg] ip=... LOAD_VAR a @ sample.cfs:4:18 stack=0 scopes=2 calls=1
dbg>
```

Inspect local variables.

```text
dbg> locals
locals:
  a = 2
  b = 3
```

Step once and inspect the stack.

```text
dbg> step
[dbg] ip=... LOAD_VAR b @ sample.cfs:4:22 stack=1 scopes=2 calls=1
dbg> stack
stack:
  [0] 2
```

Continue to the end.

```text
dbg> continue
```

## Debugging Compiled CFB Files

The interactive debugger also works with `.cfb` files.

```powershell
dotnet run --project CFGS_NE -- -di .\script.cfb
```

CFB files contain bytecode metadata and source locations. They do not store local source paths for plugins. If source locations look different from your original script path, the file may have been compiled from another location.

## REPL Notes

The REPL supports the `$debug` command, which toggles normal debug log mode. REPL commands use the `$` prefix so they do not collide with normal script input.

Interactive debugger mode is a CLI startup option. If the REPL is started with `-di`, every submitted snippet runs under the interactive debugger.

## Limitations

The debugger currently works at VM-instruction level. Source-line breakpoints map to the instructions generated for that line, so a single source line may stop more than once if it compiles to multiple instructions.

`print <name>` resolves variables by name from the current VM environment. It does not evaluate arbitrary expressions yet.

`next` is based on VM call stack depth. It is useful for stepping over calls, but it is not a full source-level statement stepper.
