using System.Reflection;
using System.Runtime.Loader;

namespace CFGS_VM.VMCore.Plugin
{
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginDirectory;

        private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CFGS_VM"
        };

        public PluginLoadContext(string pluginPath)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _pluginDirectory = Path.GetDirectoryName(pluginPath) ?? AppContext.BaseDirectory;
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

            // Some plugins ship additional runtime assemblies next to the DLL
            // without listing them in deps.json. Fall back to local probing so
            // plugin-private closures can still load in their own context.
            if (!string.IsNullOrWhiteSpace(n))
            {
                string localCandidate = Path.Combine(_pluginDirectory, n + ".dll");
                if (File.Exists(localCandidate))
                    return LoadFromAssemblyPath(localCandidate);
            }

            return null;
        }
    }
}
