# CFGS Samples

This folder contains runnable sample scripts for the current CFGS language and
runtime feature set. The samples are organized by topic so each feature can be
studied in isolation and then combined in larger scripts.

Run a sample from the repository root after building the VM:

```powershell
dotnet .\dist\Debug\net10.0\CFGS_VM.dll .\CFGS_NE\Samples\01_basics\01_literals_variables_operators.cfs
```

Useful tooling commands:

```powershell
dotnet .\dist\Debug\net10.0\CFGS_VM.dll -check .\CFGS_NE\Samples\01_basics\01_literals_variables_operators.cfs
dotnet .\dist\Debug\net10.0\CFGS_VM.dll -lint .\CFGS_NE\Samples
dotnet .\dist\Debug\net10.0\CFGS_VM.dll -format .\CFGS_NE\Samples --check
```

The `.cfs` files contain detailed English comments next to the code they
demonstrate. The examples are intentionally small, practical, and self-checking:
each script throws if an important result is wrong.

Because they are self-checking, they double as a test suite. Run all of them with:

```powershell
pwsh CFGS_NE/Samples/run_all_samples.ps1
```

The runner executes each sample rather than only compiling it, since some mistakes
surface only at runtime, and it fails when a sample exits non-zero, prints a diagnostic,
or does not reach its end-of-run marker. It is also wired into
`scripts/run-all-tests.ps1` as a fourth suite. Files that other samples import are
detected automatically and only checked for clean initialization.

## Folders

- `01_basics`: literals, variables, constants, operators, type conversion.
- `02_control_flow`: conditionals, loops, `match`, patterns, errors.
- `03_functions`: functions, closures, defaults, named args, rest and spread.
- `04_oop`: classes, properties, visibility, inheritance, enums, interfaces.
- `05_namespaces_modules`: imports, exports, namespaces, and `use`.
- `06_async`: `async`, `await`, `yield`, tasks, `defer`, `using`.
- `07_standard_library`: standard namespaces, files, binary I/O, encoding.
- `08_plugins_interop`: official plugin namespaces.
- `09_embedding_tooling`: embedding-friendly scripts plus CLI, CFB, REPL notes.
