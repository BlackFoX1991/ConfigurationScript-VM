namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="ClassInfo" />
    /// </summary>
    public sealed class ClassInfo
    {
        /// <summary>
        /// Gets the Name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the BaseName
        /// </summary>
        public string? BaseName { get; }

        /// <summary>
        /// Gets the InstanceMembers
        /// </summary>
        public HashSet<string> InstanceMembers { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets the StaticMembers
        /// </summary>
        public HashSet<string> StaticMembers { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="ClassInfo"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="baseName">The baseName<see cref="string?"/></param>
        public ClassInfo(string name, string? baseName)
        {
            Name = name;
            BaseName = baseName;
        }

        /// <summary>
        /// The IsInstanceMember
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool IsInstanceMember(string name) => InstanceMembers.Contains(name);

        /// <summary>
        /// The IsStaticMember
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool IsStaticMember(string name) => StaticMembers.Contains(name);
    }

}
