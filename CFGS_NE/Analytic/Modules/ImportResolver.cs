using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Lowering;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Modules
{
    internal sealed class ImportResolver
    {
        private readonly Action<string>? _loadPluginDll;
        private readonly Func<string, string?>? _loadImportSource;
        private readonly Dictionary<string, List<Stmt>> _astByImportKey;
        private readonly Stack<string> _importStack;
        private readonly SourceResolver _sourceResolver;

        public ImportResolver(
            Action<string>? loadPluginDll = null,
            Func<string, string?>? loadImportSource = null,
            Dictionary<string, List<Stmt>>? sharedAstByHash = null,
            Stack<string>? sharedImportStack = null,
            SourceResolver? sourceResolver = null)
        {
            _loadPluginDll = loadPluginDll;
            _loadImportSource = loadImportSource;
            _astByImportKey = sharedAstByHash ?? new Dictionary<string, List<Stmt>>();
            _importStack = sharedImportStack ?? new Stack<string>();
            _sourceResolver = sourceResolver ?? new SourceResolver();
        }

        public bool TryHandleDllImport(string rawPath, int line, int col, string sourceFileName)
        {
            if (_loadPluginDll == null)
                return false;

            if (string.IsNullOrWhiteSpace(rawPath) ||
                !rawPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? resolvedPath = _sourceResolver.ResolveImportPath(rawPath, sourceFileName);
            if (resolvedPath is null)
            {
                throw new ParserException(
                    $"plugin dll not found '{rawPath}' (searched script dir, cwd, exe dir)",
                    line,
                    col,
                    sourceFileName);
            }

            try
            {
                _loadPluginDll(resolvedPath);
            }
            catch (Exception ex)
            {
                throw new ParserException(
                    $"failed to load plugin dll '{rawPath}': {ex.Message}",
                    line,
                    col,
                    sourceFileName);
            }

            return true;
        }

        public List<Stmt> GetImports(string path, int line, int col, string sourceFileName, string specClass = "")
        {
            if (_sourceResolver.IsHttpUrl(path, out Uri? uri))
                return GetHttpImports(uri, path, line, col, sourceFileName, specClass);

            return GetFileImports(path, line, col, sourceFileName, specClass);
        }

        private List<Stmt> GetHttpImports(Uri uri, string path, int line, int col, string sourceFileName, string specClass)
        {
            List<Stmt> result = new();
            string resourceId = uri.ToString();
            string cacheKey = BuildImportCacheKeyForUrl(resourceId);

            if (!string.IsNullOrWhiteSpace(sourceFileName) &&
                string.Equals(resourceId, sourceFileName, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Import Warning: Ignoring self-import of '{resourceId}'.");
                return result;
            }

            if (_importStack.Contains(resourceId))
            {
                string chain = string.Join(" -> ", _importStack.Reverse().Append(resourceId));
                throw new ParserException($"Import cycle detected: {chain}", line, col, sourceFileName);
            }

            if (_astByImportKey.TryGetValue(cacheKey, out List<Stmt>? cachedAst))
                return SelectImportedStatements(cachedAst, path, line, col, sourceFileName, specClass, isFileImport: false);

            byte[] bytes;
            try
            {
                bytes = _sourceResolver.DownloadHttpImportBytes(uri);
            }
            catch (Exception ex)
            {
                throw new ParserException($"failed to download '{resourceId}': {ex.Message}", line, col, sourceFileName);
            }

            _importStack.Push(resourceId);
            try
            {
                string sourceText;
                using (MemoryStream memory = new(bytes, writable: false))
                using (StreamReader reader = new(memory, detectEncodingFromByteOrderMarks: true))
                    sourceText = reader.ReadToEnd();

                List<Stmt> importedAst = ParseImportedAst(resourceId, sourceText);
                _astByImportKey[cacheKey] = importedAst;
                return SelectImportedStatements(importedAst, path, line, col, sourceFileName, specClass, isFileImport: false);
            }
            catch (ParserException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ParserException(ex.Message, line, col, sourceFileName);
            }
            finally
            {
                _importStack.Pop();
            }
        }

        private List<Stmt> GetFileImports(string path, int line, int col, string sourceFileName, string specClass)
        {
            string? resolvedPath = _sourceResolver.ResolveImportPath(path, sourceFileName);
            if (resolvedPath is null)
            {
                throw new ParserException(
                    $"import path not found '{path}' (searched script dir, cwd, exe dir)",
                    line,
                    col,
                    sourceFileName);
            }

            string fullPath = Path.GetFullPath(resolvedPath);
            string cacheKey = BuildImportCacheKeyForFile(fullPath);

            string? thisFile = string.IsNullOrWhiteSpace(sourceFileName) ? null : Path.GetFullPath(sourceFileName);
            if (thisFile != null && string.Equals(fullPath, thisFile, StringComparison.OrdinalIgnoreCase))
                throw new ParserException($"self-import of '{fullPath}'.", line, col, sourceFileName);

            if (_importStack.Contains(fullPath))
            {
                string chain = string.Join(" -> ", _importStack.Reverse().Append(fullPath));
                throw new ParserException($"Import cycle detected: {chain}", line, col, sourceFileName);
            }

            if (_astByImportKey.TryGetValue(cacheKey, out List<Stmt>? cachedAst))
                return SelectImportedStatements(cachedAst, path, line, col, sourceFileName, specClass, isFileImport: true);

            _importStack.Push(fullPath);
            try
            {
                string? overlaySource = _loadImportSource?.Invoke(fullPath);
                string sourceText;
                if (overlaySource is not null)
                {
                    sourceText = overlaySource;
                }
                else
                {
                    using StreamReader reader = new(fullPath, detectEncodingFromByteOrderMarks: true);
                    sourceText = reader.ReadToEnd();
                }

                List<Stmt> importedAst = ParseImportedAst(fullPath, sourceText);
                _astByImportKey[cacheKey] = importedAst;
                return SelectImportedStatements(importedAst, path, line, col, sourceFileName, specClass, isFileImport: true);
            }
            catch (ParserException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ParserException(ex.Message, line, col, sourceFileName);
            }
            finally
            {
                _importStack.Pop();
            }
        }

        private List<Stmt> ParseImportedAst(string sourceName, string sourceText)
        {
            Lexer lexer = new(sourceName, sourceText);
            Parser parser = new(lexer, this);
            return new SyntaxLowerer().Lower(parser.Parse());
        }

        private static List<Stmt> SelectImportedStatements(
            List<Stmt> importedAst,
            string path,
            int line,
            int col,
            string sourceFileName,
            string specClass,
            bool isFileImport)
        {
            if (string.IsNullOrWhiteSpace(specClass))
                return new List<Stmt>(importedAst);

            Stmt? cls = importedAst.FirstOrDefault(stmt => stmt is ClassDeclStmt c && c.Name == specClass);
            if (cls is null)
            {
                string target = isFileImport ? $"import file '{path}'" : $"import '{path}'";
                throw new ParserException($"Could not find class '{specClass}' in {target}", line, col, sourceFileName);
            }

            return new List<Stmt> { cls };
        }

        private static string BuildImportCacheKeyForUrl(string resourceId)
            => $"url::{resourceId}";

        private static string BuildImportCacheKeyForFile(string fullPath)
        {
            string normalized = Path.GetFullPath(fullPath);
            if (OperatingSystem.IsWindows())
                normalized = normalized.ToUpperInvariant();
            return $"file::{normalized}";
        }
    }
}
