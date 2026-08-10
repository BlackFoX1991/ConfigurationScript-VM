using CFGS_VM.VMCore;

namespace CFGS_VM.Hosting
{
    public sealed class CfgsScriptEngine
    {
        private readonly List<string> _pluginPaths = [];
        private readonly List<string> _pluginDirectories = [];
        private readonly Dictionary<string, object?> _services = new(StringComparer.Ordinal);

        public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

        public string NativeAssemblyBaseDirectory { get; set; } = string.Empty;

        public string CompilerVersion { get; set; } = string.Empty;

        public CfgsScriptPermissions Permissions { get; } = new();

        public IReadOnlyDictionary<string, object?> Services => _services;

        public void SetService(string name, object? service)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Service name must not be empty.", nameof(name));

            _services[name] = service;
        }

        public void LoadPlugin(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Plugin path must not be empty.", nameof(path));
            if (!Permissions.AllowPluginLoading)
                throw new InvalidOperationException("Plugin loading is disabled for this engine.");

            _pluginPaths.Add(Path.GetFullPath(path));
        }

        public void LoadPluginsFromDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Plugin directory must not be empty.", nameof(directory));
            if (!Permissions.AllowPluginLoading)
                throw new InvalidOperationException("Plugin loading is disabled for this engine.");

            _pluginDirectories.Add(Path.GetFullPath(directory));
        }

        public CfgsScript CompileFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Script path must not be empty.", nameof(path));

            string fullPath = Path.GetFullPath(path);
            string scriptDirectory = Path.GetDirectoryName(fullPath) ?? WorkingDirectory;
            try
            {
                VM vm = CreateVm(fullPath, scriptDirectory);
                CompiledScript compiled = CfgsCompiler.CompileFile(
                    fullPath,
                    loadPluginDll: Permissions.AllowPluginLoading ? vm.LoadPlugin : null,
                    namespaces: vm.Namespaces,
                    compilerVersion: CompilerVersion);

                vm.LoadCompiledScript(compiled);
                ApplyServices(vm);
                return new CfgsScript(vm, compiled, fullPath, Permissions.Clone());
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, fullPath);
            }
        }

        public CfgsScript CompileSource(string name, string source)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Script name must not be empty.", nameof(name));
            ArgumentNullException.ThrowIfNull(source);

            string workingDirectory = ResolveWorkingDirectory(name);
            try
            {
                VM vm = CreateVm(name, workingDirectory);
                CompiledScript compiled = CfgsCompiler.CompileSource(
                    name,
                    source,
                    loadPluginDll: Permissions.AllowPluginLoading ? vm.LoadPlugin : null,
                    namespaces: vm.Namespaces,
                    workingDirectory: workingDirectory,
                    compilerVersion: CompilerVersion);

                vm.LoadCompiledScript(compiled);
                ApplyServices(vm);
                return new CfgsScript(vm, compiled, name, Permissions.Clone());
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, name);
            }
        }

        public CfgsScript LoadCfb(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("CFB path must not be empty.", nameof(path));

            string fullPath = Path.GetFullPath(path);
            string workingDirectory = Path.GetDirectoryName(fullPath) ?? Path.GetFullPath(WorkingDirectory);
            try
            {
                CompiledScript compiled = CfbFile.Load(fullPath);
                VM vm = CreateVm(fullPath, workingDirectory);
                foreach (string plugin in compiled.RequiredPlugins)
                    vm.LoadPlugin(ResolvePluginPath(plugin, workingDirectory));

                vm.LoadCompiledScript(compiled);
                ApplyServices(vm);
                return new CfgsScript(vm, compiled, fullPath, Permissions.Clone());
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, fullPath);
            }
        }

        public CompiledScript CompileFileToCfb(string inputPath, string outputPath)
        {
            CfgsScript script = CompileFile(inputPath);
            CfbFile.Save(script.CompiledScript, outputPath);
            return script.CompiledScript;
        }

        private VM CreateVm(string entryScriptPath, string workingDirectory)
        {
            VM vm = new()
            {
                EntryScriptPath = entryScriptPath,
                WorkingDirectory = workingDirectory,
                NativeAssemblyBaseDirectory = string.IsNullOrWhiteSpace(NativeAssemblyBaseDirectory)
                    ? workingDirectory
                    : Path.GetFullPath(NativeAssemblyBaseDirectory),
                AllowFileIO = Permissions.AllowFileIO,
                AllowNativeInterop = Permissions.AllowNativeInterop,
                AllowPluginLoading = Permissions.AllowPluginLoading,
                RestrictFileIoToWorkspace = Permissions.RestrictFileIoToWorkspace
            };

            foreach (string directory in _pluginDirectories)
                vm.LoadPluginsFrom(directory);

            foreach (string pluginPath in _pluginPaths)
                vm.LoadPlugin(pluginPath);

            return vm;
        }

        private void ApplyServices(VM vm)
        {
            foreach ((string name, object? service) in _services)
                vm.SetGlobal(name, service);
        }

        /// <summary>
        /// Resolves a <c>.cfb</c> plugin reference against the script directory. The reference arrives
        /// already reduced to a bare file name (<c>CfbFile.ReadPluginReference</c>); expansion is applied
        /// because a reference may legitimately be written with an environment variable, and the file name
        /// is taken again afterwards because expansion can reintroduce a path (<c>"%TEMP%evil.dll"</c>
        /// expands to an absolute one). A compiled artifact must never be able to point the loader at a
        /// location it chose itself, so no fully qualified reference is passed through.
        /// </summary>
        private static string ResolvePluginPath(string pluginReference, string workingDirectory)
        {
            string plugin = Path.GetFileName(Environment.ExpandEnvironmentVariables(pluginReference.Trim()));
            if (string.IsNullOrWhiteSpace(plugin))
                throw new InvalidDataException($"plugin reference '{pluginReference}' does not name a plugin file");

            return Path.GetFullPath(Path.Combine(workingDirectory, plugin));
        }

        private string ResolveWorkingDirectory(string scriptName)
        {
            if (!string.IsNullOrWhiteSpace(scriptName) &&
                !scriptName.StartsWith('<') &&
                Path.GetDirectoryName(scriptName) is string scriptDirectory &&
                !string.IsNullOrWhiteSpace(scriptDirectory))
            {
                return Path.GetFullPath(scriptDirectory);
            }

            return Path.GetFullPath(WorkingDirectory);
        }
    }
}
