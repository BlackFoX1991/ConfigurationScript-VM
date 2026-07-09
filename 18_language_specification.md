# CFGS Language Specification

## Status and Scope

This document defines the intended behavior of the CFGS language as implemented by the current lexer, parser, compiler, bytecode VM, CLI, and LSP pipeline.

The tutorial documentation explains how to use CFGS. This specification defines what CFGS means. It is written as a contract for language users, compiler work, VM work, LSP behavior, formatter and linter work, and future compatibility decisions.

The normative words in this document have the following meaning:

- **MUST** means the implementation is required to behave this way.
- **MUST NOT** means the implementation is required to reject or prevent this behavior.
- **SHOULD** means the behavior is expected unless a documented compatibility reason applies.
- **MAY** means the behavior is allowed but not required in every host or tool.

## Processing Model

A CFGS source file is processed in this order:

1. Source text is tokenized by the lexer.
2. Tokens are parsed into an AST.
3. Imports and external sources are resolved.
4. Syntax lowering normalizes parser-level sugar into compiler-facing AST shapes.
5. Semantic passes index declarations and validate language rules.
6. Bytecode is emitted.
7. The VM executes bytecode.

The LSP analysis path MUST reuse the same frontend and compiler semantics as normal execution. Editor diagnostics MUST NOT rely on a second incompatible parser or semantic model.

## Source Text and Lexical Rules

Source files are plain text CFGS programs, normally stored with the `.cfs` extension.

The lexer recognizes these token families:

- identifiers
- keywords
- numeric literals
- string literals
- character literals
- operators
- punctuation
- comments
- end of file

Numeric literals may be written in decimal, hexadecimal (`0x`), binary (`0b`), or octal (`0o`) form, with optional `_` digit separators. Non-decimal literals are interpreted as unsigned magnitudes and promote to `BigInteger` when they exceed 64 bits; they never carry a two's-complement sign.

String literals may be plain (`"..."`), verbatim (`@"..."`, where backslashes are literal and `""` denotes a single quote), interpolated (`$"..."`, where `{expr}` embeds an expression and `{{`/`}}` denote literal braces), or both (`$@"..."` and `@$"..."`). An interpolation hole MAY carry a `,alignment` and a `:format` specifier following .NET composite formatting. Because `:` begins the format specifier, a conditional expression inside an interpolation hole MUST be parenthesized.

Operators are matched with longest-match precedence. In particular the three-character merge operators `<<<` and `>>>` are recognized before the two-character shift operators `<<` and `>>`.

Whitespace separates tokens where needed and otherwise has no semantic meaning, except inside string and character literals.

Comments are ignored by later language phases and MUST NOT affect runtime behavior.

Identifiers name variables, constants, functions, classes, namespaces, imports, fields, methods, properties, enum members, parameters, and pattern bindings.

Keywords are reserved by the language and MUST NOT be used as ordinary identifiers where an identifier is required. Contextual language constructs such as receiver names have additional restrictions described below.

## Program Structure

A CFGS program is a sequence of top-level statements and declarations.

Directly executed root scripts MAY contain compatibility-style executable top-level statements. Imported modules and namespace bodies are declaration-oriented and MUST reject free imperative statements that belong inside functions or methods.

Top-level declaration forms include:

- imports
- exports
- namespace declarations
- function declarations
- class declarations
- interface declarations
- enum declarations
- variable declarations
- constant declarations

CFGS has no auto-main. After module initialization the CLI runs the script's top-level statements; a `main` function is executed only if the script calls it explicitly (for example `main();`, or `var _ = await main();` for an async `main`).

## Values and Runtime Types

CFGS runtime values include:

- `null`
- booleans
- numeric values
- strings
- characters
- arrays
- dictionaries
- closures
- class instances
- static class objects
- namespace values
- enum objects and enum values
- exception objects
- task and awaitable values
- plugin-provided host objects
- native CLR host objects

`null` represents the absence of a value. It is distinct from `false`, `0`, an empty string, an empty array, and an empty dictionary.

Arrays are ordered, integer-indexed, mutable sequences.

