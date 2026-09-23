# Embedding CFGS in .NET Applications

CFGS exposes a host API through `CFGS_VM.Hosting`. A host application can:

- compile CFGS source or files
- run the top-level module body
- invoke `main()` (or any function) explicitly when desired
- read and write globals
- call exported CFGS functions
- construct script classes and call their members
- inject host services
- load plugins and CFB artifacts
- restrict file I/O, plugin loading, and native interop

The embedding surface is built around two types:

- `CfgsScriptEngine`: compile/load configuration plus host-wide defaults
- `CfgsScript`: one compiled script instance with its own VM state

## Quick Start

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

await script.RunAsync();

string? appName = script.GetGlobal<string>("appName");
int maxUsers = script.GetGlobal<int>("maxUsers");
object? sum = await script.InvokeAsync("add", 10, 20);
int typedSum = await script.InvokeAsync<int>("add", 10, 20);
```

`RunAsync()` executes the script module body. Top-level globals declared in the script are only initialized after that step.

## Main Types

### `CfgsScriptEngine`

`CfgsScriptEngine` owns the configuration used to create script VMs:

```csharp
CfgsScriptEngine engine = new()
{
    WorkingDirectory = AppContext.BaseDirectory,
    NativeAssemblyBaseDirectory = AppContext.BaseDirectory,
    CompilerVersion = "my-host/1.0"
};
```

Important members:

- `WorkingDirectory`: VM-local working directory for relative script paths
- `NativeAssemblyBaseDirectory`: base path for `import native`
- `CompilerVersion`: optional version string forwarded into compile metadata
- `Permissions`: fail-closed host permission model
- `SetService(name, value)`: inject host globals into every script created by the engine
- `LoadPlugin(path)`: register one plugin DLL for later compiled/loaded scripts
- `LoadPluginsFromDirectory(path)`: register every plugin DLL in a directory
- `CompileSource(name, source)`: compile from an in-memory string
- `CompileFile(path)`: compile a `.cfs` file
- `CompileFileToCfb(input, output)`: compile directly to a binary CFB artifact
- `LoadCfb(path)`: load a previously compiled CFB artifact

### `CfgsScript`

Each `CfgsScript` owns one VM instance and therefore one isolated script state.

Important members:

- `RunAsync(...)`
- `InvokeAsync(...)`
- `NewAsync(...)`
- `GetGlobal(...)`, `TryGetGlobal(...)`, `GetGlobals()`
- `SetGlobal(...)`, `TrySetGlobal(...)`
- `GetMember(...)`, `SetMember(...)`, `InvokeMemberAsync(...)`
- `SaveCfb(path)`
- `Runtime`: direct access to the underlying VM when advanced host customization is needed

## Compile Options and Path Resolution

### `CompileSource`

```csharp
CfgsScript script = engine.CompileSource("<host>", source);
```

Use a synthetic name such as `"<host>"` when the script is purely in-memory.

If the `name` passed to `CompileSource(...)` looks like a real file path, CFGS uses that path's directory as the script working directory. Otherwise it falls back to `engine.WorkingDirectory`.

### `CompileFile`

```csharp
CfgsScript script = engine.CompileFile("scripts/rules.cfs");
```

`CompileFile(...)` resolves imports the same way as the CLI:

- first from the script location
- then from the configured working directory and normal runtime/plugin resolution rules

### VM-local workspace

The configured `WorkingDirectory` is VM-local. Relative file APIs inside CFGS resolve through the VM and do not change the host process current directory.

This means a script may update its own workspace view without affecting `Directory.GetCurrentDirectory()` in the host process.

## Lifecycle and Execution Model

The host lifecycle is intentionally explicit:

1. Create an engine
2. Compile or load a script
3. Run the top-level module body
4. Read globals, call functions, or create classes

### Top-level module body

Top-level statements execute only once per `CfgsScript` instance.

```csharp
await script.RunAsync();
```

After the top-level body has run:

- top-level variables are initialized
- top-level functions are callable
- top-level classes are constructible
- imports, namespaces, and script declarations are loaded into that VM instance

### Running main

CFGS has no auto-main. `RunAsync()` loads the top-level body (once per `CfgsScript`) and never invokes `main`. A host runs `main` by calling it explicitly.

```csharp
await script.RunAsync();           // load top-level once
await script.InvokeAsync("main");  // run main explicitly when you want it
```

Rules:

- The top-level body runs at most once per `CfgsScript`
- `main` is never invoked implicitly; the host decides when, and whether, to call it
- Use a two-phase flow when a host needs to inspect or adjust state before `main`:
  1. `RunAsync()` to load declarations and inspect globals
  2. inject or modify state with `SetGlobal(...)`
  3. `InvokeAsync("main")` to run `main`

## End-to-End Host Example

The sample `CFGS_NE\Samples\09_embedding_tooling\01_embedding_library_style.cfs` is the reference script for this style. A host can drive it like this:

```csharp
using CFGS_VM.Hosting;

