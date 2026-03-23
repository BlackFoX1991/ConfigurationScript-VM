using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using System.Text;

namespace CFGS_VM.Analytic.Core
{
    /// <summary>
    /// Defines the <see cref="Parser" />
    /// </summary>
    public class Parser
    {
        /// <summary>
        /// Defines the _lexer
        /// </summary>
        private readonly Lexer _lexer;

        /// <summary>
        /// Defines the _current
        /// </summary>
        private Token _current;

        /// <summary>
        /// Defines the _next
        /// </summary>
        private Token _next;

        /// <summary>
        /// Defines the _funcOrClassDepth
        /// </summary>
        private int _funcOrClassDepth = 0;

        /// <summary>
        /// Gets a value indicating whether IsInFunctionOrClass
        /// </summary>
        private bool IsInFunctionOrClass { get => (_funcOrClassDepth > 0); }

        /// <summary>
        /// Defines the _funcDepth
        /// </summary>
        private int _funcDepth = 0;

        /// <summary>
        /// Gets a value indicating whether IsInFunction
        /// </summary>
        private bool IsInFunction { get => (_funcDepth > 0); }

        /// <summary>
        /// Defines the _asyncFuncDepth
        /// </summary>
        private int _asyncFuncDepth = 0;

        /// <summary>
        /// Gets a value indicating whether IsInAsyncFunction
        /// </summary>
        private bool IsInAsyncFunction { get => (_asyncFuncDepth > 0); }

        /// <summary>
        /// Defines the _loopDepth
        /// </summary>
        private int _loopDepth = 0;

        /// <summary>
        /// Gets a value indicating whether IsInLoop
        /// </summary>
        private bool IsInLoop { get => (_loopDepth > 0); }

        /// <summary>
        /// Defines the multipleVarDecl
        /// </summary>
        private bool multipleVarDecl = false;

        /// <summary>
        /// Defines the _destructureParamCounter
        /// </summary>
        private int _destructureParamCounter = 0;

        /// <summary>
        /// Defines the _foreachDestructureCounter
        /// </summary>
        private int _foreachDestructureCounter = 0;

        /// <summary>
        /// Defines the _namespaceScopeCounter
        /// </summary>
        private int _namespaceScopeCounter = 0;

        /// <summary>
        /// Defines the _outBlock
        /// </summary>
        private int _outBlock = 0;

        /// <summary>
        /// Gets a value indicating whether IsInOutBlock
        /// </summary>
        private bool IsInOutBlock { get => _outBlock > 0; }

        private const int MaxRecursionDepth = 256;
        private int _recursionDepth = 0;

        /// <summary>
        /// Defines the _loadPluginDll
        /// </summary>
        private readonly Action<string>? _loadPluginDll;
        private readonly Func<string, string?>? _loadImportSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="Parser"/> class.
        /// </summary>
        /// <param name="lexer">The lexer<see cref="Lexer"/></param>
        /// <param name="loadPluginDll">The loadPluginDll<see cref="Action{string}?"/></param>
        /// <param name="sharedAstByHash">Optional import cache (legacy name) shared across nested parser instances.</param>
        /// <param name="sharedImportStack">Optional import stack shared across nested parser instances.</param>
        public Parser(
            Lexer lexer,
            Action<string>? loadPluginDll = null,
            Dictionary<string, List<Stmt>>? sharedAstByHash = null,
            Stack<string>? sharedImportStack = null,
            Func<string, string?>? loadImportSource = null)
        {
            _lexer = lexer;
            _current = _lexer.GetNextToken();
            _next = _lexer.GetNextToken();

            _loadPluginDll = loadPluginDll;
            _astByImportKey = sharedAstByHash ?? new Dictionary<string, List<Stmt>>();
            _importStack = sharedImportStack ?? new Stack<string>();
            _loadImportSource = loadImportSource;
        }

        /// <summary>
        /// The Advance
        /// </summary>
        private void Advance()
        {
            _current = _next;
            _next = _lexer.GetNextToken();
        }

        /// <summary>
        /// The Eat
        /// </summary>
        /// <param name="type">The type<see cref="TokenType"/></param>
        private void Eat(TokenType type)
        {
            if (_current.Type != type)
                throw new ParserException($"Expected {type}, got {_current.Type} -> '{_current.Value}'", _current.Line, _current.Column, _current.Filename);
            Advance();
        }

        /// <summary>
        /// The IsReservedBindingName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReservedBindingName(string name)
        {
            if (name.StartsWith("__", StringComparison.Ordinal))
                return true;

            return name == "this" || name == "type" || name == "super" || name == "outer";
        }

        /// <summary>
        /// The ThrowIfInvalidParameterName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        private static void ThrowIfInvalidParameterName(string name, int line, int col, string file)
        {
            if (Lexer.Keywords.ContainsKey(name) || IsReservedBindingName(name))
                throw new ParserException($"invalid parameter name '{name}'", line, col, file);
        }

        /// <summary>
        /// Defines the _astByImportKey
        /// </summary>
        private readonly Dictionary<string, List<Stmt>> _astByImportKey;

        /// <summary>
        /// Defines the _importStack
        /// </summary>
        private readonly Stack<string> _importStack;

        /// <summary>
        /// Defines the _seenFunctions
        /// </summary>
        private readonly HashSet<string> _seenFunctions = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines the _seenClasses
        /// </summary>
        private readonly HashSet<string> _seenClasses = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines the _seenEnums
        /// </summary>
        private readonly HashSet<string> _seenEnums = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines the _seenTopLevelSymbols
        /// </summary>
        private readonly Dictionary<string, (string Kind, string Origin)> _seenTopLevelSymbols = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines known top-level symbol names while parsing the current script.
        /// </summary>
        private readonly HashSet<string> _knownTopLevelNames = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines known top-level kinds while parsing the current script.
        /// </summary>
        private readonly Dictionary<string, string> _knownTopLevelKinds = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines namespace roots introduced by namespace declarations in the current script.
        /// </summary>
        private readonly HashSet<string> _knownNamespaceRoots = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines the _http
        /// </summary>
        private static readonly HttpClient _http = CreateImportHttpClient();

        /// <summary>
        /// Defines the HttpImportMaxBytes
        /// </summary>
        private static readonly int HttpImportMaxBytes = ParsePositiveEnvInt("CFGS_IMPORT_HTTP_MAX_BYTES", 4 * 1024 * 1024);

        /// <summary>
        /// The ParsePositiveEnvInt
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="fallback">The fallback<see cref="int"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static int ParsePositiveEnvInt(string name, int fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            if (int.TryParse(raw, out int parsed) && parsed > 0)
                return parsed;
            return fallback;
        }

        /// <summary>
        /// The CreateImportHttpClient
        /// </summary>
        /// <returns>The <see cref="HttpClient"/></returns>
        private static HttpClient CreateImportHttpClient()
        {
            HttpClient client = new(new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            });

            client.Timeout = TimeSpan.FromMilliseconds(ParsePositiveEnvInt("CFGS_IMPORT_HTTP_TIMEOUT_MS", 15000));
            return client;
        }

        /// <summary>
        /// The DownloadHttpImportBytes
        /// </summary>
        /// <param name="uri">The uri<see cref="Uri"/></param>
        /// <returns>The <see cref="byte[]"/></returns>
        private static byte[] DownloadHttpImportBytes(Uri uri)
        {
            using HttpRequestMessage req = new(HttpMethod.Get, uri);
            using HttpResponseMessage resp = _http.Send(req, HttpCompletionOption.ResponseHeadersRead);

            if (!resp.IsSuccessStatusCode)
                throw new IOException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

            long? contentLength = resp.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > HttpImportMaxBytes)
                throw new IOException($"import exceeds size limit ({contentLength.Value} > {HttpImportMaxBytes} bytes)");

