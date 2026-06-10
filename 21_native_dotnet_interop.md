# Native .NET Interop

Native .NET interop lets a CFGS script load a regular .NET assembly and use public CLR types directly from script code.

This is separate from the plugin system. Plugins are still the official way to register CFGS namespace members, builtins, and intrinsics. Native interop is for cases where you want to call existing .NET classes, constructors, static members, instance members, and methods without writing a CFGS-specific plugin wrapper first.

## Importing an Assembly

Use `import native` in the script header.

```cfs
import native "MyLibrary.dll" as Net;

func main() {
    var box = Net.MyCompany.Tools.Box.new(10);
    print(box.Value);
}
```

The alias after `as` becomes the root object for the imported assembly.

The path is resolved like other local imports. Simple DLL names can be found in the script directory, restored `.cfgs/plugins` directories, the current working directory, the application directory, or `plugins` below the application directory.

## Type and Namespace Access

CLR namespaces and types are traversed with dot syntax.

```cfs
import native "MyLibrary.dll" as Net;

func main() {
    var text = Net.MyCompany.Tools.TextTools.Upper("hello");
    print(text);
}
```

Each dot segment either extends a namespace path or resolves a type. Public nested types are also reachable through dot access.

Bracket access is not used for .NET member lookup. Keep `[]` for arrays, dictionaries, and other indexable values.

## Constructors

Constructors are exposed through `.new(...)` on a CLR type.

```cfs
var person = Net.Demo.Person.new("Ada", 42);
```

Overload resolution uses argument count and basic type conversion. If no overload matches, or if the call is ambiguous, the runtime reports a native interop call error.

Native calls support positional arguments, named arguments, optional CLR parameters, and C# `params` arrays.

```cfs
var text = box.OptionalJoin("A", right: "Z");
var sum = box.SumParams("sum", 1, 2, 3, 4);
```

Simple generic methods can be called when the generic type argument can be inferred from the supplied CFGS value.

```cfs
var echoed = Net.Demo.Tools.EchoGeneric("value");
```

## Static Members

Public static fields, properties, and methods are available through dot access on the CLR type.

```cfs
var now = Net.System.DateTime.Now;
var path = Net.System.IO.Path.Combine("data", "app.json");
```

Writable public static fields and properties can be assigned.

```cfs
Net.Demo.Settings.Mode = "dev";
```

## Instance Members

Objects returned from constructors or methods are represented as native host objects. Public instance fields, properties, and methods are available through dot access.

```cfs
var box = Net.Demo.Box.new(7);
box.Value = 20;
print(box.Add(5));
```

CLR indexers are available through bracket syntax on native objects.

```cfs
print(box[2]);
box[2] = "44";
```

## Delegates and Events

CLR delegates returned by .NET APIs are callable from CFGS.

```cfs
var adder = box.MakeAdder();
print(adder(2, 3));
```

When a CLR method or event expects a delegate type, a CFGS function can be converted to that delegate type.

```cfs
func multiply(a, b) {
    return a * b;
}

print(box.UseBinary(3, 4, multiply));
```

Events expose `add` and `remove` accessors through dot syntax.

```cfs
var total = 0;

func on_number(v) {
    total = total + v;
}

box.NumberChanged.add(on_number);
box.RaiseNumber(7);
```

Callbacks are synchronous. If a CFGS callback uses `await`, the runtime rejects it because CLR delegate invocation expects an immediate return value.

## Value Conversion

CFGS values are converted to CLR arguments when possible.

- numbers convert to numeric CLR parameter types
- strings convert to `string`, `char`, and enum values
- booleans convert to `bool`
- arrays convert to CLR arrays when the target parameter is an array type
- CFGS functions convert to compatible CLR delegate types
- wrapped native objects are unwrapped when passed back to CLR code

CLR return values are converted back to CFGS values where practical.

- primitive values, strings, decimals, dates, and GUIDs are returned directly
- CLR delegates become callable native delegate handles
- dictionaries become CFGS dictionaries
- enumerable values become CFGS arrays
- `Task` results are adapted to awaitable CFGS task values
- other CLR objects stay wrapped as native host objects

## CFB Behavior

Compiled `.cfb` files store native assembly references without local file paths. The artifact stores the DLL file name, so the DLL must be available next to the CFB file, in a package restore directory, in the application directory, or below the application `plugins` directory at runtime.

That keeps CFB files portable and avoids leaking build-machine paths.

## When To Use Plugins Instead

Use a plugin when you want a stable CFGS-facing API.

- You want clean namespace members, builtins, or intrinsics with CFGS names.
- You need custom argument validation or domain-specific error messages.
- You want to hide CLR implementation details.
- You need controlled async or non-blocking behavior.
- You want a small public API instead of exposing a broad .NET assembly surface.

Use native interop when you need direct access to an existing .NET API and the public CLR shape is already acceptable for script authors.
