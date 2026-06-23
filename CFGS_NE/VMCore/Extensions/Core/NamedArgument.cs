using CFGS_VM.VMCore;

namespace CFGS_VM.VMCore.Extensions.Core
{
    public sealed class NamedArgument
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public Value Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NamedArgument"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="Value"/></param>
        public NamedArgument(string name, Value value)
        {
            Name = name;
            Value = value;
        }
    }
}
