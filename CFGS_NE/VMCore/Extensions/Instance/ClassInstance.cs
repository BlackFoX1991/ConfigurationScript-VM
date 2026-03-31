namespace CFGS_VM.VMCore.Extensions.Instance
{
    /// <summary>
    /// Defines the <see cref="ClassInstance" />
    /// </summary>
    public sealed class ClassInstance
    {
        /// <summary>
        /// Synchronizes field access for this runtime instance.
        /// </summary>
        public object SyncRoot { get; } = new();

        /// <summary>
        /// Gets the ClassName
        /// </summary>
        public string ClassName { get; }

        /// <summary>
        /// Gets the Fields
        /// </summary>
        public Dictionary<string, object> Fields { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClassInstance"/> class.
        /// </summary>
        /// <param name="className">The className<see cref="string"/></param>
        public ClassInstance(string className)
        {
            ClassName = className;
            Fields = new Dictionary<string, object>();
        }
    }
}
