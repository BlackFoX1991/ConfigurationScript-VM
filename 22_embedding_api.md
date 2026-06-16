# Embedding CFGS in .NET Applications

CFGS can be embedded in other .NET applications through `CFGS_VM.Hosting`.
The host application references the CFGS assembly, compiles a script, runs the
top-level module body, and then reads globals or invokes CFGS functions.

## Basic Host Usage

```csharp
using CFGS_VM.Hosting;

CfgsScriptEngine engine = new()
{
    WorkingDirectory = AppContext.BaseDirectory,
    CompilerVersion = "host"
};

CfgsScript script = engine.CompileSource("<host>", """
var appName = "Warehouse";
var maxUsers = 25;

func add(a, b) {
    return a + b;
}
""");

await script.RunAsync(autoInvokeMain: false);

string? appName = script.GetGlobal<string>("appName");
int maxUsers = script.GetGlobal<int>("maxUsers");
object? sum = await script.InvokeAsync("add", 10, 20);
int typedSum = await script.InvokeAsync<int>("add", 10, 20);
```

`RunAsync()` executes top-level statements. Top-level variables are only
initialized after that step.

## Loading Files

```csharp
CfgsScript script = engine.CompileFile("scripts/rules.cfs");
await script.RunAsync();
object? result = await script.InvokeAsync("calculate", 10, 20);
```

File imports are resolved from the script location and the configured working
directory, following the same import rules as the CLI.

## Globals

The host can read and write top-level variables through the script wrapper.

```csharp
await script.RunAsync(autoInvokeMain: false);

object? value = script.GetGlobal("config");
bool exists = script.TryGetGlobal("config", out object? config);
IReadOnlyDictionary<string, object?> globals = script.GetGlobals();

script.SetGlobal("hostValue", 123);
bool changed = script.TrySetGlobal("hostValue", 456);
```

`SetGlobal` creates a new global if the name does not exist. Existing `const`
globals cannot be changed.

Host arrays, lists, and dictionaries are normalized recursively into CFGS
runtime collections.

```csharp
script.SetGlobal("items", new[] { 2, 4, 6 });
script.SetGlobal("user", new Dictionary<string, object?>
{
    ["name"] = "Ada",
    ["score"] = 10
});
```

```cfs
func readHostData() {
    return items[1] + user["score"];
}
```

The same normalization is applied to values passed as function arguments.

```csharp
await script.InvokeAsync("process", new[] { "A", "B" });
```

## Host Services

Host services can be registered before compilation. They are injected as global
values into each script VM created by the engine.

```csharp
engine.SetService("clock", new SystemClock());
engine.SetService("currentUser", "Ada");

CfgsScript script = engine.CompileFile("scripts/app.cfs");
await script.RunAsync(autoInvokeMain: false);
```

```cfs
func userName() {
    return currentUser;
}
```

For explicit construction in host code, use the convenience helpers.

```csharp
List<object> items = CfgsScript.CreateArray("A", "B", 3);
Dictionary<string, object> user = CfgsScript.CreateDictionary(new Dictionary<string, object?>
{
    ["name"] = "Ada",
    ["active"] = true
});
```

## Function Calls

```csharp
object? result = await script.InvokeAsync("run", "abc", 123);
string? text = await script.InvokeAsync<string>("format", 10);
```

Arguments are passed as CLR values and converted by the normal CFGS runtime
rules. Return values can be read as `object?` or converted directly with the
typed overloads. `CfgsScript.ToHost<T>(value)` can be used when a value was
received through another API.

## Script Class Instances

CFGS classes can be created by script code or directly from the host after the
top-level module body has run.

```cfs
class Person {
    public var name = "";

    func init(name) {
        this.name = name;
    }

    public func greet(prefix = "Hello") {
        return prefix + ", " + this.name;
    }
}

func createPerson(name) {
    return new Person(name);
}

func readCurrentPerson() {
    return currentPerson.name;
}
```

```csharp
object? person = await script.InvokeAsync("createPerson", "Ada");
script.SetGlobal("currentPerson", person);
object? name = await script.InvokeAsync("readCurrentPerson");
```

The host can also construct script classes, read and write members, and call
methods through the same runtime rules used by CFGS itself.

```csharp
object? person = await script.NewAsync("Person", "Ada");

string? name = script.GetMember<string>(person!, "name");
script.SetMember(person!, "name", "Grace");

string? greeting = await script.InvokeMemberAsync<string>(person!, "greet", "Hi");
```

This keeps class construction under the same rules as normal CFGS code:
constructors, visibility, properties, object initialization, inheritance, and
runtime member access all behave exactly as they do inside a script.

## Auto Main

`RunAsync()` follows the normal CFGS auto-main behavior by default. If a script
contains only declarations and defines `main`, the host call will invoke it after
the top-level body has loaded.

```csharp
await script.RunAsync();                  // auto-main enabled
await script.RunAsync(autoInvokeMain: false); // only load top-level body
```

## Plugins

Plugins can be registered before compiling the script.

```csharp
engine.LoadPlugin("plugins/CFGS.Microsoft.SQL.dll");
engine.LoadPluginsFromDirectory("plugins");

CfgsScript script = engine.CompileFile("scripts/db.cfs");
```

Plugin namespaces and builtins are available to the compiler and runtime for the
compiled script.

## Permissions

The engine exposes a small permission surface for embedded hosts.

```csharp
engine.Permissions.AllowPluginLoading = false;
engine.Permissions.AllowNativeInterop = false;
engine.Permissions.AllowFileIO = false;
engine.Permissions.MaxExecutionTime = TimeSpan.FromSeconds(5);
```

`AllowPluginLoading` prevents loading plugin DLLs through the host engine and
through script plugin loading paths attached to the VM. `AllowNativeInterop`
blocks `import native` resolution and native member invocation. `MaxExecutionTime`
is applied to host run and call operations through VM instruction cancellation.
It cannot forcibly interrupt native or plugin code that blocks inside a CLR call.

## Native .NET Interop

Native interop stays script-driven.

```cfs
import native "SomeLibrary.dll" as Net;

func create() {
    return Net.Some.Namespace.Factory.Create();
}
```

The host can set the base directory used for relative native assembly paths.

```csharp
engine.NativeAssemblyBaseDirectory = AppContext.BaseDirectory;
```

## CFB Artifacts

The hosting API can save and load compiled binary scripts.

```csharp
CfgsScript script = engine.CompileFile("scripts/rules.cfs");
script.SaveCfb("rules.cfb");

CompiledScript compiled = engine.CompileFileToCfb("scripts/rules.cfs", "rules.cfb");

CfgsScript loaded = engine.LoadCfb("rules.cfb");
await loaded.RunAsync(autoInvokeMain: false);
object? result = await loaded.InvokeAsync("calculate", 10, 20);
```

CFB loading restores the compiled script into a fresh VM. Required plugin
references stored in the artifact are resolved relative to the CFB file
directory when they are not absolute paths.

## Multiple Scripts

Each compiled `CfgsScript` receives its own VM instance. That keeps globals,
function tables, loaded bytecode, and execution state isolated between scripts.
Plugin paths, plugin directories, host services, and permission settings
configured on the engine are applied to every newly compiled or loaded script.