Dictionaries are string-keyed mutable maps. Bracket access on dictionaries is key lookup or key assignment.

Strings are immutable text values. Indexing and slicing MAY read strings, but assigning into a string MUST fail.

Class instances and static class objects are not dictionaries. Their members MUST be accessed with dot syntax, not bracket syntax.

## Variables, Constants, and Scopes

`var` introduces a mutable binding.

`const` introduces a binding that MUST NOT be reassigned after initialization.

A declaration introduces its binding into the current scope. Redeclaring the same name in the same scope MUST fail.

Nested scopes MAY shadow names from outer scopes when the language construct allows local declarations.

Name lookup resolves the nearest visible binding first. If a local binding exists, it wins over outer scopes and imported or namespace-level names.

Assignment resolves an existing mutable binding. Assignment to an undeclared variable MUST fail. Assignment to a constant binding MUST fail.

Function calls create a call scope containing parameters and local bindings. Blocks, loops, functions, methods, namespaces, and lowered constructs MAY introduce additional scopes according to their implementation-defined lowering, but visible behavior MUST follow lexical scoping rules.

## Expressions

Expressions produce runtime values. Expression evaluation is generally left-to-right except where an operator or control construct defines short-circuit behavior.

Supported expression categories include:

- literals
- variable references
- unary expressions
- binary expressions
- ternary expressions
- null coalescing expressions
- function calls
- method calls
- function expressions
- `new` object construction
- array and dictionary literals
- indexing
- slicing
- member access
- `match` expressions
- `await` expressions where permitted

Short-circuit logical operators MUST avoid evaluating the right operand when the left operand determines the result.

The null coalescing operator MUST evaluate and return the right operand only when the left operand is `null`.

## Operators

Arithmetic operators operate on numeric values unless a documented overload exists.

The `+` operator is overloaded by operand type, evaluated in this order:

1. If both operands are numeric, they add.
2. Otherwise, if at least one operand is a string, both operands are converted to their textual form and concatenated.
3. Otherwise, if both operands are arrays, the result is a new array containing the left operand's elements followed by the right operand's elements. The operands are not modified.
4. Any other operand combination MUST fail with a runtime error.

A char operand in any arithmetic, comparison, or equality operation coerces to its Unicode code point. This applies uniformly, including to digit characters, so `'5'` evaluates as 53.

Any arithmetic or bitwise operator (`+`, `-`, `*`, `/`, `%`, `**`, `&`, `|`, `^`, `<<`, `>>`) applied to a `null` operand MUST fail with a runtime error rather than producing `null`.

The dictionary merge operators `<<<` and `>>>` require two dictionaries and produce a new dictionary; the operands are not modified. `<<<` keeps the left operand's value on a key conflict, `>>>` keeps the right operand's value. The result preserves the left operand's key order and appends keys that occur only on the right. `a <<< b` is equivalent to `b >>> a`. These operators are left-associative and bind between the bitwise-and level and the shift level, so they are looser than `<<` and `>>` but tighter than `&`.

Comparison operators return boolean values. Comparison operators MUST NOT be chained: an
expression such as `a < b < c` is a parse error. Use parentheses to express a deliberate
comparison of a comparison result, e.g. `(a < b) < c`.

The null coalescing operator `??` binds more tightly than the ternary conditional `?:`.
Therefore `x ?? y ? a : b` parses as `(x ?? y) ? a : b`.

Logical operators use CFGS truthiness rules.

Bitwise operators require integral-compatible numeric values.

Invalid operand combinations MUST fail with a diagnostic or runtime error.

Compound assignments behave as an operation followed by assignment to the original target. The target MUST be assignable.

Increment and decrement require assignable numeric targets.

## Truthiness

`false` and `null` are falsey.

Numeric zero values are falsey.

Empty strings, empty arrays, and empty dictionaries are falsey.

Other non-null values are truthy unless a host object defines a documented runtime conversion.

## Collections and Indexing

Array indexing requires an integer index. Non-integer array indexes MUST fail.

