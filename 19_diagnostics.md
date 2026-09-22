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
Lexer, parser, compiler, and runtime exceptions can carry an explicit diagnostic code. When no explicit code is supplied, CFGS falls back to category/message inference for older throw sites.

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
- `CFGS8000`-`CFGS8999`: linter, formatter, and source style diagnostics.
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
- `CFGS3005`: reserved and currently unused.
- `CFGS3006`: arithmetic overflow; numeric value outside the target type range. Ordinary
  script arithmetic widens instead of overflowing, so this reports a host call or an
  explicit conversion such as `byte(300)`.
- `CFGS3007`: division by zero, for both `/` and `%`.
- `CFGS3008`: invalid cast or conversion.
- `CFGS3009`: invalid numeric format.
- `CFGS3010`: dictionary key not found.
- `CFGS3011`: numeric operation error, such as an arithmetic operator on a non-numeric value.
- `CFGS3012`: null reference, including a `null` operand in an arithmetic or bitwise operator.
- `CFGS4001`: import resolution error.
- `CFGS5000`: CFB or bytecode artifact error.
- `CFGS8000`: tooling input error.
- `CFGS8001`: trailing whitespace. Strict preset only; off in the recommended preset.
- `CFGS8002`: tab indentation warning.
- `CFGS8003`: line length warning.
- `CFGS8004`: excessive blank lines.
- `CFGS8005`: missing final newline. Strict preset only; off in the recommended preset.
- `CFGS8011`: unused local variable or parameter.
- `CFGS8012`: unreachable statement after terminating control flow.
- `CFGS8013`: top-level function is never called in the same file.
- `CFGS8014`: async function does not await anything.
- `CFGS8015`: local, parameter, or import shadows an outer declaration.
- `CFGS8016`: imported binding is never used.
- `CFGS8017`: empty control-flow block.
- `CFGS8018`: suspicious equality comparison with an always-known outcome.
- `CFGS8019`: constant condition. Strict preset only; off in the recommended preset.
- `CFGS8020`: unreachable statement after guaranteed terminating control flow.
- `CFGS8021`: magic number. Strict preset only; off in the recommended preset.
- `CFGS8022`: a char literal is used in arithmetic or a bitwise operation, where it silently coerces to its Unicode code point. Reported only when both operands are literals, so it never fires on string concatenation or on a char combined with a variable; subtraction and comparisons are exempt because those are the idiomatic char patterns.
- `CFGS8023`: a dictionary literal key is a bare identifier that names no declared variable, as in `{host: 1}`. Dictionary keys are expressions, so the key reads a variable; the author almost always meant the string key `"host"`. The rule stays silent when the file has a bare import or a `use` directive, which can bring in names the linter cannot see, and inside class members, where a bare name may be an implicit member access.
- `CFGS9000`: host or system error.
- `CFGS9100`: operation canceled.

## Lint Mode

Lint mode validates `.cfs` files and emits compiler diagnostics plus source style diagnostics.

```powershell
dotnet run --project CFGS_NE -- -lint .\script.cfs
dotnet run --project CFGS_NE -- -lint .\src --json
dotnet run --project CFGS_NE -- -lint .\src --fix
```

Text output uses this shape.

```text
script.cfs:6:1: warning CFGS8004: more than two consecutive blank lines
[LINT] files=1, errors=0, warnings=1, info=0
```

JSON output uses a compact diagnostic array.

```json
[
  {
    "code": "CFGS8004",
    "severity": "warning",
    "message": "more than two consecutive blank lines",
    "source": "script.cfs",
    "line": 6,
    "column": 1,
    "spanLength": 1
  }
]
```

Warnings and errors return a non-zero process exit code. Informational diagnostics alone do not fail the command.

`--fix` applies only safe source-style fixes and then lints the updated source again. It can remove trailing whitespace, collapse excessive blank lines, add a missing final newline, and convert leading tabs to spaces unless `--allow-tabs` is set. Semantic and policy diagnostics such as `CFGS8011`-`CFGS8023` are reported but are not auto-fixed.

Lint options may be set in the nearest `cfgs.json`. Existing flat keys under `tooling` remain valid. The richer rule configuration lives under `tooling.lint`, and those nested values override the flat defaults from the same manifest.

```json
{
  "tooling": {
    "maxLineLength": 140,
    "warningsAsErrors": false,
    "allowTabs": false,
    "indentSize": 4,
    "lint": {
      "preset": "recommended",
      "rules": {
        "CFGS8018": "error",
        "CFGS8021": "off"
      }
    }
  }
}
```

`preset` supports `recommended` and `strict`. `rules` accepts booleans, severity strings (`info`, `warning`, `error`, `off`), or objects with `enabled` and `severity`.

The CLI options `--max-line-length`, `--warnings-as-errors`, `--allow-tabs`, and `--indent` override the manifest defaults.

Lint suppressions may be attached inline:

```cfs
# cfgs-disable CFGS8016
# cfgs-disable-next-line CFGS8018
var value = 42; # cfgs-disable-line CFGS8021
```

The legacy `# lint:disable`, `# lint:disable-next-line`, and `# lint:disable-line` forms remain supported for backward compatibility.

## Format Mode

Format mode validates the file first, then normalizes indentation. It does not execute user code.

```powershell
dotnet run --project CFGS_NE -- -format .\script.cfs
dotnet run --project CFGS_NE -- -format .\script.cfs --write
dotnet run --project CFGS_NE -- -format .\src --check
```

`--write` updates files in place. `--check` returns a non-zero exit code when a file would change. `--json` emits per-file formatter results for tooling.

Formatter defaults may also be set in `cfgs.json`:

```json
{
  "tooling": {
    "indentSize": 4,
    "useTabs": false
  }
}
```

The CLI options `--indent`, `--tabs`, and `--spaces` override those defaults.

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
