using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Plugin;
using System.Globalization;
using System.Threading;

namespace CFGS.EdgeAwaitables;

public sealed class EdgeAwaitablesPlugin : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces)
    {
        builtins.Register(new BuiltinDescriptor("edge_task_void_delay", 1, 1, (args, instr) =>
        {
            int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            return Task.Delay(ms);
        }, NativeCallOptions.Awaitable));

        builtins.Register(new BuiltinDescriptor("edge_task_void_fault", 1, 1, (args, instr) =>
        {
            string msg = args[0]?.ToString() ?? "edge_task_void_fault";
            return Task.FromException(new InvalidOperationException(msg));
        }, NativeCallOptions.Awaitable));

        builtins.Register(new BuiltinDescriptor("edge_task_void_canceled", 0, 0, (args, instr) =>
        {
            return Task.FromCanceled(new CancellationToken(canceled: true));
        }, NativeCallOptions.Awaitable));

        builtins.Register(new BuiltinDescriptor("edge_valuetask_void_delay", 1, 1, (args, instr) =>
        {
            int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            return new ValueTask(Task.Delay(ms));
        }, NativeCallOptions.Awaitable));

        builtins.Register(new BuiltinDescriptor("edge_valuetask_void_fault", 1, 1, (args, instr) =>
        {
            string msg = args[0]?.ToString() ?? "edge_valuetask_void_fault";
            return new ValueTask(Task.FromException(new InvalidOperationException(msg)));
        }, NativeCallOptions.Awaitable));

        builtins.Register(new BuiltinDescriptor("edge_valuetask_void_canceled", 0, 0, (args, instr) =>
        {
            return new ValueTask(Task.FromCanceled(new CancellationToken(canceled: true)));
        }, NativeCallOptions.Awaitable));

        builtins.Register(new BuiltinDescriptor("edge_task_int_value", 0, 0, (args, instr) =>
        {
            return Task.FromResult(123);
        }, NativeCallOptions.Awaitable));

        builtins.Register(new BuiltinDescriptor("edge_task_string_value", 0, 0, (args, instr) =>
        {
            return Task.FromResult("EDGE_TASK_STRING");
        }, NativeCallOptions.Awaitable));

        builtins.Register(new BuiltinDescriptor("edge_valuetask_int_value", 0, 0, (args, instr) =>
        {
            return new ValueTask<int>(456);
        }, NativeCallOptions.Awaitable));

        builtins.Register(new BuiltinDescriptor("edge_valuetask_string_value", 0, 0, (args, instr) =>
        {
            return new ValueTask<string>("EDGE_VALUETASK_STRING");
        }, NativeCallOptions.Awaitable));

        builtins.Register(new BuiltinDescriptor("edge_manual_offloaded_sync", 2, 2, (args, instr) =>
        {
            int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            object? value = args[1];
            Thread.Sleep(ms);
            return value;
        }, NativeCallOptions.Offloaded));

        intrinsics.Register(typeof(string), new IntrinsicDescriptor("edge_manual_offloaded_intrinsic", 2, 2, (recv, args, instr) =>
        {
            int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            string suffix = args[1]?.ToString() ?? "";
            Thread.Sleep(ms);
            return (recv?.ToString() ?? "") + suffix;
        }, NativeCallOptions.Offloaded));
    }

    [Builtin("edge_attr_offloaded_sync", 2, 2, OffloadBlockingWork = true)]
    private static object EdgeAttrOffloadedSync(List<object> args, Instruction instr)
    {
        int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
        object? value = args[1];
        Thread.Sleep(ms);
        return value!;
    }

    [Builtin("edge_attr_task_value", 1, 1)]
    private static Task<object?> EdgeAttrTaskValue(List<object> args, Instruction instr)
    {
        object? value = args[0];
        return Task.Run<object?>(async () =>
        {
            await Task.Delay(10).ConfigureAwait(false);
            return value;
        });
    }

    [Intrinsic(typeof(string), "edge_attr_offloaded_intrinsic", 2, 2, OffloadBlockingWork = true)]
    private static object EdgeAttrOffloadedIntrinsic(object recv, List<object> args, Instruction instr)
    {
        int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
        string suffix = args[1]?.ToString() ?? "";
        Thread.Sleep(ms);
        return (recv?.ToString() ?? "") + suffix;
    }
}

public sealed class EdgeNativeBox
{
    private readonly Dictionary<int, string> _items = new();

    public EdgeNativeBox(int value)
    {
        Value = value;
    }

    public int Value { get; set; }

    public static string StaticText { get; set; } = "EDGE_NATIVE_STATIC";

    public event Action<int>? NumberChanged;

    public string this[int index]
    {
        get => _items.TryGetValue(index, out string? value) ? value : $"idx:{index}:{Value}";
        set
        {
            _items[index] = value;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                Value = parsed;
        }
    }

    public int Add(int amount)
        => Value + amount;

    public string OptionalJoin(string left, string middle = "M", string right = "R")
        => $"{left}:{middle}:{right}";

    public int SumParams(string label, params int[] values)
        => values.Sum();

    public Func<int, int, int> MakeAdder()
        => static (left, right) => left + right;

    public int UseBinary(int left, int right, Func<int, int, int> op)
        => op(left, right);

    public void RaiseNumber(int value)
        => NumberChanged?.Invoke(value);

    public static int StaticAdd(int left, int right)
        => left + right;

    public static T EchoGeneric<T>(T value)
        => value;
}
