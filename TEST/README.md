# CFGS Feature Showcase

This folder contains a runnable CFGS showcase that demonstrates the current core language surface in one place.

Files:
- `00_run_all_features.cfs` - main showcase entry script
- `01_same_namespace_contracts.cfs` - bare-imported namespace contracts using file-scoped namespace syntax
- `02_same_namespace_impl.cfs` - same namespace continued in a second file with unqualified inheritance/interface references
- `03_exports.cfs` - explicit exports for named imports
- `04_star_tools.cfs` - explicit exports for namespace imports
- `05_single_symbol.cfs` - explicit export for single-symbol import

How to run:

```powershell
dotnet "C:\Users\Artur\source\repos\BlackFoX1991\ConfigurationScript-VM\dist\Debug\net10.0\CFGS_VM.dll" ".\00_run_all_features.cfs"
```

What is covered:
- literals, operators, arithmetic, comparisons, null-coalescing, ternary
- normal, verbatim, interpolated, and formatted strings
- arrays, dictionaries, slices, delete, destructuring, `out`
- `if`, `while`, `do/while`, `for`, `foreach`, `match`, `try/catch/finally`
- functions, closures, recursion, defaults, named args, rest, spread, destructuring parameters
- classes, fields, properties, `field`, object initializers, enums
- inheritance, `this`, `type`, `super`, `outer`, static and instance dispatch
- interfaces, default interface methods, explicit interface methods, indexer-style patterns via normal members, `implements`, `interfaces`, `interfaceof`, `interface_call`
- namespaces, merged namespaces across files, `use`, bare import, named import, namespace import, single-symbol import
- async, `await`, `yield`, `task()`, await over arrays and dictionaries
- `defer`, `using`, `destroy`
- standard namespaces like `json`, `regex`, `math`, and `encoding`

Intentionally not auto-run here:
- plugin DLL imports such as `import "CFGS.IO.dll"`
- native .NET interop via `import native "...dll" as Alias`
- HTTPS imports

Those are supported by the language/runtime too, but they depend on environment, assemblies, or network and are therefore not forced in the portable core showcase.
