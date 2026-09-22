# Creating Plugins

## The CFGS Plugin Model

A plugin is a .NET assembly that teaches the CFGS runtime new namespace members, builtins, and intrinsics. The runtime supports two paths for that.

1. Manual registration through `IVmPlugin`.
2. Attribute based registration through `BuiltinAttribute` and `IntrinsicAttribute`.

Both paths can exist in the same assembly at the same time.

This is separate from native .NET interop. Native interop can load a regular .NET assembly and expose public CLR types directly through an alias, but it does not register CFGS namespace members, builtins, or intrinsics. Use plugins when you want a deliberate CFGS API; use native interop when direct CLR access is the goal.

## How Plugins Are Loaded

A script loads a plugin through a normal DLL import.

```cfs
import "dist/Debug/net10.0/MyPlugin.dll";
```

When that happens, the loader does the following.

1. It loads the assembly through its own load context.
2. It instantiates every non abstract type that implements `IVmPlugin`.
3. It calls their `Register` method.
4. It then scans static methods with `BuiltinAttribute` and `IntrinsicAttribute`.
5. Registrations are staged first.
6. If registration fails halfway through, nothing is partially applied.

That is an important quality point. A plugin that crashes during registration does not leave behind half applied registrations by accident.

## Project File for a Plugin

A minimal plugin project looks like this.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\\CFGS_NE\\CFGS_VM.csproj" />
  </ItemGroup>
</Project>
```

If your plugin brings its own dependencies, `CopyLocalLockFileAssemblies` is often useful so all required DLLs are copied into the output together.

## Path 1. `IVmPlugin`

The direct interface is the clearest form.

```csharp
using CFGS_VM.VMCore.Plugin;
using CFGS_VM.VMCore.Command;

namespace Demo.Plugin;

public sealed class DemoPlugin : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces)
    {
        BuiltinDescriptor sum = new(
            "demo_sum",
            2,
            2,
            (args, instr) => Convert.ToInt32(args[0]) + Convert.ToInt32(args[1])
        );

        namespaces.Register(NamespaceDescriptor.FromDescriptors(
            "demo",
            [
                ("sum", sum)
            ]));

        intrinsics.Register(typeof(string), new IntrinsicDescriptor(
            "demo_wrap",
            2,
            2,
            (recv, args, instr) =>
            {
                string left = args[0]?.ToString() ?? "";
                string right = args[1]?.ToString() ?? "";
                return left + (recv?.ToString() ?? "") + right;
            }
        ));
    }
}
```

From CFGS, the public function is now reached through the namespace:

```cfs
print(demo.sum(2, 3));
```

The descriptor name `demo_sum` is only the native implementation identity. Because it is not registered into `IBuiltinRegistry`, `demo_sum(...)` is not a global CFGS function.

### `NamespaceDescriptor`

A namespace descriptor exposes a native namespace root to CFGS.

- `Path` is the namespace path, such as `demo` or `std.demo`.
- `Instance` is the object reached by dot access.
- `Members` is the visible member list used by namespace resolution and `use`.

For callable namespace members, `NamespaceDescriptor.FromDescriptors(...)` is the usual helper. It maps public member names such as `sum` to native `BuiltinDescriptor` implementations without also registering global builtin aliases.

### `BuiltinDescriptor`

A builtin descriptor has these key pieces. It can be registered globally through `IBuiltinRegistry`, or used as a namespace member through `NamespaceDescriptor.FromDescriptors(...)`.

- name
- minimum arity
- maximum arity
- delegate for the actual call
- optional `NativeCallOptions`

### `IntrinsicDescriptor`

An intrinsic descriptor follows the same idea, but includes a receiver type.

- receiver type
- name
- minimum arity
- maximum arity
- delegate with `receiver`, `args`, `instr`
- optional `NativeCallOptions`
- optional `IsValue`: when set, dot access evaluates the intrinsic with no arguments and yields its result instead of a callable, the way `math.PI` works. Such an intrinsic must accept zero arguments.

## Path 2. Attributes

Attributes are convenient when you want to hang many small helpers off static methods. Attribute-based `BuiltinAttribute` registrations are global builtins. For a clean namespaced public API, prefer `IVmPlugin` and `INamespaceRegistry`.

```csharp
using CFGS_VM.VMCore.Plugin;
using CFGS_VM.VMCore.Command;

namespace Demo.Plugin;

public static class DemoAttributed
{
    [Builtin("edge_attr_task_value", 1, 1)]
    private static Task<object?> EchoTask(List<object> args, Instruction instr)
    {
        object? value = args[0];
        return Task.FromResult(value);
    }

