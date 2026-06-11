using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Plugin;

namespace CFGS.EdgeAwaitables;

public sealed class EdgeAwaitablesPlugin : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces)
    {
        builtins.Register(new BuiltinDescriptor("edge_manual_offloaded_sync", 2, 2, ManualOffloadedSync, NativeCallOptions.Offloaded));
        intrinsics.Register(typeof(string), new IntrinsicDescriptor("edge_manual_offloaded_intrinsic", 2, 2, ManualOffloadedIntrinsic, NativeCallOptions.Offloaded));
    }

    private static object ManualOffloadedSync(List<object> args, Instruction instr)
    {
        Thread.Sleep(Convert.ToInt32(args[0]));
        return Convert.ToInt32(args[1]);
    }

    private static object ManualOffloadedIntrinsic(object receiver, List<object> args, Instruction instr)
    {
        Thread.Sleep(Convert.ToInt32(args[0]));
        return (receiver?.ToString() ?? "") + (args[1]?.ToString() ?? "");
    }
}

public static class EdgeAwaitableBuiltins
{
    [Builtin("edge_task_void_delay", 1, 1)]
    public static Task TaskVoidDelay(List<object> args, Instruction instr)
        => Task.Delay(Convert.ToInt32(args[0]));

    [Builtin("edge_valuetask_void_delay", 1, 1)]
    public static async ValueTask ValueTaskVoidDelay(List<object> args, Instruction instr)
        => await Task.Delay(Convert.ToInt32(args[0]));

    [Builtin("edge_task_void_fault", 1, 1)]
    public static async Task TaskVoidFault(List<object> args, Instruction instr)
    {
        await Task.Yield();
        throw new InvalidOperationException(args[0]?.ToString() ?? "EDGE_TASK_FAULT");
    }

    [Builtin("edge_valuetask_void_fault", 1, 1)]
    public static async ValueTask ValueTaskVoidFault(List<object> args, Instruction instr)
    {
        await Task.Yield();
        throw new InvalidOperationException(args[0]?.ToString() ?? "EDGE_VALUETASK_FAULT");
    }

    [Builtin("edge_task_void_canceled", 0, 0)]
    public static Task TaskVoidCanceled(List<object> args, Instruction instr)
        => Task.FromCanceled(new CancellationToken(canceled: true));

    [Builtin("edge_valuetask_void_canceled", 0, 0)]
    public static ValueTask ValueTaskVoidCanceled(List<object> args, Instruction instr)
        => new(Task.FromCanceled(new CancellationToken(canceled: true)));

    [Builtin("edge_task_int_value", 0, 0)]
    public static Task<int> TaskIntValue(List<object> args, Instruction instr)
        => Task.FromResult(123);

    [Builtin("edge_task_string_value", 0, 0)]
    public static Task<string> TaskStringValue(List<object> args, Instruction instr)
        => Task.FromResult("EDGE_TASK_STRING");

    [Builtin("edge_valuetask_int_value", 0, 0)]
    public static ValueTask<int> ValueTaskIntValue(List<object> args, Instruction instr)
        => ValueTask.FromResult(456);

    [Builtin("edge_valuetask_string_value", 0, 0)]
    public static ValueTask<string> ValueTaskStringValue(List<object> args, Instruction instr)
        => ValueTask.FromResult("EDGE_VALUETASK_STRING");

    [Builtin("edge_attr_offloaded_sync", 2, 2, OffloadBlockingWork = true)]
    public static object AttrOffloadedSync(List<object> args, Instruction instr)
    {
        Thread.Sleep(Convert.ToInt32(args[0]));
        return Convert.ToInt32(args[1]);
    }

    [Builtin("edge_attr_task_value", 1, 1)]
    public static Task<object> AttrTaskValue(List<object> args, Instruction instr)
        => Task.FromResult<object>(Convert.ToInt32(args[0]));

    [Intrinsic(typeof(string), "edge_attr_offloaded_intrinsic", 2, 2, OffloadBlockingWork = true)]
    public static object AttrOffloadedIntrinsic(object receiver, List<object> args, Instruction instr)
    {
        Thread.Sleep(Convert.ToInt32(args[0]));
        return (receiver?.ToString() ?? "") + (args[1]?.ToString() ?? "");
    }
}

public sealed class EdgeNativeBox
{
    public EdgeNativeBox(int value)
    {
        Value = value;
    }

    public event Action<int>? NumberChanged;

    public int Value { get; set; }

    public static string StaticText => "EDGE_NATIVE_STATIC";

    public int Add(int value) => Value + value;

    public static int StaticAdd(int left, int right) => left + right;

    public string OptionalJoin(string left, string middle = "M", string right = "R")
        => $"{left}:{middle}:{right}";

    public int SumParams(string label, params int[] values)
        => values.Sum();

    public static T EchoGeneric<T>(T value) => value;

    public string this[int index]
    {
        get => $"idx:{index}:{Value}";
        set => Value = Convert.ToInt32(value);
    }

    public Func<int, int, int> MakeAdder()
        => static (left, right) => left + right;

    public int UseBinary(int left, int right, Func<int, int, int> callback)
        => callback(left, right);

    public void RaiseNumber(int value)
        => NumberChanged?.Invoke(value);
}