Array indexes are zero-based. Out-of-range indexes MUST fail.

Dictionary data is accessed with bracket indexing and requires a string-compatible key. Any non-empty string is a valid data key; intrinsic method names are NOT reserved, because dictionary data and dictionary methods are separated by syntax (see Dot Member Access). Dot access on a dictionary resolves an intrinsic method only and MUST NOT read or write a data key.

Reading a dictionary key that is absent MUST return `null` rather than fail, which is what makes the `??` fallback idiom work. Deleting an absent key MUST fail with a runtime error.

Slicing is supported for arrays and strings, and dictionary slice behavior is defined by the runtime collection rules.

Push syntax appends to arrays or writes supported collection targets. Invalid push targets MUST fail.

`delete` clears the entire array or dictionary named by a bare variable, removes an element by index, removes a range by slice, or removes a dictionary entry by bracket key, only where the target form allows deletion. It does not unbind the variable itself, and a dictionary entry MUST be removed through bracket access rather than dot access.

Bracket access is collection/index access. It MUST NOT be treated as class member access.

## Dot Member Access

Dot access is used for object, static class, enum, namespace, property, method, and intrinsic member lookup.

Dot access on a dictionary resolves only its intrinsic methods (for example `cfg.merge(other)`); it MUST NOT read or write a data key. Dictionary data keys are addressed exclusively with bracket access (`cfg["host"]`). Namespaces are a distinct value kind, not dictionaries, and expose their members through member access.

Examples:

```cfs
C.test
obj.value
text.length()
Namespace.Type
```

Class and static members MUST be accessed through dot syntax.

The following form MUST NOT be treated as static member lookup:

```cfs
C["test"]
```

For class/static targets, bracket access MUST fail because class instances and static class objects are not indexable dictionaries.

Constructors are invoked with `new Type(...)` or through supported callable type behavior. Constructors are not exposed as a public `"new"` dictionary member.

## Functions and Calls

A function declaration introduces a named callable binding.

Function expressions produce closures. Closures capture their surrounding environment.

Parameters are bound at call time.

Call arguments may be:

- positional
- named
- spread

Positional arguments MUST NOT follow named arguments.

Named arguments MUST match parameters unless the callee explicitly supports a rest or host-specific argument contract.

Duplicate named arguments MUST fail.

Default parameters are applied when the corresponding argument is omitted at the call site. An explicitly passed `null` is a supplied value and MUST NOT trigger the default. The two cases are distinguished by parameter presence, not by comparing the parameter to `null`, so a caller can pass `null` as a meaningful argument to an optional parameter.

Rest parameters collect remaining positional arguments into an array and MUST appear last.

Spread arguments expand arrays into positional arguments. Spreading non-array values MUST fail.

Return without a value returns `null`.

Calling a non-callable value MUST fail.

## Classes and Objects

A class declaration defines a static class object and a runtime instance shape.

Instances are created with `new ClassName(...)`.

Each simple parameter in the class header (including the rest parameter) MUST be promoted to a public instance field of the same name, initialized from the corresponding constructor argument. A destructured header parameter MUST NOT be promoted, though its bindings remain available to field initializers, property initializers, and the base constructor call. Promotion happens before any explicit field initializer of the same name, so where both exist the parameter value is the one observed after construction.

If a class defines an `init` method, object construction calls `init` after allocating the instance and after header-parameter promotion. Constructor parameters and `init` parameters MUST follow the class constructor rules documented for classes.

Instance members belong to instances. Static members belong to the static class object.

`this` is available only in instance member contexts.

`type` is available in member contexts and refers to the current static class object.

`super` is available only where a base class exists and the member context allows it.

`outer` is available only for nested instance contexts that have an outer instance.

Using receiver names outside their valid context MUST fail.

Class runtime-reserved members such as `__type`, `__base`, and constructor backing slots MUST NOT be assigned from user code.

## Fields, Methods, and Properties

Fields store values on instances or static class objects.

Methods are callable members. Instance methods receive an implicit instance receiver. Static methods receive a static receiver.

Properties are member declarations with accessors.

