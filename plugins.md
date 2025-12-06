# Building a Plugin for CFGS (Template + Ready-to-Use Skeleton)

This short guide shows how to create a **CFGS plugin** from scratch and includes a **ready-to-use skeleton** file (with Builtins, Intrinsics, and optional async behavior via `Task`/`await`).

> **Important (dependency requirement):**  
> Your plugin project **must reference `CFGS_VM`**. The plugin interfaces and descriptors used for registration are defined there (`IVmPlugin`, `IBuiltinRegistry`, `IIntrinsicRegistry`, `BuiltinDescriptor`, `IntrinsicDescriptor`, `VMException`, etc.).  
> If your plugin does **not** reference the VM assembly, type identities won’t match the runtime’s expectations and your plugin **cannot be registered or used**.

---

## Plugin loading (import-based)

Starting with **version 3.1.1** (update date **2025-12-06**), plugins are **no longer auto-loaded** and are **no longer discovered from a `plugins/` folder**.

Plugins must now be **explicitly included via the `import` statement** at the top of a script.

### Standard Library requirement

Because plugin loading is now explicit, the **Standard Library is no longer implicitly available**.  
You must import it manually when you need it:

```cfgs
import "CFGS.StandardLibrary.dll";
```

### Import examples

Import a plugin DLL from the current base path:

```cfgs
import "./libs/MyCompany.MyPlugin.dll";
```

Import a plugin DLL by name (resolved via CFGS path rules):

```cfgs
import "MyCompany.MyPlugin.dll";
```

> **Reminder:**  
> `import` can only be used **at the top of a script**, before any executable code runs.

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

### Example `csproj` reference options

**ProjectReference (same solution):**

```xml
<ItemGroup>
  <ProjectReference Include="..\CFGS_VM\CFGS_VM.csproj" />
</ItemGroup>
```

**Binary reference (prebuilt DLL):**

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
- When scripts use `try`, errors are routed to `catch` blocks automatically.

---

## 6) Testing from CFGS

Example script:

```cfgs
import "CFGS.StandardLibrary.dll";
import "./libs/CFGS_SKELETON.dll";

print(hello());
print("sum2(3,4) = " + str(sum2(3,4)));

var h = demo_handle();
h.start();
print("running = " + h.is_running());
print(h.say("Hello CFGS"));
h.stop();

# Async:
var t = sleep(200);        # returns Task
await t;                   # unwrap

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

- Build the plugin as a separate DLL.
- Distribute it alongside your scripts or in a shared location referenced by your import setup.
- Optionally expose a `version()` builtin.
- Stick to semantic versioning and keep a small changelog.

---

## 10) Performance & safety tips

- Don’t block long operations on the VM thread: return a `Task` and let scripts use `await`.
- Close resources deterministically.
- Validate inputs; provide clear error messages.

---

### Why the `CFGS_VM` reference is required (recap)

- The plugin system expects implementations of `IVmPlugin` defined in `CFGS_VM`.
- Your plugin must reference the **same assembly identity** so:
  - `IVmPlugin` matches the runtime’s type identity.
  - Registration via `BuiltinDescriptor` / `IntrinsicDescriptor` works correctly.
  - `VMException` integrates cleanly with script-level error handling.
  - Attributes (if used) resolve to the same VM-defined types.
