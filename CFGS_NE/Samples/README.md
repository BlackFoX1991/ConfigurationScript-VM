# CFGS Samples

This folder contains runnable sample scripts for the current CFGS language and
runtime feature set. The samples are organized by topic so each feature can be
studied in isolation and then combined in larger scripts.

With a ready-to-run `CFGS_VM.exe`, open a terminal in the folder containing these samples. Run a sample like this:

```text
CFGS_VM.exe 01_basics/01_literals_variables_operators.cfs
```

Useful tooling commands:

```text
CFGS_VM.exe -check 01_basics/01_literals_variables_operators.cfs
CFGS_VM.exe -lint 01_basics
CFGS_VM.exe -format 01_basics --check
```

The `.cfs` files contain detailed English comments next to the code they
demonstrate. The examples are intentionally small, practical, and self-checking:
each script throws if an important result is wrong.

Each sample checks its own expected results and reports an error if a check fails. You can open any `.cfs` file to read its comments and then run it with `CFGS_VM.exe`.

## Folders

- `01_basics`: literals, variables, constants, operators, type conversion.
- `02_control_flow`: conditionals, loops, `match`, patterns, errors.
- `03_functions`: functions, closures, defaults, named args, rest and spread.
- `04_oop`: classes, properties, visibility, inheritance, enums, interfaces.
- `05_namespaces_modules`: imports, exports, namespaces, and `use`.
- `06_async`: `async`, `await`, `yield`, tasks, `defer`, `using`.
- `07_standard_library`: standard namespaces, files, binary I/O, encoding.
- `08_plugins_interop`: official plugin namespaces, including UDP loopback.
- `09_embedding_tooling`: embedding-friendly scripts plus CLI, CFB, REPL notes.