    [Intrinsic(typeof(string), "demo_suffix", 1, 1)]
    private static object AddSuffix(object recv, List<object> args, Instruction instr)
    {
        return (recv?.ToString() ?? "") + (args[0]?.ToString() ?? "");
    }
}
```

The method signatures should effectively look like this.

- builtin method. `static object Method(List<object> args, Instruction instr)`
- intrinsic method. `static object Method(object recv, List<object> args, Instruction instr)`

Return types may also be awaitable. The loader recognizes `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>`.

## `NativeCallOptions`

`NativeCallOptions` controls how the VM adapts a native call result.

- `NativeCallOptions.Default` adapts awaitable return values.
- `NativeCallOptions.Awaitable` documents that the call intentionally returns an awaitable value.
- `NativeCallOptions.Direct` disables awaitable adaptation for special low-level cases.
- `NativeCallOptions.Offloaded` runs synchronous blocking code on a background task and adapts the result as awaitable.

With attributes, awaitable adaptation is also enabled automatically when the method return type is awaitable. Attribute-based calls use the properties `AdaptAwaitables` and `OffloadBlockingWork`, because .NET attributes cannot accept arbitrary options objects.

In practice that means the following. If your builtin returns `Task<T>`, it already feels like a native awaitable value in CFGS.

`Offloaded` is for synchronous code that you intentionally want to move off the direct execution path. The runtime starts that call inside a background task.

Example.

```csharp
[Builtin("edge_attr_blocking_sync", 2, 2, OffloadBlockingWork = true)]
private static object SlowCall(List<object> args, Instruction instr)
{
    Thread.Sleep(Convert.ToInt32(args[0]));
    return args[1];
}
```

This is not a replacement for real asynchronous I O. It is a pragmatic bridge for blocking code you cannot easily change.

## Your Own Receiver Types

Intrinsics are not limited to primitive types. You can also introduce your own handle classes.

```csharp
public sealed class CacheHandle
{
    public Dictionary<string, object?> Data { get; } = new();
}
```

Then you register intrinsics on `typeof(CacheHandle)` and return a `CacheHandle` instance from a builtin.

From CFGS, that ends up feeling very natural.

```cfs
var cache = make_cache();
cache.set("host", "localhost");
print(cache.get("host"));
```

## Plugin Dependencies

If your plugin has additional NuGet or project dependencies, those DLLs need to be available next to your plugin at runtime. Otherwise the loader may find the assembly itself but still fail to activate specific types.

If you only want to use public .NET classes from a DLL without registering CFGS namespace members, builtins, or intrinsics, see [Native .NET Interop](21_native_dotnet_interop.md).

## Packaging and Importing

The typical flow is simple.

1. Build the plugin.
2. Make sure the output DLL and its dependencies live in a known location.
3. Add it to `cfgs.json` with `install Path\\MyPlugin.dll`.
4. Run `restore` to copy the plugin artifacts into `.cfgs/plugins`.
5. Load it in the script with `import "MyPlugin.dll";`.
6. Use the registered namespace members or intrinsics like normal language features.

Direct path imports still work, but the manifest workflow gives projects a stable dependency list and lockfile.

## Troubleshooting

If a plugin does not load cleanly, check these points.

- Is the DLL path correct.
- Are all dependencies present next to it.
- Was the assembly built for the current runtime target.
- Does the plugin register duplicate names.
- Does `Register` itself throw an exception.

For more loader detail, you can set the environment variable `CFGS_PLUGIN_VERBOSE` to `1` or `true`.

## Practical Design Recommendations

- Give namespace members clear verbal names.
- Use intrinsics for receiver centered behavior.
- Use `Task` or `ValueTask` when the call is naturally async.
- Use `NativeCallOptions.Offloaded` or `OffloadBlockingWork` only for real blocking foreign code emergencies.
- Prefer namespace members over global builtins for new public plugin APIs.
- Do not register names that collide with existing namespaces, builtins, or intrinsics.
- Treat error messages as part of your plugin API.

## Complete Mini Example

### C#

```csharp
using CFGS_VM.VMCore.Plugin;
using CFGS_VM.VMCore.Command;

namespace Demo.Plugin;

public sealed class DemoPlugin : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces)
    {
        BuiltinDescriptor hello = new(
            "demo_hello",
            1,
            1,
            (args, instr) => "Hello " + (args[0]?.ToString() ?? "")
        );

        namespaces.Register(NamespaceDescriptor.FromDescriptors(
            "demo",
            [
                ("hello", hello)
            ]));

        intrinsics.Register(typeof(string), new IntrinsicDescriptor(
            "surround",
            2,
            2,
            (recv, args, instr) =>
            {
                string left = args[0]?.ToString() ?? "";
                string right = args[1]?.ToString() ?? "";
                return left + (recv?.ToString() ?? "") + right;
            }
        ));
    }
}
```

### CFGS

```cfs
import "dist/Debug/net10.0/Demo.Plugin.dll";

print(demo.hello("World"));
print("core".surround("[", "]"));
```

At that point you have the whole plugin surface of the project in your hands. If you want practical examples next, [Using the HTTP and SQL Plugins](10_using_http_and_sql_plugins.md) is the best follow up stop.
