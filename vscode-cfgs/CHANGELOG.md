# Change Log

## 0.7.0 - 2026-06-26

### Added
- Reference CodeLens on all declared symbols (classes, interfaces, enums, functions, methods, fields, namespaces, and more). The count is computed across the whole workspace, including open files and other `.cfs` files on disk. Clicking the lens opens the reference peek so you can jump to each file and line.
- Precise semantic highlighting for user-defined types. Class, interface, enum, function, method, parameter, and namespace references are colored by their resolved kind through the language server (similar to C# in Visual Studio). Semantic token scope fallbacks are contributed so the colors also apply on themes without explicit semantic rules, and semantic highlighting is enabled by default for CFGS files.

### Changed
- Updated the bundled CFGS language server to the current compiler, including the new namespace semantics: a namespace is not a value, and using one as a value (for example `var ns = App.Core;`) is reported in the editor as error CFGS2016.
- Removed the heuristic grammar rule that colored every capitalized identifier as a type. Type coloring is now driven by the language server, so only real types are highlighted instead of every PascalCase word.

### Fixed
- Reference counts that previously stayed at "0 references" for symbols used from other files now reflect the actual cross-file usage.
