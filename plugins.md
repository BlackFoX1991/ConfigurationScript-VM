# Building a Plugin for CFGS (Template + Ready-to-Use Skeleton)

This short guide shows how to create a **CFGS plugin** from scratch and includes a **ready-to-use skeleton** file (with Builtins, Intrinsics, and optional async behavior via `Task`/`await`).

> **Important (dependency requirement):**  
> Your plugin project **must reference `CFGS_VM`**. The **PluginLoader** lives in the VM assembly and loads plugins via reflection. It looks for the interfaces and descriptors **defined in `CFGS_VM`** (`IVmPlugin`, `IBuiltinRegistry`, `IIntrinsicRegistry`, `BuiltinDescriptor`, `IntrinsicDescriptor`, `VMException`, etc.).  
> If your plugin does **not** reference the VM assembly, type identities won’t match the loader’s expectations and your plugin **will not be discovered or activated**.

---

## Plugin discovery / auto-loading

The host loads plugins using the VM’s `PluginLoader` (typically from a `plugins/` folder next to the VM executable).  
To deploy your plugin, copy the compiled **`.dll`** (plus any dependencies) into that folder.

Example layout:
```
/YourApp
  /plugins
    MyCompany.MyPlugin.dll
  CFGS_VM.exe   # your host / VM
```

**Notes**
- Make sure the plugin’s target framework matches the host’s runtime.
- Ensure the `CFGS_VM` reference is resolvable at runtime (next to the DLL or in the probing path).
- The loader instantiates classes that implement **`IVmPlugin`** and calls their `Register(...)`.
- If your plugin also uses attribute-based registration (e.g., `BuiltinAttribute`, `IntrinsicAttribute`), those attributes must also come from the same `CFGS_VM` assembly reference.

---

## 1) Project setup

