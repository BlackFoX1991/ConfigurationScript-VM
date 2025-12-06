using System.Reflection;
using System.Runtime.Loader;

namespace CFGS_VM.VMCore.Plugin
{
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        // Optional: Name deiner Host-Assembly(s)
        private static readonly string[] SharedPrefixes =
        {
            "CFGS_VM",
            "CFGS"
        };

        public PluginLoadContext(string pluginPath)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // 1) WICHTIG: Alles was zum Host gehört, NICHT im Plugin-Context laden
            string? n = assemblyName.Name;

            if (!string.IsNullOrWhiteSpace(n))
            {
                foreach (var p in SharedPrefixes)
                {
                    if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    {
                        // null => Default Context übernimmt
                        return null;
                    }
                }
            }

            // 2) Normale Dependency-Auflösung
            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path != null)
                return LoadFromAssemblyPath(path);

            return null;
        }
    }
}
