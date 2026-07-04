using CFGS_VM.Analytic;
using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Plugin;
using System.Security.Cryptography;
using System.Text;

namespace CFGS_VM.VMCore
{
    public static class CfgsCompiler
    {
        public static CompiledScript CompileFile(
            string path,
            Action<string>? loadPluginDll = null,
            INamespaceRegistry? namespaces = null,
            string compilerVersion = "",
            IReadOnlyList<string>? additionalSearchRoots = null)
        {
            string source = File.ReadAllText(path);
            string workingDirectory =
                FrontendPipeline.TryGetWorkingDirectory(path)
                ?? Path.GetDirectoryName(Path.GetFullPath(path))
                ?? Environment.CurrentDirectory;

            return CompileSource(path, source, loadPluginDll, namespaces, workingDirectory, compilerVersion, additionalSearchRoots);
        }

        public static CompiledScript CompileSource(
            string name,
            string source,
            Action<string>? loadPluginDll = null,
            INamespaceRegistry? namespaces = null,
            string? workingDirectory = null,
            string compilerVersion = "",
            IReadOnlyList<string>? additionalSearchRoots = null)
        {
            List<string> requiredPlugins = new();
            Action<string> trackingPluginLoader = path =>
            {
                string fullPath = Path.GetFullPath(path);
                requiredPlugins.Add(fullPath);
                loadPluginDll?.Invoke(fullPath);
            };

            // Wenn der Aufrufer (Laufzeit/REPL) keine expliziten Suchwurzeln uebergibt, wird derselbe
            // kanonische Wurzelsatz berechnet, den Linter und LSP verwenden. Damit loest die Ausfuehrung
            // dieselben Importe auf wie das Tooling (kein "im Editor gruen, zur Laufzeit rot").
            IReadOnlyList<string> effectiveSearchRoots =
                additionalSearchRoots ?? FrontendPipeline.BuildImportSearchRoots(name, explicitBoundary: null);

            FrontendPipeline frontendPipeline = new(
                loadPluginDll: trackingPluginLoader,
                namespaces: namespaces,
                workingDirectory: workingDirectory,
                additionalSearchRoots: effectiveSearchRoots);

            FrontendBuildResult frontendBuild = frontendPipeline.BuildLoweredAstWithSyntax(name, source);
            Compiler compiler = new(name);
            List<Instruction> bytecode = compiler.Compile(frontendBuild.LoweredAst);

            return new CompiledScript(
                name,
                ComputeSourceHash(source),
                compilerVersion,
                CompiledScript.CurrentBytecodeVersion,
                requiredPlugins,
                bytecode,
                compiler.Functions,
                compiler.RootScopeDescriptors);
        }

        private static string ComputeSourceHash(string source)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
