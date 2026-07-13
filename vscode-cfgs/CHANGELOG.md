# Change Log

## 0.8.6 - 2026-07-13

### Fixed
- The language server no longer freezes while typing on momentarily broken code. A parse attempt that failed on a token the error recovery did not step over (a stray `}`, or a keyword that is invalid at exactly that position, such as a top level `if` or an `export` inside a function body) retried the identical parse forever. The server then spun at full CPU with growing memory, and diagnostics, hovers, and live lint stopped updating until a restart. Since an extra or missing brace is a normal intermediate state during editing, this could trigger on almost every editing session. Error recovery now always makes progress, and a regression test covers the previously hanging inputs.
- Statement error recovery itself can no longer abort the resilient parse: skipping garbage tokens now bypasses the reserved identifier validation, which previously could throw from inside the recovery path.

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
