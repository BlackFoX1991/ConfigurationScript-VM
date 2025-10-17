namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="BuiltinCallable" />
    /// </summary>
    public sealed class BuiltinCallable
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
        /// Initializes a new instance of the <see cref="BuiltinCallable"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="min">The min<see cref="int"/></param>
        /// <param name="max">The max<see cref="int"/></param>
        public BuiltinCallable(string name, int min, int max)
        {
            Name = name; ArityMin = min; ArityMax = max;
        }

        /// <summary>
        /// The ToString
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        public override string ToString() => $"<builtin {Name}/{ArityMin}..{ArityMax}>";
    }
}

