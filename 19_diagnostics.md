# Diagnostics and Error Codes

CFGS diagnostics are intended to be useful both for humans in the CLI and for tools such as the LSP, CI checks, and editors.

## Diagnostic Shape

A diagnostic should carry these fields when the information is available.

- `code`: stable diagnostic code such as `CFGS2003`.
- `category`: broad error category such as `CompileError` or `RuntimeError`.
- `message`: the original human-readable error message.
- `sourceFile`: source file or artifact that produced the error.
- `line` and `column`: 1-based source position.
- `spanLength`: length of the highlighted source range. Parser diagnostics prefer the offending token length when available.
- `help`: short fix hint when the runtime can infer one.
- `sourceSpan`: formatted source line and caret marker for CLI display.

The CLI text format keeps the category and message, but prefixes the stable code.

```text
CFGS2003: [CompileError] class static members are not indexable; use dot syntax for static members.
  at script.cfs:6:13
   |
6  |     var x = C["test1"];
   |             ^
= help: use dot syntax for class members, for example C.member.
```

## Check Mode

Use check mode to validate source or bytecode without executing user code.

```powershell
dotnet run --project CFGS_NE -- -check .\script.cfs
dotnet run --project CFGS_NE -- -check .\script.cfb
```

With `--json`, successful checks return an empty array.

```powershell
dotnet run --project CFGS_NE -- -check .\script.cfs --json
```

```json
[]
```

Failed checks return a JSON array of diagnostics.

```json
[
  {
    "code": "CFGS2003",
    "category": "CompileError",
    "message": "class static members are not indexable; use dot syntax for static members.",
    "sourceFile": "script.cfs",
    "line": 6,
    "column": 13,
    "spanLength": 1,
    "help": "use dot syntax for class members, for example C.member.",
    "sourceSpan": "   |\n6  |     var x = C[\"test1\"];\n   |             ^"
  }
]
```

## Code Ranges

- `CFGS0000`-`CFGS0999`: lexical diagnostics.
- `CFGS1000`-`CFGS1999`: parse diagnostics.
- `CFGS2000`-`CFGS2999`: compile and semantic diagnostics.
- `CFGS3000`-`CFGS3999`: runtime diagnostics.
- `CFGS4000`-`CFGS4999`: import, module, and plugin diagnostics.
- `CFGS5000`-`CFGS5999`: CFB and bytecode diagnostics.
- `CFGS9000`-`CFGS9999`: host and system diagnostics.

## Common Codes

- `CFGS0001`: lexical token or character error.
- `CFGS1000`: general parser error.
- `CFGS1001`: expected syntax element is missing.
- `CFGS1002`: duplicate parser-level declaration or binding.
- `CFGS2000`: general compile or semantic error.
- `CFGS2001`: unknown member or symbol.
- `CFGS2002`: inaccessible member.
- `CFGS2003`: dot access versus bracket access mismatch.
- `CFGS2004`: invalid member receiver.
- `CFGS2005`: duplicate semantic declaration.
- `CFGS2006`: invalid override.
- `CFGS2007`: invalid assignment target or read-only target.
- `CFGS2008`: static versus instance member access mismatch.
- `CFGS2009`: reserved member or reserved name misuse.
- `CFGS2010`: interface contract error.
- `CFGS2011`: constructor or base constructor error.
- `CFGS2012`: property, accessor, or backing-field error.
- `CFGS2013`: ambiguous semantic reference.
- `CFGS2014`: async, await, or yield semantic error.
- `CFGS2015`: argument, parameter, spread, rest, or default-value shape error.
- `CFGS2016`: namespace semantic error.
- `CFGS3000`: general runtime error.
- `CFGS3001`: undefined variable.
- `CFGS3002`: index operation error.
- `CFGS3003`: value is not indexable.
- `CFGS3004`: argument or call-shape error.
- `CFGS4001`: import resolution error.
- `CFGS5000`: CFB or bytecode artifact error.
- `CFGS9000`: host or system error.

## Suggestions

The compiler and runtime may append a `did you mean` hint when a nearby identifier exists. Current suggestions cover common member and variable mistakes, including misspelled class members and misspelled variables in runtime scopes.

Examples:

```text
unknown static member 'tset1' on class 'C'; did you mean 'test1'?
```

```text
undefined variable 'totalCout'; did you mean 'totalCount'?
```

Suggestions are best-effort. They should improve diagnostics without changing language semantics.

## LSP Behavior

The LSP diagnostic code should match the CLI code for equivalent lexer, parser, compiler, and host errors. Editor diagnostics should keep the same message category while exposing the stable `CFGSxxxx` code in the diagnostic payload.