CfgsScriptEngine engine = new()
{
    WorkingDirectory = AppContext.BaseDirectory,
    CompilerVersion = "host-demo"
};

engine.SetService("currentUser", "Ada");

CfgsScript script = engine.CompileFile("CFGS_NE/Samples/09_embedding_tooling/01_embedding_library_style.cfs");

await script.RunAsync();

Dictionary<string, object> data = CfgsScript.CreateDictionary(new Dictionary<string, object?>
{
    ["count"] = 3
});

object? payload = await script.InvokeAsync("createPayload", "sync-call", data);
int requestCount = script.GetGlobal<int>("requestCount");

object? counter = await script.NewAsync("ScriptCounter", 10);
int next = await script.InvokeMemberAsync<int>(counter!, "next");
```

This host flow is usually the best baseline for embedding:

- compile
- top-level load with `RunAsync()` (main is never auto-invoked)
- call exported functions explicitly (including `main`, if desired)
- use `NewAsync(...)` plus member helpers for script objects

## Globals

The host can read and write top-level globals through the script wrapper.

```csharp
await script.RunAsync();

object? value = script.GetGlobal("config");
bool exists = script.TryGetGlobal("config", out object? config);
IReadOnlyDictionary<string, object?> globals = script.GetGlobals();

script.SetGlobal("hostValue", 123);
bool changed = script.TrySetGlobal("hostValue", 456);
```

Behavior:

- `GetGlobal(name)` throws if the global does not exist
- `TryGetGlobal(name, out value)` returns `false` instead of throwing
- A namespace member is reached by its qualified name, because a script namespace exists only at compile time

```csharp
int seed = script.GetGlobal<int>("App.Core.seed");
script.SetGlobal("App.Core.seed", 25);
object? result = await script.InvokeAsync("App.Core.add", 1, 2);
object? box = await script.NewAsync("App.Core.Box", 7);
```

`GetGlobals()` lists namespace members under the same qualified names.
- `SetGlobal(name, value)` creates the global if it does not exist
- `TrySetGlobal(name, value)` returns `false` if the target global is `const`
- existing `const` globals cannot be modified

Recommended usage:

- use `SetService(...)` for host-provided globals that should exist before script execution
- use `SetGlobal(...)` after `RunAsync()` when you intentionally want to adjust state between top-level load and script calls

## Host Services

Host services are injected as globals into each script VM created by the engine.

```csharp
engine.SetService("clock", new SystemClock());
engine.SetService("currentUser", "Ada");

CfgsScript script = engine.CompileFile("scripts/app.cfs");
await script.RunAsync();
```

```cfs
func userName() {
    return currentUser;
}
```

Services are copied into every new `CfgsScript` created by that engine. Changing the engine service table later affects future script instances, not already created ones.

## Value Conversion and Normalization

Host values are normalized before they enter CFGS runtime state. This lets hosts pass plain CLR values instead of building CFGS runtime objects by hand.

### Scalars

These pass through in their natural CLR form and are converted by normal CFGS runtime rules:

- `string`
- `char`
- `bool`
- integer and floating-point primitives
- `decimal`
- `BigInteger`
- `DateTime`
- `DateTimeOffset`
- `TimeSpan`
- `Guid`
- `byte[]`

### Collections

Collections are normalized recursively:

- `IEnumerable` becomes a CFGS runtime list/array representation
- `IDictionary` becomes a CFGS string-keyed dictionary
- nested values are normalized recursively

```csharp
script.SetGlobal("items", new[] { 2, 4, 6 });
script.SetGlobal("user", new Dictionary<string, object?>
{
    ["name"] = "Ada",
    ["score"] = 10
});

