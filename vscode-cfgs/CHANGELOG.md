# Change Log

## 0.11.0 - 2026-09-23

### Changed
- Updated the bundled CFGS language server and CLI to v20.0.0.
- Namespaces are resolved entirely at compile time. Hover, go to definition, references, and completion now follow a namespace member through every form of reference: qualified, bare inside the namespace, shortened by `use`, and through a namespace import alias. References also find uses in other blocks and files of the same namespace.

### Breaking
- v20.0.0 makes a script namespace a compile-time construct. There is no namespace object at runtime, and every member has exactly one binding, which changes what scripts run through the lint and format commands report:
  - A namespace variable is one storage location. A write through the qualified name is visible inside the namespace, and a write inside the namespace is visible through the qualified name. Previously the namespace object held a copy taken when the declaration ran.
  - A member that the namespace does not declare is a compile error (`CFGS2001`) instead of a runtime error, in value positions and in type positions such as `new App.Core.Ghost()`.
  - Only a namespace variable can be assigned. Assigning to a namespace function, class, interface, or enum is `CFGS2007`.
  - A namespace import alias (`import * as X`) is a compile-time alias, not a value. `X` as a value is `CFGS2016`, `X["member"]` is `CFGS2003`, and an export the module does not have is `CFGS2001`. A local or class member of the same name no longer hides `X.member`.
  - A bare name inside a namespace now resolves against the whole namespace, across all of its blocks and files, and then against the enclosing namespaces, before global names.
  - A destructuring declaration in a namespace body, and a namespace root that collides with a native namespace root such as `math`, are rejected.
- The bytecode version moved from 8 to 9. Existing `.cfb` files are rejected with a clear version error and have to be recompiled.

## 0.10.0 - 2026-09-22

### Changed
- Updated the bundled CFGS language server and CLI to v19.0.0.
- Syntax highlighting: `#+` starts a block comment only when whitespace or the end of the line follows, matching the new lexer rule. `#+1 for this` is highlighted as a line comment.

### Breaking
- v19.0.0 changes several language behaviours, which affects scripts run through the lint and format commands and any `.cfb` artifact in a workspace:
  - A class field without an initializer starts as `null` instead of `0`.
  - `|` in a `match` value pattern separates or-pattern alternatives (`case 1 | 2:`). A bitwise-or value is written in parentheses, `(1 | 2)`.
  - `math.round` rounds half-way values away from zero, and `math.PI` and `math.E` are values instead of zero-argument methods.
  - A derived `init` is no longer held to the base `init` signature.
  - A class can render its instances through a public `toString()` method, which print, str, interpolation, format, and concatenation now call.
- The bytecode version moved from 7 to 8. Existing `.cfb` files are rejected with a clear version error and have to be recompiled.

### Fixed
- `break` or `continue` inside a closure, local function, or local-class method no longer passes the checker and then jumps into the enclosing loop at runtime; it is reported as a parse error. `await` inside a synchronous closure nested in an async function is reported too.
- A function or method named `this`, `type`, `super`, or `outer` is reported where it is declared instead of failing at runtime.
- `ok && f();`, `x ? a() : b();`, and other expression statements that start with an identifier parse.
- `var a = 1, b = 2;` keeps both declarators in a braceless `if` body, works in a `for` initializer, and works in a namespace body with the modifier applied to every name.
- A `const` instance field can be assigned in `init`.
- Trailing commas are accepted in array and dictionary literals, argument lists, and parameter lists; enum values may be negative.

### Added
- Lint rule `CFGS8023` warns when a dictionary key such as `{host: 1}` is a bare identifier that names no declared variable.

## 0.9.0 - 2026-08-17

### Changed
- Updated the bundled CFGS language server and CLI to v18.0.0. Editor behaviour does change this time: the language server used to keep its own copy of the diagnostic-code inference table, which had drifted from the runtime's, so the code shown in the editor could differ from the one the CLI reported for the same error. The server now uses the runtime's table, which is what the language specification requires of the editor path.
- Runtime diagnostic codes in the problems panel are more specific for scripts run through the lint and format commands. Division by zero now reports `CFGS3007`, a `null` operand in an arithmetic or bitwise operator `CFGS3012`, and a non-numeric operand `CFGS3011`. All three previously reported the generic `CFGS3000`, because the code was being dropped while the language stack trace was attached.
- Error messages no longer show internals. Indexing a non-indexable value named the bytecode opcode (`INDEX_GET`) and CLR type names (`System.String`); arithmetic errors named opcodes such as `ADD` and `MUL`. They now name the operator as written in source and the types as `typeof` reports them.
- The language this extension highlights and checks gained decimal literals (`1.5m`), `is` against built-in type names (`1 is Int`), and camelCase aliases for the four snake_case intrinsics (`insertAt`, `removeRange`, `replaceRange`, `getOr`).

