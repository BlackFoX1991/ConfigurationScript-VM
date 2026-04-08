using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Lowering;
using CFGS_VM.Analytic.Modules;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic
{
    public sealed class FrontendPipeline
    {
        private readonly Action<string>? _loadPluginDll;
        private readonly Func<string, string?>? _loadImportSource;
        private readonly string? _workingDirectory;
        private readonly Dictionary<string, List<Stmt>>? _sharedAstByHash;
        private readonly Stack<string>? _sharedImportStack;

        public FrontendPipeline(
            Action<string>? loadPluginDll = null,
            Func<string, string?>? loadImportSource = null,
            string? workingDirectory = null,
            Dictionary<string, List<Stmt>>? sharedAstByHash = null,
            Stack<string>? sharedImportStack = null)
        {
            _loadPluginDll = loadPluginDll;
            _loadImportSource = loadImportSource;
            _workingDirectory = NormalizeWorkingDirectory(workingDirectory);
            _sharedAstByHash = sharedAstByHash;
            _sharedImportStack = sharedImportStack;
        }

        public List<Stmt> BuildLoweredAst(string origin, string sourceText)
        {
            Lexer lexer = new(origin, sourceText);
            Parser parser = new(lexer);
            SourceResolver sourceResolver = new(_workingDirectory);
            ImportResolver importResolver = new(
                _loadPluginDll,
                _loadImportSource,
                _sharedAstByHash,
                _sharedImportStack,
                sourceResolver);

            List<Stmt> ast = parser.Parse();
            ast = importResolver.ResolveImports(ast);
            return new SyntaxLowerer().Lower(ast);
        }

        public static string? TryGetWorkingDirectory(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return null;

            string? localPath = TryGetLocalPath(origin);
            string? directory = localPath is null ? null : Path.GetDirectoryName(localPath);
            return NormalizeWorkingDirectory(directory);
        }

        private static string? TryGetLocalPath(string origin)
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? parsed))
            {
                if (parsed.IsFile)
                    return Path.GetFullPath(parsed.LocalPath);

                return null;
            }

            if (Path.IsPathFullyQualified(origin) || File.Exists(origin))
                return Path.GetFullPath(origin);

            return null;
        }

        private static string? NormalizeWorkingDirectory(string? workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
                return null;

            try
            {
                string fullPath = Path.GetFullPath(workingDirectory);
                return Directory.Exists(fullPath) ? fullPath : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
