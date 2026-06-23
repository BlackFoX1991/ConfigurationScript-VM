using CFGS_VM.VMCore;

namespace CFGS_VM.VMCore.Extensions.Core
{
    public sealed class SpreadArgument
    {
        /// <summary>
        /// Gets the Value
        /// </summary>
        public Value Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SpreadArgument"/> class.
        /// </summary>
        /// <param name="value">The value<see cref="Value"/></param>
        public SpreadArgument(Value value)
        {
            Value = value;
        }
    }
}
