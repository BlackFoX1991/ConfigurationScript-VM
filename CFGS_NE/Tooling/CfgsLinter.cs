using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.Tooling;

internal sealed record CfgsLintOptions(
    int MaxLineLength = 140,
    bool WarningsAsErrors = false,
    bool Fix = false,
    bool AllowTabs = false,
    int IndentSize = 4);

internal sealed record CfgsLintResult(IReadOnlyList<CfgsToolDiagnostic> Diagnostics, string? FixedText = null, bool Fixed = false)
{
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == CfgsToolDiagnosticSeverity.Error);
    public bool HasWarnings => Diagnostics.Any(static d => d.Severity == CfgsToolDiagnosticSeverity.Warning);
}

internal static class CfgsLinter
{
    public static CfgsLintResult LintFile(string path, CfgsLintOptions options, string compilerVersion)
    {
        List<CfgsToolDiagnostic> diagnostics = [];
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            diagnostics.Add(new CfgsToolDiagnostic("CFGS8000", CfgsToolDiagnosticSeverity.Error, $"file not found: {path}", path, 1, 1, 1));
            return new CfgsLintResult(diagnostics);
        }

        string sourceText = File.ReadAllText(fullPath);
        AddStyleDiagnostics(fullPath, sourceText, diagnostics, options);
        AddCompileDiagnostic(fullPath, diagnostics, compilerVersion);
        AddAstDiagnostics(fullPath, sourceText, diagnostics);
        ApplySuppressions(sourceText, diagnostics);

        string? fixedText = null;
        bool fixedChanged = false;
        if (options.Fix)
        {
            fixedText = FixStyle(sourceText, options);
            fixedChanged = !string.Equals(sourceText, fixedText, StringComparison.Ordinal);
        }

