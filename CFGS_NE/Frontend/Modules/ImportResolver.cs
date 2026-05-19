using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Lowering;
using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Modules
{
    public sealed class ImportResolver
    {
        private readonly Action<string>? _loadPluginDll;
        private readonly SourceResolver _sourceResolver;
        private readonly ModuleGraphBuilder _moduleGraphBuilder;

        public ImportResolver(
            Action<string>? loadPluginDll = null,
            Func<string, string?>? loadImportSource = null,
            Dictionary<string, List<Stmt>>? sharedAstByHash = null,
            Stack<string>? sharedImportStack = null)
            : this(loadPluginDll, loadImportSource, sharedAstByHash, sharedImportStack, null)
        {
        }

        internal ImportResolver(
            Action<string>? loadPluginDll,
            Func<string, string?>? loadImportSource,
            Dictionary<string, List<Stmt>>? sharedAstByHash,
            Stack<string>? sharedImportStack,
            SourceResolver? sourceResolver)
        {
            _loadPluginDll = loadPluginDll;
            _sourceResolver = sourceResolver ?? new SourceResolver();
            _moduleGraphBuilder = new ModuleGraphBuilder(
                ResolveImportedSyntaxTree,
                loadImportSource,
                sharedAstByHash,
                sharedImportStack,
                _sourceResolver);
        }

        public List<Stmt> ResolveImports(List<Stmt> statements)
        {
            List<Stmt> resolved = new();
            Dictionary<string, (string Kind, string Origin)> seenImportedTopLevelSymbols = new(StringComparer.Ordinal);
            Dictionary<string, string> knownTopLevelKinds = new(StringComparer.Ordinal);
            HashSet<string> knownNamespaceRoots = new(StringComparer.Ordinal);

            foreach (Stmt stmt in statements)
            {
                switch (stmt)
                {
                    case BareImportSyntaxStmt bareImport:
                        ResolveBareImport(bareImport, resolved, seenImportedTopLevelSymbols, knownTopLevelKinds, knownNamespaceRoots);
                        break;

                    case NamespaceImportSyntaxStmt namespaceImport:
                        ResolveNamespaceImport(namespaceImport, resolved, seenImportedTopLevelSymbols, knownTopLevelKinds, knownNamespaceRoots);
                        break;

                    case NamedImportSyntaxStmt namedImport:
                        ResolveNamedImport(namedImport, resolved, seenImportedTopLevelSymbols, knownTopLevelKinds, knownNamespaceRoots);
                        break;

                    case DefaultImportSyntaxStmt defaultImport:
                        ResolveDefaultImport(defaultImport, resolved, seenImportedTopLevelSymbols, knownTopLevelKinds, knownNamespaceRoots);
                        break;

                    default:
                        ValidateCurrentTopLevelStatement(stmt, knownTopLevelKinds, knownNamespaceRoots);
                        resolved.Add(stmt);
                        TrackKnownTopLevelSymbols([stmt], knownTopLevelKinds);
                        TrackKnownNamespaceRoots([stmt], knownNamespaceRoots);
                        break;
                }
            }

            List<Stmt> useResolved = new UseNamespaceResolver().Resolve(statements, resolved);
            TopLevelSymbolFacts.ValidateTopLevelSymbolUniqueness(useResolved);
            return useResolved;
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
                    $"plugin dll not found '{rawPath}' (searched script dir, working dir, exe dir)",
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
            => _moduleGraphBuilder.GetImports(path, line, col, sourceFileName, specClass);

        private void ResolveBareImport(
            BareImportSyntaxStmt bareImport,
            List<Stmt> resolved,
            Dictionary<string, (string Kind, string Origin)> seenImportedTopLevelSymbols,
            Dictionary<string, string> knownTopLevelKinds,
            HashSet<string> knownNamespaceRoots)
        {
            if (TryHandleDllImport(bareImport.Path, bareImport.Line, bareImport.Col, bareImport.OriginFile))
                return;

            List<Stmt> imported = GetImports(bareImport.Path, bareImport.Line, bareImport.Col, bareImport.OriginFile);
            if (HasExplicitExports(imported))
            {
                throw new ParserException(
                    "bare import is not allowed for modules with explicit exports. Use named or namespace import.",
                    bareImport.Line,
                    bareImport.Col,
                    bareImport.OriginFile);
            }

            List<Stmt> materialized = FilterDuplicateTopLevel(imported, seenImportedTopLevelSymbols, allowIdempotentSameOrigin: true);
            resolved.AddRange(materialized);
            IndexImportedTopLevelSymbols(materialized, seenImportedTopLevelSymbols);
            TrackKnownTopLevelSymbols(materialized, knownTopLevelKinds);
            TrackKnownNamespaceRoots(materialized, knownNamespaceRoots);
        }

        private void ResolveNamespaceImport(
            NamespaceImportSyntaxStmt namespaceImport,
            List<Stmt> resolved,
            Dictionary<string, (string Kind, string Origin)> seenImportedTopLevelSymbols,
            Dictionary<string, string> knownTopLevelKinds,
            HashSet<string> knownNamespaceRoots)
        {
            if (namespaceImport.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new ParserException(
                    "namespace import from dll is not supported. Use: import \"path.dll\";",
                    namespaceImport.Line,
                    namespaceImport.Col,
                    namespaceImport.OriginFile);
            }

            List<Stmt> imported = GetImports(namespaceImport.Path, namespaceImport.Line, namespaceImport.Col, namespaceImport.OriginFile);
            Dictionary<string, Stmt> surface = BuildModuleSurface(imported);

            List<Stmt> materialized = FilterDuplicateTopLevel(imported, seenImportedTopLevelSymbols, allowIdempotentSameOrigin: true);
            resolved.AddRange(materialized);
            IndexImportedTopLevelSymbols(materialized, seenImportedTopLevelSymbols);
            TrackKnownTopLevelSymbols(materialized, knownTopLevelKinds);
            TrackKnownNamespaceRoots(materialized, knownNamespaceRoots);

            List<Stmt> aliasStatements =
            [
                new NamespaceImportAliasStmt(
                    namespaceImport.Alias,
                    surface.Keys.OrderBy(static x => x, StringComparer.Ordinal).ToList(),
                    namespaceImport.Line,
                    namespaceImport.Col,
                    namespaceImport.OriginFile)
            ];

            List<Stmt> filteredAliasStatements = FilterDuplicateTopLevel(aliasStatements, seenImportedTopLevelSymbols, allowIdempotentSameOrigin: false);
            resolved.AddRange(filteredAliasStatements);
            IndexImportedTopLevelSymbols(filteredAliasStatements, seenImportedTopLevelSymbols);
            TrackKnownTopLevelSymbols(filteredAliasStatements, knownTopLevelKinds);
        }

        private void ResolveNamedImport(
            NamedImportSyntaxStmt namedImport,
            List<Stmt> resolved,
            Dictionary<string, (string Kind, string Origin)> seenImportedTopLevelSymbols,
            Dictionary<string, string> knownTopLevelKinds,
            HashSet<string> knownNamespaceRoots)
        {
            if (namedImport.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new ParserException(
                    "named import from dll is not supported. Use: import \"path.dll\";",
                    namedImport.Line,
                    namedImport.Col,
                    namedImport.OriginFile);
            }

            List<Stmt> imported = GetImports(namedImport.Path, namedImport.Line, namedImport.Col, namedImport.OriginFile);
            Dictionary<string, Stmt> surface = BuildModuleSurface(imported);

            foreach (ImportBindingSpec binding in namedImport.Imports)
            {
                if (!surface.ContainsKey(binding.ImportName))
                {
                    throw new ParserException(
                        $"Could not find export '{binding.ImportName}' in import '{namedImport.Path}'",
                        namedImport.Line,
                        namedImport.Col,
                        namedImport.OriginFile);
                }
            }

            List<Stmt> materialized = FilterDuplicateTopLevel(imported, seenImportedTopLevelSymbols, allowIdempotentSameOrigin: true);
            resolved.AddRange(materialized);
            IndexImportedTopLevelSymbols(materialized, seenImportedTopLevelSymbols);
            TrackKnownTopLevelSymbols(materialized, knownTopLevelKinds);
            TrackKnownNamespaceRoots(materialized, knownNamespaceRoots);

            List<Stmt> aliasStatements = new();
            foreach (ImportBindingSpec binding in namedImport.Imports)
            {
                if (!string.Equals(binding.ImportName, binding.LocalName, StringComparison.Ordinal))
                {
                    aliasStatements.Add(new ImportAliasDeclStmt(
                        binding.LocalName,
                        binding.ImportName,
                        namedImport.Line,
                        namedImport.Col,
                        namedImport.OriginFile));
                }
            }

            if (aliasStatements.Count == 0)
                return;

            List<Stmt> filteredAliasStatements = FilterDuplicateTopLevel(aliasStatements, seenImportedTopLevelSymbols, allowIdempotentSameOrigin: false);
            resolved.AddRange(filteredAliasStatements);
            IndexImportedTopLevelSymbols(filteredAliasStatements, seenImportedTopLevelSymbols);
            TrackKnownTopLevelSymbols(filteredAliasStatements, knownTopLevelKinds);
        }

        private void ResolveDefaultImport(
            DefaultImportSyntaxStmt defaultImport,
            List<Stmt> resolved,
            Dictionary<string, (string Kind, string Origin)> seenImportedTopLevelSymbols,
            Dictionary<string, string> knownTopLevelKinds,
            HashSet<string> knownNamespaceRoots)
        {
            if (defaultImport.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new ParserException(
                    "named import from dll is not supported. Use: import \"path.dll\";",
                    defaultImport.Line,
                    defaultImport.Col,
                    defaultImport.OriginFile);
            }

            List<Stmt> imported = GetImports(defaultImport.Path, defaultImport.Line, defaultImport.Col, defaultImport.OriginFile);
            Dictionary<string, Stmt> surface = BuildModuleSurface(imported);
            if (!surface.ContainsKey(defaultImport.ImportName))
            {
                throw new ParserException(
                    $"Could not find export '{defaultImport.ImportName}' in import '{defaultImport.Path}'",
                    defaultImport.Line,
                    defaultImport.Col,
                    defaultImport.OriginFile);
            }

            List<Stmt> materialized = FilterDuplicateTopLevel(imported, seenImportedTopLevelSymbols, allowIdempotentSameOrigin: true);
            resolved.AddRange(materialized);
            IndexImportedTopLevelSymbols(materialized, seenImportedTopLevelSymbols);
            TrackKnownTopLevelSymbols(materialized, knownTopLevelKinds);
            TrackKnownNamespaceRoots(materialized, knownNamespaceRoots);
        }

        private List<Stmt> ResolveImportedSyntaxTree(List<Stmt> statements)
            => ResolveImports(statements);

        private static void ValidateCurrentTopLevelStatement(
            Stmt stmt,
            Dictionary<string, string> knownTopLevelKinds,
            HashSet<string> knownNamespaceRoots)
        {
            if (stmt is NamespaceDeclStmt namespaceDecl)
            {
                string root = namespaceDecl.Parts[0];
                if (knownTopLevelKinds.TryGetValue(root, out string? rootKind) && !knownNamespaceRoots.Contains(root))
                {
                    string label = TopLevelSymbolFacts.NamespaceRootConflictLabel(rootKind);
                    throw new ParserException(
                        $"namespace root '{root}' conflicts with existing {label} '{root}'",
                        namespaceDecl.Line,
                        namespaceDecl.Col,
                        namespaceDecl.OriginFile);
                }

                return;
            }

            if (!TopLevelSymbolFacts.TryGetNamedTopLevelWithKind(stmt, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                return;

            string currentKind = kind ?? "symbol";
            if (knownTopLevelKinds.TryGetValue(name, out string? previousKind))
            {
                throw new ParserException(
                    TopLevelSymbolFacts.DuplicateTopLevelMessage(name, currentKind, previousKind),
                    stmt.Line,
                    stmt.Col,
                    stmt.OriginFile);
            }
        }

        private static void TrackKnownTopLevelSymbols(IEnumerable<Stmt> stmts, Dictionary<string, string> knownTopLevelKinds)
        {
            foreach (Stmt stmt in stmts)
            {
                if (!TopLevelSymbolFacts.TryGetNamedTopLevelWithKind(stmt, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                    continue;

                if (!knownTopLevelKinds.ContainsKey(name))
                    knownTopLevelKinds[name] = kind ?? "symbol";
            }
        }

        private static void TrackKnownNamespaceRoots(IEnumerable<Stmt> stmts, HashSet<string> knownNamespaceRoots)
        {
            foreach (Stmt stmt in stmts)
            {
                if (stmt is ExportStmt ex)
                {
                    TrackKnownNamespaceRoots([ex.Inner], knownNamespaceRoots);
                    continue;
                }

                if (stmt is NamespaceDeclStmt ns && ns.Parts.Count > 0)
                    knownNamespaceRoots.Add(ns.Parts[0]);
                else if (stmt is VarDecl v && v.IsSyntheticNamespaceRoot)
                    knownNamespaceRoots.Add(v.Name);
            }
        }

        private static void IndexImportedTopLevelSymbols(
            IEnumerable<Stmt> stmts,
            Dictionary<string, (string Kind, string Origin)> seenImportedTopLevelSymbols)
        {
            foreach (Stmt stmt in stmts)
            {
                if (!TopLevelSymbolFacts.TryGetNamedTopLevelWithKind(stmt, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                    continue;

                string origin = TopLevelSymbolFacts.NormalizeOriginKey(stmt.OriginFile);
                if (!seenImportedTopLevelSymbols.ContainsKey(name))
                    seenImportedTopLevelSymbols[name] = (kind ?? "symbol", origin);
            }
        }

        private static List<Stmt> FilterDuplicateTopLevel(
            List<Stmt> stmts,
            Dictionary<string, (string Kind, string Origin)> seenImportedTopLevelSymbols,
            bool allowIdempotentSameOrigin)
        {
            List<Stmt> filtered = new(stmts.Count);
            Dictionary<string, (string Kind, string Origin)> localSeen = new(StringComparer.Ordinal);

            foreach (Stmt stmt in stmts)
            {
                if (!TopLevelSymbolFacts.TryGetNamedTopLevelWithKind(stmt, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                {
                    filtered.Add(stmt);
                    continue;
                }

                string currentKind = kind ?? "symbol";
                string currentOrigin = TopLevelSymbolFacts.NormalizeOriginKey(stmt.OriginFile);

                if (localSeen.TryGetValue(name, out (string Kind, string Origin) localPrevious))
                {
                    if (TopLevelSymbolFacts.CanMergeTopLevelSymbols(currentKind, localPrevious.Kind))
                    {
                        filtered.Add(stmt);
                        continue;
                    }

                    throw new ParserException(
                        TopLevelSymbolFacts.DuplicateTopLevelMessage(name, currentKind, localPrevious.Kind),
                        stmt.Line,
                        stmt.Col,
                        stmt.OriginFile);
                }

                localSeen[name] = (currentKind, currentOrigin);

                if (seenImportedTopLevelSymbols.TryGetValue(name, out (string Kind, string Origin) previous))
                {
                    if (allowIdempotentSameOrigin && string.Equals(previous.Origin, currentOrigin, StringComparison.Ordinal))
                        continue;

                    if (TopLevelSymbolFacts.CanMergeTopLevelSymbols(currentKind, previous.Kind))
                    {
                        filtered.Add(stmt);
                        continue;
                    }

                    throw new ParserException(
                        TopLevelSymbolFacts.DuplicateTopLevelMessage(name, currentKind, previous.Kind),
                        stmt.Line,
                        stmt.Col,
                        stmt.OriginFile);
                }

                filtered.Add(stmt);
            }

            return filtered;
        }

        private static bool HasExplicitExports(IEnumerable<Stmt> imported)
            => imported.Any(static stmt => stmt is ExportStmt);

        private static Dictionary<string, Stmt> BuildModuleSurface(List<Stmt> imported)
        {
            Dictionary<string, Stmt> all = new(StringComparer.Ordinal);
            Dictionary<string, Stmt> exported = new(StringComparer.Ordinal);

            foreach (Stmt stmt in imported)
            {
                if (TopLevelSymbolFacts.TryGetNamedTopLevel(stmt, out string? name) && !string.IsNullOrWhiteSpace(name))
                {
                    if (!all.ContainsKey(name))
                        all[name] = stmt;
                }

                if (stmt is ExportStmt export &&
                    TopLevelSymbolFacts.TryGetNamedTopLevel(export.Inner, out string? exportName) &&
                    !string.IsNullOrWhiteSpace(exportName))
                {
                    if (!exported.ContainsKey(exportName))
                        exported[exportName] = export.Inner;
                }
            }

            return exported.Count > 0 ? exported : all;
        }
    }
}
