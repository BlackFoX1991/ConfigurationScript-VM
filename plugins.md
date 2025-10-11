# Building a Plugin for CFGS (Empty Skeleton Included)

This short guide shows how to create a **CFGS plugin** from scratch and includes a **ready-to-use skeleton** file.

> **Important:** You **must** add a reference to the core assembly **`CFGS_VM`**. Without this reference, types like `IVmPlugin`, `IBuiltinRegistry`, `IIntrinsicRegistry`, `BuiltinDescriptor`, and `IntrinsicDescriptor` will not be available.

---

## Plugin discovery / auto-loading

CFGS **automatically loads all plugins** from a folder named **`plugins`** that sits **next to the VM executable** (i.e., the same directory as the VM).  
To deploy your plugin, simply copy your compiled **`.dll`** (and any required dependencies) into that `plugins` folder.

Example layout:
```
/YourApp
  /plugins
    MyCompany.MyPlugin.dll
    MyCompany.AnotherPlugin.dll
  CFGS_VM.exe     # or your host executable
```

**Notes**
- Place each plugin assembly (`*.dll`) directly inside the `plugins` folder (subfolders are typically not scanned unless your host does that explicitly).
- Make sure the plugin DLL targets a compatible runtime and references **CFGS_VM**.
- If your plugin has extra dependencies, put them either next to the plugin DLL or alongside the VM executable so they can be resolved at load time.

---

## 1) Project Setup

1. Create a new **Class Library** project (C#).
2. **Add a reference to `CFGS_VM`** (project reference or assembly reference, depending on your solution layout).
3. Confirm your target framework matches your host application (e.g., .NET version).

Recommended folder structure:
```
/YourSolution
  /CFGS_VM                  # core project/assembly you already have
  /Plugins
    /CFGS_SKELETON          # your new plugin project
      CFGS_SKELETON.cs
```

---

## 2) Implement `IVmPlugin`

Every plugin must implement **`IVmPlugin`** and provide a `Register(IBuiltinRegistry, IIntrinsicRegistry)` method. The VM calls this entry point to let your plugin **register built-ins** (free functions) and **intrinsics** (methods bound to a receiver type).

Skeleton (simplified):
```csharp
public sealed class CFGS_SKELETON : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
    {
        RegisterBuiltins(builtins);
        RegisterIntrinsics(intrinsics);
    }
}
```

- **Built-ins** are registered via `BuiltinDescriptor(name, minArgs, maxArgs, handler)`.
- **Intrinsics** are registered via `IIntrinsicRegistry.Register(receiverType, IntrinsicDescriptor(...))`.

---

## 3) Built-ins (free functions)

A built-in is invoked from CFGS code like:
```cfgs
# Use '#' for comments in CFGS
var x = hello();
var y = echo("world");
var n = to_int("123");
```

Registration example:
```csharp
builtins.Register(new BuiltinDescriptor("hello", 0, 0, (args, instr) => "Hello from CFGS_SKELETON!"));
builtins.Register(new BuiltinDescriptor("echo", 1, 1, (args, instr) => args[0]));
builtins.Register(new BuiltinDescriptor("to_int", 1, 1, (args, instr) =>
{
    try { return Convert.ToInt32(args[0], CultureInfo.InvariantCulture); }
    catch (Exception ex)
    {
        throw new VMException("to_int: invalid number: " + ex.Message, instr.Line, instr.Col, instr.OriginFile);
    }
}));
```

**Notes**
- `minArgs` and `maxArgs` specify the allowed arity. The VM validates arity before calling your handler.
- The handler receives `args` as `List<object?>` and an `instr` with source info (line/column/file). Throw a `VMException` to report errors with precise locations in the user’s script.
- Return values must be representable as CFGS values (e.g., `int`, `double`, `string`, `bool`, `Dictionary<string, object>`, etc.).

---

## 4) Intrinsics (receiver-bound methods)

Intrinsics are methods invoked on an instance created or provided by your built-ins. Typical flow:

```cfgs
# Create a handle via a builtin (you write this builtin if needed)
var h = my_handle();         

# Then call intrinsics bound to the handle’s type
h.start();
print(h.is_running());
print(h.say("Hi!"));
h.stop();
```

Registration example (for `DemoHandle`):
```csharp
var T = typeof(DemoHandle);

intrinsics.Register(T, new IntrinsicDescriptor("start", 0, 0, (recv, a, i) => { ((DemoHandle)recv).Start(); return recv!; }));
intrinsics.Register(T, new IntrinsicDescriptor("stop", 0, 0, (recv, a, i) => { ((DemoHandle)recv).Stop(); return recv!; }));
intrinsics.Register(T, new IntrinsicDescriptor("is_running", 0, 0, (recv, a, i) => ((DemoHandle)recv).IsRunning));
intrinsics.Register(T, new IntrinsicDescriptor("say", 1, 1, (recv, a, i) =>
{
    var msg = a[0]?.ToString() ?? string.Empty;
    return ((DemoHandle)recv).Say(msg);
}));
```

Receiver type (example):
```csharp
public sealed class DemoHandle
{
    public bool IsRunning { get; private set; }
    public void Start() => IsRunning = true;
    public void Stop()  => IsRunning = false;
    public string Say(string msg) => IsRunning ? "[running] " + msg : "[stopped] " + msg;
}
```

**Notes**
- You can expose any number of methods. Keep intrinsics small and predictable.
- Return `recv` (the instance) from mutating operations if you want to support chaining.
- Ensure thread-safety if your handle manages background work.

---

## 5) Add a factory builtin (optional)

Often you’ll provide a builtin that **constructs** your handle, so CFGS code can do:
```cfgs
var h = my_handle();  # returns an instance of your handle type
h.start();
```

Example:
```csharp
builtins.Register(new BuiltinDescriptor("my_handle", 0, 0, (args, instr) => new CFGS_SKELETON.DemoHandle()));
```

---

## 6) Testing from CFGS

Example script:
```cfgs
var x = hello();
print(x);

var n = to_int("123");
print("n = " + n);

var h = my_handle();
h.start();
print("running = " + h.is_running());
print(h.say("Hello CFGS"));
h.stop();
```

---

## 7) Error handling

- Throw **`VMException`** to report user-facing errors with source context:
  ```csharp
  throw new VMException("message", instr.Line, instr.Col, instr.OriginFile);
  ```
- Validate inputs early (null checks, ranges, type checks).

---

## 8) Packaging & versioning

- Keep your plugin in a separate project under `plugins/`.
- If distributing binaries, package the DLL alongside your CFGS host and ensure the loader picks it up.
- Consider semantic versioning and a small changelog.

---

## 9) Performance & safety tips

- Avoid blocking long operations on the VM thread. Offload to background tasks if needed (and expose simple intrinsics to poll/stop).
- Be explicit with timeouts and resource cleanup.
- Validate file/network access if your plugin touches the system.

---

## Files in this starter

- `CFGS_SKELETON.cs` — the empty but functional skeleton you can start from.

---

[Back](README.md)
