using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Plugin;
using System.Globalization;
using System.Threading;

namespace CFGS.EdgeAwaitables;

public sealed class EdgeAwaitablesPlugin : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
    {
        builtins.Register(new BuiltinDescriptor("edge_task_void_delay", 1, 1, (args, instr) =>
        {
            int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            return Task.Delay(ms);
        }, smartAwait: true));

        builtins.Register(new BuiltinDescriptor("edge_task_void_fault", 1, 1, (args, instr) =>
        {
            string msg = args[0]?.ToString() ?? "edge_task_void_fault";
            return Task.FromException(new InvalidOperationException(msg));
        }, smartAwait: true));

        builtins.Register(new BuiltinDescriptor("edge_task_void_canceled", 0, 0, (args, instr) =>
        {
            return Task.FromCanceled(new CancellationToken(canceled: true));
        }, smartAwait: true));

        builtins.Register(new BuiltinDescriptor("edge_valuetask_void_delay", 1, 1, (args, instr) =>
        {
            int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            return new ValueTask(Task.Delay(ms));
        }, smartAwait: true));

        builtins.Register(new BuiltinDescriptor("edge_valuetask_void_fault", 1, 1, (args, instr) =>
        {
            string msg = args[0]?.ToString() ?? "edge_valuetask_void_fault";
            return new ValueTask(Task.FromException(new InvalidOperationException(msg)));
        }, smartAwait: true));

        builtins.Register(new BuiltinDescriptor("edge_valuetask_void_canceled", 0, 0, (args, instr) =>
        {
            return new ValueTask(Task.FromCanceled(new CancellationToken(canceled: true)));
        }, smartAwait: true));

        builtins.Register(new BuiltinDescriptor("edge_task_int_value", 0, 0, (args, instr) =>
        {
            return Task.FromResult(123);
        }, smartAwait: true));

        builtins.Register(new BuiltinDescriptor("edge_task_string_value", 0, 0, (args, instr) =>
        {
            return Task.FromResult("EDGE_TASK_STRING");
        }, smartAwait: true));

        builtins.Register(new BuiltinDescriptor("edge_valuetask_int_value", 0, 0, (args, instr) =>
        {
            return new ValueTask<int>(456);
        }, smartAwait: true));

        builtins.Register(new BuiltinDescriptor("edge_valuetask_string_value", 0, 0, (args, instr) =>
        {
            return new ValueTask<string>("EDGE_VALUETASK_STRING");
        }, smartAwait: true));

        builtins.Register(new BuiltinDescriptor("edge_manual_nonblocking_sync", 2, 2, (args, instr) =>
        {
            int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            object? value = args[1];
            Thread.Sleep(ms);
            return value;
        }, smartAwait: true, nonBlocking: true));

        intrinsics.Register(typeof(string), new IntrinsicDescriptor("edge_manual_nonblocking_intrinsic", 2, 2, (recv, args, instr) =>
        {
            int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            string suffix = args[1]?.ToString() ?? "";
            Thread.Sleep(ms);
            return (recv?.ToString() ?? "") + suffix;
        }, smartAwait: true, nonBlocking: true));
    }

    [Builtin("edge_attr_nonblocking_sync", 2, 2, NonBlocking = true)]
    private static object EdgeAttrNonBlockingSync(List<object> args, Instruction instr)
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

    [Intrinsic(typeof(string), "edge_attr_nonblocking_intrinsic", 2, 2, NonBlocking = true)]
    private static object EdgeAttrNonBlockingIntrinsic(object recv, List<object> args, Instruction instr)
    {
        int ms = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
        string suffix = args[1]?.ToString() ?? "";
        Thread.Sleep(ms);
        return (recv?.ToString() ?? "") + suffix;
    }
}
