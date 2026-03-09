namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="NamedArgument" />
    /// </summary>
    public sealed class NamedArgument
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Value
        /// </summary>
        public object? Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NamedArgument"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object?"/></param>
        public NamedArgument(string name, object? value)
        {
            Name = name;
            Value = value;
        }
    }
}