Property access through dot syntax invokes the appropriate accessor when applicable.

Property setters and init accessors MUST obey visibility and assignment rules.

The `field` backing symbol is valid only inside property accessors that have hidden backing storage. Using `field` outside that context MUST fail.

## Visibility

Supported visibility modifiers include:

- `public`
- `private`
- `protected`

Public members are accessible wherever the target is visible.

Private members are accessible only from within the declaring class context.

Protected members are accessible from the declaring class and derived class contexts according to class hierarchy rules.

Visibility applies to both instance and static members.

Invalid visibility access MUST fail before or during runtime member resolution.

## Inheritance and Overrides

A class may extend a base class.

Self-inheritance and invalid inheritance cycles MUST fail.

Derived classes inherit valid base members according to visibility rules.

Overrides MUST match the overridden member kind. An instance member cannot override a static member, and a field cannot override a method or property.

Override signatures MUST be compatible with the base member signature.

Overrides MUST NOT narrow visibility in a way forbidden by the semantic rules.

Base constructor calls MUST satisfy the base constructor signature, including positional, named, default, rest, and spread rules.

## Nested Classes and Enums

Classes may contain nested classes and enums where the grammar and semantic rules allow them.

Nested instance construction may bind an outer instance.

Nested static access does not imply an outer instance.

Accessing `outer` in a static nested context MUST fail.

## Interfaces

Interfaces define required member contracts.

Classes that implement interfaces MUST satisfy the required members with compatible visibility and member kinds.

Interface method contracts MAY be instance or static and MAY include default bodies.

If a class implements an interface method contract and does not declare or inherit that method, a default interface method body MUST be made available on the implementing class.

Interface property contracts MAY be instance or static and define required `get`, `set`, and `init` accessors.

Interface indexer contracts use `index get(...)` and `index set(...)`. Implementing classes MUST provide compatible public instance methods named `get_index` and `set_index`; runtime index access on class instances dispatches to those methods when present.

Explicit interface method implementations use an interface-qualified method declaration such as `func IFace.member(...) { ... }`. Implementations MUST satisfy the same signature and visibility rules as normal interface methods. `interface_call(value, interfaceType, methodName, args)` MUST prefer explicit implementations before falling back to normal methods.

The `is` operator and the `implements` builtin are nominal for interface targets: they MUST accept a declared implementation (direct, through a base class, or through interface inheritance) and MUST NOT accept a value that merely exposes compatible members. Structural, shape-based satisfaction MUST be requested explicitly through the `implements_shape` builtin, which accepts both nominal implementations and values whose public members match the interface. `interface_call` MUST gate on the same nominal check as `is`.

The builtins `interfaces(value)`, `implements(value, interfaceType)`, `implements_shape(value, interfaceType)`, `interfaceof(interfaceType)`, and `interface_call(value, interfaceType, methodName, args)` expose interface reflection and dispatch support. `implements` is nominal (equivalent to `value is interfaceType`); `implements_shape` additionally accepts structural matches.

Interfaces MUST NOT extend classes.

Interface diagnostics SHOULD identify the missing or incompatible member.

## Enums

Enum declarations define named integer-like values.

Duplicate enum values or invalid enum shapes MUST fail according to enum validation rules.

Enum members are accessed through dot syntax. Bracket access such as `Mode["Safe"]` MUST fail, because an enum is not an indexable value. For a statically known enum this MUST be reported at compile time.

Assigning to enum members MUST fail.

## Namespaces and Name Resolution

Namespaces group declarations under qualified names.

Namespace declarations with the same qualified path may merge when allowed by the namespace rules.

Namespace body declarations MAY use `public`, `private`, or `protected` visibility modifiers.

Namespace members are resolved through qualified dot paths.

Known namespace members MUST use dot access and MUST NOT rely on bracket access as namespace lookup.

Namespace visibility is defined relative to the declaring namespace path:

- `public` members are visible everywhere,
- `private` members are visible only inside the same namespace path,
- `protected` members are visible inside the same namespace path and descendant namespace paths.

