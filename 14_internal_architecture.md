# Internal Architecture

## Purpose

This document describes the productive CFGS architecture after the parser, compiler, and VM refactor. It is about code ownership and pipeline boundaries, not language syntax.

## End-to-End Pipeline

The productive execution path is now:

1. `Lexer` tokenizes source text.
2. `Parser` builds syntax and AST nodes.
3. `ImportResolver` and `SourceResolver` resolve imported modules and external sources.
4. `SyntaxLowerer` normalizes raw syntax sugar into the compiler-facing tree.
5. `Compiler` runs semantic/index passes and emits bytecode.
6. `VM` loads bytecode and executes it through `ExecutionEngine` plus focused runtime services.

The language server uses the same frontend and compiler path. There is no separate editor-only parser or semantic model.

## Folder Ownership

### `CFGS_NE/Analytic`

- `Core`
  - `Lexer`, `TokenCursor`, `Parser`, `ParserContext`
  - Owns syntax parsing and parser-local context rules.
- `Modules`
  - `ImportResolver`, `SourceResolver`
  - Owns file, HTTP, DLL, cache, and recursive import resolution.
- `Lowering`
  - `SyntaxLowerer`, `NamespaceLowerer`, `ParamLowerer`
  - Owns syntax sugar lowering after parse and before compile.
- `Semantics`
  - `SymbolIndex`, `TopLevelValidator`, `ClassCatalog`, `InterfaceCatalog`, `ClassSemanticValidator`, `MemberAccessRules`
  - Owns declaration indexing and semantic validation that must happen before bytecode emission.
- `Tree`
  - AST node definitions shared by parsing, lowering, semantics, and codegen.

### `CFGS_NE/VMCore/Codegen`

- `CompilationPipeline`
  - Orchestrates the compiler passes in order.
- `CompilationContext`
  - Holds compiler-wide state such as function tables, type maps, and emitted program state.
- `BytecodeEmitter`
  - Owns final program emission.
- `Emitters`
  - `StmtEmitter`, `ExprEmitter`, `PatternEmitter`, `ClassEmitter`, `FlowEmitter`, `CallEmitter`
  - Own statement, expression, pattern, class, flow, and call emission logic.

### `CFGS_NE/VMCore/Runtime`

- `VmState`
  - Owns mutable VM execution state.
- `ExecutionEngine`
  - Owns `Run`, `RunAsync`, resume handling, and the dispatch loop.
- `CallRuntime`
  - Owns closure calls, argument binding, builtins, intrinsics, and returns.
- `AsyncRuntime`
  - Owns `await`, `yield`, and hot-start async behavior.
- `CollectionOps`
  - Owns list, dictionary, slice, and mutable-lock behavior.
- `ObjectRuntime`
  - Owns instances, static types, member access, visibility, const checks, and lifecycle handling.
- `BindingRuntime`
  - Owns builtin and intrinsic registries plus plugin loading.
- `ValueOps`
  - Owns numeric operators, comparisons, and truthiness.
- `ExceptionRuntime`
  - Owns try-stack handling, throw routing, and language stack formatting.
- `ValuePrinter`
  - Owns value formatting, console printing, and JSON stringification.

## Compiler Passes

The compiler pipeline is intentionally pass-based:

1. Build the symbol index.
2. Validate top-level declarations.
3. Resolve class and interface graphs.
4. Run semantic rules for overrides, base constructors, visibility, and member access.
5. Emit bytecode.

Validation and emission are separate responsibilities. Semantic code should not emit instructions, and emitters should not introduce new semantic rules.

## Runtime Split

`VM.cs` still contains the public VM surface plus opcode dispatch plumbing, but the runtime behavior now lives in the runtime service files. The rule is simple:

- shared execution state belongs in `VmState`
- the dispatch loop belongs in `ExecutionEngine`
- opcode families belong in the matching runtime service

If a runtime change mostly touches arrays, dictionaries, or slicing, it belongs in `CollectionOps`, not in `VM.cs`. If it mostly touches member access or lifecycle rules, it belongs in `ObjectRuntime`.

## LSP Analysis Path

The language server intentionally reuses the same pipeline as normal compilation:

1. `LspServer` manages open-document overlays and workspace state.
2. `CfgsAnalyzer` builds analysis input from the current document plus overlay-backed imports.
3. `Lexer`, `Parser`, `SyntaxLowerer`, and `Compiler` run in the same order as the CLI/runtime path.
4. Diagnostics, symbols, hovers, definitions, references, rename, semantic tokens, and quick fixes are derived from that shared analysis result.

This keeps runtime and editor semantics aligned and avoids a second architecture that can drift.

## Architectural Guardrails

These boundaries should stay hard:

- The parser must not own HTTP, DLL, or filesystem import logic directly.
- Lowerers may reshape syntax trees, but they do not emit bytecode.
- Semantic validators may reject programs, but they do not perform runtime work.
- Emitters may translate validated AST into instructions, but they do not redefine visibility or type rules.
- Runtime services may execute bytecode, but they do not rebuild compiler semantics.
- LSP features must reuse the productive frontend and compiler path instead of maintaining an editor-only fork.

## Practical Entry Points

- CLI/runtime entry: `CFGS_NE/Program.cs`
- LSP analysis entry: `CFGS.Lsp/Analysis/CfgsAnalyzer.cs`
- LSP server entry: `CFGS.Lsp/LspServer.cs`
- Compiler pipeline entry: `CFGS_NE/VMCore/Codegen/CompilationPipeline.cs`
- Runtime loop entry: `CFGS_NE/VMCore/Runtime/ExecutionEngine.cs`
