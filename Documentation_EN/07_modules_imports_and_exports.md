# Modules, Imports, and Exports

## Core Model

CFGS has two import worlds.

1. Source modules from `.cfs` files.
2. Plugin assemblies from `.dll` files.

For `.cfs`, the focus is exported language symbols. For `.dll`, the focus is runtime extensions such as builtins and intrinsics.

## Import Header Rule

Imports must live in the script header. In practice that means every `import` statement belongs near the start of the file, before normal top level logic begins.

Clean.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import { add } from "math.cfs";

func main() {
    print(add(2, 3));
}
```

Not clean would be placing imports in the middle of a function or after regular top level execution has already started.

## Exporting

These declaration forms can be exported.

- `export const`
- `export func`
- `export async func`
- `export class`
- `export enum`
- `export var` with exactly one variable

Example.

```cfs
export const base = 100;

export func add(a, b) {
    return a + b;
}

export class Acc(seed) {
    var value = 0;

    func init(seed) {
        this.value = seed;
    }

    func add(v) {
        this.value = this.value + v;
        return this.value;
    }
}
```

`export var` does not support destructuring and only supports a single declaration.

## Bare Import

```cfs
import "helpers.cfs";
```

This is only allowed if the imported module does not use explicit `export` declarations. In that case the top level declarations are materialized as if they had been declared directly in the current module.

If a module uses explicit `export`, you must use named import or namespace import.

## Named Import

```cfs
import { base, add, Acc, Mode } from "math.cfs";
import { base as BASE, add as sum } from "math.cfs";
```

This is the most precise form when you only need part of the module surface.

## Single Symbol Import

```cfs
import triple from "tools.cfs";
```

This is the shorthand form for importing exactly one exported symbol.

## Namespace Import

```cfs
import * as Tools from "tools.cfs";
```

After that you access exports through the namespace alias.

```cfs
Tools["add_base"](5)
Tools["Flag"]["On"]
```

Dot access also works in many cases. The bracket form is still especially explicit and robust.

## File Resolution

Relative and simple module paths are searched in this order.

1. The current script directory.
2. The current working directory.
3. The directory of the running CLI.

This applies to both `.cfs` and `.dll`.

## Self Imports and Cycles

The import logic detects these cases.

- self imports
- import cycles
- missing files
- missing exported names

That keeps module errors visible early instead of surfacing much later during execution.

## Repeated Imports

Repeated imports of the same file are handled idempotently. This matters for transitive module trees and avoids double materialization of already known contents.

## URL Imports

The parser can also import HTTP or HTTPS resources as long as the path is an absolute URL.

```cfs
import "https://example.com/module.cfs";
```

Important notes.

- The resource is treated as a source module.
- There is protection through timeout and size limits.
- Relevant environment variables are `CFGS_IMPORT_HTTP_TIMEOUT_MS` and `CFGS_IMPORT_HTTP_MAX_BYTES`.

This form is useful in controlled environments. For most production scripts, local dependencies are easier to audit.

## DLL Imports

Plugins are loaded through normal import syntax.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Web.Http.dll";
```

Important details.

- DLLs do not support named import.
- DLLs do not support namespace import.
- The import loads and activates plugins in the assembly.
- Attribute based builtins and intrinsics inside that assembly are registered as well.

## Namespaces and Modules Together

Modules can expose namespaces by containing top level namespace declarations. The import then brings the resulting top level symbols into the current context, or into a namespace alias, depending on the import form.

## Practical Example

### Module `mod_math.cfs`

```cfs
export const base = 100;

export func add(a, b) {
    return a + b;
}
```

### Module `main.cfs`

```cfs
import { base as BASE, add } from "mod_math.cfs";

func main() {
    print(BASE);
    print(add(2, 3));
}

main();
```

## Typical Failure Cases

- Bare import on a module that uses explicit exports.
- Import not placed in the header.
- Named import on a DLL.
- Namespace import on a DLL.
- Alias conflicts with existing symbols.
- Import of a name that is not actually exported.

The engine error messages are already fairly direct in these situations and usually mention the symbol or import path that caused the problem.

If you want to combine modules with async code, continue with [Async, Await, and Yield](08_async_await_and_yield.md). If you want to understand the DLL side immediately, jump to [Using the HTTP and SQL Plugins](10_using_http_and_sql_plugins.md).