Namespace value objects MUST expose only public members. Accessible non-public members are resolved through statically known namespace references inside allowed namespace contexts.

`use namespace` imports visible namespace members into resolution scope.

Ambiguous `use namespace` references MUST fail.

Unknown namespace references MUST fail.

File-scoped namespace declarations MUST appear where the header rules allow them.

## Modules, Imports, and Exports

Imports are header-oriented. Imports in invalid positions MUST fail.

CFGS supports source imports, module imports, named imports, alias imports, namespace imports, URL imports, DLL plugin imports, and native .NET imports where enabled by the host.

A namespace import alias (`import * as Alias from ...`) introduces a namespace value. Its members MUST be accessed through dot member syntax (`Alias.member`), and bracket access on the alias MUST fail, the same rule that applies to declared namespaces.

Import resolution searches documented locations in order:

1. the script directory
2. the nearest restored `.cfgs/plugins` directory from the script directory
3. the current working directory
4. the nearest restored `.cfgs/plugins` directory from the current working directory
5. the directory of the running CLI executable
6. the `plugins` directory below the running CLI executable

The process `PATH` environment variable is not consulted for import resolution.

Exported declarations define the public import surface of a module.

Importing hidden or non-exported symbols MUST fail.

Import cycles MUST fail unless explicitly supported by a future documented module initialization model.

Imported modules are declaration-oriented and MUST NOT execute arbitrary imperative top-level code.

Native .NET imports use the form `import native "path.dll" as Alias;`. The alias introduces a native assembly root. Native assembly roots MUST use dot access for namespace, type, constructor, static member, and instance member traversal. Bracket access MUST NOT be interpreted as native member lookup.

## Plugins and Host Integration

DLL imports load plugins through the VM plugin loader.

Plugins MAY register native CFGS namespace roots, builtins, and intrinsics.

New plugin APIs SHOULD prefer namespaced members for public script-facing functions. Native namespace roots are accessed through dot syntax and MAY participate in header-only `use namespace` resolution when the host exposes them through the namespace registry.

Plugin registration errors MUST fail clearly.

Plugin-provided values MUST obey the VM call and await contracts when exposed to CFGS code.

Host integrations such as file I/O, HTTP, SQL, XML, PDF, crypto, and custom plugins are outside the core syntax, but their exposed namespace members, builtins, and intrinsics MUST follow the same runtime value and error conventions.

Native .NET interop MAY expose public CLR types, constructors, fields, properties, methods, indexers, delegates, and events directly. Hosts that support this feature MUST keep it separate from plugin registration: loading a native interop assembly MUST NOT imply automatic builtin or intrinsic registration unless the same assembly is also loaded through the plugin import path.

## Async, Await, and Yield

`async func` defines an asynchronous function.

`await` is valid only in async contexts and allowed expression positions.

Awaiting plugin-provided `Task` or `ValueTask` values is supported when the host value follows the await adapter contract.

Awaiting arrays and dictionaries recursively awaits contained awaitable values according to runtime await rules.

`yield` is valid only in async function contexts where the language permits it.

Invalid `await` or `yield` positions MUST fail.

Async calls may hot-start according to the VM async model. Observable ordering MUST follow the documented async behavior.

### Shared-State Memory Model

The runtime shares one mutable heap across the root execution context and every hot-started async call: closure environments, globals, class instances, arrays, and dictionaries are reachable by reference from concurrently pending async calls. To keep that shared heap consistent, bytecode execution is serialized: at any instant at most one execution context runs bytecode, while genuine I/O awaits run outside the serialization boundary. Awaiting an array of hot-started calls still overlaps their I/O, but their bytecode segments resume one at a time.

As a consequence:

- Concurrent structural mutation of a shared array or dictionary from parallel async calls MUST NOT corrupt the collection or fault the runtime.
- A read-modify-write across an `await` or `yield` boundary is still subject to interleaving: another async call MAY observe or change the shared value between the read and the write. Programs that require an indivisible update across a suspension point MUST serialize it explicitly (for example by not suspending between the read and the write).

