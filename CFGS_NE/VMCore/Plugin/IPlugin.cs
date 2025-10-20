namespace CFGS_VM.VMCore.Plugin
{
    /// <summary>
    /// The BuiltinInvoker
    /// </summary>
    /// <param name="args">The args<see cref="List{object}"/></param>
    /// <param name="instr">The instr<see cref="Instruction"/></param>
    /// <returns>The <see cref="object"/></returns>
    public delegate object BuiltinInvoker(List<object> args, Instruction instr);

    /// <summary>
    /// The IntrinsicInvoker
    /// </summary>
    /// <param name="receiver">The receiver<see cref="object"/></param>
    /// <param name="args">The args<see cref="List{object}"/></param>
    /// <param name="instr">The instr<see cref="Instruction"/></param>
    /// <returns>The <see cref="object"/></returns>
    public delegate object IntrinsicInvoker(object receiver, List<object> args, Instruction instr);

    /// <summary>
    /// Defines the <see cref="BuiltinDescriptor" />
    /// </summary>
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

        public bool smartAwait { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BuiltinDescriptor"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="arityMin">The arityMin<see cref="int"/></param>
        /// <param name="arityMax">The arityMax<see cref="int"/></param>
        /// <param name="invoke">The invoke<see cref="BuiltinInvoker"/></param>
        public BuiltinDescriptor(string name, int arityMin, int arityMax, BuiltinInvoker invoke, bool smartAwait = true)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ArityMin = arityMin;
            ArityMax = arityMax;
            Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            this.smartAwait = smartAwait;

        }

        /// <summary>
        /// The ToString
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        public override string ToString() => $"<builtin {Name}/{ArityMin}..{ArityMax}>";
    }

    /// <summary>
    /// Defines the <see cref="IntrinsicDescriptor" />
    /// </summary>
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

        public bool SmartAwait { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntrinsicDescriptor"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="arityMin">The arityMin<see cref="int"/></param>
        /// <param name="arityMax">The arityMax<see cref="int"/></param>
        /// <param name="invoke">The invoke<see cref="IntrinsicInvoker"/></param>
        public IntrinsicDescriptor(string name, int arityMin, int arityMax, IntrinsicInvoker invoke, bool smartAwait = true)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ArityMin = arityMin;
            ArityMax = arityMax;
            Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            SmartAwait = smartAwait;

        }

        /// <summary>
        /// The ToString
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        public override string ToString() => $"<intrinsic {Name}/{ArityMin}..{ArityMax}>";
    }

    /// <summary>
    /// Defines the <see cref="IBuiltinRegistry" />
    /// </summary>
    public interface IBuiltinRegistry
    {
        /// <summary>
        /// The Register
        /// </summary>
        /// <param name="d">The d<see cref="BuiltinDescriptor"/></param>
        void Register(BuiltinDescriptor d);

        /// <summary>
        /// The TryGet
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="d">The d<see cref="BuiltinDescriptor"/></param>
        /// <returns>The <see cref="bool"/></returns>
        bool TryGet(string name, out BuiltinDescriptor d);
    }

    /// <summary>
    /// Defines the <see cref="IIntrinsicRegistry" />
    /// </summary>
    public interface IIntrinsicRegistry
    {
        /// <summary>
        /// The Register
        /// </summary>
        /// <param name="receiverType">The receiverType<see cref="Type"/></param>
        /// <param name="d">The d<see cref="IntrinsicDescriptor"/></param>
        void Register(Type receiverType, IntrinsicDescriptor d);

        /// <summary>
        /// The Register
        /// </summary>
        /// <param name="receiverType">The receiverType<see cref="Type"/></param>
        /// <param name="ds">The ds<see cref="IEnumerable{IntrinsicDescriptor}"/></param>
        void Register(Type receiverType, IEnumerable<IntrinsicDescriptor> ds);

        /// <summary>
        /// The TryGet
        /// </summary>
        /// <param name="receiverType">The receiverType<see cref="Type"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="d">The d<see cref="IntrinsicDescriptor"/></param>
        /// <returns>The <see cref="bool"/></returns>
        bool TryGet(Type receiverType, string name, out IntrinsicDescriptor d);
    }

    /// <summary>
    /// Defines the <see cref="IVmPlugin" />
    /// </summary>
    public interface IVmPlugin
    {
        /// <summary>
        /// The Register
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics);
    }

    /// <summary>
    /// Defines the <see cref="BuiltinAttribute" />
    /// </summary>
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
    }

    /// <summary>
    /// Defines the <see cref="IntrinsicAttribute" />
    /// </summary>
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
    }
}