await script.InvokeAsync("process", new[] { "A", "B" });
```

```cfs
func readHostData() {
    return items[1] + user["score"];
}
```

### Helper methods

For explicit host-side construction:

```csharp
List<object> items = CfgsScript.CreateArray("A", "B", 3);
Dictionary<string, object> user = CfgsScript.CreateDictionary(new Dictionary<string, object?>
{
    ["name"] = "Ada",
    ["active"] = true
});
```

For converting values that came back through a less-typed path:

```csharp
object? raw = await script.InvokeAsync("run");
string? text = CfgsScript.ToHost<string>(raw);
```

## Function Calls

```csharp
object? result = await script.InvokeAsync("run", "abc", 123);
string? text = await script.InvokeAsync<string>("format", 10);
```

Notes:

- arguments are normalized from CLR values into CFGS runtime values
- typed overloads convert the returned host object into the requested CLR type
- `InvokeAsync(...)` can call sync or async CFGS functions
- function lookup happens against the script's loaded global function table

## Script Class Instances

CFGS classes can be created either by script code or directly by the host once the module body has run.

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

Script-created instance:

```csharp
object? person = await script.InvokeAsync("createPerson", "Ada");
script.SetGlobal("currentPerson", person);
object? name = await script.InvokeAsync("readCurrentPerson");
```

Host-created instance:

```csharp
object? person = await script.NewAsync("Person", "Ada");

string? name = script.GetMember<string>(person!, "name");
script.SetMember(person!, "name", "Grace");

string? greeting = await script.InvokeMemberAsync<string>(person!, "greet", "Hi");
```

Member access goes through the same CFGS runtime rules as normal script execution. That means:

- constructors still run
- visibility rules still apply
- property accessors still apply
- inheritance and object initialization still behave as they do inside CFGS

This makes the host API a real language-level bridge instead of a parallel object model.

## Concurrency Model

One `CfgsScript` instance owns one VM and one mutable script state.

Public host operations are serialized per script instance:

- `RunAsync`
- `InvokeAsync`
- `NewAsync`
- `GetMember`
- `SetMember`
- `InvokeMemberAsync`
- global access helpers

This protects VM state when host code accidentally issues concurrent calls against the same script instance.

If you need true parallel execution, create multiple `CfgsScript` instances.

## Permissions and Security

The embedding API is fail-closed. Every sensitive capability defaults to disabled until the host explicitly enables it.

```csharp
engine.Permissions.AllowPluginLoading = true;
engine.Permissions.AllowNativeInterop = true;
engine.Permissions.AllowFileIO = true;
engine.Permissions.RestrictFileIoToWorkspace = true;
engine.Permissions.MaxExecutionTime = TimeSpan.FromSeconds(5);
```

### Permission fields

| Setting | Default | Meaning |
| --- | --- | --- |
| `AllowPluginLoading` | `false` | Allows plugin DLL loading through host and script plugin paths |
| `AllowNativeInterop` | `false` | Allows `import native` and native CLR member access |
| `AllowFileIO` | `false` | Allows file-system operations exposed through runtime/plugin APIs |
| `RestrictFileIoToWorkspace` | `false` | Jails resolved file paths to the VM working directory, including paths that leave it through a symlink or junction |
| `MaxExecutionTime` | unset | Cancels host-run and host-invoked script execution after the configured timeout |

### Important notes

- The non-I/O standard library is built into the runtime
- File-system APIs live in the `CFGS.IO` plugin, so hosts that need file I/O must also enable plugin loading
- When `RestrictFileIoToWorkspace` is enabled, paths escaping the workspace are rejected, whether through an absolute path, `..` traversal, or a symlink or junction that points out of the workspace
- Async host invocations inherit the same security policy, including workspace jailing
- `MaxExecutionTime` is cooperative: it can stop VM execution, but it cannot forcibly break a blocking native/plugin CLR call

### Recommended secure baseline

For untrusted or semi-trusted scripts, start here:

```csharp
CfgsScriptEngine engine = new()
{
    WorkingDirectory = workspace
};

engine.Permissions.AllowPluginLoading = false;
engine.Permissions.AllowNativeInterop = false;
engine.Permissions.AllowFileIO = false;
engine.Permissions.RestrictFileIoToWorkspace = true;
engine.Permissions.MaxExecutionTime = TimeSpan.FromSeconds(5);
```

Then open only the capabilities the script truly needs.

## Errors and Cancellation

Host-facing failures are wrapped as `CfgsScriptException`.

```csharp
try
{
    await script.RunAsync();
}
catch (CfgsScriptException ex)
{
    Console.WriteLine(ex.Code);
    Console.WriteLine(ex.Category);
    Console.WriteLine(ex.SourceFile);
    Console.WriteLine(ex.Line);
    Console.WriteLine(ex.Column);
    Console.WriteLine(ex.LanguageStack);
}
```

Useful properties:

- `Code`
- `Category`
- `SourceFile`
- `Line`
- `Column`
- `LanguageStack`

Cancellation sources:

- a caller-supplied `CancellationToken`
- `Permissions.MaxExecutionTime`

Operation cancellation is surfaced as a host exception with structured diagnostic metadata.

## Plugins

Plugins can be attached before compilation or CFB loading.

```csharp
engine.Permissions.AllowPluginLoading = true;
engine.LoadPlugin("plugins/CFGS.Microsoft.SQL.dll");
engine.LoadPluginsFromDirectory("plugins");

