using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Lowering;
using CFGS_VM.Analytic.Modules;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Plugin;

namespace CFGS_VM.Analytic
{
    public sealed record FrontendBuildResult(List<Stmt> SyntaxAst, List<Stmt> LoweredAst);
    public sealed record FrontendTolerantBuildResult(List<Stmt> SyntaxAst, List<Stmt> LoweredAst, IReadOnlyList<ParserException> Diagnostics);

    public sealed class FrontendPipeline
    {
        private readonly Action<string>? _loadPluginDll;
        private readonly Func<string, string?>? _loadImportSource;
        private readonly INamespaceRegistry? _namespaces;
        private readonly string? _workingDirectory;
        private readonly IReadOnlyList<string>? _additionalSearchRoots;
        private readonly Dictionary<string, List<Stmt>>? _sharedAstByHash;
        private readonly Stack<string>? _sharedImportStack;
        private readonly ModuleSyntaxCache? _syntaxCache;

        public FrontendPipeline(
            Action<string>? loadPluginDll = null,
            Func<string, string?>? loadImportSource = null,
            INamespaceRegistry? namespaces = null,
            string? workingDirectory = null,
            Dictionary<string, List<Stmt>>? sharedAstByHash = null,
            Stack<string>? sharedImportStack = null,
            IReadOnlyList<string>? additionalSearchRoots = null,
            ModuleSyntaxCache? syntaxCache = null)
        {
            _loadPluginDll = loadPluginDll;
            _loadImportSource = loadImportSource;
            _namespaces = namespaces;
            _workingDirectory = NormalizeWorkingDirectory(workingDirectory);
            _additionalSearchRoots = additionalSearchRoots;
            _sharedAstByHash = sharedAstByHash;
            _sharedImportStack = sharedImportStack;
            _syntaxCache = syntaxCache;
        }

        public FrontendBuildResult BuildLoweredAstWithSyntax(
            string origin,
            string sourceText,
            Parser.TopLevelMode topLevelMode = Parser.TopLevelMode.Script)
        {
            Lexer lexer = new(origin, sourceText);
            Parser parser = new(lexer, topLevelMode);
            SourceResolver sourceResolver = new(_workingDirectory, _additionalSearchRoots);
            ImportResolver importResolver = new(
                _loadPluginDll,
                _loadImportSource,
                _namespaces,
                _sharedAstByHash,
                _sharedImportStack,
                sourceResolver,
                _syntaxCache);

            List<Stmt> syntaxAst = parser.Parse();
            List<Stmt> resolvedAst = importResolver.ResolveImports(syntaxAst);
            List<Stmt> loweredAst = new SyntaxLowerer().Lower(resolvedAst);
            return new FrontendBuildResult(syntaxAst, loweredAst);
        }

        public List<Stmt> BuildLoweredAst(
            string origin,
            string sourceText,
            Parser.TopLevelMode topLevelMode = Parser.TopLevelMode.Script)
        {
            return BuildLoweredAstWithSyntax(origin, sourceText, topLevelMode).LoweredAst;
        }

        public FrontendTolerantBuildResult BuildLoweredAstTolerant(
            string origin,
            string sourceText,
            Parser.TopLevelMode topLevelMode = Parser.TopLevelMode.Script)
        {
            List<ParserException> diagnostics = [];
            List<Stmt> syntaxAst = [];

            try
            {
                Lexer lexer = new(origin, sourceText);
                Parser parser = new(lexer, topLevelMode, tolerant: true);
                ParserParseResult parseResult = parser.ParseTolerant();
                syntaxAst = parseResult.Statements;
                diagnostics.AddRange(parseResult.Diagnostics);
            }
            catch (Exception ex) when (ex is ParserException or LexerException)
            {
                diagnostics.Add(NormalizeDiagnostic(ex));
            }

            SourceResolver sourceResolver = new(_workingDirectory, _additionalSearchRoots);
            ImportResolver importResolver = new(
                _loadPluginDll,
                _loadImportSource,
                _namespaces,
                _sharedAstByHash,
                _sharedImportStack,
                sourceResolver,
                _syntaxCache);

            List<Stmt> resolvedAst = syntaxAst;
            try
            {
                resolvedAst = importResolver.ResolveImports(syntaxAst);
            }
            catch (Exception ex) when (ex is ParserException or LexerException)
            {
                diagnostics.Add(NormalizeDiagnostic(ex));
            }

            List<Stmt> loweredAst = resolvedAst;
            try
            {
                loweredAst = new SyntaxLowerer().Lower(resolvedAst);
            }
            catch (Exception ex) when (ex is ParserException or LexerException)
            {
                diagnostics.Add(NormalizeDiagnostic(ex));
            }

            return new FrontendTolerantBuildResult(syntaxAst, loweredAst, diagnostics);
        }

        /// <summary>
        /// Baut die Kette moeglicher Projekt-/Source-Roots von der Datei aufwaerts. CFGS loest relative
        /// Imports gegen das Arbeitsverzeichnis der Einstiegs-Datei auf; wenn Tools (LSP, Einzeldatei-Lint)
        /// die Einstiegs-Datei nicht kennen, kann jedes Vorfahren-Verzeichnis der Projekt-Root sein.
        /// Bei bekanntem <paramref name="boundaryDirectory"/> (Workspace-/Lint-Ordner) wird bis dorthin
        /// inklusive hochgelaufen, sonst nur <paramref name="maxUnboundedLevels"/> Ebenen.
        /// </summary>
        public static IReadOnlyList<string> BuildAncestorSearchRoots(
            string? startFileOrDirectory,
            string? boundaryDirectory,
            int maxUnboundedLevels = 6)
        {
            if (string.IsNullOrWhiteSpace(startFileOrDirectory))
                return Array.Empty<string>();

            string? directory;
            try
            {
                string full = Path.GetFullPath(startFileOrDirectory);
                directory = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            }
            catch (ArgumentException)
            {
                return Array.Empty<string>();
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }

            if (string.IsNullOrWhiteSpace(directory))
                return Array.Empty<string>();

            string? boundary = null;
            if (!string.IsNullOrWhiteSpace(boundaryDirectory))
            {
                try { boundary = Path.GetFullPath(boundaryDirectory); }
                catch (ArgumentException) { }
                catch (IOException) { }
            }

            List<string> roots = new();
            string? current = directory;
            int guard = 0;
            while (!string.IsNullOrWhiteSpace(current) && guard++ < 64)
            {
                roots.Add(current!);

                if (boundary is not null && string.Equals(current, boundary, StringComparison.OrdinalIgnoreCase))
                    break;

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;

                current = parent;

                if (boundary is null && roots.Count >= maxUnboundedLevels)
                    break;
            }

            return roots;
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

        private static ParserException NormalizeDiagnostic(Exception ex)
        {
            return ex switch
            {
                ParserException parserException => parserException,
                LexerException lexerException => new ParserException(
                    lexerException.RawMessage,
                    lexerException.Line,
                    lexerException.Column,
                    lexerException.Filename,
                    lexerException.SpanLength,
                    lexerException.DiagnosticCode),
                _ => new ParserException(ex.Message, 1, 1, string.Empty, diagnosticCode: "CFGS9000")
            };
        }
    }
}