The serialization boundary does not add CPU parallelism; a bytecode interpreter over a shared heap gains none. It preserves I/O concurrency while removing the data-race class on shared script state.

## Error Handling

`try`, `catch`, and `finally` provide structured error handling.

`throw` raises a runtime error payload or value.

Errors route to the nearest applicable handler.

`finally` blocks MUST run according to structured control flow rules when leaving a protected block.

Errors that are not handled MUST report source location and language stack information when available.

The implementation distinguishes at least:

- lexer errors
- parser errors
- compile/semantic errors
- runtime errors
- system or host errors

Diagnostics SHOULD include file, line, column, and a clear message.

Diagnostic output SHOULD also include a stable diagnostic code, a source span when source text is available, and a short help hint when the implementation can infer a useful fix.

Parser diagnostics SHOULD prefer the offending token span over a one-character fallback when the parser still has access to the current token.

Diagnostic code ranges are:

- `CFGS0000`-`CFGS0999` for lexical diagnostics
- `CFGS1000`-`CFGS1999` for parse diagnostics
- `CFGS2000`-`CFGS2999` for compile and semantic diagnostics
- `CFGS3000`-`CFGS3999` for runtime diagnostics
- `CFGS4000`-`CFGS4999` for import, module, and plugin diagnostics
- `CFGS5000`-`CFGS5999` for CFB and bytecode diagnostics
- `CFGS8000`-`CFGS8999` for linter, formatter, and source style diagnostics
- `CFGS9000`-`CFGS9999` for host/system diagnostics

CLI diagnostics SHOULD preserve the original category and message while prefixing the stable diagnostic code.

## Match and Patterns

`match` statements and expressions compare a subject against ordered patterns.

The first matching arm is selected.

Default arms match when no earlier arm matches. The default is expressed by `default:` in a statement and by an unguarded `_` arm in an expression. An unguarded wildcard `case _:` in a statement is equivalent to `default:`: it is a default arm, not a positional arm, and is order-independent. A guarded wildcard (`_ if (...)`) is an ordinary arm in either form.

Multiple defaults in the same match construct MUST fail. This includes combining `default:` with an unguarded `case _:`, and any repetition of the unguarded wildcard default.

Pattern variable bindings introduce names in the arm scope.

Duplicate bindings in one pattern MUST fail.

Guards are evaluated only after the structural pattern matches.

## Destructuring

Destructuring may appear in declarations, assignments, parameters, and foreach forms where the grammar allows it.

Destructuring targets MUST be valid assignable or bindable forms.

Invalid destructuring targets MUST fail.

Duplicate bindings in a destructuring pattern MUST fail.

Constant destructuring declarations require an initializer.

## Control Flow

Supported control flow includes:

- `if` and `else`
- `while`
- `do while`
- `for`
- `foreach`
- `break`
- `continue`
- `return`
- `try`, `catch`, `finally`
- `throw`

`break` and `continue` are valid only inside loops.

`return` is valid only inside function or method bodies.

Invalid control-flow positions MUST fail.

## CLI Execution Semantics

The CLI may execute source `.cfs` files or compiled `.cfb` files.

When running a script file, the CLI sets the script working directory to the directory of that script.

The host MAY support a project manifest named `cfgs.json` and a lockfile named `cfgs.lock.json`. A conforming local package implementation MUST keep restored plugin artifacts under a project-local package directory such as `.cfgs/plugins`, and DLL import resolution MAY search that directory for bare plugin file names.

Check mode is enabled with `-check`, `--check`, or `check`. It validates a source `.cfs` file by compiling it and validates a `.cfb` file by loading the binary artifact. Check mode MUST NOT execute the script.

When check mode is combined with `--json`, successful checks emit an empty JSON diagnostic array and failed checks emit a JSON diagnostic array.

Lint mode is enabled with `-lint`, `--lint`, or `lint`. It validates `.cfs` source files without executing them and MAY add source style diagnostics such as trailing whitespace, indentation, blank-line, final-newline, and line-length diagnostics. It MAY also add conservative AST diagnostics such as unused locals, unreachable statements, top-level functions that are never called in the same file, and async functions without await expressions. Lint mode MAY accept a file or a directory. Directory linting discovers `.cfs` files recursively while ignoring common build output directories.

