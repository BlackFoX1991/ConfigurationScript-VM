namespace CFGS_VM.VMCore.Extensions.Core
{
    public sealed class SpreadArgument
    {
        /// <summary>
        /// Gets the Value
        /// </summary>
        public object? Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SpreadArgument"/> class.
        /// </summary>
        /// <param name="value">The value<see cref="object?"/></param>
        public SpreadArgument(object? value)
        {
            Value = value;
        }
    }
}
