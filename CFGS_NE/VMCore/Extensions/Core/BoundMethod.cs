using CFGS_VM.VMCore.Extensions.Instance;

namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="BoundMethod" />
    /// </summary>
    public sealed class BoundMethod
    {
        /// <summary>
        /// Gets the Function
        /// </summary>
        public Closure Function { get; }

        /// <summary>
        /// Gets the Receiver
        /// </summary>
        public object Receiver { get; }

        /// <summary>
        /// Gets the DeclaringType
        /// </summary>
        public StaticInstance? DeclaringType { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BoundMethod"/> class.
        /// </summary>
        /// <param name="function">The function<see cref="Closure"/></param>
        /// <param name="receiver">The receiver<see cref="object"/></param>
        /// <param name="declaringType">The declaringType<see cref="StaticInstance?"/></param>
        public BoundMethod(Closure function, object receiver, StaticInstance? declaringType = null)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
            Receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
            DeclaringType = declaringType;
        }

        /// <summary>
        /// The ToString
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        public override string ToString() => $"<bound {Function}>";
    }
}
