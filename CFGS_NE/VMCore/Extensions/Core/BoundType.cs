using CFGS_VM.VMCore.Extensions.Instance;

namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="BoundType" />
    /// </summary>
    public sealed class BoundType
    {
        /// <summary>
        /// Gets the Type
        /// </summary>
        public StaticInstance Type { get; }

        /// <summary>
        /// Gets the Outer
        /// </summary>
        public object Outer { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BoundType"/> class.
        /// </summary>
        /// <param name="type">The type<see cref="StaticInstance"/></param>
        /// <param name="outer">The outer<see cref="object"/></param>
        public BoundType(StaticInstance type, object outer)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Outer = outer ?? throw new ArgumentNullException(nameof(outer));
        }

        /// <summary>
        /// The ToString
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        public override string ToString() => $"<boundtype {Type.ClassName}>";
    }
}

