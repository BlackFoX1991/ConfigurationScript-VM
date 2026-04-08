using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions.Intrinsics.Core;
using CFGS_VM.VMCore.Plugin;

namespace CFGS_VM.VMCore
{
    public partial class VM : IVmBindingRuntime
    {
        private readonly BuiltinRegistry _builtins = new();
        private readonly IntrinsicRegistry _intrinsics = new();

        /// <summary>
        /// Gets the builtin registry abstraction.
        /// </summary>
        public IBuiltinRegistry Builtins => _builtins;

        /// <summary>
        /// Gets the intrinsic registry abstraction.
        /// </summary>
        public IIntrinsicRegistry Intrinsics => _intrinsics;

        /// <summary>
        /// Loads every plugin DLL in the supplied directory into the VM binding registries.
        /// </summary>
        public void LoadPluginsFrom(string directory)
            => PluginLoader.LoadDirectory(directory, _builtins, _intrinsics);

        /// <summary>
        /// Loads a single plugin DLL into the VM binding registries.
        /// </summary>
        public void LoadPlugin(string dllPath)
            => PluginLoader.LoadDll(dllPath, _builtins, _intrinsics);

        /// <summary>
        /// Tries to bind a builtin by name.
        /// </summary>
        private bool TryGetBuiltin(string name, out BuiltinDescriptor descriptor)
            => _builtins.TryGet(name, out descriptor);

        /// <summary>
        /// Copies the current binding layer into another VM binding surface.
        /// </summary>
        private void CopyBindingsTo(IVmBindingRuntime target)
        {
            foreach (BuiltinDescriptor descriptor in _builtins.Snapshot())
                target.Builtins.Register(descriptor);

            foreach ((Type ReceiverType, IntrinsicDescriptor Descriptor) entry in _intrinsics.Snapshot())
                target.Intrinsics.Register(entry.ReceiverType, entry.Descriptor);
        }

        /// <summary>
        /// Tries to bind an intrinsic for the given receiver and member name.
        /// </summary>
        private bool TryBindIntrinsic(object receiver, string name, out IntrinsicBound bound, Instruction instr)
        {
            Type receiverType = receiver?.GetType() ?? typeof(object);
            if (_intrinsics.TryGet(receiverType, name, out IntrinsicDescriptor descriptor))
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
            => _intrinsics.TryGet(receiverType, name, out _);

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
