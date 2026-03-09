# Creating Plugins

## The CFGS Plugin Model

A plugin is a .NET assembly that teaches the CFGS runtime new builtins and or new intrinsics. The runtime supports two paths for that.

1. Manual registration through `IVmPlugin`.
2. Attribute based registration through `BuiltinAttribute` and `IntrinsicAttribute`.

Both paths can exist in the same assembly at the same time.

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

That is an important quality point. A plugin that crashes during registration does not leave behind half registered builtins by accident.

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
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
    {
        builtins.Register(new BuiltinDescriptor(
            "demo_sum",
            2,
            2,
            (args, instr) => Convert.ToInt32(args[0]) + Convert.ToInt32(args[1])
        ));

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

### `BuiltinDescriptor`

A builtin descriptor has these key pieces.

- name
- minimum arity
- maximum arity
- delegate for the actual call
- optional `smartAwait`
- optional `nonBlocking`

### `IntrinsicDescriptor`

An intrinsic descriptor follows the same idea, but includes a receiver type.

- receiver type
- name
- minimum arity
- maximum arity
- delegate with `receiver`, `args`, `instr`
- optional `smartAwait`
- optional `nonBlocking`

## Path 2. Attributes

Attributes are convenient when you want to hang many small helpers off static methods.

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

## `smartAwait`

`smartAwait` tells the runtime that the result should be treated as meaningfully awaitable. With attributes, this is also enabled automatically when the return type is awaitable anyway.

In practice that means the following. If your builtin returns `Task<T>`, it already feels like a native awaitable value in CFGS.

## `nonBlocking`

`nonBlocking` is for synchronous code that you intentionally want to move off the direct execution path. The runtime starts that call inside a background task.

Example.

```csharp
[Builtin("edge_attr_nonblocking_sync", 2, 2, NonBlocking = true)]
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

## Packaging and Importing

The typical flow is simple.

1. Build the plugin.
2. Make sure the output DLL and its dependencies live in a known location.
3. Load it in the script with `import "Path\\MyPlugin.dll";`.
4. Use the registered builtins or intrinsics like normal language features.

## Troubleshooting

If a plugin does not load cleanly, check these points.

- Is the DLL path correct.
- Are all dependencies present next to it.
- Was the assembly built for the current runtime target.
- Does the plugin register duplicate names.
- Does `Register` itself throw an exception.

For more loader detail, you can set the environment variable `CFGS_PLUGIN_VERBOSE` to `1` or `true`.

## Practical Design Recommendations

- Give builtins clear verbal names.
- Use intrinsics for receiver centered behavior.
- Use `Task` or `ValueTask` when the call is naturally async.
- Use `nonBlocking` only for real blocking foreign code emergencies.
- Do not register names that collide with existing builtins.
- Treat error messages as part of your plugin API.

## Complete Mini Example

### C#

```csharp
using CFGS_VM.VMCore.Plugin;
using CFGS_VM.VMCore.Command;

namespace Demo.Plugin;

public sealed class DemoPlugin : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
    {
        builtins.Register(new BuiltinDescriptor(
            "demo_hello",
            1,
            1,
            (args, instr) => "Hello " + (args[0]?.ToString() ?? "")
        ));

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
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/Demo.Plugin.dll";

print(demo_hello("World"));
print("core".surround("[", "]"));
```

At that point you have the whole plugin surface of the project in your hands. If you want practical examples next, [Using the HTTP and SQL Plugins](10_using_http_and_sql_plugins.md) is the best follow up stop.
