using CFGS_VM.Analytic.Tree;

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
        /// Gets a value indicating whether this class is nested.
        /// </summary>
        public bool IsNested { get; }

        /// <summary>
        /// Gets the InstanceMembers
        /// </summary>
        public HashSet<string> InstanceMembers { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets the StaticMembers
        /// </summary>
        public HashSet<string> StaticMembers { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets the InstanceVisibility
        /// </summary>
        public Dictionary<string, MemberVisibility> InstanceVisibility { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets the StaticVisibility
        /// </summary>
        public Dictionary<string, MemberVisibility> StaticVisibility { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="ClassInfo"/> class.
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="baseName">The baseName<see cref="string?"/></param>
        /// <param name="isNested">The isNested<see cref="bool"/></param>
        public ClassInfo(string name, string? baseName, bool isNested = false)
        {
            Name = name;
            BaseName = baseName;
            IsNested = isNested;
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

        /// <summary>
        /// The TryGetInstanceVisibility
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="visibility">The visibility<see cref="MemberVisibility"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool TryGetInstanceVisibility(string name, out MemberVisibility visibility)
            => InstanceVisibility.TryGetValue(name, out visibility);

        /// <summary>
        /// The TryGetStaticVisibility
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="visibility">The visibility<see cref="MemberVisibility"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool TryGetStaticVisibility(string name, out MemberVisibility visibility)
            => StaticVisibility.TryGetValue(name, out visibility);
    }
}
