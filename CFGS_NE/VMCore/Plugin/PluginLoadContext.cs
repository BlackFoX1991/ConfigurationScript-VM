using System.Reflection;
using System.Runtime.Loader;

namespace CFGS_VM.VMCore.Plugin
{
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CFGS_VM"
        };

        public PluginLoadContext(string pluginPath)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Host contracts must stay in default context for type identity.
            string? n = assemblyName.Name;

            if (!string.IsNullOrWhiteSpace(n) && SharedAssemblyNames.Contains(n))
                return null;

            // Resolve plugin-private dependencies via resolver.
            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path != null)
                return LoadFromAssemblyPath(path);

            return null;
        }
    }
}
