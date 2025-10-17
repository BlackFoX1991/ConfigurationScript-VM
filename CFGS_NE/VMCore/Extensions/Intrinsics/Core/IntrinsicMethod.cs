namespace CFGS_VM.VMCore.Extensions.Intrinsics.Core
{

    public sealed class IntrinsicMethod
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

        /// <summary>
        /// Initializes a new instance of the <see cref="IntrinsicMethod"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="arityMin">The arityMin<see cref="int"/></param>
        /// <param name="arityMax">The arityMax<see cref="int"/></param>
        /// <param name="invoke">The invoke<see cref="IntrinsicInvoker"/></param>
        public IntrinsicMethod(string name, int arityMin, int arityMax, IntrinsicInvoker invoke)
        {
            Name = name; ArityMin = arityMin; ArityMax = arityMax; Invoke = invoke;
        }

        /// <summary>
        /// The ToString
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        public override string ToString() => $"<intrinsic {Name}>";
    }
}