Lint fix mode is enabled with `--fix`. It MUST only apply safe source-style changes that do not require semantic rewrites, such as trimming trailing whitespace, normalizing excessive blank lines, converting leading tabs according to the configured indentation policy, and adding a final newline.

Format mode is enabled with `-format`, `--format`, or `format`. It validates `.cfs` source files before applying formatting. The formatter MUST NOT execute user code. A conforming formatter MAY start with indentation and spacing normalization and expand toward deeper AST-aware formatting over time. `--check` MUST report whether formatting would change files without writing them.

Tooling defaults MAY be read from the nearest `cfgs.json` `tooling` object. Supported defaults include line length, warning promotion, tab policy, indentation size, and formatter tab usage. Explicit CLI flags MUST override manifest defaults.

The REPL evaluates submitted snippets in a shared VM session. It preserves global variable state and plugin registrations across snippets while replacing snippet-local bytecode and function declarations for each submission.

REPL commands MUST use the `$` prefix so command words cannot collide with normal script input. Examples include `$help`, `$exit`, and `$debug`.

The REPL does not auto-invoke `main`; a snippet must call `main()` explicitly, like any other script.

REPL plugin autoloading is active only in REPL mode and reads `repl_plugins.json` from the application path.

Debug log mode is enabled with `-d` or `-debug`.

Interactive VM debugging is enabled with `-di`, `-debugger`, or `--debugger`.

Windows service hosting is a Windows-only facility. `-as_service <script>` runs a script under the Windows Service Control Manager: the script starts when the service starts and observes a cancellation signal when the service stops, so a long-running script terminates cooperatively. `-as_service` MUST be started by the service control manager and not from an interactive console. `-install_service NAME -script FILE` registers such a service and `-uninstall_service NAME` removes it; both delegate to the platform service tooling and require administrator rights. Service lifecycle events and unhandled script errors are reported to the Windows Event Log because a service has no console.

## CFB Bytecode Artifacts

`.cfb` files are compiled CFGS bytecode artifacts.

A CFB file stores bytecode, function metadata, source hash, compiler version, plugin DLL names discovered during compilation, and native interop DLL names discovered during compilation.

CFB files MUST NOT store local source file paths or local plugin file paths as dependency identities.

Running a CFB file MUST NOT re-run lexing, parsing, lowering, semantic validation, or bytecode emission for the main script.

The VM MUST reject unsupported or invalid binary artifact shapes. A conforming loader MUST validate length-prefixed element counts against the remaining input and reject unknown opcodes, so that a corrupt or hostile artifact fails with a clear data error instead of an unbounded allocation.

Future bytecode compatibility changes SHOULD be versioned explicitly.

## Debugging Semantics

Debug log mode MUST NOT alter program semantics.

Interactive debugger mode pauses before VM instructions and MAY inspect stack, locals, globals, call stack, instruction pointer, and source locations.

Debugger commands MUST NOT mutate user program state unless a future command explicitly documents mutation.

Source-line breakpoints map to generated instructions for that source line. A line may stop more than once when it emits multiple instructions.

## Compatibility and Evolution

Language changes SHOULD update this specification and the feature documentation together.

Breaking changes SHOULD state:

- what behavior changed
- why it changed
- whether existing `.cfs` code is affected
- whether existing `.cfb` files must be recompiled

Runtime-only fixes that do not affect language meaning MAY update implementation documentation without changing this specification.

## Implementation Conformance

An implementation is CFGS-conformant when it:

1. accepts all valid programs described by this specification,
2. rejects invalid programs described by this specification,
3. preserves documented runtime behavior,
4. reports meaningful diagnostics for rejected programs,
5. executes bytecode according to the VM semantics used by the official implementation.

The official edgecase suite is the executable regression companion to this specification. When this document and an edgecase disagree, the disagreement SHOULD be resolved by updating either the implementation, the edgecase, or this specification so that all three match.