CfgsScript script = engine.CompileFile("scripts/db.cfs");
```

Plugin namespaces, builtins, and intrinsics become part of the compiled script environment.

If a loaded CFB artifact declares required plugins, those references are resolved when the artifact is loaded into a fresh VM.

## Native .NET Interop

Native interop stays script-driven.

```cfs
import native "SomeLibrary.dll" as Net;

func create() {
    return Net.Some.Namespace.Factory.Create();
}
```

The host can choose the base directory used for relative native assembly paths:

```csharp
engine.NativeAssemblyBaseDirectory = AppContext.BaseDirectory;
```

If `AllowNativeInterop` is `false`, native imports and native member execution are rejected.

## CFB Artifacts

The hosting API can save and load compiled binary CFGS artifacts.

```csharp
CfgsScript script = engine.CompileFile("scripts/rules.cfs");
script.SaveCfb("rules.cfb");

CompiledScript compiled = engine.CompileFileToCfb("scripts/rules.cfs", "rules.cfb");

CfgsScript loaded = engine.LoadCfb("rules.cfb");
await loaded.RunAsync();
object? result = await loaded.InvokeAsync("calculate", 10, 20);
```

Notes:

- CFB loading restores the compiled script into a fresh VM
- required plugin references stored in the artifact are resolved relative to the CFB file directory when they are not absolute
- plugin load policy still flows through the host runtime configuration

## Multiple Scripts and Isolation

Each compiled or loaded `CfgsScript` receives:

- its own VM instance
- its own globals
- its own function table
- its own execution state

This means two script instances created from the same file do not share runtime variables unless the host intentionally shares CLR objects between them.

Engine-level configuration is copied into each new script instance:

- registered services
- registered plugin paths/directories
- permission settings
- working directory defaults

## Running a Script as a Windows Service

Besides in-process embedding, CFGS can host a script as a long-running Windows service. This mode is not a public embedding type you construct. It is driven by the CLI and implemented by the internal `CfgsServiceHost` in `CFGS_VM.Hosting`. The Service Control Manager (SCM) launches the process with `-as_service`, the VM dispatch loop and the script's `main` observe the stop request cooperatively through a cancellation token, and lifecycle events plus unhandled script errors are written to the Windows Event Log under the source `CFGS`, because a service has no console.

```powershell
cfgs.exe -install_service MyJob -script C:\jobs\job.cfs
sc start MyJob
sc stop MyJob
cfgs.exe -uninstall_service MyJob
```

A service script is normally a `main` that loops until the stop signal arrives. Installing and uninstalling need an elevated command prompt, and the whole feature is Windows only. This service mode runs through the CLI execution path rather than a host-configured `CfgsScriptEngine`, so it uses the CLI runtime policy and not the fail-closed embedding permissions described above. For the full command list and flags such as `-display`, `-desc`, and `-startup`, see [Getting Started and Running Scripts](01_getting_started_and_running.md).

## Recommended Host Patterns

### Library-style embedding

Use this when CFGS is an internal rules engine or scripting extension:

1. `CompileFile(...)` or `CompileSource(...)`
2. `RunAsync()`
3. inspect globals or inject state
4. call exported functions explicitly

### Script-like embedding

Use this when the host wants CLI-like behavior:

1. compile or load the script
2. call `RunAsync()` to run the top-level body
3. call `InvokeAsync("main")` to run `main`

### Secure automation embedding

Use this when scripts come from outside the host team:

1. disable plugin loading and native interop
2. leave file I/O disabled unless required
3. if file I/O is needed, enable workspace jail
4. configure `MaxExecutionTime`
5. expose only deliberate host services/globals

## Practical Notes

- Prefer `SetService(...)` for long-lived host-provided dependencies or context
- Prefer `SetGlobal(...)` for per-run values that should be injected after top-level loading
- Use separate `CfgsScript` instances instead of sharing one instance across real parallel work
- Use `RunAsync()` for deterministic library-style hosts
- Use the sample `CFGS_NE\Samples\09_embedding_tooling\01_embedding_library_style.cfs` as the reference shape for exported host APIs