        if (options.WarningsAsErrors)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == CfgsToolDiagnosticSeverity.Warning)
                    diagnostics[i] = diagnostics[i] with { Severity = CfgsToolDiagnosticSeverity.Error };
            }
        }

        return new CfgsLintResult(diagnostics, fixedText, fixedChanged);
    }

    private static void AddCompileDiagnostic(string path, List<CfgsToolDiagnostic> diagnostics, string compilerVersion)
    {
        try
        {
            VM vm = new();
            _ = CfgsCompiler.CompileFile(
                path,
                loadPluginDll: vm.LoadPlugin,
                namespaces: vm.Namespaces,
                compilerVersion: compilerVersion);
        }
        catch (Exception ex)
        {
            ErrorDiagnostic diag = ErrorDiagnostics.FromException(ex, path);
            diagnostics.Add(new CfgsToolDiagnostic(
                diag.Code,
                CfgsToolDiagnosticSeverity.Error,
                diag.Message,
                diag.SourceFile ?? path,
                Math.Max(1, diag.Line ?? 1),
                Math.Max(1, diag.Column ?? 1),
                Math.Max(1, diag.SpanLength ?? 1)));
        }
    }

    private static void AddStyleDiagnostics(
        string path,
        string sourceText,
        List<CfgsToolDiagnostic> diagnostics,
        CfgsLintOptions options)
    {
        string normalized = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        int consecutiveBlankLines = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            bool isLastArtificialLine = i == lines.Length - 1 && lines[i].Length == 0 && normalized.EndsWith('\n');
            if (isLastArtificialLine)
                continue;

            string line = lines[i];
            int lineNumber = i + 1;

            if (line.Length > 0 && char.IsWhiteSpace(line[^1]))
            {
                diagnostics.Add(new CfgsToolDiagnostic(
                    "CFGS8001",
                    CfgsToolDiagnosticSeverity.Warning,
                    "trailing whitespace",
                    path,
                    lineNumber,
                    line.Length,
                    1));
            }

            if (!options.AllowTabs && line.StartsWith('\t'))
            {
                diagnostics.Add(new CfgsToolDiagnostic(
                    "CFGS8002",
                    CfgsToolDiagnosticSeverity.Warning,
                    "indentation uses tabs; prefer spaces or run the formatter with --tabs intentionally",
                    path,
                    lineNumber,
                    1,
                    1));
            }

            if (line.Length > options.MaxLineLength)
            {
                diagnostics.Add(new CfgsToolDiagnostic(
                    "CFGS8003",
                    CfgsToolDiagnosticSeverity.Info,
                    $"line is longer than {options.MaxLineLength} characters",
                    path,
                    lineNumber,
                    options.MaxLineLength + 1,
                    Math.Max(1, line.Length - options.MaxLineLength)));
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                consecutiveBlankLines++;
                if (consecutiveBlankLines > 2)
                {
                    diagnostics.Add(new CfgsToolDiagnostic(
                        "CFGS8004",
                        CfgsToolDiagnosticSeverity.Warning,
                        "more than two consecutive blank lines",
                        path,
                        lineNumber,
                        1,
                        1));
                }
            }
            else
            {
                consecutiveBlankLines = 0;
            }
        }

        if (sourceText.Length > 0 && !sourceText.EndsWith('\n') && !sourceText.EndsWith("\r\n", StringComparison.Ordinal))
        {
            diagnostics.Add(new CfgsToolDiagnostic(
                "CFGS8005",
                CfgsToolDiagnosticSeverity.Warning,
                "missing final newline",
                path,
                Math.Max(1, lines.Length),
                Math.Max(1, lines.LastOrDefault()?.Length ?? 1),
                1));
        }
    }

    private static string FixStyle(string sourceText, CfgsLintOptions options)
    {
        string normalized = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        List<string> fixedLines = [];
        int blankRun = 0;
        string indent = new(' ', Math.Max(1, options.IndentSize));

        for (int i = 0; i < lines.Length; i++)
        {
            bool isLastArtificialLine = i == lines.Length - 1 && lines[i].Length == 0 && normalized.EndsWith('\n');
            if (isLastArtificialLine)
                continue;

            string line = lines[i].TrimEnd();
            if (!options.AllowTabs)
            {
                int tabs = 0;
                while (tabs < line.Length && line[tabs] == '\t')
                    tabs++;
                if (tabs > 0)
                    line = string.Concat(Enumerable.Repeat(indent, tabs)) + line[tabs..];
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                blankRun++;
                if (blankRun > 2)
                    continue;
                fixedLines.Add(string.Empty);
            }
            else
            {
                blankRun = 0;
                fixedLines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, fixedLines) + Environment.NewLine;
    }

    private static void AddAstDiagnostics(string path, string sourceText, List<CfgsToolDiagnostic> diagnostics)
    {
        List<Stmt> ast;
        try
        {
            Lexer lexer = new(path, sourceText);
            Parser parser = new(lexer, Parser.TopLevelMode.Script);
            ast = parser.Parse();
        }
        catch
        {
            return;
        }

        AstLintWalker walker = new(path, diagnostics);
        walker.VisitStatements(ast, isTopLevel: true);
        walker.Finish();
    }

    private sealed class AstLintWalker
    {
        private readonly string _path;
        private readonly List<CfgsToolDiagnostic> _diagnostics;
        private readonly Stack<Scope> _scopes = new();
        private readonly Dictionary<string, Declaration> _functions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _calledFunctions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _externallyReachableFunctions = new(StringComparer.Ordinal);
        private int _namespaceDepth;

        public AstLintWalker(string path, List<CfgsToolDiagnostic> diagnostics)
        {
            _path = path;
            _diagnostics = diagnostics;
            _scopes.Push(new Scope(isTopLevel: true));
        }

        public void VisitStatements(IEnumerable<Stmt> statements, bool isTopLevel = false)
        {
            if (!isTopLevel)
                _scopes.Push(new Scope(isTopLevel: false));

            bool terminated = false;
            foreach (Stmt stmt in statements)
            {
                if (terminated)
                {
                    Add("CFGS8012", CfgsToolDiagnosticSeverity.Warning, "unreachable statement after terminating control flow", stmt);
                    terminated = false;
                }

                VisitStatement(stmt);
                if (IsTerminating(stmt))
                    terminated = true;
            }

            if (!isTopLevel)
            {
                ReportUnusedLocals(_scopes.Peek());
                _scopes.Pop();
            }
        }

        public void Finish()
        {
            ReportUnusedLocals(_scopes.Peek());
            foreach (Declaration function in _functions.Values)
            {
                if (!function.Name.StartsWith('_') &&
                    !string.Equals(function.Name, "main", StringComparison.Ordinal) &&
                    !_calledFunctions.Contains(function.Name) &&
                    !_externallyReachableFunctions.Contains(function.Name))
                    Add("CFGS8013", CfgsToolDiagnosticSeverity.Info, $"function '{function.Name}' is never called in this file", function.Line, function.Column);
            }
        }

        private void VisitStatement(Stmt stmt)
        {
            switch (stmt)
            {
                case VarDecl v:
                    Declare(v.Name, v.Line, v.Col);
                    VisitExpr(v.Value);
                    break;
                case ConstDecl c:
                    Declare(c.Name, c.Line, c.Col);
                    VisitExpr(c.Value);
                    break;
                case FuncDeclStmt f:
                    if (_scopes.Peek().IsTopLevel)
                    {
                        _functions.TryAdd(f.Name, new Declaration(f.Name, f.Line, f.Col));
                        if (_namespaceDepth > 0)
                            _externallyReachableFunctions.Add(f.Name);
                    }
                    VisitFunction(f.Name, f.IsAsync, f.Parameters, f.RestParameter, f.Body, f.Line, f.Col);
                    break;
                case ExprStmt e:
                    VisitExpr(e.Expression);
                    break;
                case ReturnStmt r:
                    VisitExpr(r.Value);
                    break;
                case ThrowStmt t:
                    VisitExpr(t.Value);
                    break;
                case AssignStmt a:
                    Use(a.Name);
                    VisitExpr(a.Value);
                    break;
                case AssignExprStmt a:
                    VisitExpr(a.Target);
                    VisitExpr(a.Value);
                    break;
                case AssignIndexExprStmt a:
                    VisitExpr(a.Target);
                    VisitExpr(a.Value);
                    break;
                case CompoundAssignStmt c:
                    VisitExpr(c.Target);
                    VisitExpr(c.Value);
                    break;
                case PushStmt p:
                    VisitExpr(p.Target);
                    VisitExpr(p.Value);
                    break;
                case DeleteVarStmt d:
                    Use(d.Name);
                    break;
                case DeleteIndexStmt d:
                    Use(d.Name);
                    VisitExpr(d.Index);
                    break;
                case DeleteAllStmt d:
                    VisitExpr(d.Target);
                    break;
                case DeleteExprStmt d:
                    VisitExpr(d.Target);
                    break;
                case BlockStmt b:
                    VisitStatements(b.Statements);
                    break;
                case IfStmt i:
                    VisitExpr(i.Condition);
                    VisitStatement(i.ThenBlock);
                    if (i.ElseBranch is not null)
                        VisitStatement(i.ElseBranch);
                    break;
                case WhileStmt w:
                    VisitExpr(w.Condition);
                    VisitStatement(w.Body);
                    break;
                case DoWhileStmt d:
                    VisitStatement(d.Body);
                    VisitExpr(d.Condition);
                    break;
                case ForStmt f:
                    if (f.Init is not null) VisitStatement(f.Init);
                    VisitExpr(f.Condition);
                    if (f.Increment is not null) VisitStatement(f.Increment);
                    VisitStatement(f.Body);
                    break;
                case ForeachStmt f:
                    VisitExpr(f.Iterable);
                    _scopes.Push(new Scope(isTopLevel: false));
                    if (f.DeclareLocal && !string.IsNullOrWhiteSpace(f.VarName))
                        Declare(f.VarName, f.Line, f.Col);
                    VisitStatement(f.Body);
                    ReportUnusedLocals(_scopes.Peek());
                    _scopes.Pop();
                    break;
                case TryStmt t:
                    VisitStatement(t.TryBlock);
                    if (t.CatchBlock is not null)
                    {
                        _scopes.Push(new Scope(isTopLevel: false));
                        if (!string.IsNullOrWhiteSpace(t.CatchIdent))
                            Declare(t.CatchIdent!, t.Line, t.Col);
                        VisitStatements(t.CatchBlock.Statements, isTopLevel: true);
                        ReportUnusedLocals(_scopes.Peek());
                        _scopes.Pop();
                    }
                    if (t.FinallyBlock is not null)
                        VisitStatement(t.FinallyBlock);
                    break;
                case UsingStmt u:
                    VisitExpr(u.Resource);
                    _scopes.Push(new Scope(isTopLevel: false));
                    if (!string.IsNullOrWhiteSpace(u.BindingName))
                        Declare(u.BindingName!, u.Line, u.Col);
                    VisitStatements(u.Body.Statements, isTopLevel: true);
                    ReportUnusedLocals(_scopes.Peek());
                    _scopes.Pop();
                    break;
                case NamespaceDeclStmt n:
                    _namespaceDepth++;
                    try
                    {
                        VisitStatements(n.BodyStatements, isTopLevel: true);
                    }
                    finally
                    {
                        _namespaceDepth--;
                    }
                    break;
                case ClassDeclStmt c:
                    foreach (FuncDeclStmt method in c.Methods)
                        VisitFunction(method.Name, method.IsAsync, method.Parameters, method.RestParameter, method.Body, method.Line, method.Col);
                    foreach (FuncDeclStmt method in c.StaticMethods)
                        VisitFunction(method.Name, method.IsAsync, method.Parameters, method.RestParameter, method.Body, method.Line, method.Col);
                    foreach (PropertyDeclStmt property in c.Properties.Concat(c.StaticProperties))
                        foreach (PropertyAccessorDecl accessor in property.Accessors)
                            if (accessor.Body is not null)
                                VisitStatement(accessor.Body);
                    foreach (Expr? value in c.Fields.Values.Concat(c.StaticFields.Values))
                        VisitExpr(value);
                    foreach (ClassDeclStmt nested in c.NestedClasses)
                        VisitStatement(nested);
                    break;
                case InterfaceDeclStmt i:
                    foreach (InterfaceMethodDecl method in i.Methods)
                    {
                        if (method.Body is not null)
                            VisitFunction(method.Name, method.IsAsync, method.Parameters, method.RestParameter, method.Body, method.Line, method.Col);
                    }
                    break;
                case ExportStmt e:
                    if (e.Inner is FuncDeclStmt exportedFunction)
                        _externallyReachableFunctions.Add(exportedFunction.Name);
                    VisitStatement(e.Inner);
                    break;
            }
        }

        private void VisitFunction(string name, bool isAsync, IReadOnlyList<string> parameters, string? restParameter, BlockStmt body, int line, int col)
        {
            _scopes.Push(new Scope(isTopLevel: false));
            foreach (string parameter in parameters)
                Declare(parameter, line, col, isParameter: true);
            if (!string.IsNullOrWhiteSpace(restParameter))
                Declare(restParameter!, line, col, isParameter: true);

            bool hasAwait = ContainsAwait(body);
            if (isAsync && !hasAwait)
                Add("CFGS8014", CfgsToolDiagnosticSeverity.Info, $"async function '{name}' does not await anything", line, col);

            VisitStatements(body.Statements, isTopLevel: true);
            ReportUnusedLocals(_scopes.Peek());
            _scopes.Pop();
        }

        private void VisitExpr(Expr? expr)
        {
            if (expr is null)
                return;

            switch (expr)
            {
                case VarExpr v:
                    Use(v.Name);
                    break;
                case CallExpr c:
                    if (c.Target is VarExpr target)
                        _calledFunctions.Add(target.Name);
                    VisitExpr(c.Target);
                    foreach (Expr arg in c.Args) VisitExpr(arg);
                    break;
                case MethodCallExpr m:
                    VisitExpr(m.Target);
                    foreach (Expr arg in m.Args) VisitExpr(arg);
                    break;
                case BinaryExpr b:
                    VisitExpr(b.Left);
                    VisitExpr(b.Right);
                    break;
                case UnaryExpr u:
                    VisitExpr(u.Right);
                    break;
                case PrefixExpr p:
                    VisitExpr(p.Target);
                    break;
                case PostfixExpr p:
                    VisitExpr(p.Target);
                    break;
                case ArrayExpr a:
                    foreach (Expr item in a.Elements) VisitExpr(item);
                    break;
                case DictExpr d:
                    foreach ((Expr key, Expr value) in d.Pairs)
                    {
                        VisitExpr(key);
                        VisitExpr(value);
                    }
                    break;
                case IndexExpr i:
                    VisitExpr(i.Target);
                    VisitExpr(i.Index);
                    break;
                case SliceExpr s:
                    VisitExpr(s.Target);
                    VisitExpr(s.Start);
                    VisitExpr(s.End);
                    break;
                case ConditionalExpr c:
                    VisitExpr(c.Condition);
                    VisitExpr(c.ThenExpr);
                    VisitExpr(c.ElseExpr);
                    break;
                case AwaitExpr a:
                    VisitExpr(a.Inner);
                    break;
                case OutExpr o:
                    VisitStatement(o.Body);
                    break;
                case FuncExpr f:
                    VisitFunction("<anonymous>", f.IsAsync, f.Parameters, f.RestParameter, f.Body, f.Line, f.Col);
                    break;
                case NamedArgExpr n:
                    VisitExpr(n.Value);
                    break;
                case SpreadArgExpr s:
                    VisitExpr(s.Value);
                    break;
                case NewExpr n:
                    foreach (Expr arg in n.Args) VisitExpr(arg);
                    break;
                case GetFieldExpr g:
                    VisitExpr(g.Target);
                    break;
                case ObjectInitExpr o:
                    VisitExpr(o.Target);
                    foreach ((_, Expr value) in o.Inits) VisitExpr(value);
                    break;
            }
        }

        private bool ContainsAwait(Node node)
        {
            if (node is AwaitExpr)
                return true;
            if (node is FuncDeclStmt or FuncExpr)
                return false;

            foreach (object child in EnumerateNodeChildren(node))
            {
                if (child is Node childNode && ContainsAwait(childNode))
                    return true;
            }

            return false;
        }

        private static IEnumerable<object> EnumerateNodeChildren(object node)
        {
            foreach (System.Reflection.PropertyInfo property in node.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length > 0 || property.PropertyType == typeof(string))
                    continue;

                object? value = property.GetValue(node);
                if (value is null)
                    continue;

                if (value is Node child)
                    yield return child;
                else if (value is System.Collections.IEnumerable enumerable)
                    foreach (object? item in enumerable)
                        if (item is Node itemNode)
                            yield return itemNode;
            }

            foreach (System.Reflection.FieldInfo field in node.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                if (field.FieldType == typeof(string))
                    continue;

                object? value = field.GetValue(node);
                if (value is null)
                    continue;

                if (value is Node child)
                    yield return child;
                else if (value is System.Collections.IEnumerable enumerable)
                    foreach (object? item in enumerable)
                        if (item is Node itemNode)
                            yield return itemNode;
            }
        }

        private static bool IsTerminating(Stmt stmt)
            => stmt is ReturnStmt or ThrowStmt or BreakStmt or ContinueStmt;

        private void Declare(string name, int line, int col, bool isParameter = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            Scope scope = _scopes.Peek();
            if (!scope.Declarations.ContainsKey(name))
                scope.Declarations[name] = new Declaration(name, line, col, isParameter);
        }

        private void Use(string name)
        {
            foreach (Scope scope in _scopes)
            {
                if (scope.Declarations.ContainsKey(name))
                {
                    scope.Used.Add(name);
                    return;
                }
            }
        }

        private void ReportUnusedLocals(Scope scope)
        {
            if (scope.IsTopLevel)
                return;

            foreach (Declaration declaration in scope.Declarations.Values)
            {
                if (declaration.Name.StartsWith('_') || scope.Used.Contains(declaration.Name))
                    continue;

                string kind = declaration.IsParameter ? "parameter" : "local";
                Add("CFGS8011", CfgsToolDiagnosticSeverity.Info, $"{kind} '{declaration.Name}' is never used", declaration.Line, declaration.Column);
            }
        }

        private void Add(string code, CfgsToolDiagnosticSeverity severity, string message, Node node)
            => Add(code, severity, message, node.Line, node.Col);

        private void Add(string code, CfgsToolDiagnosticSeverity severity, string message, int line, int col)
            => _diagnostics.Add(new CfgsToolDiagnostic(code, severity, message, _path, Math.Max(1, line), Math.Max(1, col), 1));

        private sealed class Scope(bool isTopLevel)
        {
            public bool IsTopLevel { get; } = isTopLevel;
            public Dictionary<string, Declaration> Declarations { get; } = new(StringComparer.Ordinal);
            public HashSet<string> Used { get; } = new(StringComparer.Ordinal);
        }

        private sealed record Declaration(string Name, int Line, int Column, bool IsParameter = false);
    }

    private static void ApplySuppressions(string sourceText, List<CfgsToolDiagnostic> diagnostics)
    {
        LintSuppressions suppressions = LintSuppressions.Parse(sourceText);
        if (suppressions.IsEmpty)
            return;

        diagnostics.RemoveAll(suppressions.IsSuppressed);
    }

    private sealed class LintSuppressions
    {
        private readonly HashSet<string> _globalCodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, HashSet<string>> _lineCodes = new();

        public bool IsEmpty => _globalCodes.Count == 0 && _lineCodes.Count == 0;

        public static LintSuppressions Parse(string sourceText)
        {
            LintSuppressions result = new();
            string normalized = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            string[] lines = normalized.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int commentStart = line.IndexOf('#');
                if (commentStart < 0)
                    continue;

                string comment = line[commentStart..].Trim();
                if (comment.StartsWith("# lint:disable-next-line", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string code in ParseCodes(comment["# lint:disable-next-line".Length..]))
                        result.AddLine(i + 2, code);
                    continue;
                }

                if (comment.StartsWith("# lint:disable-line", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string code in ParseCodes(comment["# lint:disable-line".Length..]))
                        result.AddLine(i + 1, code);
                    continue;
                }

                if (comment.StartsWith("# lint:disable", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string code in ParseCodes(comment["# lint:disable".Length..]))
                        result._globalCodes.Add(code);
                }
            }

            return result;
        }

        public bool IsSuppressed(CfgsToolDiagnostic diagnostic)
        {
            if (ContainsCode(_globalCodes, diagnostic.Code))
                return true;

            return _lineCodes.TryGetValue(diagnostic.Line, out HashSet<string>? codes) &&
                   ContainsCode(codes, diagnostic.Code);
        }

        private void AddLine(int line, string code)
        {
            if (!_lineCodes.TryGetValue(line, out HashSet<string>? codes))
            {
                codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _lineCodes[line] = codes;
            }

            codes.Add(code);
        }

        private static bool ContainsCode(HashSet<string> codes, string diagnosticCode)
            => codes.Contains("all") || codes.Contains(diagnosticCode);

        private static IEnumerable<string> ParseCodes(string raw)
        {
            string[] parts = raw
                .Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length == 0)
                yield return "all";

            foreach (string part in parts)
                yield return part;
        }
    }
}
