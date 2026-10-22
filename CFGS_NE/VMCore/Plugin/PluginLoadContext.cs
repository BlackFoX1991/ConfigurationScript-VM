using System.Reflection;
using System.Runtime.Loader;
using CFGS_VM.Packaging;

namespace CFGS_VM.VMCore.Plugin
{
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginDirectory;
        private readonly string? _managedPluginDirectory;

        private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CFGS_VM"
        };

        public PluginLoadContext(string pluginPath)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _pluginDirectory = Path.GetDirectoryName(pluginPath) ?? AppContext.BaseDirectory;
            _managedPluginDirectory = CfgsPackageManager.GetManagedPluginDirectory(pluginPath);
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
            {
                VerifyDependency(path);
                return LoadFromAssemblyPath(path);
            }

            // Some plugins ship additional runtime assemblies next to the DLL
            // without listing them in deps.json. Fall back to local probing so
            // plugin-private closures can still load in their own context.
            if (!string.IsNullOrWhiteSpace(n))
            {
                string localCandidate = Path.Combine(_pluginDirectory, n + ".dll");
                if (File.Exists(localCandidate))
                {
                    VerifyDependency(localCandidate);
                    return LoadFromAssemblyPath(localCandidate);
                }
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path != null)
            {
                VerifyDependency(path);
                return LoadUnmanagedDllFromPath(path);
            }

            string localCandidate = Path.Combine(_pluginDirectory, unmanagedDllName);
            if (File.Exists(localCandidate))
            {
                VerifyDependency(localCandidate);
                return LoadUnmanagedDllFromPath(localCandidate);
            }

            if (!unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                string localDllCandidate = Path.Combine(_pluginDirectory, unmanagedDllName + ".dll");
                if (File.Exists(localDllCandidate))
                {
                    VerifyDependency(localDllCandidate);
                    return LoadUnmanagedDllFromPath(localDllCandidate);
                }
            }

            return IntPtr.Zero;
        }

        private void VerifyDependency(string path)
        {
            if (_managedPluginDirectory is null)
                return;

            string fullPath = Path.GetFullPath(path);
            string relative = Path.GetRelativePath(_managedPluginDirectory, fullPath);
            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException($"package dependency '{fullPath}' is outside the plugin directory. Run restore again.");

            CfgsPackageManager.VerifyLockedPluginArtifact(fullPath);
        }
    }
}