### Breaking
- v18.0.0 changes two language behaviours, which affects scripts run through the lint and format commands and any `.cfb` artifact in a workspace. A three-part `for` now gives its loop variable a fresh binding per iteration, so closures created in the body capture that iteration's value rather than the final one. Text inside an array or dictionary is now quoted and escaped when printed, so `{"a": "1"}` and `{"a": 1}` are no longer indistinguishable.
- The bytecode version moved from 6 to 7. Existing `.cfb` files are rejected with a clear version error and have to be recompiled, because the loop change lives in the compiler and an older artifact would otherwise keep the old semantics silently.

## 0.8.8 - 2026-08-10

### Changed
- Updated the bundled CFGS language server and CLI to v17.0.1. This closes the remaining half of a loading gap in compiled `.cfb` artifacts: v17.0.0 stopped such an artifact from pointing the plugin loader at an arbitrary absolute or UNC path, but a native assembly reference inside the bytecode could still do so. Both are now reduced to a bare file name when the artifact is read. Editor behaviour is unchanged; this affects the runtime that the lint and format commands invoke.

## 0.8.7 - 2026-08-10

### Fixed
- Analysis of files containing many interpolated strings is markedly faster, which shows up as quicker diagnostics while typing. For every `{...}` hole the lexer copied the entire remainder of the file, so the cost grew with file size multiplied by hole count, and the whole document is re-lexed on every keystroke. Measured on a 156 KB file with 4000 holes, a full check went from 610 ms to 204 ms.

### Changed
- Updated the bundled CFGS language server and CLI to v17.0.0. Besides the change above this release hardens the runtime that the lint and format commands invoke: compiled `.cfb` artifacts can no longer point the plugin loader at an arbitrary absolute or UNC path, the workspace jail now also catches paths that leave the workspace through a symlink or junction, and deeply nested script callbacks report a normal runtime error instead of terminating the process.
- Note for anyone building their own CFGS plugins against the VM assembly: v17.0.0 removes the static `VM.AllowFileIO` property and `VM.PushAllowFileIOScope`. File I/O permission is now a per-VM property, read through `VM.CurrentVm?.AllowFileIO`.

## 0.8.6 - 2026-07-13

### Changed
- The entry script's top level is fully imperative in the updated language server: control flow (`if`, loops, `match`, `try`), `throw`, `using`, `delete`, and bare `await` statements are now valid directly at the top level of a directly executed script, so the editor no longer reports "invalid top-level statement" for them. Imported modules and namespace bodies stay declaration-only. Context-bound forms keep their diagnostics (`return`/`yield` need a function, `break`/`continue` need a loop, `defer` needs a block).

### Fixed
- The language server no longer freezes while typing on momentarily broken code. A parse attempt that failed on a token the error recovery did not step over (a stray `}`, or a keyword that is invalid at exactly that position, such as an `export` inside a function body) retried the identical parse forever. The server then spun at full CPU with growing memory, and diagnostics, hovers, and live lint stopped updating until a restart. Since an extra or missing brace is a normal intermediate state during editing, this could trigger on almost every editing session. Error recovery now always makes progress, and a regression test covers the previously hanging inputs.
- Statement error recovery itself can no longer abort the resilient parse: skipping garbage tokens now bypasses the reserved identifier validation, which previously could throw from inside the recovery path.
- Lint rule CFGS8013 (function is never called in this file) no longer flags functions whose calls appear before their declaration; forward calls are legal because declarations are hoisted.
- Lint rule CFGS8014 (async function does not await anything) no longer flags async functions whose only suspension point is `yield`, since `yield` requires an async function.

## 0.8.5 - 2026-07-10

### Fixed
- The language server no longer crashes on deeply nested source. A file with a very deep chain of nested parentheses, brackets, or blocks previously overflowed the parser's call stack, which terminated the whole server process (all diagnostics, hovers, and references disappeared until a restart). The parser now reports a clean "maximum nesting depth exceeded" diagnostic instead.

### Changed
- Updated the bundled CFGS language server to the current compiler. Editor diagnostics now reflect several language refinements: interface checks with `is` and the `implements` builtin are nominal (a new `implements_shape` builtin covers structural matches and is offered in completion); a namespace import alias (`import * as X`) is accessed with dot syntax only; an unguarded `case _:` acts as the match default; and an explicitly passed `null` no longer triggers a parameter default.

