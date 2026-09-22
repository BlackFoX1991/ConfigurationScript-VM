using CFGS_VM.VMCore.Plugin;

namespace CFGS.EdgePartial;

/// <summary>
/// Registers a builtin and an intrinsic, then fails. The plugin loader stages registrations, so the
/// import must fail as a whole and leave neither registration behind.
/// </summary>
public sealed class EdgePartialPlugin : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces)
    {
        builtins.Register(new BuiltinDescriptor("edge_partial_before_throw", 0, 0, (args, instr) => 1));
        intrinsics.Register(typeof(string), new IntrinsicDescriptor("edge_partial_intr_before_throw", 0, 0, (recv, args, instr) => recv));

        throw new InvalidOperationException("edge partial plugin registration failure");
    }
}
