using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Modules
{
    internal sealed class ModuleGraphBuilder
    {
        private readonly Func<List<Stmt>, List<Stmt>> _materializeModule;
        private readonly Func<string, string?>? _loadImportSource;
        private readonly Dictionary<string, List<Stmt>> _astByImportKey;
        private readonly Dictionary<string, ModuleGraphNode> _moduleByImportKey;
        private readonly Stack<string> _importStack;
        private readonly SourceResolver _sourceResolver;

        public ModuleGraphBuilder(
            Func<List<Stmt>, List<Stmt>> materializeModule,
            Func<string, string?>? loadImportSource = null,
            Dictionary<string, List<Stmt>>? sharedAstByHash = null,
            Stack<string>? sharedImportStack = null,
            SourceResolver? sourceResolver = null)
        {
            _materializeModule = materializeModule;
            _loadImportSource = loadImportSource;
            _astByImportKey = sharedAstByHash ?? new Dictionary<string, List<Stmt>>();
            _moduleByImportKey = new Dictionary<string, ModuleGraphNode>();
            _importStack = sharedImportStack ?? new Stack<string>();
            _sourceResolver = sourceResolver ?? new SourceResolver();
        }

        public List<Stmt> GetImports(string path, int line, int col, string sourceFileName, string specClass = "")
        {
            ModuleGraphNode? module = GetModuleNode(path, line, col, sourceFileName);
            if (module is null)
                return new List<Stmt>();

            return SelectImportedStatements(module.ResolvedStatements, path, line, col, sourceFileName, specClass, module.IsFileImport);
        }

        private ModuleGraphNode? GetModuleNode(string path, int line, int col, string sourceFileName)
        {
            if (_sourceResolver.IsHttpUrl(path, out Uri? uri))
                return GetHttpModule(uri, path, line, col, sourceFileName);

            return GetFileModule(path, line, col, sourceFileName);
        }

        private ModuleGraphNode? GetHttpModule(Uri uri, string path, int line, int col, string sourceFileName)
        {
            string resourceId = uri.ToString();
            string cacheKey = BuildImportCacheKeyForUrl(resourceId);

            if (!string.IsNullOrWhiteSpace(sourceFileName) &&
                string.Equals(resourceId, sourceFileName, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Import Warning: Ignoring self-import of '{resourceId}'.");
                return null;
            }

            if (_importStack.Contains(resourceId))
            {
                string chain = string.Join(" -> ", _importStack.Reverse().Append(resourceId));
                throw new ParserException($"Import cycle detected: {chain}", line, col, sourceFileName);
            }

            if (_moduleByImportKey.TryGetValue(cacheKey, out ModuleGraphNode? cachedNode))
                return cachedNode;

            if (_astByImportKey.TryGetValue(cacheKey, out List<Stmt>? cachedStatements))
                return GetOrCreateCachedNode(cacheKey, resourceId, cachedStatements, isFileImport: false);

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

                return BuildModuleNode(cacheKey, resourceId, sourceText, isFileImport: false);
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

        private ModuleGraphNode GetFileModule(string path, int line, int col, string sourceFileName)
        {
            string? resolvedPath = _sourceResolver.ResolveImportPath(path, sourceFileName);
            if (resolvedPath is null)
            {
                throw new ParserException(
                    $"import path not found '{path}' (searched script dir, working dir, exe dir)",
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

            if (_moduleByImportKey.TryGetValue(cacheKey, out ModuleGraphNode? cachedNode))
                return cachedNode;

            if (_astByImportKey.TryGetValue(cacheKey, out List<Stmt>? cachedStatements))
                return GetOrCreateCachedNode(cacheKey, fullPath, cachedStatements, isFileImport: true);

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

                return BuildModuleNode(cacheKey, fullPath, sourceText, isFileImport: true);
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

        private ModuleGraphNode BuildModuleNode(string cacheKey, string sourceName, string sourceText, bool isFileImport)
        {
            Lexer lexer = new(sourceName, sourceText);
            Parser parser = new(lexer);
            List<Stmt> syntaxStatements = parser.Parse();

            ModuleGraphNode node = new(
                cacheKey,
                sourceName,
                syntaxStatements,
                ExtractImportEdges(syntaxStatements),
                isFileImport);

            _moduleByImportKey[cacheKey] = node;

            foreach (ModuleImportEdge import in node.Imports)
            {
                if (import.IsDllImport)
                    continue;

                ModuleGraphNode? target = GetModuleNode(import.RawPath, import.Line, import.Col, sourceName);
                import.AttachTarget(target);
            }

            List<Stmt> materializedStatements = _materializeModule(syntaxStatements);
            node.SetResolvedStatements(materializedStatements);
            _astByImportKey[cacheKey] = materializedStatements;
            return node;
        }

        private ModuleGraphNode GetOrCreateCachedNode(string cacheKey, string sourceName, List<Stmt> cachedStatements, bool isFileImport)
        {
            if (_moduleByImportKey.TryGetValue(cacheKey, out ModuleGraphNode? node))
                return node;

            node = ModuleGraphNode.CreateResolved(cacheKey, sourceName, cachedStatements, isFileImport);
            _moduleByImportKey[cacheKey] = node;
            return node;
        }

        private static List<ModuleImportEdge> ExtractImportEdges(IEnumerable<Stmt> statements)
        {
            List<ModuleImportEdge> imports = new();

            foreach (Stmt stmt in statements)
            {
                switch (stmt)
                {
                    case BareImportSyntaxStmt bare:
                        imports.Add(new ModuleImportEdge(ModuleImportKind.Bare, bare.Path, bare.Line, bare.Col, bare.OriginFile));
                        break;
                    case NamespaceImportSyntaxStmt ns:
                        imports.Add(new ModuleImportEdge(ModuleImportKind.Namespace, ns.Path, ns.Line, ns.Col, ns.OriginFile));
                        break;
                    case NamedImportSyntaxStmt named:
                        imports.Add(new ModuleImportEdge(ModuleImportKind.Named, named.Path, named.Line, named.Col, named.OriginFile));
                        break;
                    case DefaultImportSyntaxStmt def:
                        imports.Add(new ModuleImportEdge(ModuleImportKind.Default, def.Path, def.Line, def.Col, def.OriginFile));
                        break;
                    default:
                        return imports;
                }
            }

            return imports;
        }

        private static List<Stmt> SelectImportedStatements(
            IReadOnlyList<Stmt> importedAst,
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

    internal sealed class ModuleGraphNode
    {
        public ModuleGraphNode(
            string cacheKey,
            string sourceName,
            IReadOnlyList<Stmt> syntaxStatements,
            IReadOnlyList<ModuleImportEdge> imports,
            bool isFileImport)
        {
            CacheKey = cacheKey;
            SourceName = sourceName;
            SyntaxStatements = syntaxStatements;
            Imports = imports;
            IsFileImport = isFileImport;
            ResolvedStatements = Array.Empty<Stmt>();
        }

        public string CacheKey { get; }

        public string SourceName { get; }

        public IReadOnlyList<Stmt> SyntaxStatements { get; }

        public IReadOnlyList<ModuleImportEdge> Imports { get; }

        public IReadOnlyList<Stmt> ResolvedStatements { get; private set; }

        public bool IsFileImport { get; }

        public void SetResolvedStatements(IReadOnlyList<Stmt> statements)
        {
            ResolvedStatements = statements;
        }

        public static ModuleGraphNode CreateResolved(
            string cacheKey,
            string sourceName,
            IReadOnlyList<Stmt> resolvedStatements,
            bool isFileImport)
        {
            ModuleGraphNode node = new(cacheKey, sourceName, Array.Empty<Stmt>(), Array.Empty<ModuleImportEdge>(), isFileImport);
            node.SetResolvedStatements(resolvedStatements);
            return node;
        }
    }

    internal sealed class ModuleImportEdge
    {
        public ModuleImportEdge(ModuleImportKind kind, string rawPath, int line, int col, string originFile)
        {
            Kind = kind;
            RawPath = rawPath;
            Line = line;
            Col = col;
            OriginFile = originFile;
        }

        public ModuleImportKind Kind { get; }

        public string RawPath { get; }

        public int Line { get; }

        public int Col { get; }

        public string OriginFile { get; }

        public ModuleGraphNode? Target { get; private set; }

        public bool IsDllImport => RawPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        public void AttachTarget(ModuleGraphNode? target)
        {
            Target = target;
        }
    }

    internal enum ModuleImportKind
    {
        Bare,
        Namespace,
        Named,
        Default
    }
}