### Added
- New lint diagnostic CFGS8022 flags a char literal used in arithmetic or a bitwise operation, where it silently coerces to its Unicode code point (for example `'a' + 'b'`). It reports only literal operands, so it never fires on string concatenation, on a char combined with a variable, or on subtraction and comparisons.

## 0.8.4 - 2026-07-08

### Fixed
- The language server no longer stops responding after unexpected input. A single malformed protocol message or an edge case inside a request handler previously terminated the whole server process, so diagnostics and references silently disappeared until a restart. Message handling is now isolated per request, and any unexpected error is written to the "CFGS Language Server" output channel instead of killing the server.
- Duplicate reference CodeLens labels over file scoped namespaces. A namespace such as `namespace App.Model;` whose root segment was not already introduced by an earlier import produced a second overlapping "N references" label on the namespace line. Only one lens per declaration is emitted now.
- Reference CodeLens deduplication now collapses any lenses that begin at the same source position, keeping the widest range, so overlapping labels cannot stack regardless of their origin.

### Changed
- Imported files are now re watched: when a file changes on disk (saved, created, or changed outside the editor), open files that import it are re analyzed automatically. A stale "import path not found" no longer lingers after the imported file is created or fixed.
- A failed import no longer produces a confusing secondary "unknown statement type" compile diagnostic on top of the real cause (the missing path or unknown namespace).

## 0.8.2 - 2026-07-04

### Changed
- Syntax highlighting now matches the current language: the removed `out` expression block is no longer treated as a keyword (`out` highlights as an ordinary identifier).
- Keyword completion adds the `use` and `property` keywords, bringing the completion list in sync with the lexer.

## 0.8.1 - 2026-07-03

### Added
- Live linting: style and AST lint diagnostics now appear while typing, not only on save, controlled by the new `cfgs.lint.live` setting (default on). Changing the setting restarts the language server.

### Changed
- The language server no longer blocks on workspace-wide requests. Read-only requests such as references, rename, code lens resolution, workspace symbols, and call hierarchy run concurrently, so a slow reference scan no longer freezes hovers, diagnostics, or typing.
- The on-save lint CLI pass was removed from the extension because live linting now covers it; `cfgs.lint.fixOnSave` still applies safe `--fix` edits on save.

### Fixed
- Reference CodeLens no longer appears twice over symbols that share a source range (for example a class and its primary constructor).

## 0.8.3 - 2026-07-07

### Changed
- Updated the bundled CFGS language server to the current compiler. Enum members are now dot only: bracket access such as `Mode["On"]` is reported in the editor at compile time as error CFGS2003, with the squiggle placed on the bracket expression. This covers top level, namespaced, and class nested enums.
- The trailing whitespace style lint (CFGS8001) is now off in the recommended preset, so it no longer shows up by default while typing. It stays available in the strict preset, alongside the missing final newline lint (CFGS8005).

### Fixed
- Updated the TextMate grammar to recognize CFGS interpolated strings (`$"..."`), verbatim strings (`@"..."`), and combined verbatim interpolated prefixes (`$@"..."`, `@$"..."`).
- Added highlighting for explicit interface member declarations such as `func IFace.run(...)`.
- Added current declaration/accessor keywords like `native`, `get`, `set`, and `init`.
- Corrected the README package example to the current VSIX filename.

## 0.7.0 - 2026-06-26

### Added
- Reference CodeLens on all declared symbols (classes, interfaces, enums, functions, methods, fields, namespaces, and more). The count is computed across the whole workspace, including open files and other `.cfs` files on disk. Clicking the lens opens the reference peek so you can jump to each file and line.
- Precise semantic highlighting for user-defined types. Class, interface, enum, function, method, parameter, and namespace references are colored by their resolved kind through the language server (similar to C# in Visual Studio). Semantic token scope fallbacks are contributed so the colors also apply on themes without explicit semantic rules, and semantic highlighting is enabled by default for CFGS files.

### Changed
- Updated the bundled CFGS language server to the current compiler, including the new namespace semantics: a namespace is not a value, and using one as a value (for example `var ns = App.Core;`) is reported in the editor as error CFGS2016.
- Removed the heuristic grammar rule that colored every capitalized identifier as a type. Type coloring is now driven by the language server, so only real types are highlighted instead of every PascalCase word.

### Fixed
- Reference counts that previously stayed at "0 references" for symbols used from other files now reflect the actual cross-file usage.