1. Create a new **Class Library** project (C#).  
2. **Add a reference to `CFGS_VM`** (project or assembly reference).  
3. Choose a namespace (you can use the internal one below or your own):
   ```
   CFGS_VM.VMCore.Extensions.internal_plugin
   ```

Suggested solution layout:
```
/YourSolution
  /CFGS_VM                # core project/assembly
  /Plugins
    /CFGS_SKELETON        # your plugin project
      CFGS_SKELETON.cs
```

**Example `csproj` reference options:**

_ProjectReference (same solution):_
```xml
<ItemGroup>
  <ProjectReference Include="..\CFGS_VM\CFGS_VM.csproj" />
</ItemGroup>
```

_Binary reference (prebuilt DLL):_
```xml
<ItemGroup>
  <Reference Include="CFGS_VM">
    <HintPath>..\lib\CFGS_VM.dll</HintPath>
  </Reference>
</ItemGroup>
```

---

## 2) Implement `IVmPlugin`

Every plugin implements `IVmPlugin` and registers **Builtins** (free functions) and **Intrinsics** (receiver-bound methods).

```csharp
public sealed class CFGS_SKELETON : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
    {
        RegisterBuiltins(builtins);
        RegisterDemoHandleIntrinsics(intrinsics);
    }
}
```

- **Builtins**: `builtins.Register(new BuiltinDescriptor(...))`
- **Intrinsics**: `intrinsics.Register(receiverType, new IntrinsicDescriptor(...))`

---

## 3) Builtins (free functions)

Call from CFGS:
```cfgs
var x = hello();
var n = to_int("123");
```

**Errors**: Throw `VMException` with source info (`instr.Line`, `instr.Col`, `instr.OriginFile`) so script-level `try/catch` can route correctly.

---

## 4) Intrinsics (methods bound to an instance)

Typical flow:
```cfgs
var h = demo_handle();  # factory builtin returning a handle instance
h.start();
print(h.is_running());
print(h.say("Hi!"));
h.stop();
```

Intrinsics are bound to a concrete **receiver type** (e.g., `DemoHandle`).

---

## 5) Asynchronous behavior

- A builtin or intrinsic may return a **`Task`**.  
- The VM’s `AWAIT` instruction unwraps the `Task` and yields the result.  
- When scripts use `try`, errors are routed to `catch` blocks automatically (via your VM’s exception routing).

---

## 6) Testing from CFGS

Example script:
```cfgs
print(hello());
print("sum2(3,4) = " + str(sum2(3,4)));

var h = demo_handle();
h.start();
print("running = " + h.is_running());
print(h.say("Hello CFGS"));
h.stop();

# Async:
var t = sleep(200);      # returns Task
print("typeof(t) = " + typeof(t));
await t;                 # unwrap

var t2 = h.incLater(150);  # intrinsic returning Task<Int>
var v  = await t2;
print("after await: " + str(v));
```

---

## 7) Complete, ready-to-use skeleton

```csharp
using System.Globalization;
using CFGS_VM.VMCore.Plugin;
using CFGS_VM.VMCore.Extensions;
using static CFGS_VM.VMCore.VM;

namespace CFGS_VM.VMCore.Extensions.internal_plugin
{
    /// <summary>
    /// Minimal working plugin template:
    /// - Builtins: hello, sum2, to_int, sleep
    /// - Receiver type: DemoHandle with intrinsics (start/stop/is_running/say/incLater)
    /// - Async via Task: CALL returns Task, AWAIT unwraps
    /// </summary>
    public sealed class CFGS_SKELETON : IVmPlugin
    {
        // Example handle for intrinsics
        public sealed class DemoHandle
        {
            public bool IsRunning { get; private set; }
            public int Counter { get; private set; }

            public void Start() => IsRunning = true;
            public void Stop()  => IsRunning = false;

            public string Say(string msg)
                => (IsRunning ? "[running] " : "[stopped] ") + (msg ?? string.Empty);

            public int Inc(int by = 1) { Counter += by; return Counter; }
        }

        public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {
            RegisterBuiltins(builtins);
            RegisterDemoHandleIntrinsics(intrinsics);
        }

        // -----------------------------
        // Builtins
        // -----------------------------
        private static void RegisterBuiltins(IBuiltinRegistry builtins)
        {
            builtins.Register(new BuiltinDescriptor("hello", 0, 0, (args, instr) =>
                "Hello from CFGS_SKELETON!"));

            builtins.Register(new BuiltinDescriptor("sum2", 2, 2, (args, instr) =>
            {
                try
                {
                    double a = Convert.ToDouble(args[0], CultureInfo.InvariantCulture);
                    double b = Convert.ToDouble(args[1], CultureInfo.InvariantCulture);
                    return a + b;
                }
                catch (Exception ex)
                {
                    throw new VMException($"sum2: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream);
                }
            }));

            builtins.Register(new BuiltinDescriptor("to_int", 1, 1, (args, instr) =>
            {
                try { return Convert.ToInt32(args[0], CultureInfo.InvariantCulture); }
                catch (Exception ex)
                {
                    throw new VMException($"to_int: invalid number: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream);
                }
            }));

            // Factory for our handle
            builtins.Register(new BuiltinDescriptor("demo_handle", 0, 0, (args, instr) =>
                new DemoHandle()));

            // Asynchronous builtin: returns Task; AWAIT unwraps it
            builtins.Register(new BuiltinDescriptor("sleep", 1, 1, (args, instr) =>
            {
                int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
                return Task.Run<object?>(async () =>
                {
                    await Task.Delay(ms).ConfigureAwait(false);
                    return null;
                });
            }, smartAwait: true));
        }

        // -----------------------------
        // Intrinsics for DemoHandle
        // -----------------------------
        private static void RegisterDemoHandleIntrinsics(IIntrinsicRegistry intrinsics)
        {
            var T = typeof(DemoHandle);

            intrinsics.Register(T, new IntrinsicDescriptor("start", 0, 0, (recv, a, i) =>
            { ((DemoHandle)recv!).Start(); return recv!; }));

            intrinsics.Register(T, new IntrinsicDescriptor("stop", 0, 0, (recv, a, i) =>
            { ((DemoHandle)recv!).Stop(); return recv!; }));

            intrinsics.Register(T, new IntrinsicDescriptor("is_running", 0, 0, (recv, a, i) =>
                ((DemoHandle)recv!).IsRunning));

            intrinsics.Register(T, new IntrinsicDescriptor("say", 1, 1, (recv, a, i) =>
            {
                var msg = a[0]?.ToString() ?? string.Empty;
                return ((DemoHandle)recv!).Say(msg);
            }));

            // Asynchronous intrinsic: returns Task<int>; AWAIT unwraps to Int
            intrinsics.Register(T, new IntrinsicDescriptor("incLater", 1, 1, (recv, a, i) =>
            {
                int ms = Convert.ToInt32(a[0], CultureInfo.InvariantCulture);
                var h = (DemoHandle)recv!;
                return Task.Run<object?>(async () =>
                {
                    await Task.Delay(ms).ConfigureAwait(false);
                    return h.Inc(1);
                });
            }, smartAwait: true));
        }
    }
}
```

---

## 8) Error handling

Always throw **`VMException`** with context so script-level `try/catch` works properly:
```csharp
throw new VMException("message",
    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream);
```

---

## 9) Packaging & versioning

- Build the plugin as a separate DLL and place it in the plugin folder.
- Optionally expose a `version()` builtin.
- Stick to semantic versioning and keep a small changelog.

---

## 10) Performance & safety tips

- Don’t block long operations on the VM thread: return a `Task` and let scripts use `await`.
- Close resources deterministically.
- Validate inputs; provide clear error messages.

---

### Why the VM reference is required (recap)

- The **PluginLoader** (in the VM assembly) locates classes implementing `IVmPlugin` and calls `Register(...)`.
- Your plugin must **reference the exact same `CFGS_VM` assembly** so:
  - The `IVmPlugin` interface is the **same type** the loader expects (type identity).
  - Your code can construct `BuiltinDescriptor` / `IntrinsicDescriptor` and raise `VMException`.
  - Attribute-based registration (if used) resolves correctly to the VM’s attribute types.
