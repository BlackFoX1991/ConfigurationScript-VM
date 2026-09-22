using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore.Plugin
{
    public delegate object BuiltinInvoker(List<object> args, Instruction instr);

    public delegate object IntrinsicInvoker(object receiver, List<object> args, Instruction instr);

    /// <summary>
    /// Defines how a native builtin or intrinsic result is adapted by the VM.
    /// </summary>
    public sealed class NativeCallOptions
    {
        public bool AdaptAwaitables { get; init; } = true;

        public bool OffloadBlockingWork { get; init; } = false;

        public static NativeCallOptions Default { get; } = new();

        public static NativeCallOptions Awaitable { get; } = new()
        {
            AdaptAwaitables = true,
            OffloadBlockingWork = false
        };

        public static NativeCallOptions Direct { get; } = new()
        {
            AdaptAwaitables = false,
            OffloadBlockingWork = false
        };

        public static NativeCallOptions Offloaded { get; } = new()
        {
            AdaptAwaitables = true,
            OffloadBlockingWork = true
        };

        public NativeCallOptions()
        {
        }

        public NativeCallOptions(bool adaptAwaitables, bool offloadBlockingWork = false)
        {
            AdaptAwaitables = adaptAwaitables;
            OffloadBlockingWork = offloadBlockingWork;
        }
    }

    public sealed class BuiltinDescriptor
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the ArityMin
        /// </summary>
        public int ArityMin { get; }

        /// <summary>
        /// Gets the ArityMax
        /// </summary>
        public int ArityMax { get; }

        /// <summary>
        /// Gets the Invoke
        /// </summary>
        public BuiltinInvoker Invoke { get; }

        public NativeCallOptions Options { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BuiltinDescriptor"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="arityMin">The arityMin<see cref="int"/></param>
        /// <param name="arityMax">The arityMax<see cref="int"/></param>
        /// <param name="invoke">The invoke<see cref="BuiltinInvoker"/></param>
        public BuiltinDescriptor(
            string name,
            int arityMin,
            int arityMax,
            BuiltinInvoker invoke,
            NativeCallOptions? options = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ArityMin = arityMin;
            ArityMax = arityMax;
            Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            Options = options ?? NativeCallOptions.Default;
        }

        public override string ToString() => $"<builtin {Name}/{ArityMin}..{ArityMax}>";
    }

    public sealed class IntrinsicDescriptor
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the ArityMin
        /// </summary>
        public int ArityMin { get; }

        /// <summary>
        /// Gets the ArityMax
        /// </summary>
        public int ArityMax { get; }

        /// <summary>
        /// Gets the Invoke
        /// </summary>
        public IntrinsicInvoker Invoke { get; }

        public NativeCallOptions Options { get; }

        /// <summary>
        /// Gets a value indicating whether the member is a value rather than a method. Dot access
        /// (<c>math.PI</c>) invokes it with no arguments and yields the result instead of a bound callable,
        /// so it must accept zero arguments.
        /// </summary>
        public bool IsValue { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntrinsicDescriptor"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="arityMin">The arityMin<see cref="int"/></param>
        /// <param name="arityMax">The arityMax<see cref="int"/></param>
        /// <param name="invoke">The invoke<see cref="IntrinsicInvoker"/></param>
        public IntrinsicDescriptor(
            string name,
            int arityMin,
            int arityMax,
            IntrinsicInvoker invoke,
            NativeCallOptions? options = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ArityMin = arityMin;
            ArityMax = arityMax;
            Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            Options = options ?? NativeCallOptions.Default;
        }

        public override string ToString() => $"<intrinsic {Name}/{ArityMin}..{ArityMax}>";
    }

    /// <summary>
    /// Defines a native namespace exposed by a plugin or the standard library.
    /// </summary>
    public sealed class NamespaceDescriptor
    {
        public string Path { get; }

        public object Instance { get; }

        public IReadOnlyList<string> Members { get; }

        public NamespaceDescriptor(string path, object instance, IEnumerable<string> members)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Namespace path must not be empty.", nameof(path));

            string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || string.Join(".", parts) != path)
                throw new ArgumentException($"Invalid namespace path '{path}'.", nameof(path));

            foreach (string part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    throw new ArgumentException($"Invalid namespace path '{path}'.", nameof(path));
            }

            Path = path;
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            Members = members?.Where(static member => !string.IsNullOrWhiteSpace(member))
                              .Distinct(StringComparer.Ordinal)
                              .ToArray()
                      ?? throw new ArgumentNullException(nameof(members));
        }

        public static NamespaceDescriptor FromBuiltins(
            string path,
            IEnumerable<(string MemberName, string BuiltinName, int ArityMin, int ArityMax)> members)
        {
            Dictionary<string, object> instance = new(StringComparer.Ordinal);
            List<string> memberNames = new();

            foreach ((string memberName, string builtinName, int arityMin, int arityMax) in members)
            {
                if (string.IsNullOrWhiteSpace(memberName))
                    throw new ArgumentException("Namespace member name must not be empty.", nameof(members));

                instance[memberName] = new BuiltinCallable(builtinName, arityMin, arityMax);
                memberNames.Add(memberName);
            }

            return new NamespaceDescriptor(path, instance, memberNames);
        }

        public static NamespaceDescriptor FromDescriptors(
            string path,
            IEnumerable<(string MemberName, BuiltinDescriptor Descriptor)> members)
        {
            Dictionary<string, object> instance = new(StringComparer.Ordinal);
            List<string> memberNames = new();

            foreach ((string memberName, BuiltinDescriptor descriptor) in members)
            {
                if (string.IsNullOrWhiteSpace(memberName))
                    throw new ArgumentException("Namespace member name must not be empty.", nameof(members));

                instance[memberName] = new NamespaceBuiltinCallable(memberName, descriptor);
                memberNames.Add(memberName);
            }

            return new NamespaceDescriptor(path, instance, memberNames);
        }
    }

    public sealed class NamespaceBuiltinCallable
    {
        public string MemberName { get; }

        public BuiltinDescriptor Descriptor { get; }

        public NamespaceBuiltinCallable(string memberName, BuiltinDescriptor descriptor)
        {
            MemberName = memberName ?? throw new ArgumentNullException(nameof(memberName));
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public override string ToString()
            => $"<builtin {MemberName}/{Descriptor.ArityMin}..{Descriptor.ArityMax}>";
    }

    public interface IBuiltinRegistry
    {
        void Register(BuiltinDescriptor d);

        bool TryGet(string name, out BuiltinDescriptor d);

        bool Contains(string name) => TryGet(name, out _);

        bool Remove(string name)
            => throw new NotSupportedException("This builtin registry does not support removal.");
    }

    public interface IIntrinsicRegistry
    {
        void Register(Type receiverType, IntrinsicDescriptor d);

        void Register(Type receiverType, IEnumerable<IntrinsicDescriptor> ds);

        bool TryGet(Type receiverType, string name, out IntrinsicDescriptor d);

        bool ContainsExact(Type receiverType, string name) => TryGet(receiverType, name, out _);

        bool RemoveExact(Type receiverType, string name)
            => throw new NotSupportedException("This intrinsic registry does not support removal.");
    }

    public interface INamespaceRegistry
    {
        void Register(NamespaceDescriptor descriptor);

        bool TryGet(string path, out NamespaceDescriptor descriptor);

        bool Contains(string path) => TryGet(path, out _);

        bool TryGetRoot(string name, out object value);

        bool Remove(string path)
            => throw new NotSupportedException("This namespace registry does not support removal.");

        IReadOnlyList<NamespaceDescriptor> Snapshot();
    }

    public interface IVmPlugin
    {
        void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces);
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class BuiltinAttribute : Attribute
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the ArityMin
        /// </summary>
        public int ArityMin { get; }

        /// <summary>
        /// Gets the ArityMax
        /// </summary>
        public int ArityMax { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BuiltinAttribute"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="arityMin">The arityMin<see cref="int"/></param>
        /// <param name="arityMax">The arityMax<see cref="int"/></param>
        public BuiltinAttribute(string name, int arityMin, int arityMax)
        {
            Name = name;
            ArityMin = arityMin;
            ArityMax = arityMax;
        }

        public bool AdaptAwaitables { get; set; } = true;

        public bool OffloadBlockingWork { get; set; } = false;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class IntrinsicAttribute : Attribute
    {
        /// <summary>
        /// Gets the ReceiverType
        /// </summary>
        public Type ReceiverType { get; }

        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the ArityMin
        /// </summary>
        public int ArityMin { get; }

        /// <summary>
        /// Gets the ArityMax
        /// </summary>
        public int ArityMax { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntrinsicAttribute"/> class.
        /// </summary>
        /// <param name="receiverType">The receiverType<see cref="Type"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="arityMin">The arityMin<see cref="int"/></param>
        /// <param name="arityMax">The arityMax<see cref="int"/></param>
        public IntrinsicAttribute(Type receiverType, string name, int arityMin, int arityMax)
        {
            ReceiverType = receiverType;
            Name = name;
            ArityMin = arityMin;
            ArityMax = arityMax;
        }

        public bool AdaptAwaitables { get; set; } = true;

        public bool OffloadBlockingWork { get; set; } = false;
    }
}
