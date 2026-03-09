using CFGS_VM.VMCore.Plugin;

namespace CFGS.EdgePartial;

public sealed class EdgePartialPlugin : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
    {
        builtins.Register(new BuiltinDescriptor("edge_partial_before_throw", 0, 0, (args, instr) => 1));
        intrinsics.Register(typeof(string), new IntrinsicDescriptor("edge_partial_intr_before_throw", 0, 0, (recv, args, instr) => recv));

        throw new InvalidOperationException("edge partial plugin registration failure");
    }
}