            using Stream stream = resp.Content.ReadAsStream();
            return ReadAllBytesWithLimit(stream, HttpImportMaxBytes);
        }

        /// <summary>
        /// The ReadAllBytesWithLimit
        /// </summary>
        /// <param name="stream">The stream<see cref="Stream"/></param>
        /// <param name="maxBytes">The maxBytes<see cref="int"/></param>
        /// <returns>The <see cref="byte[]"/></returns>
        private static byte[] ReadAllBytesWithLimit(Stream stream, int maxBytes)
        {
            byte[] buffer = new byte[8192];
            int total = 0;
            using MemoryStream ms = new();

            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                total += read;
                if (total > maxBytes)
                    throw new IOException($"import exceeds size limit ({total} > {maxBytes} bytes)");

                ms.Write(buffer, 0, read);
            }

            return ms.ToArray();
        }

        /// <summary>
        /// The IsHttpUrl
        /// </summary>
        /// <param name="s">The s<see cref="string?"/></param>
        /// <param name="uri">The uri<see cref="Uri"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsHttpUrl(string? s, out Uri uri)
        {
            if (Uri.TryCreate(s, UriKind.Absolute, out uri!) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return true;
            uri = default!;
            return false;
        }

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
                "function" => "function",
                "class" => "class",
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
                case EnumDeclStmt e:
                    name = e.Name;
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
                case EnumDeclStmt e:
                    name = e.Name;
                    kind = "enum";
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
            if (!string.Equals(currentKind, existingKind, StringComparison.Ordinal))
                return $"duplicate symbol '{name}'";

            return currentKind switch
            {
                "function" => $"duplicate function '{name}'",
                "class" => $"duplicate class '{name}'",
                "enum" => $"duplicate enum '{name}'",
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
                    throw new ParserException(DuplicateTopLevelMessage(name, currKind, prevKind), s.Line, s.Col, s.OriginFile);

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
        private List<Stmt> ParseNamespaceDeclStatements()
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

            bool declareRoot = !_knownTopLevelNames.Contains(parts[0]);
            List<Stmt> namespaceStmts = BuildNamespaceEnsurePathStmts(parts, declareRoot, line, col, file);

            Expr nsExpr = BuildQualifiedAccessExpr(parts, line, col, file);
            string nsTemp = $"__ns_scope_{_namespaceScopeCounter++}";
            List<Stmt> scopedBody = new()
            {
                new VarDecl(nsTemp, nsExpr, line, col, file)
            };
            scopedBody.AddRange(bodyStmts);
            foreach (Stmt stmt in bodyStmts)
            {
                if (!TryGetNamedTopLevel(stmt, out string? name) || string.IsNullOrWhiteSpace(name))
                    continue;

                IndexExpr target = new(
                    new VarExpr(nsTemp, stmt.Line, stmt.Col, stmt.OriginFile),
                    new StringExpr(name, stmt.Line, stmt.Col, stmt.OriginFile),
                    stmt.Line, stmt.Col, stmt.OriginFile);
                scopedBody.Add(new AssignExprStmt(
                    target,
                    new VarExpr(name, stmt.Line, stmt.Col, stmt.OriginFile),
                    stmt.Line, stmt.Col, stmt.OriginFile));
            }

            namespaceStmts.Add(new BlockStmt(scopedBody, line, col, file)
            {
                IsNamespaceScope = true
            });
            _knownNamespaceRoots.Add(root);
            return namespaceStmts;
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

                        if (!TryHandleDllImport(path, ln, col, fn))
                        {
                            List<Stmt> imported = GetImports(path, ln, col, fn);
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

                        List<Stmt> imported = GetImports(path, ln, col, fn);
                        Eat(TokenType.String);

                        Dictionary<string, Stmt> surface = BuildModuleSurface(imported);

                        List<Stmt> materialized = FilterDuplicateTopLevel(imported, allowIdempotentSameOrigin: true);
                        stmts.AddRange(materialized);
                        IndexTopLevelSymbols(materialized);

                        List<Stmt> aliasStmts = BuildNamespaceAliasStmts(alias, surface.Keys, ln, col, fn);
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

                        List<Stmt> imported = GetImports(path, ln, col, fn);
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
                                aliasStmts.Add(new VarDecl(
                                    localName,
                                    new VarExpr(importName, ln, col, fn),
                                    ln, col, fn));
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

                        List<Stmt> imported = GetImports(path, ln, col, fn);
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
                    List<Stmt> nsStmts = ParseNamespaceDeclStatements();
                    stmts.AddRange(nsStmts);
                    TrackKnownTopLevelSymbols(nsStmts);
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
        // --- NEW: import path resolver ----------------------------------------------

        private static string GetExeBaseDir()
        {
            try
            {
                return AppContext.BaseDirectory;
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }

        private static IEnumerable<string> GetSearchBases(string fsname)
        {
            // 1) directory of current source file
            if (!string.IsNullOrWhiteSpace(fsname))
            {
                string? srcDir = null;
                try
                {
                    srcDir = Path.GetDirectoryName(Path.GetFullPath(fsname));
                }
                catch (ArgumentException) { }
                catch (System.IO.IOException) { }

                if (!string.IsNullOrWhiteSpace(srcDir))
                    yield return srcDir!;
            }

            // 2) current working directory
            yield return Directory.GetCurrentDirectory();

            // 3) exe base directory
            yield return GetExeBaseDir();
        }

        private static string? ResolveImportPath(string rawPath, string fsname)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return null;

            // absolute path -> just check it
            if (Path.IsPathRooted(rawPath))
            {
                string fullAbs = Path.GetFullPath(rawPath);
                return File.Exists(fullAbs) ? fullAbs : null;
            }

            // relative -> try search bases
            foreach (string baseDir in GetSearchBases(fsname))
            {
                try
                {
                    string candidate = Path.GetFullPath(Path.Combine(baseDir, rawPath));
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException) { }
                catch (System.IO.IOException) { }
            }

            // extra fallback:
            // if someone wrote "folder\lib.dll" but file is next to exe
            try
            {
                string fileNameOnly = Path.GetFileName(rawPath);
                if (!string.IsNullOrWhiteSpace(fileNameOnly))
                {
                    string exeDir = GetExeBaseDir();
                    string candidate = Path.GetFullPath(Path.Combine(exeDir, fileNameOnly));
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch (ArgumentException) { }
            catch (System.IO.IOException) { }

            return null;
        }

        /// <summary>
        /// The BuildImportCacheKeyForUrl
        /// </summary>
        /// <param name="resourceId">The resourceId<see cref="string"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string BuildImportCacheKeyForUrl(string resourceId)
            => $"url::{resourceId}";

        /// <summary>
        /// The BuildImportCacheKeyForFile
        /// </summary>
        /// <param name="fullPath">The fullPath<see cref="string"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string BuildImportCacheKeyForFile(string fullPath)
        {
            string normalized = Path.GetFullPath(fullPath);
            if (OperatingSystem.IsWindows())
                normalized = normalized.ToUpperInvariant();
            return $"file::{normalized}";
        }

        /// <summary>
        /// The TryHandleDllImport
        /// </summary>
        /// <param name="rawPath">The rawPath<see cref="string"/></param>
        /// <param name="ln">The ln<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fsname">The fsname<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryHandleDllImport(string rawPath, int ln, int col, string fsname)
        {
            if (_loadPluginDll == null)
                return false;

            if (string.IsNullOrWhiteSpace(rawPath))
                return false;

            if (!rawPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return false;

            string? resolved = ResolveImportPath(rawPath, fsname);

            if (resolved == null)
                throw new ParserException($"plugin dll not found '{rawPath}' (searched script dir, cwd, exe dir)", ln, col, fsname);

            try
            {
                _loadPluginDll(resolved);
            }
            catch (Exception ex)
            {
                throw new ParserException($"failed to load plugin dll '{rawPath}': {ex.Message}", ln, col, fsname);
            }
            return true;
        }

        /// <summary>
        /// The GetImports
        /// </summary>
        /// <param name="path">The path<see cref="string"/></param>
        /// <param name="ln">The ln<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fsname">The fsname<see cref="string"/></param>
        /// <param name="specClass">The specClass<see cref="string"/></param>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        private List<Stmt> GetImports(string path, int ln, int col, string fsname, string specClass = "")
        {
            if (IsHttpUrl(path, out Uri? uri))
            {
                List<Stmt> uresult = new();
                string resourceId = uri.ToString();
                string urlCacheKey = BuildImportCacheKeyForUrl(resourceId);

                if (!string.IsNullOrWhiteSpace(fsname) &&
                    string.Equals(resourceId, fsname, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"Import Warning: Ignoring self-import of '{resourceId}'.");
                    return uresult;
                }

                if (_importStack.Contains(resourceId))
                {
                    string chain = string.Join(" -> ", _importStack.Reverse().Append(resourceId));
                    throw new ParserException($"Import cycle detected: {chain}", ln, col, fsname);
                }

                if (_astByImportKey.TryGetValue(urlCacheKey, out List<Stmt>? cachedAst))
                {
                    if (!string.IsNullOrWhiteSpace(specClass))
                    {
                        Stmt? cls = cachedAst.FirstOrDefault(s => s is ClassDeclStmt c && c.Name == specClass);
                        if (cls is null)
                            throw new ParserException($"Could not find class '{specClass}' in import '{path}'", ln, col, fsname);
                        uresult.Add(cls);
                    }
                    else
                    {
                        uresult.AddRange(cachedAst);
                    }

                    return uresult;
                }

                byte[] bytes;
                try
                {
                    bytes = DownloadHttpImportBytes(uri);
                }
                catch (Exception ex)
                {
                    throw new ParserException($"failed to download '{resourceId}': {ex.Message}", ln, col, fsname);
                }

                _importStack.Push(resourceId);
                try
                {
                    string nsrc;
                    using (MemoryStream ms = new(bytes, writable: false))
                    using (StreamReader sr = new(ms, detectEncodingFromByteOrderMarks: true))
                        nsrc = sr.ReadToEnd();

                    Lexer lex = new(resourceId, nsrc);

                    Parser prs = new(lex, _loadPluginDll, _astByImportKey, _importStack, _loadImportSource);

                    List<Stmt> importedAst = prs.Parse();

                    _astByImportKey[urlCacheKey] = importedAst;
                    if (!string.IsNullOrWhiteSpace(specClass))
                    {
                        Stmt? cls = importedAst.FirstOrDefault(s => s is ClassDeclStmt c && c.Name == specClass);
                        if (cls is null)
                            throw new ParserException($"Could not find class '{specClass}' in import '{path}'", ln, col, fsname);
                        uresult.Add(cls);
                    }
                    else
                    {
                        uresult.AddRange(importedAst);
                    }

                    return uresult;
                }
                catch (ParserException) { throw; }
                catch (Exception pex)
                {
                    throw new ParserException(pex.Message, _current.Line, _current.Column, _current.Filename);
                }
                finally
                {
                    _importStack.Pop();
                }
            }

            // ---------- LOCAL FILE IMPORTS WITH EXE-PATH FALLBACK ----------

            List<Stmt> result = new();

            // NEW: unified resolver (script dir -> cwd -> exe dir)
            string? resolved = ResolveImportPath(path, fsname);

            if (resolved == null)
                throw new ParserException($"import path not found '{path}' (searched script dir, cwd, exe dir)", ln, col, fsname);

            string fullPath = Path.GetFullPath(resolved);
            string fileCacheKey = BuildImportCacheKeyForFile(fullPath);

            string? thisFile = string.IsNullOrWhiteSpace(fsname) ? null : Path.GetFullPath(fsname);
            if (thisFile != null && string.Equals(fullPath, thisFile, StringComparison.OrdinalIgnoreCase))
                throw new ParserException($"self-import of '{fullPath}'.", ln, col, fsname);

            if (_importStack.Contains(fullPath))
            {
                string chain = string.Join(" -> ", _importStack.Reverse().Append(fullPath));
                throw new ParserException($"Import cycle detected: {chain}", ln, col, fsname);
            }

            if (_astByImportKey.TryGetValue(fileCacheKey, out List<Stmt>? cachedAstFile))
            {
                if (!string.IsNullOrWhiteSpace(specClass))
                {
                    Stmt? cls = cachedAstFile.FirstOrDefault(s => s is ClassDeclStmt c && c.Name == specClass);
                    if (cls is null)
                        throw new ParserException($"Could not find class '{specClass}' in import file '{path}'", ln, col, fsname);

                    result.Add(cls);
                }
                else
                {
                    result.AddRange(cachedAstFile);
                }

                return result;
            }

            _importStack.Push(fullPath);
            try
            {
                string? overlaySource = _loadImportSource?.Invoke(fullPath);
                string nsrc;
                if (overlaySource is not null)
                {
                    nsrc = overlaySource;
                }
                else
                {
                    using StreamReader sr = new(fullPath, detectEncodingFromByteOrderMarks: true);
                    nsrc = sr.ReadToEnd();
                }

                Lexer lex = new(fullPath, nsrc);

                Parser prs = new(lex, _loadPluginDll, _astByImportKey, _importStack, _loadImportSource);

                List<Stmt> importedAst = prs.Parse();

                _astByImportKey[fileCacheKey] = importedAst;
                if (!string.IsNullOrWhiteSpace(specClass))
                {
                    Stmt? cls = importedAst.FirstOrDefault(s => s is ClassDeclStmt c && c.Name == specClass);
                    if (cls is null)
                        throw new ParserException($"Could not find class '{specClass}' in import file '{path}'", ln, col, fsname);
                    result.Add(cls);
                }
                else
                {
                    result.AddRange(importedAst);
                }

                return result;
            }
            catch (ParserException)
            {
                throw;
            }
            catch (Exception pex)
            {
                throw new ParserException(pex.Message, _current.Line, _current.Column, _current.Filename);
            }
            finally
            {
                _importStack.Pop();
            }
        }

        /// <summary>
        /// The Statement
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt Statement()
        {
            if (multipleVarDecl)
                return ParseVarDecl;

            return IsInFunctionOrClass
                ? ParseStatementInMemberScope()
                : ParseStatementTopLevel();
        }

        /// <summary>
        /// The TryParseCommonStatement
        /// </summary>
        /// <param name="stmt">The stmt<see cref="Stmt"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryParseCommonStatement(out Stmt stmt)
        {
            switch (_current.Type)
            {
                case TokenType.Semi:
                    stmt = ParseEmptyStmt;
                    return true;
                case TokenType.Var:
                    stmt = ParseVarDecl;
                    return true;
                case TokenType.Const:
                    stmt = ParseConstDecl;
                    return true;
                case TokenType.Ident:
                    stmt = ParseAssignOrIndexAssignOrPushOrExpr();
                    return true;
                case TokenType.LParen:
                    if (_next.Type == TokenType.LBracket || _next.Type == TokenType.LBrace)
                    {
                        stmt = ParseParenDestructureAssignStmt();
                        return true;
                    }
                    stmt = default!;
                    return false;
                case TokenType.LBracket:
                    stmt = ParseDestructureAssignStmt();
                    return true;
                case TokenType.Func:
                case TokenType.Async:
                    stmt = ParseFuncDecl();
                    return true;
                case TokenType.LBrace:
                    stmt = ParseBlock();
                    return true;
                case TokenType.Class:
                    stmt = ParseClassDecl();
                    return true;
                case TokenType.Enum:
                    stmt = ParseEnumDecl();
                    return true;
                default:
                    stmt = default!;
                    return false;
            }
        }

        /// <summary>
        /// The ParseStatementInMemberScope
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseStatementInMemberScope()
        {
            switch (_current.Type)
            {
                case TokenType.ForEach: return ParseForeach();
                case TokenType.Try: return ParseTry();
                case TokenType.Throw: return ParseThrow();
                case TokenType.Return: return ParseReturnStmt();
                case TokenType.Yield: return ParseYieldStmt();
                case TokenType.Delete: return ParseDelete;
                case TokenType.Match: return ParseMatch();
                case TokenType.If: return ParseIf();
                case TokenType.While: return ParseWhile();
                case TokenType.Do: return ParseDoWhile();
                case TokenType.Break: return ParseBreak;
                case TokenType.Continue: return ParseContinue;
                case TokenType.For: return ParseFor();
            }

            if (TryParseCommonStatement(out Stmt stmt))
                return stmt;

            return ParseExprStmt;
        }

        /// <summary>
        /// The ParseStatementTopLevel
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseStatementTopLevel()
        {
            if (_current.Type == TokenType.Export)
                return ParseExportDecl();

            if (TryParseCommonStatement(out Stmt stmt))
                return stmt;

            throw new ParserException(
                $"invalid top-level statement {_current.Type}",
                _current.Line, _current.Column, _current.Filename);
        }

        /// <summary>
        /// The ParseThrow
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseThrow()
        {
            int line = _current.Line, col = _current.Column;
            Eat(TokenType.Throw);
            Expr ex = Expr();
            Eat(TokenType.Semi);
            return new ThrowStmt(ex, line, col, _current.Filename);
        }

        /// <summary>
        /// The ParseTry
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseTry()
        {
            int line = _current.Line, col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Try);
            BlockStmt tryBlock = ParseEmbeddedBlockOrSingleStatement();

            string? catchIdent = null;
            BlockStmt? catchBlock = null;
            if (_current.Type == TokenType.Catch)
            {
                Eat(TokenType.Catch);
                Eat(TokenType.LParen);

                if (_current.Type == TokenType.Ident)
                {
                    catchIdent = _current.Value!.ToString();

                    if (catchIdent is not null && Lexer.Keywords.ContainsKey(catchIdent))
                        throw new ParserException($"invalid symbol declaration name '{catchIdent}'", _current.Line, _current.Column, _current.Filename);

                    Advance();
                }
                else if (_current.Type != TokenType.RParen)
                {
                    throw new ParserException("expected identifier or ')'", _current.Line, _current.Column, _current.Filename);
                }

                Eat(TokenType.RParen);
                catchBlock = ParseEmbeddedBlockOrSingleStatement();
            }

            BlockStmt? finallyBlock = null;
            if (_current.Type == TokenType.Finally)
            {
                Eat(TokenType.Finally);
                finallyBlock = ParseEmbeddedBlockOrSingleStatement();
            }

            if (catchBlock == null && finallyBlock == null)
                throw new ParserException("try must have at least catch or finally", line, col, file);

            return new TryStmt(tryBlock, catchIdent, catchBlock, finallyBlock, line, col, file);
        }

        /// <summary>
        /// Gets the ParseEmptyStmt
        /// </summary>
        private Stmt ParseEmptyStmt
        {
            get
            {
                int line = _current.Line;
                int col = _current.Column;
                string fs = _current.Filename;

                Eat(TokenType.Semi);
                return new EmptyStmt(TokenType.Semi, line, col, fs);
            }
        }

        /// <summary>
        /// The ParseEnumDecl
        /// </summary>
        /// <returns>The <see cref="EnumDeclStmt"/></returns>
        private EnumDeclStmt ParseEnumDecl()
        {
            int declLine = _current.Line;
            int declCol = _current.Column;

            Eat(TokenType.Enum);

            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected enum name", declLine, declCol, _current.Filename);

            string name = _current.Value.ToString() ?? "";

            if (Lexer.Keywords.ContainsKey(name))
                throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.Ident);

            Eat(TokenType.LBrace);

            List<EnumMemberNode> members = new();
            HashSet<string> usedNames = new(StringComparer.Ordinal);
            HashSet<int> usedValues = new();

            int nextAutoValue = 0;

            while (_current.Type != TokenType.RBrace)
            {
                int memberLine = _current.Line;
                int memberCol = _current.Column;

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected identifier in enum body", memberLine, memberCol, _current.Filename);

                string memberName = _current.Value.ToString() ?? "";
                if (Lexer.Keywords.ContainsKey(memberName))
                    throw new ParserException($"invalid symbol declaration name '{memberName}'", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Ident);

                int value;
                if (_current.Type == TokenType.Assign)
                {
                    Eat(TokenType.Assign);
                    if (_current.Type != TokenType.Number || _current.Value is not int iv)
                        throw new ParserException("expected number after '='", _current.Line, _current.Column, _current.Filename);

                    value = Convert.ToInt32(_current.Value);
                    Eat(TokenType.Number);
                }
                else
                {
                    value = nextAutoValue;
                }

                if (!usedNames.Add(memberName))
                    throw new ParserException(
                        $"duplicate enum member name '{memberName}' in enum '{name}'",
                        memberLine, memberCol, _current.Filename
                    );

                if (!usedValues.Add(value))
                    throw new ParserException(
                        $"duplicate enum value '{value}' in enum '{name}'",
                        memberLine, memberCol, _current.Filename
                    );

                members.Add(new EnumMemberNode(memberName, value, memberLine, memberCol, _current.Filename));

                nextAutoValue = value + 1;

                if (_current.Type == TokenType.Comma)
                {
                    Eat(TokenType.Comma);

                    if (_current.Type == TokenType.RBrace)
                        break;
                }
                else
                {
                    break;
                }
            }

            Eat(TokenType.RBrace);

            return new EnumDeclStmt(name, members, declLine, declCol, _current.Filename);
        }

        /// <summary>
        /// Gets the ParseExprStmt
        /// </summary>
        private Stmt ParseExprStmt
        {
            get
            {
                int line = _current.Line;
                int col = _current.Column;

                Expr e = Expr();
                Eat(TokenType.Semi);
                return new ExprStmt(e, line, col, _current.Filename);
            }
        }

        /// <summary>
        /// The ParseClassDecl
        /// </summary>
        /// <returns>The <see cref="ClassDeclStmt"/></returns>
        private ClassDeclStmt ParseClassDecl()
        {
            int line = _current.Line;
            int col = _current.Column;

            Eat(TokenType.Class);

            if (_current.Type != TokenType.Ident)
                throw new ParserException($"expected class name", line, col, _current.Filename);

            string name = _current.Value.ToString() ?? "";
            if (Lexer.Keywords.ContainsKey(name))
                throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);
            Eat(TokenType.Ident);

            List<string> ctorParams = ParseParams();

            string? baseName = null;
            List<Expr> baseArgs = new();
            if (_current.Type == TokenType.Colon)
            {
                Eat(TokenType.Colon);

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected base class name after ':'", line, col, _current.Filename);

                baseName = _current.Value.ToString() ?? "";

                if (baseName == name)
                    throw new ParserException("self inheritance not allowed", _current.Line, _current.Column, _current.Filename);

                Eat(TokenType.Ident);

                if (_current.Type == TokenType.LParen)
                {
                    Eat(TokenType.LParen);

                    if (_current.Type != TokenType.RParen)
                        baseArgs = ParseExprList(allowNamedArgs: true);

                    Eat(TokenType.RParen);
                }
            }

            _funcOrClassDepth++;
            Eat(TokenType.LBrace);

            List<FuncDeclStmt> methods = new();
            Dictionary<string, Expr?> fields = new();

            List<FuncDeclStmt> staticMethods = new();
            Dictionary<string, Expr?> staticFields = new();
            List<EnumDeclStmt> staticEnums = new();
            List<ClassDeclStmt> nestedClasses = new();
            Dictionary<string, MemberVisibility> fieldVisibility = new(StringComparer.Ordinal);
            Dictionary<string, MemberVisibility> staticFieldVisibility = new(StringComparer.Ordinal);
            Dictionary<string, MemberVisibility> methodVisibility = new(StringComparer.Ordinal);
            Dictionary<string, MemberVisibility> staticMethodVisibility = new(StringComparer.Ordinal);
            Dictionary<string, MemberVisibility> enumVisibility = new(StringComparer.Ordinal);
            Dictionary<string, MemberVisibility> nestedClassVisibility = new(StringComparer.Ordinal);
            HashSet<string> constFields = new(StringComparer.Ordinal);
            HashSet<string> staticConstFields = new(StringComparer.Ordinal);

            bool CheckFieldNames(string nm) =>
                (from sm in staticMethods where sm.Name == nm select sm).Any() ||
                (from en in staticEnums where en.Name == nm select en).Any() ||
                (from sf in staticFields where sf.Key == nm select sf).Any() ||
                (from mt in methods where mt.Name == nm select mt).Any() ||
                (from fld in fields where fld.Key == nm select fld).Any() ||
                (from nsc in nestedClasses where nsc.Name == nm select nsc).Any();

            static MemberVisibility ParseVisibility(TokenType type)
            {
                return type switch
                {
                    TokenType.Public => MemberVisibility.Public,
                    TokenType.Private => MemberVisibility.Private,
                    TokenType.Protected => MemberVisibility.Protected,
                    _ => MemberVisibility.Public
                };
            }

            while (_current.Type != TokenType.RBrace)
            {
                bool StaticSet = false;
                bool seenStatic = false;
                bool seenVisibility = false;
                bool ConstSet = false;
                bool seenConst = false;
                MemberVisibility visibility = MemberVisibility.Public;

                while (true)
                {
                    if (_current.Type == TokenType.Static)
                    {
                        if (seenStatic)
                            throw new ParserException("duplicate 'static' modifier in class member declaration", _current.Line, _current.Column, _current.Filename);

                        seenStatic = true;
                        StaticSet = true;
                        Eat(TokenType.Static);
                        continue;
                    }

                    if (_current.Type == TokenType.Public || _current.Type == TokenType.Private || _current.Type == TokenType.Protected)
                    {
                        if (seenVisibility)
                            throw new ParserException("duplicate access modifier in class member declaration", _current.Line, _current.Column, _current.Filename);

                        visibility = ParseVisibility(_current.Type);
                        seenVisibility = true;
                        Advance();
                        continue;
                    }

                    if (_current.Type == TokenType.Const)
                    {
                        if (seenConst)
                            throw new ParserException("duplicate 'const' modifier in class member declaration", _current.Line, _current.Column, _current.Filename);

                        seenConst = true;
                        ConstSet = true;
                        Eat(TokenType.Const);
                        continue;
                    }

                    break;
                }

                if (_current.Type == TokenType.Var || (ConstSet && _current.Type == TokenType.Ident))
                {
                    if (_current.Type == TokenType.Var)
                        Eat(TokenType.Var);

                    string fieldName = _current.Value.ToString() ?? "";
                    if (CheckFieldNames(fieldName))
                        throw new ParserException($"Field '{fieldName}' already declared in class '{name}'", _current.Line, _current.Column, _current.Filename);
                    Eat(TokenType.Ident);

                    Expr? init = null;
                    if (_current.Type == TokenType.Assign)
                    {
                        Eat(TokenType.Assign);
                        init = Expr();
                    }
                    Eat(TokenType.Semi);

                    if (StaticSet)
                    {
                        staticFields[fieldName] = init ?? new NumberExpr(0, line, col, _current.Filename);
                        staticFieldVisibility[fieldName] = visibility;
                        if (ConstSet) staticConstFields.Add(fieldName);
                    }
                    else
                    {
                        fields[fieldName] = init ?? new NumberExpr(0, line, col, _current.Filename);
                        fieldVisibility[fieldName] = visibility;
                        if (ConstSet) constFields.Add(fieldName);
                    }
                }
                else if (_current.Type == TokenType.Class)
                {
                    if (StaticSet)
                        throw new ParserException("nested classes cannot be static", _current.Line, _current.Column, _current.Filename);
                    ClassDeclStmt inner = ParseClassDecl();

                    if (CheckFieldNames(inner.Name))
                        throw new ParserException($"Field '{inner.Name}' already declared in class '{name}'", _current.Line, _current.Column, _current.Filename);
                    inner = new ClassDeclStmt(
                        inner.Name, inner.Methods, inner.Enums,
                        inner.Fields, inner.StaticFields, inner.StaticMethods,
                        inner.Parameters, inner.Line, inner.Col, inner.OriginFile,
                        inner.BaseName, inner.BaseCtorArgs, inner.NestedClasses, isNested: true,
                        fieldVisibility: inner.FieldVisibility,
                        staticFieldVisibility: inner.StaticFieldVisibility,
                        methodVisibility: inner.MethodVisibility,
                        staticMethodVisibility: inner.StaticMethodVisibility,
                        enumVisibility: inner.EnumVisibility,
                        nestedClassVisibility: inner.NestedClassVisibility,
                        constFields: inner.ConstFields,
                        staticConstFields: inner.StaticConstFields
                    );
                    nestedClasses.Add(inner);
                    nestedClassVisibility[inner.Name] = visibility;
                }

                else if (_current.Type == TokenType.Enum)
                {
                    EnumDeclStmt enm = ParseEnumDecl();
                    if (CheckFieldNames(enm.Name))
                        throw new ParserException($"Field '{enm.Name}' already declared in class '{name}'", _current.Line, _current.Column, _current.Filename);
                    staticEnums.Add(enm);
                    enumVisibility[enm.Name] = visibility;
                }
                else if (_current.Type == TokenType.Func || _current.Type == TokenType.Async)
                {
                    FuncDeclStmt func = ParseFuncDecl();
                    if (CheckFieldNames(func.Name))
                        throw new ParserException($"Field '{func.Name}' already declared in class '{name}'", _current.Line, _current.Column, _current.Filename);
                    if (StaticSet)
                    {
                        staticMethods.Add(func);
                        staticMethodVisibility[func.Name] = visibility;
                    }
                    else
                    {
                        methods.Add(func);
                        methodVisibility[func.Name] = visibility;
                    }
                }
                else
                {
                    throw new ParserException(
                        $"unexpected token in class body: {_current.Type}",
                        _current.Line, _current.Column, _current.Filename
                    );
                }
            }

            Eat(TokenType.RBrace);
            _funcOrClassDepth--;
            return new ClassDeclStmt(
                name,
                methods,
                staticEnums,
                fields,
                staticFields,
                staticMethods,
                ctorParams,
                line,
                col,
                _current.Filename,
                baseName,
                baseArgs,
                nestedClasses,
                false,
                fieldVisibility,
                staticFieldVisibility,
                methodVisibility,
                staticMethodVisibility,
                enumVisibility,
                nestedClassVisibility,
                constFields,
                staticConstFields
            );
        }

        /// <summary>
        /// Gets the ParseNew
        /// </summary>
        private Expr ParseNew
        {
            get
            {
                int line = _current.Line;
                int col = _current.Column;
                string origin = _current.Filename;

                Eat(TokenType.New);

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected class name after 'new'", line, col, origin);

                StringBuilder qn = new();
                qn.Append(_current.Value.ToString());
                Eat(TokenType.Ident);

                while (_current.Type == TokenType.Dot)
                {
                    Eat(TokenType.Dot);
                    if (_current.Type != TokenType.Ident)
                        throw new ParserException("expected identifier after '.' in qualified class name",
                                                  _current.Line, _current.Column, origin);
                    qn.Append('.');
                    qn.Append(_current.Value.ToString());
                    Eat(TokenType.Ident);
                }

                List<Expr> args = new();

                if (_current.Type == TokenType.LParen)
                {
                    Eat(TokenType.LParen);
                    if (_current.Type != TokenType.RParen)
                        args = ParseExprList(allowNamedArgs: true);
                    Eat(TokenType.RParen);
                }

                List<(string, Expr)> inits = new();
                if (_current.Type == TokenType.LBrace)
                    inits = ParseInitializerItems();

                return new NewExpr(qn.ToString(), args, inits, line, col, origin);
            }
        }

        /// <summary>
        /// The ParseInitializerItems
        /// </summary>
        /// <returns>The <see cref="List{(string, Expr)}"/></returns>
        private List<(string, Expr)> ParseInitializerItems()
        {
            List<(string, Expr)> items = new();

            Eat(TokenType.LBrace);

            if (_current.Type != TokenType.RBrace)
            {
                while (true)
                {
                    if (_current.Type != TokenType.Ident)
                        throw new ParserException("expected identifier as initializer key",
                                                  _current.Line, _current.Column, _current.Filename);

                    string name = _current.Value.ToString()!;
                    Eat(TokenType.Ident);

                    if (_current.Type == TokenType.Colon || _current.Type == TokenType.Assign)
                        Advance();
                    else
                        throw new ParserException("expected ':' or '=' in object initializer",
                                                  _current.Line, _current.Column, _current.Filename);

                    Expr val = Expr();
                    items.Add((name!, val!));

                    if (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBrace) break;
                        continue;
                    }
                    break;
                }
            }

            Eat(TokenType.RBrace);
            return items;
        }

        /// <summary>
        /// The MaybeParseObjectInitializer
        /// </summary>
        /// <param name="target">The target<see cref="Expr"/></param>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr MaybeParseObjectInitializer(Expr target)
        {
            if (target is NewExpr) return target;

            if (_current.Type == TokenType.LBrace)
            {
                List<(string, Expr)> inits = ParseInitializerItems();
                return new ObjectInitExpr(target, inits, target.Line, target.Col, target.OriginFile);
            }
            return target;
        }

        /// <summary>
        /// The ParseMatch
        /// </summary>
        /// <returns>The <see cref="MatchStmt"/></returns>
        private MatchStmt ParseMatch()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.Match);
            Eat(TokenType.LParen);
            Expr expr = Expr();
            Eat(TokenType.RParen);
            Eat(TokenType.LBrace);

            List<CaseClause> cases = new();
            BlockStmt? defaultCase = null;

            while (_current.Type == TokenType.Case || _current.Type == TokenType.Default)
            {
                if (_current.Type == TokenType.Case)
                {
                    Eat(TokenType.Case);
                    MatchPattern pattern = ParseMatchPattern();
                    ValidateUniquePatternBindings(pattern);
                    Expr? guard = ParseOptionalMatchGuard();
                    Eat(TokenType.Colon);
                    BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
                    cases.Add(new CaseClause(pattern, guard, body, pattern.Line, pattern.Col, pattern.OriginFile));
                }
                else if (_current.Type == TokenType.Default)
                {
                    if (defaultCase is not null)
                        throw new ParserException("multiple default case not allowed", _current.Line, _current.Column, _current.Filename);
                    Eat(TokenType.Default);
                    Eat(TokenType.Colon);
                    defaultCase = ParseEmbeddedBlockOrSingleStatement();
                }
            }

            Eat(TokenType.RBrace);
            return new MatchStmt(expr, cases, defaultCase, line, col, file);
        }

        /// <summary>
        /// The ParseMatchPattern
        /// </summary>
        /// <returns>The <see cref="MatchPattern"/></returns>
        private MatchPattern ParseMatchPattern()
        {
            if (_current.Type == TokenType.Ident && (_current.Value?.ToString() ?? "") == "_")
            {
                Token tok = _current;
                Eat(TokenType.Ident);
                return new WildcardMatchPattern(tok.Line, tok.Column, tok.Filename);
            }

            if (_current.Type == TokenType.Var)
                return ParseMatchBindingPattern();

            if (_current.Type == TokenType.LBracket)
                return ParseArrayMatchPattern();

            if (_current.Type == TokenType.LBrace)
                return ParseDictMatchPattern();

            Expr value = Expr();
            return new ValueMatchPattern(value, value.Line, value.Col, value.OriginFile);
        }

        /// <summary>
        /// The ParseMatchBindingPattern
        /// </summary>
        /// <returns>The <see cref="BindingMatchPattern"/></returns>
        private BindingMatchPattern ParseMatchBindingPattern()
        {
            Eat(TokenType.Var);
            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected identifier after 'var' in match pattern", _current.Line, _current.Column, _current.Filename);

            Token identTok = _current;
            string name = identTok.Value?.ToString() ?? "";
            if (name == "_")
                throw new ParserException("cannot bind '_' in match pattern; use '_' directly as wildcard", identTok.Line, identTok.Column, identTok.Filename);

            if (name == "this" || name == "type" || name == "super" || name == "outer")
                throw new ParserException($"reserved name '{name}' cannot be used as match binding", identTok.Line, identTok.Column, identTok.Filename);

            Eat(TokenType.Ident);
            return new BindingMatchPattern(name, identTok.Line, identTok.Column, identTok.Filename);
        }

        /// <summary>
        /// The ParseArrayMatchPattern
        /// </summary>
        /// <returns>The <see cref="ArrayMatchPattern"/></returns>
        private ArrayMatchPattern ParseArrayMatchPattern()
        {
            Token open = _current;
            Eat(TokenType.LBracket);

            List<MatchPattern> elements = new();
            if (_current.Type != TokenType.RBracket)
            {
                while (true)
                {
                    elements.Add(ParseMatchPattern());
                    if (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBracket)
                            break;
                        continue;
                    }
                    break;
                }
            }

            Eat(TokenType.RBracket);
            return new ArrayMatchPattern(elements, open.Line, open.Column, open.Filename);
        }

        /// <summary>
        /// The ParseDictMatchPattern
        /// </summary>
        /// <returns>The <see cref="DictMatchPattern"/></returns>
        private DictMatchPattern ParseDictMatchPattern()
        {
            Token open = _current;
            Eat(TokenType.LBrace);

            List<(string Key, MatchPattern Pattern)> entries = new();
            HashSet<string> seenKeys = new(StringComparer.Ordinal);

            if (_current.Type != TokenType.RBrace)
            {
                while (true)
                {
                    Token keyTok = _current;
                    string key = _current.Type switch
                    {
                        TokenType.Ident => _current.Value?.ToString() ?? "",
                        TokenType.String => _current.Value?.ToString() ?? "",
                        _ => throw new ParserException("expected identifier or string key in dictionary match pattern", _current.Line, _current.Column, _current.Filename)
                    };

                    if (_current.Type == TokenType.Ident)
                        Eat(TokenType.Ident);
                    else
                        Eat(TokenType.String);

                    if (!seenKeys.Add(key))
                        throw new ParserException($"duplicate key '{key}' in dictionary match pattern", keyTok.Line, keyTok.Column, keyTok.Filename);

                    Eat(TokenType.Colon);
                    MatchPattern pat = ParseMatchPattern();
                    entries.Add((key, pat));

                    if (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBrace)
                            break;
                        continue;
                    }
                    break;
                }
            }

            Eat(TokenType.RBrace);
            return new DictMatchPattern(entries, open.Line, open.Column, open.Filename);
        }

        /// <summary>
        /// The ValidateUniquePatternBindings
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        private static void ValidateUniquePatternBindings(MatchPattern pattern)
        {
            HashSet<string> names = new(StringComparer.Ordinal);

            static void Walk(MatchPattern p, HashSet<string> seen)
            {
                switch (p)
                {
                    case WildcardMatchPattern:
                    case ValueMatchPattern:
                        return;

                    case BindingMatchPattern b:
                        if (!seen.Add(b.Name))
                            throw new ParserException($"duplicate match binding '{b.Name}'", b.Line, b.Col, b.OriginFile);
                        return;

                    case ArrayMatchPattern a:
                        foreach (MatchPattern elem in a.Elements)
                            Walk(elem, seen);
                        return;

                    case DictMatchPattern d:
                        foreach ((string _, MatchPattern sub) in d.Entries)
                            Walk(sub, seen);
                        return;

                    default:
                        throw new ParserException($"unsupported match pattern node '{p.GetType().Name}'", p.Line, p.Col, p.OriginFile);
                }
            }

            Walk(pattern, names);
        }

        /// <summary>
        /// The ParseOptionalMatchGuard
        /// </summary>
        /// <returns>The <see cref="Expr?"/></returns>
        private Expr? ParseOptionalMatchGuard()
        {
            if (_current.Type != TokenType.If)
                return null;

            Eat(TokenType.If);
            Eat(TokenType.LParen);
            Expr guard = Expr();
            Eat(TokenType.RParen);
            return guard;
        }

        /// <summary>
        /// The ParseDestructurePattern
        /// </summary>
        /// <returns>The <see cref="MatchPattern"/></returns>
        private MatchPattern ParseDestructurePattern()
        {
            if (_current.Type == TokenType.Ident)
            {
                Token tok = _current;
                string name = tok.Value?.ToString()
                              ?? throw new ParserException("invalid destructuring target", tok.Line, tok.Column, tok.Filename);
                Eat(TokenType.Ident);

                if (name == "_")
                    return new WildcardMatchPattern(tok.Line, tok.Column, tok.Filename);

                if (Lexer.Keywords.ContainsKey(name))
                    throw new ParserException($"invalid destructuring binding name '{name}'", tok.Line, tok.Column, tok.Filename);

                return new BindingMatchPattern(name, tok.Line, tok.Column, tok.Filename);
            }

            if (_current.Type == TokenType.LBracket)
                return ParseArrayDestructurePattern();

            if (_current.Type == TokenType.LBrace)
                return ParseDictDestructurePattern();

            throw new ParserException("invalid destructuring target", _current.Line, _current.Column, _current.Filename);
        }

        /// <summary>
        /// The ParseArrayDestructurePattern
        /// </summary>
        /// <returns>The <see cref="ArrayMatchPattern"/></returns>
        private ArrayMatchPattern ParseArrayDestructurePattern()
        {
            Token open = _current;
            Eat(TokenType.LBracket);

            List<MatchPattern> elements = new();
            if (_current.Type != TokenType.RBracket)
            {
                while (true)
                {
                    elements.Add(ParseDestructurePattern());

                    if (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBracket)
                            break;
                        continue;
                    }
                    break;
                }
            }

            Eat(TokenType.RBracket);
            return new ArrayMatchPattern(elements, open.Line, open.Column, open.Filename);
        }

        /// <summary>
        /// The ParseDictDestructurePattern
        /// </summary>
        /// <returns>The <see cref="DictMatchPattern"/></returns>
        private DictMatchPattern ParseDictDestructurePattern()
        {
            Token open = _current;
            Eat(TokenType.LBrace);

            List<(string Key, MatchPattern Pattern)> entries = new();
            HashSet<string> seenKeys = new(StringComparer.Ordinal);

            if (_current.Type != TokenType.RBrace)
            {
                while (true)
                {
                    Token keyTok = _current;
                    string key = _current.Type switch
                    {
                        TokenType.Ident => _current.Value?.ToString()
                            ?? throw new ParserException("invalid key in destructuring pattern", _current.Line, _current.Column, _current.Filename),
                        TokenType.String => _current.Value?.ToString()
                            ?? throw new ParserException("invalid key in destructuring pattern", _current.Line, _current.Column, _current.Filename),
                        _ => throw new ParserException("expected identifier or string key in destructuring pattern", _current.Line, _current.Column, _current.Filename)
                    };
                    Advance();

                    if (!seenKeys.Add(key))
                        throw new ParserException($"duplicate key '{key}' in destructuring pattern", keyTok.Line, keyTok.Column, keyTok.Filename);

                    MatchPattern pat;
                    if (_current.Type == TokenType.Colon)
                    {
                        Eat(TokenType.Colon);
                        pat = ParseDestructurePattern();
                    }
                    else
                    {
                        if (keyTok.Type != TokenType.Ident)
                            throw new ParserException("string key in destructuring pattern requires ':'", keyTok.Line, keyTok.Column, keyTok.Filename);

                        if (key == "_")
                            pat = new WildcardMatchPattern(keyTok.Line, keyTok.Column, keyTok.Filename);
                        else
                        {
                            if (Lexer.Keywords.ContainsKey(key))
                                throw new ParserException($"invalid destructuring binding name '{key}'", keyTok.Line, keyTok.Column, keyTok.Filename);
                            pat = new BindingMatchPattern(key, keyTok.Line, keyTok.Column, keyTok.Filename);
                        }
                    }

                    entries.Add((key, pat));

                    if (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBrace)
                            break;
                        continue;
                    }
                    break;
                }
            }

            Eat(TokenType.RBrace);
            return new DictMatchPattern(entries, open.Line, open.Column, open.Filename);
        }

        /// <summary>
        /// The ValidateUniqueDestructureBindings
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        private static void ValidateUniqueDestructureBindings(MatchPattern pattern)
        {
            HashSet<string> names = new(StringComparer.Ordinal);

            static void Walk(MatchPattern p, HashSet<string> seen)
            {
                switch (p)
                {
                    case WildcardMatchPattern:
                        return;

                    case BindingMatchPattern b:
                        if (!seen.Add(b.Name))
                            throw new ParserException($"duplicate destructuring binding '{b.Name}'", b.Line, b.Col, b.OriginFile);
                        return;

                    case ArrayMatchPattern a:
                        foreach (MatchPattern elem in a.Elements)
                            Walk(elem, seen);
                        return;

                    case DictMatchPattern d:
                        foreach ((string _, MatchPattern sub) in d.Entries)
                            Walk(sub, seen);
                        return;

                    default:
                        throw new ParserException($"unsupported destructuring pattern node '{p.GetType().Name}'", p.Line, p.Col, p.OriginFile);
                }
            }

            Walk(pattern, names);
        }

        /// <summary>
        /// The CollectDestructureBindingNames
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        /// <returns>The <see cref="List{string}"/></returns>
        private static List<string> CollectDestructureBindingNames(MatchPattern pattern)
        {
            List<string> names = new();

            static void Walk(MatchPattern p, List<string> acc)
            {
                switch (p)
                {
                    case WildcardMatchPattern:
                        return;

                    case BindingMatchPattern b:
                        acc.Add(b.Name);
                        return;

                    case ArrayMatchPattern a:
                        foreach (MatchPattern elem in a.Elements)
                            Walk(elem, acc);
                        return;

                    case DictMatchPattern d:
                        foreach ((string _, MatchPattern sub) in d.Entries)
                            Walk(sub, acc);
                        return;

                    default:
                        throw new ParserException($"unsupported destructuring pattern node '{p.GetType().Name}'", p.Line, p.Col, p.OriginFile);
                }
            }

            Walk(pattern, names);
            return names;
        }

        /// <summary>
        /// Gets the ParseBreak
        /// </summary>
        private Stmt ParseBreak
        {
            get
            {
                if (!IsInLoop)
                    throw new ParserException("break can only be used in loops", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Break);
                Eat(TokenType.Semi);
                return new BreakStmt(_current.Line, _current.Column, _current.Filename);
            }
        }

        /// <summary>
        /// Gets the ParseContinue
        /// </summary>
        private Stmt ParseContinue
        {
            get
            {
                if (!IsInLoop)
                    throw new ParserException("continue can only be used in loops", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Continue);
                Eat(TokenType.Semi);
                return new ContinueStmt(_current.Line, _current.Column, _current.Filename);
            }
        }

        /// <summary>
        /// Gets the ParseVarDecl
        /// </summary>
        private Stmt ParseVarDecl
        {
            get
            {
                if (!multipleVarDecl) Eat(TokenType.Var);

                if (_current.Type == TokenType.LBracket || _current.Type == TokenType.LBrace)
                {
                    if (multipleVarDecl)
                        throw new ParserException("destructuring declaration cannot follow ',' in var declaration", _current.Line, _current.Column, _current.Filename);

                    MatchPattern pattern = ParseDestructurePattern();
                    ValidateUniqueDestructureBindings(pattern);

                    if (_current.Type != TokenType.Assign)
                        throw new ParserException("destructuring declarations require an initializer", _current.Line, _current.Column, _current.Filename);

                    Eat(TokenType.Assign);
                    Expr value = Expr();
                    Eat(TokenType.Semi);

                    return new DestructureDeclStmt(pattern, value, isConst: false, _current.Line, _current.Column, _current.Filename);
                }

                string name = _current.Value.ToString() ?? "";
                if (Lexer.Keywords.ContainsKey(name))
                    throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Ident);
                Expr? v = null;
                if (_current.Type == TokenType.Assign)
                {
                    Eat(TokenType.Assign);
                    v = Expr();
                }
                if (_current.Type == TokenType.Comma)
                {
                    multipleVarDecl = true;
                    Eat(TokenType.Comma);
                }
                else
                {
                    multipleVarDecl = false;
                    Eat(TokenType.Semi);
                }

                return new VarDecl(name, v, _current.Line, _current.Column, _current.Filename);
            }
        }

        /// <summary>
        /// Gets the ParseConstDecl
        /// </summary>
        private Stmt ParseConstDecl
        {
            get
            {
                Eat(TokenType.Const);

                if (_current.Type == TokenType.LBracket || _current.Type == TokenType.LBrace)
                {
                    MatchPattern pattern = ParseDestructurePattern();
                    ValidateUniqueDestructureBindings(pattern);

                    if (_current.Type != TokenType.Assign)
                        throw new ParserException("const destructuring declarations require an initializer", _current.Line, _current.Column, _current.Filename);

                    Eat(TokenType.Assign);
                    Expr destructValue = Expr();
                    Eat(TokenType.Semi);

                    return new DestructureDeclStmt(pattern, destructValue, isConst: true, _current.Line, _current.Column, _current.Filename);
                }

                string name = _current.Value?.ToString() ?? "";
                if (Lexer.Keywords.ContainsKey(name))
                    throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Ident);

                if (_current.Type != TokenType.Assign)
                    throw new ParserException("const declarations require an initializer", _current.Line, _current.Column, _current.Filename);

                Eat(TokenType.Assign);
                Expr value = Expr();
                Eat(TokenType.Semi);

                return new ConstDecl(name, value, _current.Line, _current.Column, _current.Filename);
            }
        }

        /// <summary>
        /// The ParseExportDecl
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseExportDecl()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Export);

            Stmt inner = _current.Type switch
            {
                TokenType.Func => ParseFuncDecl(),
                TokenType.Async => ParseFuncDecl(),
                TokenType.Class => ParseClassDecl(),
                TokenType.Enum => ParseEnumDecl(),
                TokenType.Var => ParseSingleVarDecl(),
                TokenType.Const => ParseConstDecl,
                _ => throw new ParserException("export supports only var/const/func(async func)/class/enum declarations", _current.Line, _current.Column, _current.Filename),
            };

            if (!TryGetNamedTopLevel(inner, out string? name) || string.IsNullOrWhiteSpace(name))
                throw new ParserException("exported declaration must have a symbol name", line, col, file);

            return new ExportStmt(name, inner, line, col, file);
        }

        /// <summary>
        /// The ParseSingleVarDecl
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseSingleVarDecl()
        {
            Eat(TokenType.Var);

            if (_current.Type == TokenType.LBracket || _current.Type == TokenType.LBrace)
                throw new ParserException("export var destructuring is not supported", _current.Line, _current.Column, _current.Filename);

            string name = _current.Value?.ToString() ?? "";
            if (Lexer.Keywords.ContainsKey(name))
                throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);
            Eat(TokenType.Ident);

            Expr? value = null;
            if (_current.Type == TokenType.Assign)
            {
                Eat(TokenType.Assign);
                value = Expr();
            }

            if (_current.Type == TokenType.Comma)
                throw new ParserException("export var only supports a single declaration", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.Semi);
            return new VarDecl(name, value, _current.Line, _current.Column, _current.Filename);
        }

        /// <summary>
        /// The ParseDestructureAssignStmt
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseDestructureAssignStmt()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            MatchPattern pattern = ParseDestructurePattern();
            ValidateUniqueDestructureBindings(pattern);

            if (_current.Type != TokenType.Assign)
                throw new ParserException("destructuring assignment requires '='", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.Assign);
            Expr value = Expr();
            Eat(TokenType.Semi);
            return new DestructureAssignStmt(pattern, value, line, col, file);
        }

        /// <summary>
        /// The ParseParenDestructureAssignStmt
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseParenDestructureAssignStmt()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.LParen);
            MatchPattern pattern = ParseDestructurePattern();
            ValidateUniqueDestructureBindings(pattern);

            if (_current.Type != TokenType.Assign)
                throw new ParserException("destructuring assignment requires '='", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.Assign);
            Expr value = Expr();
            Eat(TokenType.RParen);
            Eat(TokenType.Semi);
            return new DestructureAssignStmt(pattern, value, line, col, file);
        }

        /// <summary>
        /// The ParseAssignOrIndexAssignOrPushOrExpr
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseAssignOrIndexAssignOrPushOrExpr()
        {
            int line = _current.Line;
            int col = _current.Column;
            string fsname = _current.Filename;

            string name = _current.Value.ToString() ?? "";
            Eat(TokenType.Ident);

            Expr target = new VarExpr(name, line, col, fsname);

            while (true)
            {
                if (_current.Type == TokenType.Dot)
                {
                    Eat(TokenType.Dot);

                    if (_current.Type != TokenType.Ident)
                        throw new ParserException("expected identifier after '.'", line, col, _current.Filename);

                    string fieldName = _current.Value.ToString() ?? "";
                    Eat(TokenType.Ident);

                    target = new IndexExpr(target, new StringExpr(fieldName, line, col, fsname), line, col, fsname);
                }

                else if (_current.Type == TokenType.LBracket)
                {
                    Eat(TokenType.LBracket);

                    if (_current.Type == TokenType.RBracket)
                    {
                        Eat(TokenType.RBracket);
                        Eat(TokenType.Assign);
                        Expr val = Expr();
                        Eat(TokenType.Semi);
                        return new PushStmt(target, val, line, col, fsname);
                    }

                    Expr? idxStart = Expr();
                    Expr? idxEnd = null;

                    if (_current.Type == TokenType.Range)
                    {
                        Eat(TokenType.Range);

                        if (_current.Type != TokenType.RBracket)
                            idxEnd = Expr();

                        target = new SliceExpr(target, idxStart, idxEnd, line, col, fsname);
                    }
                    else
                    {
                        target = new IndexExpr(target, idxStart, line, col, fsname);
                    }

                    Eat(TokenType.RBracket);

                    if (_current.Type == TokenType.Assign)
                    {
                        Eat(TokenType.Assign);
                        Expr val = Expr();
                        Eat(TokenType.Semi);

                        if (target is SliceExpr slice)
                            return new SliceSetStmt(slice, val, line, col, fsname);
                        else
                            return new AssignExprStmt(target, val, line, col, fsname);
                    }
                }

                else if (_current.Type == TokenType.LParen)
                {
                    Eat(TokenType.LParen);
                    List<Expr> args = new();
                    if (_current.Type != TokenType.RParen)
                        args.AddRange(ParseExprList(allowNamedArgs: true));
                    Eat(TokenType.RParen);

                    target = new CallExpr(target, args, line, col, fsname);
                }
                else
                {
                    break;
                }
            }

            if (_current.Type == TokenType.Assign ||
                _current.Type == TokenType.PlusAssign ||
                _current.Type == TokenType.MinusAssign ||
                _current.Type == TokenType.StarAssign ||
                _current.Type == TokenType.SlashAssign ||
                _current.Type == TokenType.ModAssign)
            {
                TokenType op = _current.Type;
                Eat(op);
                Expr val = Expr();
                Eat(TokenType.Semi);

                if (op == TokenType.Assign)
                {
                    if (target is SliceExpr slice)
                        return new SliceSetStmt(slice, val, line, col, fsname);
                    else
                        return new AssignExprStmt(target, val, line, col, fsname);
                }
                else
                {
                    return new CompoundAssignStmt(target, op, val, line, col, fsname);
                }
            }

            if (_current.Type == TokenType.PlusPlus || _current.Type == TokenType.MinusMinus)
            {
                TokenType op = _current.Type;
                Eat(op);
                Eat(TokenType.Semi);
                return new ExprStmt(new PostfixExpr(target, op, line, col, fsname), line, col, fsname);
            }

            Eat(TokenType.Semi);
            return new ExprStmt(target, line, col, fsname);
        }

        /// <summary>
        /// Gets the ParseDelete
        /// </summary>
        private Stmt ParseDelete
        {
            get
            {
                int line = _current.Line;
                int col = _current.Column;
                string fsname = _current.Filename;

                Eat(TokenType.Delete);

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected identifier after delete", line, col, _current.Filename);

                Expr target = new VarExpr(_current.Value?.ToString() ?? "", line, col, fsname);
                Eat(TokenType.Ident);

                while (true)
                {
                    if (_current.Type == TokenType.Dot)
                    {
                        Eat(TokenType.Dot);
                        if (_current.Type != TokenType.Ident)
                            throw new ParserException("expected identifier after '.'", line, col, _current.Filename);

                        string fieldName = _current.Value?.ToString() ?? "";
                        Eat(TokenType.Ident);

                        target = new IndexExpr(
                            target,
                            new StringExpr(fieldName, line, col, fsname),
                            line, col, fsname
                        );
                        continue;
                    }

                    if (_current.Type == TokenType.LBracket)
                    {
                        Eat(TokenType.LBracket);

                        if (_current.Type == TokenType.RBracket)
                        {
                            Eat(TokenType.RBracket);
                            Eat(TokenType.Semi);
                            return new DeleteExprStmt(target, true, line, col, fsname);
                        }

                        Expr? start = null;
                        Expr? end = null;

                        if (_current.Type != TokenType.Range)
                            start = Expr();

                        if (_current.Type == TokenType.Range)
                        {
                            Eat(TokenType.Range);
                            if (_current.Type != TokenType.RBracket)
                                end = Expr();

                            Eat(TokenType.RBracket);
                            target = new SliceExpr(target, start, end, line, col, fsname);
                        }
                        else
                        {
                            if (start is null)
                                throw new ParserException("expected index expression before ']'", line, col, fsname);

                            Eat(TokenType.RBracket);
                            target = new IndexExpr(target, start, line, col, fsname);
                        }

                        continue;
                    }

                    break;
                }

                Eat(TokenType.Semi);

                bool deleteAll = target is VarExpr;

                return new DeleteExprStmt(target, deleteAll, line, col, fsname);
            }
        }

        /// <summary>
        /// The ParseEmbeddedBlockOrSingleStatement
        /// </summary>
        /// <returns>The <see cref="BlockStmt"/></returns>
        private BlockStmt ParseEmbeddedBlockOrSingleStatement()
        {
            if (_current.Type == TokenType.LBrace)
            {
                return ParseBlock();
            }

            Stmt one = Statement();
            return new BlockStmt(new List<Stmt> { one }, one.Line, one.Col, one.OriginFile);
        }

        /// <summary>
        /// The ParseBlock
        /// </summary>
        /// <returns>The <see cref="BlockStmt"/></returns>
        private BlockStmt ParseBlock()
        {
            List<Stmt> stmts = new();
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.LBrace);
            while (_current.Type != TokenType.RBrace)
            {
                if (_current.Type == TokenType.EOF)
                    throw new ParserException("expected '}' before end of file", _current.Line, _current.Column, _current.Filename);
                stmts.Add(Statement());
            }

            Eat(TokenType.RBrace);

            return new BlockStmt(stmts, line, col, file);
        }

        /// <summary>
        /// The ParseIf
        /// </summary>
        /// <returns>The <see cref="IfStmt"/></returns>
        private IfStmt ParseIf()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.If);
            Eat(TokenType.LParen);
            Expr cond = Expr();
            Eat(TokenType.RParen);
            BlockStmt thenBlk = ParseEmbeddedBlockOrSingleStatement();

            Stmt? elseBlk = null;
            if (_current.Type == TokenType.Else)
            {
                Eat(TokenType.Else);

                if (_current.Type == TokenType.If)
                {
                    elseBlk = ParseIf();
                }
                else
                {
                    elseBlk = ParseEmbeddedBlockOrSingleStatement();
                }
            }

            return new IfStmt(cond, thenBlk, elseBlk, line, col, file);
        }

        /// <summary>
        /// The ParseWhile
        /// </summary>
        /// <returns>The <see cref="WhileStmt"/></returns>
        private WhileStmt ParseWhile()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.While);
            Eat(TokenType.LParen);
            Expr cond = Expr();
            Eat(TokenType.RParen);
            _loopDepth++;
            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
            _loopDepth--;
            return new WhileStmt(cond, body, line, col, file);
        }

        /// <summary>
        /// The ParseDoWhile
        /// </summary>
        /// <returns>The <see cref="DoWhileStmt"/></returns>
        private DoWhileStmt ParseDoWhile()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.Do);
            _loopDepth++;
            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
            _loopDepth--;
            Eat(TokenType.While);
            Eat(TokenType.LParen);
            Expr cond = Expr();
            Eat(TokenType.RParen);
            if (_current.Type == TokenType.Semi)
                Eat(TokenType.Semi);
            return new DoWhileStmt(body, cond, line, col, file);
        }

        /// <summary>
        /// The ParseForeach
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseForeach()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.ForEach);
            Eat(TokenType.LParen);

            bool declare = false;
            string name;
            MatchPattern? targetPattern = null;
            bool useIndexValuePair = false;

            if (_current.Type == TokenType.Var)
            {
                declare = true;
                Eat(TokenType.Var);
            }

            if (_current.Type == TokenType.Ident && _next.Type == TokenType.Comma)
            {
                Token firstTok = _current;
                string firstName = _current.Value?.ToString() ?? "";
                Eat(TokenType.Ident);
                Eat(TokenType.Comma);

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected identifier after ',' in foreach", _current.Line, _current.Column, _current.Filename);

                Token secondTok = _current;
                string secondName = _current.Value?.ToString() ?? "";
                Eat(TokenType.Ident);

                targetPattern = new ArrayMatchPattern(
                    new List<MatchPattern>
                    {
                        new BindingMatchPattern(firstName, firstTok.Line, firstTok.Column, firstTok.Filename),
                        new BindingMatchPattern(secondName, secondTok.Line, secondTok.Column, secondTok.Filename)
                    },
                    firstTok.Line,
                    firstTok.Column,
                    firstTok.Filename);
                ValidateUniqueDestructureBindings(targetPattern);

                useIndexValuePair = true;
                name = $"__fe_pair_{_foreachDestructureCounter++}";
            }
            else if (_current.Type == TokenType.Ident)
            {
                name = _current.Value?.ToString() ?? "";
                Eat(TokenType.Ident);
            }
            else if (_current.Type == TokenType.LBracket || _current.Type == TokenType.LBrace)
            {
                targetPattern = ParseDestructurePattern();
                ValidateUniqueDestructureBindings(targetPattern);
                name = $"__fe_ds_{_foreachDestructureCounter++}";
            }
            else
            {
                throw new ParserException("expected identifier or destructuring pattern after 'foreach('", line, col, _current.Filename);
            }

            if (_current.Type != TokenType.In)
                throw new ParserException("expected 'in' in foreach", _current.Line, _current.Column, _current.Filename);
            Eat(TokenType.In);

            Expr iterable = Expr();

            Eat(TokenType.RParen);
            _loopDepth++;
            Stmt body = ParseEmbeddedBlockOrSingleStatement();
            _loopDepth--;
            return new ForeachStmt(name, targetPattern, declare, iterable, useIndexValuePair, body, line, col, file);
        }

        /// <summary>
        /// The ParseFor
        /// </summary>
        /// <returns>The <see cref="ForStmt"/></returns>
        private ForStmt ParseFor()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.For);
            Eat(TokenType.LParen);

            Stmt? init = null;
            if (_current.Type != TokenType.Semi)
            {
                if (_current.Type == TokenType.Var)
                    init = ParseVarDecl;
                else if (_current.Type == TokenType.Const)
                    init = ParseConstDecl;
                else
                    init = ParseAssignOrIndexAssignOrPushOrExpr();
            }

            else
                Eat(TokenType.Semi);

            Expr? cond = null;
            if (_current.Type != TokenType.Semi)
                cond = Expr();
            Eat(TokenType.Semi);

            Stmt? inc = null;
            if (_current.Type != TokenType.RParen)
            {
                switch (_current.Type)
                {
                    case TokenType.Var:
                    case TokenType.Const:
                    case TokenType.Delete:
                    case TokenType.Func:
                    case TokenType.Async:
                    case TokenType.Class:
                    case TokenType.Enum:
                    case TokenType.Return:
                    case TokenType.Break:
                    case TokenType.Continue:
                    case TokenType.Try:
                    case TokenType.Throw:
                    case TokenType.If:
                    case TokenType.While:
                    case TokenType.For:
                    case TokenType.ForEach:
                    case TokenType.Match:
                    case TokenType.Yield:
                        throw new ParserException("invalid expression in for statement", _current.Line, _current.Column, _current.Filename);

                }
                inc = Statement();
            }
            Eat(TokenType.RParen);
            _loopDepth++;
            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
            _loopDepth--;
            return new ForStmt(init, cond, inc, body, line, col, file);
        }

        /// <summary>
        /// The Expr
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Expr()
        {
            if (++_recursionDepth > MaxRecursionDepth)
                throw new ParserException($"Maximum nesting depth ({MaxRecursionDepth}) exceeded", _current.Line, _current.Column, _lexer.FileName);
            try { return Coalesce(); }
            finally { _recursionDepth--; }
        }

        /// <summary>
        /// The Coalesce
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Coalesce()
        {
            Expr node = Conditional();
            while (_current.Type == TokenType.QQNull)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.QQNull);
                Expr rhs = Conditional();
                node = new BinaryExpr(node, TokenType.QQNull, rhs, line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The Conditional
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Conditional()
        {
            Expr condition = Or();

            if (_current.Type == TokenType.Question)
            {
                int line = _current.Line;
                int col = _current.Column;
                string fs = _current.Filename;

                Eat(TokenType.Question);

                Expr thenExpr = Conditional();

                Eat(TokenType.Colon);

                Expr elseExpr = Conditional();

                return new ConditionalExpr(condition, thenExpr, elseExpr, line, col, fs);
            }

            return condition;
        }

        /// <summary>
        /// The Or
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Or()
        {
            Expr node = And();
            while (_current.Type == TokenType.OrOr)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.OrOr);
                node = new BinaryExpr(node, TokenType.OrOr, And(), line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The And
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr And()
        {
            Expr node = Comparison();
            while (_current.Type == TokenType.AndAnd)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.AndAnd);
                node = new BinaryExpr(node, TokenType.AndAnd, Comparison(), line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The Comparison
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Comparison()
        {
            Expr node = BitwiseOr();
            while (_current.Type == TokenType.Eq || _current.Type == TokenType.Neq ||
                   _current.Type == TokenType.Lt || _current.Type == TokenType.Gt ||
                   _current.Type == TokenType.Le || _current.Type == TokenType.Ge ||
                   _current.Type == TokenType.Is)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, BitwiseOr(), line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The BitwiseOr
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr BitwiseOr()
        {
            Expr node = BitwiseXor();
            while (_current.Type == TokenType.bOr)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, BitwiseXor(), line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The BitwiseXor
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr BitwiseXor()
        {
            Expr node = BitwiseAnd();
            while (_current.Type == TokenType.bXor)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, BitwiseAnd(), line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The BitwiseAnd
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr BitwiseAnd()
        {
            Expr node = Shift();
            while (_current.Type == TokenType.bAnd)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Shift(), line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The Shift
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Shift()
        {
            Expr node = Additive();
            while (_current.Type == TokenType.bShiftL || _current.Type == TokenType.bShiftR)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Additive(), line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The Additive
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Additive()
        {
            Expr node = Term();
            while (_current.Type == TokenType.Plus || _current.Type == TokenType.Minus)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Term(), line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The Power
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Power()
        {
            Expr? node = Unary();

            if (_current.Type == TokenType.Expo)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Power(), line, col, file);
            }

            return node;
        }

        /// <summary>
        /// The Term
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Term()
        {
            Expr node = Power();
            while (_current.Type == TokenType.Star || _current.Type == TokenType.Slash || _current.Type == TokenType.Modulo)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Power(), line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The Factor
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Factor()
        {
            Expr? node;

            if (_current.Type == TokenType.Number)
            {
                object v = _current.Value;
                node = new NumberExpr(v, _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Number);
            }
            else if (_current.Type == TokenType.Null)
            {
                node = new NullExpr(_current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Null);
            }
            else if (_current.Type == TokenType.True)
            {
                node = new BoolExpr(true, _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.True);
            }
            else if (_current.Type == TokenType.False)
            {
                node = new BoolExpr(false, _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.False);
            }
            else if (_current.Type == TokenType.New)
            {
                node = ParseNew;
            }
            else if (_current.Type == TokenType.String)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                string s = _current.Value.ToString() ?? "";
                Eat(TokenType.String);
                node = new StringExpr(s, line, col, file);
            }
            else if (_current.Type == TokenType.Char)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                if (!char.TryParse(_current.Value.ToString(), out char vlc))
                    throw new ParserException($"invalid char value '{_current.Value.ToString()}'", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Char);
                node = new CharExpr(vlc, line, col, file);
            }
            else if (_current.Type == TokenType.Ident)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                string name = _current.Value.ToString() ?? "";
                Eat(TokenType.Ident);
                node = new VarExpr(name, line, col, file);
            }
            else if (_current.Type == TokenType.LParen)
            {
                Eat(TokenType.LParen);
                node = Expr();
                Eat(TokenType.RParen);
            }
            else if (_current.Type == TokenType.LBracket)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.LBracket);
                List<Expr> elems = new();
                if (_current.Type != TokenType.RBracket)
                    elems.AddRange(ParseExprList());

                Eat(TokenType.RBracket);
                node = new ArrayExpr(elems, line, col, file);
            }
            else if (_current.Type == TokenType.LBrace)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.LBrace);
                List<(Expr, Expr)> pairs = new();

                if (_current.Type != TokenType.RBrace)
                {
                    do
                    {
                        Expr key = Expr();
                        Eat(TokenType.Colon);
                        Expr value = Expr();
                        pairs.Add((key, value));

                        if (_current.Type == TokenType.Comma)
                            Eat(TokenType.Comma);
                        else
                            break;
                    } while (true);
                }

                Eat(TokenType.RBrace);
                node = new DictExpr(pairs, line, col, file);
            }
            else if (_current.Type == TokenType.Func || _current.Type == TokenType.Async)
            {
                bool isAsync = false;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                if (_current.Type == TokenType.Async)
                {
                    int asyncLine = _current.Line;
                    int asyncCol = _current.Column;
                    string asyncFile = _current.Filename;
                    Eat(TokenType.Async);
                    if (_current.Type != TokenType.Func)
                        throw new ParserException("expected 'func' after 'async'", asyncLine, asyncCol, asyncFile);
                    isAsync = true;
                }

                Eat(TokenType.Func);
                (List<string> parameters, int minArgs, string? restParameter, List<Stmt> defaultInitializers) = ParseFunctionParamsWithDefaults();

                _funcOrClassDepth++;
                _funcDepth++;
                if (isAsync) _asyncFuncDepth++;
                BlockStmt body = ParseBlock();
                if (isAsync) _asyncFuncDepth--;
                _funcOrClassDepth--;
                _funcDepth--;

                if (defaultInitializers.Count > 0)
                    body.Statements.InsertRange(0, defaultInitializers);

                node = new FuncExpr(parameters, body, minArgs, restParameter, line, col, file, isAsync);
            }
            else if (_current.Type == TokenType.Out)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.Out);
                _outBlock++;
                BlockStmt body = ParseBlock();
                _outBlock--;
                node = new OutExpr(body, line, col, file);
            }
            else
            {
                throw new ParserException($"invalid factor, token {_current.Type}", _current.Line, _current.Column, _current.Filename);
            }

            while (true)
            {
                if (_current.Type == TokenType.LParen)
                {
                    if (node is NullExpr)
                        throw new ParserException("invalid access on null reference", _current.Line, _current.Column, _current.Filename);

                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;
                    Eat(TokenType.LParen);
                    List<Expr> args = new();
                    if (_current.Type != TokenType.RParen)
                        args.AddRange(ParseExprList(allowNamedArgs: true));
                    Eat(TokenType.RParen);

                    node = new CallExpr(node, args, line, col, file);
                    continue;
                }
                else if (_current.Type == TokenType.LBracket)
                {
                    if (node is NullExpr)
                        throw new ParserException("invalid access on null reference", _current.Line, _current.Column, _current.Filename);
                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;
                    Eat(TokenType.LBracket);

                    Expr? start = null;
                    Expr? end = null;

                    if (_current.Type != TokenType.RBracket)
                    {
                        if (_current.Type != TokenType.Range) start = Expr();

                        if (_current.Type == TokenType.Range)
                        {
                            Eat(TokenType.Range);

                            if (_current.Type != TokenType.RBracket)
                                end = Expr();

                            Eat(TokenType.RBracket);
                            node = new SliceExpr(node, start, end, line, col, file);
                            continue;
                        }
                        else
                        {
                            Eat(TokenType.RBracket);
                            node = new IndexExpr(node, start, line, col, file);
                            continue;
                        }
                    }

                    Eat(TokenType.RBracket);
                    node = new SliceExpr(node, null, null, line, col, file);
                    continue;
                }
                else if (_current.Type == TokenType.Dot)
                {
                    if (node is NullExpr)
                        throw new ParserException("invalid access on null reference", _current.Line, _current.Column, _current.Filename);
                    Eat(TokenType.Dot);
                    if (_current.Type != TokenType.Ident)
                        throw new ParserException("expected identifier after '.'", _current.Line, _current.Column, _current.Filename);
                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;
                    string member = _current.Value?.ToString() ?? "";
                    node = new IndexExpr(
                        node,
                        new StringExpr(member, line, col, file),
                        line, col, file
                    );
                    Eat(TokenType.Ident);
                    continue;
                }
                else if (_current.Type == TokenType.Match)
                {
                    int line = _current.Line, col = _current.Column;
                    string fs = _current.Filename;

                    Eat(TokenType.Match);
                    Eat(TokenType.LBrace);

                    List<CaseExprArm> arms = new();
                    Expr? defaultArm = null;

                    if (_current.Type != TokenType.RBrace)
                    {
                        while (true)
                        {
                            MatchPattern pat = ParseMatchPattern();
                            ValidateUniquePatternBindings(pat);
                            Expr? guard = ParseOptionalMatchGuard();
                            Eat(TokenType.Colon);
                            Expr body = Expr();

                            if (pat is WildcardMatchPattern && guard is null)
                            {
                                if (defaultArm != null)
                                    throw new ParserException("duplicate '_' default arm in match expression", _current.Line, _current.Column, _current.Filename);
                                defaultArm = body;
                            }
                            else
                            {
                                arms.Add(new CaseExprArm(pat, guard, body, pat.Line, pat.Col, pat.OriginFile));
                            }

                            if (_current.Type == TokenType.Comma)
                            {
                                Eat(TokenType.Comma);
                                if (_current.Type == TokenType.RBrace) break;
                                continue;
                            }
                            else break;
                        }
                    }

                    Eat(TokenType.RBrace);

                    node = new MatchExpr(node!, arms, defaultArm, line, col, fs);
                    continue;
                }
                else if (_current.Type == TokenType.PlusPlus || _current.Type == TokenType.MinusMinus)
                {
                    if (node is NullExpr)
                        throw new ParserException("invalid access on null reference", _current.Line, _current.Column, _current.Filename);
                    TokenType op = _current.Type;
                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;
                    Eat(op);
                    node = new PostfixExpr(node, op, line, col, file);
                    continue;
                }
                else
                {
                    break;
                }
            }

            node = MaybeParseObjectInitializer(node);

            return node ?? new NullExpr(_current.Line, _current.Column, _current.Filename);
        }

        /// <summary>
        /// The Unary
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Unary()
        {
            if (_current.Type == TokenType.Await)
            {
                int awaitLine = _current.Line;
                int awaitCol = _current.Column;
                string awaitFile = _current.Filename;

                if (IsInFunction && !IsInAsyncFunction)
                    throw new ParserException("await can only be used in async function statements", awaitLine, awaitCol, awaitFile);

                Eat(TokenType.Await);
                Expr awaited = Unary();
                return new AwaitExpr(awaited, awaitLine, awaitCol, awaitFile);
            }

            if (_current.Type == TokenType.Minus)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.Minus);
                return new UnaryExpr(TokenType.Minus, Power(), line, col, file);
            }
            if (_current.Type == TokenType.Plus)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.Plus);
                return new UnaryExpr(TokenType.Plus, Unary(), line, col, file);
            }
            if (_current.Type == TokenType.Not)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.Not);
                return new UnaryExpr(TokenType.Not, Unary(), line, col, file);
            }

            if (_current.Type == TokenType.PlusPlus || _current.Type == TokenType.MinusMinus)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);

                Expr? target = Unary();
                return new PrefixExpr(target, op, line, col, file);
            }

            return Factor();
        }

        /// <summary>
        /// The ParseFuncDecl
        /// </summary>
        /// <returns>The <see cref="FuncDeclStmt"/></returns>
        private FuncDeclStmt ParseFuncDecl()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            bool isAsync = false;

            if (_current.Type == TokenType.Async)
            {
                Eat(TokenType.Async);
                if (_current.Type != TokenType.Func)
                    throw new ParserException("expected 'func' after 'async'", _current.Line, _current.Column, _current.Filename);
                isAsync = true;
            }

            Eat(TokenType.Func);

            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected function name", line, col, _current.Filename);

            string name = _current.Value.ToString() ?? "";
            Eat(TokenType.Ident);

            (List<string> parameters, int minArgs, string? restParameter, List<Stmt> defaultInitializers) = ParseFunctionParamsWithDefaults();

            _funcOrClassDepth++;
            _funcDepth++;
            if (isAsync) _asyncFuncDepth++;
            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
            if (isAsync) _asyncFuncDepth--;
            _funcDepth--;
            _funcOrClassDepth--;

            if (defaultInitializers.Count > 0)
                body.Statements.InsertRange(0, defaultInitializers);

            return new FuncDeclStmt(name, parameters, body, minArgs, restParameter, line, col, file, isAsync);
        }

        /// <summary>
        /// The ParseReturnStmt
        /// </summary>
        /// <returns>The <see cref="ReturnStmt"/></returns>
        private ReturnStmt ParseReturnStmt()
        {
            if (!IsInFunction)
                throw new ParserException("return can only be used in function statements", _current.Line, _current.Column, _current.Filename);

            if (IsInOutBlock)
                throw new ParserException("return can not be used in out-Block", _current.Line, _current.Column, _current.Filename);

            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.Return);
            Expr? value = null;
            if (_current.Type != TokenType.Semi)
                value = Expr();

            Eat(TokenType.Semi);
            return new ReturnStmt(value, line, col, file);
        }

        /// <summary>
        /// The ParseYieldStmt
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseYieldStmt()
        {
            if (!IsInFunction)
                throw new ParserException("yield can only be used in function statements", _current.Line, _current.Column, _current.Filename);

            if (!IsInAsyncFunction)
                throw new ParserException("yield can only be used in async function statements", _current.Line, _current.Column, _current.Filename);

            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Yield);
            if (_current.Type != TokenType.Semi)
                throw new ParserException("yield does not accept a value", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.Semi);
            return new YieldStmt(line, col, file);
        }

        /// <summary>
        /// The ParseExprList
        /// </summary>
        /// <param name="allowNamedArgs">The allowNamedArgs<see cref="bool"/></param>
        /// <returns>The <see cref="List{Expr}"/></returns>
        private List<Expr> ParseExprList(bool allowNamedArgs = false)
        {
            List<Expr> list = new();
            list.Add(ParseCallArgument(allowNamedArgs));
            while (_current.Type == TokenType.Comma)
            {
                Eat(TokenType.Comma);
                list.Add(ParseCallArgument(allowNamedArgs));
            }
            return list;
        }

        /// <summary>
        /// The ParseCallArgument
        /// </summary>
        /// <param name="allowNamedArgs">The allowNamedArgs<see cref="bool"/></param>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr ParseCallArgument(bool allowNamedArgs)
        {
            if (allowNamedArgs && _current.Type == TokenType.Star)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.Star);
                Expr value = Expr();
                return new SpreadArgExpr(value, line, col, file);
            }

            if (allowNamedArgs && _current.Type == TokenType.Ident && _next.Type == TokenType.Colon)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                string name = _current.Value?.ToString() ?? "";
                Eat(TokenType.Ident);
                Eat(TokenType.Colon);
                Expr value = Expr();
                return new NamedArgExpr(name, value, line, col, file);
            }

            return Expr();
        }

        private sealed record ParsedFunctionParam(
            string Name,
            MatchPattern? DestructurePattern,
            List<string> DestructureBindingNames,
            Expr? DefaultValue,
            bool IsRest,
            int Line,
            int Col,
            string File);

        /// <summary>
        /// The ParseFunctionParamsWithDefaults
        /// </summary>
        /// <param name="requireParens">The requireParens<see cref="bool"/></param>
        /// <param name="allowTrailingComma">The allowTrailingComma<see cref="bool"/></param>
        /// <returns>The <see cref="(List{string}, int, string?, List{Stmt})"/></returns>
        private (List<string> Parameters, int MinArgs, string? RestParameter, List<Stmt> DefaultInitializers) ParseFunctionParamsWithDefaults(
            bool requireParens = true,
            bool allowTrailingComma = false)
        {
            if (_current.Type != TokenType.LParen)
                return (new List<string>(), 0, null, new List<Stmt>());

            if (requireParens || _current.Type == TokenType.LParen)
                Eat(TokenType.LParen);
            else
                return (new List<string>(), 0, null, new List<Stmt>());

            List<ParsedFunctionParam> parsed = new();
            HashSet<string> seen = new(StringComparer.Ordinal);
            HashSet<string> seenBindings = new(StringComparer.Ordinal);

            if (_current.Type == TokenType.RParen)
            {
                Eat(TokenType.RParen);
                return (new List<string>(), 0, null, new List<Stmt>());
            }

            bool sawDefault = false;
            bool sawRest = false;
            string? restParameter = null;

            while (true)
            {
                if (_current.Type == TokenType.Star)
                {
                    if (sawRest)
                        throw new ParserException("duplicate rest parameter", _current.Line, _current.Column, _current.Filename);

                    int restLine = _current.Line;
                    int restCol = _current.Column;
                    string restFile = _current.Filename;
                    Eat(TokenType.Star);

                    if (_current.Type != TokenType.Ident)
                        throw new ParserException("expected identifier after '*' in rest parameter", _current.Line, _current.Column, _current.Filename);

                    string restName = _current.Value?.ToString()
                        ?? throw new ParserException("invalid rest parameter name", _current.Line, _current.Column, _current.Filename);

                    ThrowIfInvalidParameterName(restName, _current.Line, _current.Column, _current.Filename);

                    if (!seen.Add(restName))
                        throw new ParserException($"duplicate parameter name '{restName}'", _current.Line, _current.Column, _current.Filename);
                    if (!seenBindings.Add(restName))
                        throw new ParserException($"duplicate parameter name '{restName}'", _current.Line, _current.Column, _current.Filename);

                    Eat(TokenType.Ident);

                    if (_current.Type == TokenType.Assign)
                        throw new ParserException("rest parameter cannot have a default value", _current.Line, _current.Column, _current.Filename);

                    parsed.Add(new ParsedFunctionParam(restName, null, new List<string> { restName }, null, true, restLine, restCol, restFile));
                    restParameter = restName;
                    sawRest = true;

                    if (_current.Type == TokenType.Comma)
                        throw new ParserException("rest parameter must be the last parameter", _current.Line, _current.Column, _current.Filename);

                    if (_current.Type != TokenType.RParen)
                        throw new ParserException($"invalid token {_current.Type} in parameters", _current.Line, _current.Column, _current.Filename);

                    break;
                }

                if (_current.Type != TokenType.Ident && _current.Type != TokenType.LBracket && _current.Type != TokenType.LBrace)
                    throw new ParserException($"invalid token {_current.Type} in parameters", _current.Line, _current.Column, _current.Filename);

                int pLine = _current.Line;
                int pCol = _current.Column;
                string pFile = _current.Filename;

                string paramName;
                MatchPattern? destructPattern = null;
                List<string> destructBindings = new();

                if (_current.Type == TokenType.Ident)
                {
                    string name = _current.Value?.ToString()
                                  ?? throw new ParserException("invalid parameter name", _current.Line, _current.Column, _current.Filename);

                    ThrowIfInvalidParameterName(name, _current.Line, _current.Column, _current.Filename);

                    if (!seen.Add(name))
                        throw new ParserException($"duplicate parameter name '{name}'", _current.Line, _current.Column, _current.Filename);
                    if (!seenBindings.Add(name))
                        throw new ParserException($"duplicate parameter name '{name}'", _current.Line, _current.Column, _current.Filename);

                    Eat(TokenType.Ident);
                    paramName = name;
                    destructBindings.Add(name);
                }
                else
                {
                    destructPattern = ParseDestructurePattern();
                    ValidateUniqueDestructureBindings(destructPattern);
                    destructBindings = CollectDestructureBindingNames(destructPattern);
                    foreach (string b in destructBindings)
                    {
                        ThrowIfInvalidParameterName(b, pLine, pCol, pFile);
                        if (!seenBindings.Add(b))
                            throw new ParserException($"duplicate parameter name '{b}'", pLine, pCol, pFile);
                    }

                    paramName = $"__arg_ds_{_destructureParamCounter++}";
                    while (!seen.Add(paramName))
                        paramName = $"__arg_ds_{_destructureParamCounter++}";
                }

                Expr? defaultValue = null;
                if (_current.Type == TokenType.Assign)
                {
                    Eat(TokenType.Assign);
                    defaultValue = Expr();
                    sawDefault = true;
                }
                else if (sawDefault)
                {
                    throw new ParserException("non-default parameter cannot follow a default parameter", _current.Line, _current.Column, _current.Filename);
                }

                parsed.Add(new ParsedFunctionParam(paramName, destructPattern, destructBindings, defaultValue, false, pLine, pCol, pFile));

                if (_current.Type == TokenType.Comma)
                {
                    Eat(TokenType.Comma);
                    if (allowTrailingComma && _current.Type == TokenType.RParen)
                        break;
                    continue;
                }

                if (_current.Type == TokenType.RParen)
                    break;

                throw new ParserException($"invalid token {_current.Type} in parameters", _current.Line, _current.Column, _current.Filename);
            }

            Eat(TokenType.RParen);

            List<string> parameterNames = parsed.Select(p => p.Name).ToList();
            int minArgs = parsed.Count(p => !p.IsRest && p.DefaultValue == null);
            List<Stmt> initializers = BuildParamInitializers(parsed);
            return (parameterNames, minArgs, restParameter, initializers);
        }

        /// <summary>
        /// The BuildParamInitializers
        /// </summary>
        /// <param name="parameters">The parameters<see cref="List{ParsedFunctionParam}"/></param>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        private static List<Stmt> BuildParamInitializers(List<ParsedFunctionParam> parameters)
        {
            List<Stmt> stmts = new();
            foreach (ParsedFunctionParam p in parameters)
            {
                if (p.IsRest)
                    continue;

                if (p.DefaultValue is not null)
                {
                    Expr cond = new BinaryExpr(
                        new VarExpr(p.Name, p.Line, p.Col, p.File),
                        TokenType.Eq,
                        new NullExpr(p.Line, p.Col, p.File),
                        p.Line, p.Col, p.File);

                    BlockStmt thenBlock = new(
                        new List<Stmt> { new AssignStmt(p.Name, p.DefaultValue, p.Line, p.Col, p.File) },
                        p.Line, p.Col, p.File);

                    stmts.Add(new IfStmt(cond, thenBlock, null, p.Line, p.Col, p.File));
                }

                if (p.DestructurePattern is not null)
                {
                    stmts.Add(new DestructureDeclStmt(
                        p.DestructurePattern,
                        new VarExpr(p.Name, p.Line, p.Col, p.File),
                        isConst: false,
                        p.Line,
                        p.Col,
                        p.File));
                }
            }

            return stmts;
        }

        /// <summary>
        /// The ParseParams
        /// </summary>
        /// <param name="requireParens">The requireParens<see cref="bool"/></param>
        /// <param name="allowTrailingComma">The allowTrailingComma<see cref="bool"/></param>
        /// <returns>The <see cref="List{string}"/></returns>
        private List<string> ParseParams(bool requireParens = true, bool allowTrailingComma = false)
        {
            List<string> parameters = new();
            if (_current.Type != TokenType.LParen) return parameters;
            HashSet<string> seen = new(StringComparer.Ordinal);

            if (requireParens || _current.Type == TokenType.LParen)
                Eat(TokenType.LParen);
            else
                return parameters;

            if (_current.Type == TokenType.RParen)
            {
                Eat(TokenType.RParen);
                return parameters;
            }

            bool expectParam = true;

            while (true)
            {
                if (_current.Type == TokenType.Ident)
                {
                    if (!expectParam)
                        throw new ParserException("Erwarte ',' oder ')'", _current.Line, _current.Column, _current.Filename);

                    string name = _current.Value?.ToString()
                                  ?? throw new ParserException("invalid parameter name", _current.Line, _current.Column, _current.Filename);

                    ThrowIfInvalidParameterName(name, _current.Line, _current.Column, _current.Filename);

                    if (!seen.Add(name))
                        throw new ParserException($"duplicate parameter name '{name}'", _current.Line, _current.Column, _current.Filename);

                    parameters.Add(name);
                    Eat(TokenType.Ident);
                    expectParam = false;
                }
                else if (_current.Type == TokenType.Comma)
                {
                    if (expectParam)
                        throw new ParserException("Expected parameter before ','", _current.Line, _current.Column, _current.Filename);

                    Eat(TokenType.Comma);

                    if (allowTrailingComma && _current.Type == TokenType.RParen)
                    {
                        expectParam = false;
                        break;
                    }

                    expectParam = true;
                }
                else if (_current.Type == TokenType.RParen)
                {
                    if (expectParam && parameters.Count > 0)
                        throw new ParserException("Expected parameter after ','", _current.Line, _current.Column, _current.Filename);

                    break;
                }
                else
                {
                    throw new ParserException($"invalid token {_current.Type} in parameters",
                                               _current.Line, _current.Column, _current.Filename);
                }
            }

            Eat(TokenType.RParen);
            return parameters;
        }
    }
}
