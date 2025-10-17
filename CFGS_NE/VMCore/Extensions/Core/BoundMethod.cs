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
        /// Initializes a new instance of the <see cref="BoundMethod"/> class.
        /// </summary>
        /// <param name="function">The function<see cref="Closure"/></param>
        /// <param name="receiver">The receiver<see cref="object"/></param>
        public BoundMethod(Closure function, object receiver)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
            Receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        }

        /// <summary>
        /// The ToString
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        public override string ToString() => $"<bound {Function}>";
    }
}

