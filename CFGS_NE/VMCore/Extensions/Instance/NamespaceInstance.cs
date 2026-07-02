namespace CFGS_VM.VMCore.Extensions.Instance
{
    /// <summary>
    /// A runtime namespace value. Namespaces are accessed exclusively through dot member syntax
    /// (App.Core, Ns.member); they are deliberately a distinct type from a user dictionary so that
    /// dictionary data access can require bracket syntax without breaking namespace resolution.
    /// Members are stored as raw runtime objects, exactly like the values a dictionary would hold.
    /// </summary>
    public sealed class NamespaceInstance
    {
        /// <summary>The fully qualified namespace path, used for diagnostics and printing.</summary>
        public string Name { get; }

        /// <summary>The namespace members keyed by member name.</summary>
        public Dictionary<string, object> Members { get; }

        public NamespaceInstance(string name)
        {
            Name = name ?? string.Empty;
            Members = new Dictionary<string, object>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Wraps an existing member map (shared by reference) as a namespace value. Used to expose a
        /// plugin-provided namespace root — registered as a dictionary of members — as a distinct
        /// namespace rather than a user dictionary.
        /// </summary>
        public NamespaceInstance(string name, Dictionary<string, object> members)
        {
            Name = name ?? string.Empty;
            Members = members ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }

        public override string ToString()
            => string.IsNullOrEmpty(Name) ? "<namespace>" : $"<namespace {Name}>";
    }
}
