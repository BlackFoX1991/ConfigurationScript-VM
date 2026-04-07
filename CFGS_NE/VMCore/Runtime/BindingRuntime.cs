using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions.Intrinsics.Core;
using CFGS_VM.VMCore.Plugin;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        /// <summary>
        /// Gets the Builtins
        /// </summary>
        public BuiltinRegistry Builtins { get; } = new();

        /// <summary>
        /// Gets the Intrinsics
        /// </summary>
        public IntrinsicRegistry Intrinsics { get; } = new();

        /// <summary>
        /// Loads every plugin DLL in the supplied directory into the VM binding registries.
        /// </summary>
        public void LoadPluginsFrom(string directory)
            => PluginLoader.LoadDirectory(directory, Builtins, Intrinsics);

        /// <summary>
        /// Loads a single plugin DLL into the VM binding registries.
        /// </summary>
        public void LoadPlugin(string dllPath)
            => PluginLoader.LoadDll(dllPath, Builtins, Intrinsics);

        /// <summary>
        /// Tries to bind an intrinsic for the given receiver and member name.
        /// </summary>
        private bool TryBindIntrinsic(object receiver, string name, out IntrinsicBound bound, Instruction instr)
        {
            Type receiverType = receiver?.GetType() ?? typeof(object);
            if (Intrinsics.TryGet(receiverType, name, out IntrinsicDescriptor? descriptor))
            {
                IntrinsicMethod adapted = new(
                    descriptor.Name,
                    descriptor.ArityMin,
                    descriptor.ArityMax,
                    (recv, args, ins) => descriptor.Invoke(recv, args, ins),
                    smartAwait: descriptor.SmartAwait,
                    nonBlocking: descriptor.NonBlocking);

                bound = new IntrinsicBound(adapted, receiver!);
                return true;
            }

            bound = null!;
            return false;
        }

        /// <summary>
        /// Returns whether the supplied name is reserved by an intrinsic on the given receiver type.
        /// </summary>
        private bool IsReservedIntrinsicName(Type receiverType, string name)
            => Intrinsics.TryGet(receiverType, name, out _);

        /// <summary>
        /// Returns whether the supplied runtime member expression resolves to an intrinsic name.
        /// </summary>
        private bool IsReservedIntrinsicName(object receiver, object idxObj)
        {
            if (idxObj is not string name)
                return false;

            Type receiverType = receiver?.GetType() ?? typeof(object);
            return IsReservedIntrinsicName(receiverType, name);
        }

        /// <summary>
        /// Returns whether the supplied name is reserved for runtime metadata slots.
        /// </summary>
        private static bool IsReservedRuntimeSlotName(string name)
            => name.StartsWith("__", StringComparison.Ordinal) ||
               string.Equals(name, "new", StringComparison.Ordinal);
    }
}
