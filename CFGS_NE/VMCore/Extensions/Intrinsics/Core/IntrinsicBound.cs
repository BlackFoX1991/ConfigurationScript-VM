namespace CFGS_VM.VMCore.Extensions.Intrinsics.Core
{
    public sealed class IntrinsicBound
    {
        /// <summary>
        /// Gets the Method
        /// </summary>
        public IntrinsicMethod Method { get; }

        /// <summary>
        /// Gets the Receiver
        /// </summary>
        public object Receiver { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntrinsicBound"/> class.
        /// </summary>
        /// <param name="m">The m<see cref="IntrinsicMethod"/></param>
        /// <param name="recv">The recv<see cref="object"/></param>
        public IntrinsicBound(IntrinsicMethod m, object recv)
        {
            Method = m; Receiver = recv;
        }

        /// <summary>
        /// The ToString
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        public override string ToString() => $"<bound {Method.Name}>";
    }
}
