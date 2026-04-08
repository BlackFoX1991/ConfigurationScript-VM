using CFGS_VM.VMCore.Plugin;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Exposes the VM binding surface without leaking concrete registry types.
    /// </summary>
    public interface IVmBindingRuntime
    {
        /// <summary>
        /// Gets the builtin registry abstraction.
        /// </summary>
        IBuiltinRegistry Builtins { get; }

        /// <summary>
        /// Gets the intrinsic registry abstraction.
        /// </summary>
        IIntrinsicRegistry Intrinsics { get; }

        /// <summary>
        /// Loads every plugin DLL in the supplied directory into the VM binding layer.
        /// </summary>
        void LoadPluginsFrom(string directory);

        /// <summary>
        /// Loads a single plugin DLL into the VM binding layer.
        /// </summary>
        void LoadPlugin(string dllPath);
    }
}
