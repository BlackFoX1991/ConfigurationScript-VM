using CFGS_VM.VMCore.Extensions.Instance;

namespace CFGS_VM.VMCore.Extensions.Core
{
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

        public override string ToString() => $"<boundtype {Type.ClassName}>";
    }
}

