using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using System.Text;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        /// <summary>
        /// The IndexTopLevelSymbols
        /// </summary>
        /// <param name="stmts">The stmts<see cref="IEnumerable{Stmt}"/></param>
        private void IndexTopLevelSymbols(IEnumerable<Stmt> stmts)
        {
            foreach (Stmt s in stmts)
            {
                if (!TryGetNamedTopLevelWithKind(s, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                    continue;

                string origin = NormalizeOriginKey(s.OriginFile);
                if (!_seenTopLevelSymbols.ContainsKey(name))
                    _seenTopLevelSymbols[name] = (kind ?? "symbol", origin);

                switch (kind)
                {
                    case "function":
                        _seenFunctions.Add(name);
                        break;
                    case "class":
                        _seenClasses.Add(name);
                        break;
                    case "interface":
                        break;
                    case "enum":
                        _seenEnums.Add(name);
                        break;
                }
            }
        }

        /// <summary>
        /// The TrackKnownTopLevelSymbols
        /// </summary>
        /// <param name="stmts">The stmts<see cref="IEnumerable{Stmt}"/></param>
        private void TrackKnownTopLevelSymbols(IEnumerable<Stmt> stmts)
        {
            foreach (Stmt s in stmts)
            {
                if (!TryGetNamedTopLevelWithKind(s, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                    continue;

                _knownTopLevelNames.Add(name);
                if (!_knownTopLevelKinds.ContainsKey(name))
                    _knownTopLevelKinds[name] = kind ?? "symbol";
            }
        }

        /// <summary>
        /// The NamespaceRootConflictLabel
        /// </summary>
        /// <param name="kind">The kind<see cref="string"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string NamespaceRootConflictLabel(string kind)
        {
            return kind switch
            {
                "namespace" => "namespace",
                "function" => "function",
                "class" => "class",
                "interface" => "interface",
                "enum" => "enum",
                "constant" => "constant",
                "variable" => "variable",
                _ => "symbol",
            };
        }

        /// <summary>
        /// The FilterDuplicateTopLevel
        /// </summary>
        /// <param name="stmts">The stmts<see cref="List{Stmt}"/></param>
        /// <param name="allowIdempotentSameOrigin">The allowIdempotentSameOrigin<see cref="bool"/></param>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        private List<Stmt> FilterDuplicateTopLevel(List<Stmt> stmts, bool allowIdempotentSameOrigin = true)
        {
            List<Stmt> filtered = new(stmts.Count);
            Dictionary<string, (string Kind, string Origin)> localSeen = new(StringComparer.Ordinal);
            foreach (Stmt s in stmts)
            {
                if (!TryGetNamedTopLevelWithKind(s, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                {
                    filtered.Add(s);
                    continue;
                }

                string currKind = kind ?? "symbol";
                string currOrigin = NormalizeOriginKey(s.OriginFile);

                if (localSeen.TryGetValue(name, out (string Kind, string Origin) localPrev))
                    throw new ParserException(DuplicateTopLevelMessage(name, currKind, localPrev.Kind), s.Line, s.Col, s.OriginFile);

                localSeen[name] = (currKind, currOrigin);

                if (_seenTopLevelSymbols.TryGetValue(name, out (string Kind, string Origin) prev))
                {
                    if (allowIdempotentSameOrigin &&
                        string.Equals(prev.Origin, currOrigin, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    throw new ParserException(DuplicateTopLevelMessage(name, currKind, prev.Kind), s.Line, s.Col, s.OriginFile);
                }

                filtered.Add(s);
            }
            return filtered;
        }

        /// <summary>
        /// The TryGetFuncDeclName
        /// </summary>
        /// <param name="stmt">The stmt<see cref="Stmt"/></param>
        /// <param name="name">The name<see cref="string?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetFuncDeclName(Stmt stmt, out string? name)
        {
            if (stmt is ExportStmt ex)
                return TryGetFuncDeclName(ex.Inner, out name);
            if (stmt is FuncDeclStmt f)
            {
                name = f.Name;
                return true;
            }
            name = null;
            return false;
        }

        /// <summary>
        /// The TryGetClassDeclName
        /// </summary>
        /// <param name="stmt">The stmt<see cref="Stmt"/></param>
        /// <param name="name">The name<see cref="string?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetClassDeclName(Stmt stmt, out string? name)
        {
            if (stmt is ExportStmt ex)
                return TryGetClassDeclName(ex.Inner, out name);
            if (stmt is ClassDeclStmt c)
            {
                name = c.Name;
                return true;
            }
            name = null;
            return false;
        }

        /// <summary>
        /// The TryGetEnumDeclName
        /// </summary>
        /// <param name="stmt">The stmt<see cref="Stmt"/></param>
        /// <param name="name">The name<see cref="string?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetEnumDeclName(Stmt stmt, out string? name)
        {
            if (stmt is ExportStmt ex)
                return TryGetEnumDeclName(ex.Inner, out name);
            if (stmt is EnumDeclStmt e)
            {
                name = e.Name;
                return true;
            }
            name = null;
            return false;
        }

        /// <summary>
        /// The TryGetNamedTopLevel
        /// </summary>
        /// <param name="stmt">The stmt<see cref="Stmt"/></param>
        /// <param name="name">The name<see cref="string?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetNamedTopLevel(Stmt stmt, out string? name)
        {
            if (stmt is ExportStmt ex)
                return TryGetNamedTopLevel(ex.Inner, out name);

            switch (stmt)
            {
                case FuncDeclStmt f:
                    name = f.Name;
                    return true;
                case ClassDeclStmt c:
                    name = c.Name;
                    return true;
                case InterfaceDeclStmt i:
                    name = i.Name;
                    return true;
                case EnumDeclStmt e:
                    name = e.Name;
                    return true;
                case NamespaceDeclStmt ns:
                    name = ns.Parts.Count > 0 ? ns.Parts[0] : null;
                    return !string.IsNullOrWhiteSpace(name);
                case NamespaceImportAliasStmt nsAlias:
                    name = nsAlias.Alias;
                    return true;
                case ImportAliasDeclStmt importAlias:
                    name = importAlias.LocalName;
                    return true;
                case VarDecl v:
                    name = v.Name;
                    return true;
                case ConstDecl cst:
                    name = cst.Name;
                    return true;
                default:
                    name = null;
                    return false;
            }
        }

        /// <summary>
        /// The TryGetNamedTopLevelWithKind
        /// </summary>
        /// <param name="stmt">The stmt<see cref="Stmt"/></param>
        /// <param name="name">The name<see cref="string?"/></param>
        /// <param name="kind">The kind<see cref="string?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetNamedTopLevelWithKind(Stmt stmt, out string? name, out string? kind)
        {
            if (stmt is ExportStmt ex)
                return TryGetNamedTopLevelWithKind(ex.Inner, out name, out kind);

            switch (stmt)
            {
                case FuncDeclStmt f:
                    name = f.Name;
                    kind = "function";
                    return true;
                case ClassDeclStmt c:
                    name = c.Name;
                    kind = "class";
                    return true;
                case InterfaceDeclStmt i:
                    name = i.Name;
                    kind = "interface";
                    return true;
                case EnumDeclStmt e:
                    name = e.Name;
                    kind = "enum";
                    return true;
                case NamespaceDeclStmt ns:
                    name = ns.Parts.Count > 0 ? ns.Parts[0] : null;
                    kind = "namespace";
                    return !string.IsNullOrWhiteSpace(name);
                case NamespaceImportAliasStmt nsAlias:
                    name = nsAlias.Alias;
                    kind = "variable";
                    return true;
                case ImportAliasDeclStmt importAlias:
                    name = importAlias.LocalName;
                    kind = "variable";
                    return true;
                case VarDecl v:
                    name = v.Name;
                    kind = "variable";
                    return true;
                case ConstDecl cst:
                    name = cst.Name;
                    kind = "constant";
                    return true;
                default:
                    name = null;
                    kind = null;
                    return false;
            }
        }

        /// <summary>
        /// The NormalizeOriginKey
        /// </summary>
        /// <param name="origin">The origin<see cref="string"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string NormalizeOriginKey(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return string.Empty;

            try
            {
                if (Path.IsPathRooted(origin))
                {
                    string full = Path.GetFullPath(origin);
                    return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
                }
            }
            catch (ArgumentException) { }
            catch (IOException) { }

            return origin;
        }

        /// <summary>
        /// The DuplicateTopLevelMessage
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="currentKind">The currentKind<see cref="string"/></param>
        /// <param name="existingKind">The existingKind<see cref="string"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string DuplicateTopLevelMessage(string name, string currentKind, string existingKind)
        {
            if (currentKind == "namespace" && existingKind == "namespace")
                return $"duplicate namespace root '{name}'";

            if (!string.Equals(currentKind, existingKind, StringComparison.Ordinal))
                return $"duplicate symbol '{name}'";

            return currentKind switch
            {
                "function" => $"duplicate function '{name}'",
                "class" => $"duplicate class '{name}'",
                "interface" => $"duplicate interface '{name}'",
                "enum" => $"duplicate enum '{name}'",
                "namespace" => $"duplicate namespace root '{name}'",
                "constant" => $"duplicate constant '{name}'",
                "variable" => $"duplicate variable '{name}'",
                _ => $"duplicate symbol '{name}'"
            };
        }

        /// <summary>
        /// The HasExplicitExports
        /// </summary>
        /// <param name="imported">The imported<see cref="IEnumerable{Stmt}"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool HasExplicitExports(IEnumerable<Stmt> imported)
            => imported.Any(s => s is ExportStmt);

        /// <summary>
        /// The ValidateTopLevelSymbolUniqueness
        /// </summary>
        /// <param name="stmts">The stmts<see cref="IEnumerable{Stmt}"/></param>
        private static void ValidateTopLevelSymbolUniqueness(IEnumerable<Stmt> stmts)
        {
            Dictionary<string, string> seenKinds = new(StringComparer.Ordinal);
            foreach (Stmt s in stmts)
            {
                if (!TryGetNamedTopLevelWithKind(s, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                    continue;

                string currKind = kind ?? "symbol";
                if (seenKinds.TryGetValue(name, out string? prevKind))
                {
                    if (currKind == "namespace" && prevKind == "namespace")
                        continue;

                    throw new ParserException(DuplicateTopLevelMessage(name, currKind, prevKind), s.Line, s.Col, s.OriginFile);
                }

                seenKinds[name] = currKind;
            }
        }

        /// <summary>
        /// The BuildModuleSurface
        /// </summary>
        /// <param name="imported">The imported<see cref="List{Stmt}"/></param>
        /// <returns>The <see cref="Dictionary{string, Stmt}"/></returns>
        private static Dictionary<string, Stmt> BuildModuleSurface(List<Stmt> imported)
        {
            Dictionary<string, Stmt> all = new(StringComparer.Ordinal);
            Dictionary<string, Stmt> exported = new(StringComparer.Ordinal);

            foreach (Stmt s in imported)
            {
                if (TryGetNamedTopLevel(s, out string? nm) && !string.IsNullOrWhiteSpace(nm))
                {
                    if (!all.ContainsKey(nm))
                        all[nm] = s;
                }

                if (s is ExportStmt ex && TryGetNamedTopLevel(ex.Inner, out string? enm) && !string.IsNullOrWhiteSpace(enm))
                {
                    if (!exported.ContainsKey(enm))
                        exported[enm] = ex.Inner;
                }
            }

            return exported.Count > 0 ? exported : all;
        }

        /// <summary>
        /// The BuildNamespaceAliasStmts
        /// </summary>
        /// <param name="alias">The alias<see cref="string"/></param>
        /// <param name="names">The names<see cref="IEnumerable{string}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        private static List<Stmt> BuildNamespaceAliasStmts(string alias, IEnumerable<string> names, int line, int col, string file)
        {
            List<Stmt> stmts = new()
            {
                new VarDecl(alias, new DictExpr(new List<(Expr Key, Expr Value)>(), line, col, file), line, col, file)
            };

            foreach (string n in names.OrderBy(x => x, StringComparer.Ordinal))
            {
                IndexExpr target = new(
                    new VarExpr(alias, line, col, file),
                    new StringExpr(n, line, col, file),
                    line, col, file);
                stmts.Add(new AssignExprStmt(target, new VarExpr(n, line, col, file), line, col, file));
            }

            return stmts;
        }

        /// <summary>
        /// The BuildQualifiedAccessExpr
        /// </summary>
        /// <param name="parts">The parts<see cref="IReadOnlyList{string}"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        /// <returns>The <see cref="Expr"/></returns>
        private static Expr BuildQualifiedAccessExpr(IReadOnlyList<string> parts, int line, int col, string file)
        {
            if (parts.Count == 0)
                throw new ArgumentException("qualified name parts must not be empty", nameof(parts));

            Expr expr = new VarExpr(parts[0], line, col, file);
            for (int i = 1; i < parts.Count; i++)
            {
                expr = new IndexExpr(
                    expr,
                    new StringExpr(parts[i], line, col, file),
                    line, col, file);
            }
            return expr;
        }

        /// <summary>
        /// The BuildNamespaceEnsurePathStmts
        /// </summary>
        /// <param name="parts">The parts<see cref="IReadOnlyList{string}"/></param>
        /// <param name="declareRoot">The declareRoot<see cref="bool"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        private static List<Stmt> BuildNamespaceEnsurePathStmts(
            IReadOnlyList<string> parts,
            bool declareRoot,
            int line,
            int col,
            string file)
        {
            if (parts.Count == 0)
                throw new ArgumentException("namespace parts must not be empty", nameof(parts));

            List<Stmt> stmts = new();

            if (declareRoot)
            {
                stmts.Add(new VarDecl(
                    parts[0],
                    new DictExpr(new List<(Expr Key, Expr Value)>(), line, col, file),
                    line, col, file));
            }

            Expr current = new VarExpr(parts[0], line, col, file);
            for (int i = 1; i < parts.Count; i++)
            {
                Expr next = new IndexExpr(
                    current,
                    new StringExpr(parts[i], line, col, file),
                    line, col, file);

                Expr ensureExpr = new BinaryExpr(
                    next,
                    TokenType.QQNull,
                    new DictExpr(new List<(Expr Key, Expr Value)>(), line, col, file),
                    line, col, file);

                stmts.Add(new AssignExprStmt(next, ensureExpr, line, col, file));
                current = next;
            }

            return stmts;
        }

        /// <summary>
        /// The ParseNamespacePath
        /// </summary>
        /// <returns>The <see cref="List{string}"/></returns>
        private List<string> ParseNamespacePath()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected namespace name", line, col, file);

            List<string> parts = new();

            while (true)
            {
                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected identifier in namespace name", _current.Line, _current.Column, _current.Filename);

                string part = _current.Value?.ToString() ?? "";
                if (Lexer.Keywords.ContainsKey(part))
                    throw new ParserException($"invalid symbol declaration name '{part}'", _current.Line, _current.Column, _current.Filename);

                parts.Add(part);
                Eat(TokenType.Ident);

                if (_current.Type != TokenType.Dot)
                    break;

                Eat(TokenType.Dot);
            }

            return parts;
        }

        /// <summary>
        /// The ParseQualifiedTypeName
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        private string ParseQualifiedTypeName()
        {
            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected type name", _current.Line, _current.Column, _current.Filename);

            StringBuilder sb = new();
            while (true)
            {
                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected identifier in type name", _current.Line, _current.Column, _current.Filename);

                string part = _current.Value?.ToString() ?? "";
                if (Lexer.Keywords.ContainsKey(part))
                    throw new ParserException($"invalid symbol declaration name '{part}'", _current.Line, _current.Column, _current.Filename);

                if (sb.Length > 0)
                    sb.Append('.');
                sb.Append(part);

                Eat(TokenType.Ident);

                if (_current.Type != TokenType.Dot)
                    break;

                Eat(TokenType.Dot);
            }

            return sb.ToString();
        }

        /// <summary>
        /// The ParseNamespaceBodyStatement
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseNamespaceBodyStatement()
        {
            if (_current.Type == TokenType.Export)
                throw new ParserException(
                    "export is not allowed inside namespace body",
                    _current.Line, _current.Column, _current.Filename);

            if (_current.Type == TokenType.Import)
                throw new ParserException(
                    "imports are not allowed inside namespace body",
                    _current.Line, _current.Column, _current.Filename);

            if (_current.Type == TokenType.Namespace)
                throw new ParserException(
                    "nested namespace declarations are not supported. Use a qualified namespace name (for example: namespace A.B { ... }).",
                    _current.Line, _current.Column, _current.Filename);

            if (TryParseCommonStatement(out Stmt stmt))
                return stmt;

            throw new ParserException(
                $"invalid namespace statement {_current.Type}",
                _current.Line, _current.Column, _current.Filename);
        }

        /// <summary>
        /// The ParseNamespaceDeclStatements
        /// </summary>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        private NamespaceDeclStmt ParseNamespaceDeclStatement()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Namespace);

            List<string> parts = ParseNamespacePath();
            string root = parts[0];

            if (_knownTopLevelKinds.TryGetValue(root, out string? rootKind) && !_knownNamespaceRoots.Contains(root))
            {
                string label = NamespaceRootConflictLabel(rootKind);
                throw new ParserException(
                    $"namespace root '{root}' conflicts with existing {label} '{root}'",
                    line, col, file);
            }

            Eat(TokenType.LBrace);

            List<Stmt> bodyStmts = new();
            while (_current.Type != TokenType.RBrace)
            {
                if (_current.Type == TokenType.EOF)
                    throw new ParserException("expected '}' before end of file in namespace body", _current.Line, _current.Column, _current.Filename);

                Stmt bodyStmt = ParseNamespaceBodyStatement();
                bodyStmts.Add(bodyStmt);
            }

            Eat(TokenType.RBrace);

            ValidateTopLevelSymbolUniqueness(bodyStmts);
            _knownNamespaceRoots.Add(root);
            return new NamespaceDeclStmt(parts, bodyStmts, line, col, file);
        }

        /// <summary>
        /// The Parse
        /// </summary>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        public List<Stmt> Parse()
        {
            List<Stmt> stmts = new();

            if (_current.Type == TokenType.Import)
            {
                while (_current.Type == TokenType.Import)
                {
                    Eat(TokenType.Import);

                    if (_current.Type == TokenType.String)
                    {
                        int ln = _current.Line;
                        int col = _current.Column;
                        string fn = _current.Filename;

                        string path = _current.Value?.ToString() ?? "";
                        Eat(TokenType.String);

                        if (!_importResolver.TryHandleDllImport(path, ln, col, fn))
                        {
                            List<Stmt> imported = _importResolver.GetImports(path, ln, col, fn);
                            if (HasExplicitExports(imported))
                                throw new ParserException(
                                    "bare import is not allowed for modules with explicit exports. Use named or namespace import.",
                                    ln, col, fn);

                            List<Stmt> materialized = FilterDuplicateTopLevel(imported, allowIdempotentSameOrigin: true);
                            stmts.AddRange(materialized);
                            IndexTopLevelSymbols(materialized);
                        }
                    }
                    else if (_current.Type == TokenType.Star)
                    {
                        int ln = _current.Line;
                        int col = _current.Column;
                        string fn = _current.Filename;

                        Eat(TokenType.Star);
                        Eat(TokenType.As);

                        if (_current.Type != TokenType.Ident)
                            throw new ParserException("expected identifier after 'as' in import statement", _current.Line, _current.Column, _current.Filename);

                        string alias = _current.Value?.ToString() ?? "";
                        if (Lexer.Keywords.ContainsKey(alias))
                            throw new ParserException($"invalid symbol declaration name '{alias}'", _current.Line, _current.Column, _current.Filename);
                        Eat(TokenType.Ident);
                        Eat(TokenType.From);

                        if (_current.Type != TokenType.String)
                            throw new ParserException("expected string after 'from' in import statement", _current.Line, _current.Column, _current.Filename);

                        string path = _current.Value?.ToString() ?? "";
                        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            throw new ParserException("namespace import from dll is not supported. Use: import \"path.dll\";", ln, col, fn);

                        List<Stmt> imported = _importResolver.GetImports(path, ln, col, fn);
                        Eat(TokenType.String);

                        Dictionary<string, Stmt> surface = BuildModuleSurface(imported);

                        List<Stmt> materialized = FilterDuplicateTopLevel(imported, allowIdempotentSameOrigin: true);
                        stmts.AddRange(materialized);
                        IndexTopLevelSymbols(materialized);

                        List<Stmt> aliasStmts = new()
                        {
                            new NamespaceImportAliasStmt(alias, surface.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList(), ln, col, fn)
                        };
                        List<Stmt> filteredAliasStmts = FilterDuplicateTopLevel(aliasStmts, allowIdempotentSameOrigin: false);
                        stmts.AddRange(filteredAliasStmts);
                        IndexTopLevelSymbols(filteredAliasStmts);
                    }
                    else if (_current.Type == TokenType.LBrace)
                    {
                        int ln = _current.Line;
                        int col = _current.Column;
                        string fn = _current.Filename;

                        List<(string ImportName, string LocalName)> imports = new();
                        HashSet<string> seenLocals = new(StringComparer.Ordinal);
                        Eat(TokenType.LBrace);
                        while (_current.Type != TokenType.RBrace)
                        {
                            if (_current.Type != TokenType.Ident)
                                throw new ParserException("expected identifier in named import list", _current.Line, _current.Column, _current.Filename);

                            string importName = _current.Value?.ToString() ?? "";
                            Eat(TokenType.Ident);

                            string localName = importName;
                            if (_current.Type == TokenType.As)
                            {
                                Eat(TokenType.As);
                                if (_current.Type != TokenType.Ident)
                                    throw new ParserException("expected identifier after 'as' in named import", _current.Line, _current.Column, _current.Filename);
                                localName = _current.Value?.ToString() ?? "";
                                Eat(TokenType.Ident);
                            }

                            if (Lexer.Keywords.ContainsKey(localName))
                                throw new ParserException($"invalid symbol declaration name '{localName}'", _current.Line, _current.Column, _current.Filename);

                            if (!seenLocals.Add(localName))
                                throw new ParserException($"duplicate import target '{localName}'", _current.Line, _current.Column, _current.Filename);

                            imports.Add((importName, localName));

                            if (_current.Type == TokenType.Comma)
                            {
                                Eat(TokenType.Comma);
                                if (_current.Type == TokenType.RBrace) break;
                            }
                            else break;
                        }
                        Eat(TokenType.RBrace);
                        Eat(TokenType.From);

                        if (_current.Type != TokenType.String)
                            throw new ParserException("expected string after 'from' in import statement", _current.Line, _current.Column, _current.Filename);

                        string path = _current.Value?.ToString() ?? "";
                        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            throw new ParserException("named import from dll is not supported. Use: import \"path.dll\";", ln, col, fn);

                        List<Stmt> imported = _importResolver.GetImports(path, ln, col, fn);
                        Eat(TokenType.String);

                        Dictionary<string, Stmt> surface = BuildModuleSurface(imported);
                        foreach ((string importName, _) in imports)
                        {
                            if (!surface.ContainsKey(importName))
                                throw new ParserException($"Could not find export '{importName}' in import '{path}'", ln, col, fn);
                        }

                        List<Stmt> materialized = FilterDuplicateTopLevel(imported, allowIdempotentSameOrigin: true);
                        stmts.AddRange(materialized);
                        IndexTopLevelSymbols(materialized);

                        List<Stmt> aliasStmts = new();
                        foreach ((string importName, string localName) in imports)
                        {
                            if (!string.Equals(importName, localName, StringComparison.Ordinal))
                            {
                                aliasStmts.Add(new ImportAliasDeclStmt(localName, importName, ln, col, fn));
                            }
                        }

                        if (aliasStmts.Count > 0)
                        {
                            List<Stmt> filteredAliasStmts = FilterDuplicateTopLevel(aliasStmts, allowIdempotentSameOrigin: false);
                            stmts.AddRange(filteredAliasStmts);
                            IndexTopLevelSymbols(filteredAliasStmts);
                        }
                    }
                    else if (_current.Type == TokenType.Ident)
                    {
                        string importName = _current.Value?.ToString() ?? "";
                        Eat(TokenType.Ident);
                        Eat(TokenType.From);

                        if (_current.Type != TokenType.String)
                            throw new ParserException("expected string after 'from' in import statement",
                                _current.Line, _current.Column, _current.Filename);

                        int ln = _current.Line;
                        int col = _current.Column;
                        string fn = _current.Filename;

                        string path = _current.Value?.ToString() ?? "";

                        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            throw new ParserException(
                                "named import from dll is not supported. Use: import \"path.dll\";",
                                ln, col, fn);

                        List<Stmt> imported = _importResolver.GetImports(path, ln, col, fn);
                        Dictionary<string, Stmt> surface = BuildModuleSurface(imported);
                        if (!surface.ContainsKey(importName))
                            throw new ParserException($"Could not find export '{importName}' in import '{path}'", ln, col, fn);
                        Eat(TokenType.String);

                        List<Stmt> materialized = FilterDuplicateTopLevel(imported, allowIdempotentSameOrigin: true);
                        stmts.AddRange(materialized);
                        IndexTopLevelSymbols(materialized);
                    }
                    else
                    {
                        throw new ParserException("invalid import statement",
                            _current.Line, _current.Column, _current.Filename);
                    }

                    Eat(TokenType.Semi);
                }
            }

            _knownTopLevelNames.Clear();
            _knownTopLevelKinds.Clear();
            _knownNamespaceRoots.Clear();
            foreach (string name in _seenTopLevelSymbols.Keys)
            {
                _knownTopLevelNames.Add(name);
                _knownTopLevelKinds[name] = _seenTopLevelSymbols[name].Kind;
            }

            while (_current.Type != TokenType.EOF)
            {
                if (_current.Type == TokenType.Import)
                    throw new ParserException(
                        "Invalid import statement. Imports are only allowed in the header of the script",
                        _current.Line, _current.Column, _current.Filename);

                if (_current.Type == TokenType.Namespace)
                {
                    NamespaceDeclStmt nsStmt = ParseNamespaceDeclStatement();
                    stmts.Add(nsStmt);
                    TrackKnownTopLevelSymbols(new[] { nsStmt });
                    continue;
                }

                Stmt st = Statement();
                stmts.Add(st);
                TrackKnownTopLevelSymbols(new[] { st });
            }

            ValidateTopLevelSymbolUniqueness(stmts);
            IndexTopLevelSymbols(stmts);

            return stmts;
        }
    }
}
