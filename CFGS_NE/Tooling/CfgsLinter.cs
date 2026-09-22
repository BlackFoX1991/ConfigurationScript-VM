using CFGS_VM.Analytic;
using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.Tooling;

internal sealed record CfgsLintResult(IReadOnlyList<CfgsToolDiagnostic> Diagnostics, string? FixedText = null, bool Fixed = false)
{
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == CfgsToolDiagnosticSeverity.Error);
    public bool HasWarnings => Diagnostics.Any(static d => d.Severity == CfgsToolDiagnosticSeverity.Warning);
}

internal static class CfgsLinter
{
    public static CfgsLintResult LintFile(string path, CfgsLintOptions options, string compilerVersion, IReadOnlyList<string>? additionalSearchRoots = null)
    {
        List<CfgsToolDiagnostic> diagnostics = [];
        string fullPath = Path.GetFullPath(path);
        IReadOnlyDictionary<string, CfgsLintRuleConfiguration> resolvedRules =
            CfgsLintRuleRegistry.ResolveRules(options.Preset, options.RuleOverrides);

        if (!File.Exists(fullPath))
        {
            diagnostics.Add(new CfgsToolDiagnostic("CFGS8000", CfgsToolDiagnosticSeverity.Error, $"file not found: {path}", path, 1, 1, 1));
            return new CfgsLintResult(diagnostics);
        }

        string sourceText = File.ReadAllText(fullPath);
        AddStyleDiagnostics(fullPath, sourceText, diagnostics, options, resolvedRules);
        AddCompileDiagnostic(fullPath, diagnostics, compilerVersion, additionalSearchRoots);
        AddAstDiagnostics(fullPath, sourceText, diagnostics, resolvedRules);
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

    /// <summary>
    /// Lintet UNGESPEICHERTEN Quelltext (Style + AST-Regeln, ohne Compile-Pass und ohne Fix). Fuer die
    /// Live-Nutzung im LSP gedacht: der Compile-/Semantik-Pass liefert dort bereits die Editor-Diagnosen,
    /// also wuerde ihn hier zu wiederholen nur Duplikate erzeugen. Der <paramref name="path"/> dient nur als
    /// Anzeigename/Anker fuer die Diagnosen; gelesen wird ausschliesslich <paramref name="sourceText"/>.
    /// </summary>
    public static CfgsLintResult LintSource(string path, string sourceText, CfgsLintOptions options)
    {
        List<CfgsToolDiagnostic> diagnostics = [];
        string fullPath = Path.GetFullPath(path);
        IReadOnlyDictionary<string, CfgsLintRuleConfiguration> resolvedRules =
            CfgsLintRuleRegistry.ResolveRules(options.Preset, options.RuleOverrides);

        AddStyleDiagnostics(fullPath, sourceText, diagnostics, options, resolvedRules);
        AddAstDiagnostics(fullPath, sourceText, diagnostics, resolvedRules);
        ApplySuppressions(sourceText, diagnostics);

        if (options.WarningsAsErrors)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == CfgsToolDiagnosticSeverity.Warning)
                    diagnostics[i] = diagnostics[i] with { Severity = CfgsToolDiagnosticSeverity.Error };
            }
        }

        return new CfgsLintResult(diagnostics);
    }

    private static void AddCompileDiagnostic(string path, List<CfgsToolDiagnostic> diagnostics, string compilerVersion, IReadOnlyList<string>? additionalSearchRoots)
    {
        try
        {
            VM vm = new();
            _ = CfgsCompiler.CompileFile(
                path,
                loadPluginDll: vm.LoadPlugin,
                namespaces: vm.Namespaces,
                compilerVersion: compilerVersion,
                additionalSearchRoots: additionalSearchRoots);
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
        CfgsLintOptions options,
        IReadOnlyDictionary<string, CfgsLintRuleConfiguration> rules)
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
                AddRuleDiagnostic(diagnostics, rules, "CFGS8001", "trailing whitespace", path, lineNumber, line.Length);

            if (!options.AllowTabs && line.StartsWith('\t'))
                AddRuleDiagnostic(diagnostics, rules, "CFGS8002", "indentation uses tabs; prefer spaces or run the formatter with --tabs intentionally", path, lineNumber, 1);

            if (line.Length > options.MaxLineLength)
            {
                AddRuleDiagnostic(
                    diagnostics,
                    rules,
                    "CFGS8003",
                    $"line is longer than {options.MaxLineLength} characters",
                    path,
                    lineNumber,
                    options.MaxLineLength + 1,
                    Math.Max(1, line.Length - options.MaxLineLength));
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                consecutiveBlankLines++;
                if (consecutiveBlankLines > 2)
                    AddRuleDiagnostic(diagnostics, rules, "CFGS8004", "more than two consecutive blank lines", path, lineNumber, 1);
            }
            else
            {
                consecutiveBlankLines = 0;
            }
        }

        if (sourceText.Length > 0 && !sourceText.EndsWith('\n') && !sourceText.EndsWith("\r\n", StringComparison.Ordinal))
        {
            AddRuleDiagnostic(
                diagnostics,
                rules,
                "CFGS8005",
                "missing final newline",
                path,
                Math.Max(1, lines.Length),
                Math.Max(1, lines.LastOrDefault()?.Length ?? 1));
        }
    }

    private static void AddRuleDiagnostic(
        List<CfgsToolDiagnostic> diagnostics,
        IReadOnlyDictionary<string, CfgsLintRuleConfiguration> rules,
        string code,
        string message,
        string path,
        int line,
        int col,
        int spanLength = 1)
    {
        if (!rules.TryGetValue(code, out CfgsLintRuleConfiguration configuration) || !configuration.Enabled)
            return;

        diagnostics.Add(new CfgsToolDiagnostic(
            code,
            configuration.Severity,
            message,
            path,
            Math.Max(1, line),
            Math.Max(1, col),
            Math.Max(1, spanLength)));
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

    private static void AddAstDiagnostics(
        string path,
        string sourceText,
        List<CfgsToolDiagnostic> diagnostics,
        IReadOnlyDictionary<string, CfgsLintRuleConfiguration> rules)
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

        AstLintWalker walker = new(path, diagnostics, rules);
        walker.VisitStatements(ast, isTopLevel: true);
        walker.Finish();
    }

    private sealed class AstLintWalker
    {
        private readonly string _path;
        private readonly List<CfgsToolDiagnostic> _diagnostics;
        private readonly IReadOnlyDictionary<string, CfgsLintRuleConfiguration> _rules;
        private readonly Stack<Scope> _scopes = new();
        private readonly Dictionary<string, Declaration> _functions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _calledFunctions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _referencedNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _externallyReachableFunctions = new(StringComparer.Ordinal);
        private int _namespaceDepth;
        private int _magicNumberAllowanceDepth;

        public AstLintWalker(
            string path,
            List<CfgsToolDiagnostic> diagnostics,
            IReadOnlyDictionary<string, CfgsLintRuleConfiguration> rules)
        {
            _path = path;
            _diagnostics = diagnostics;
            _rules = rules;
            _scopes.Push(new Scope(isTopLevel: true));
        }

        public void VisitStatements(IEnumerable<Stmt> statements, bool isTopLevel = false)
        {
            if (!isTopLevel)
                _scopes.Push(new Scope(isTopLevel: false));

            bool immediateTerminationReached = false;
            bool guaranteedTerminationReached = false;

            foreach (Stmt stmt in statements)
            {
                if (immediateTerminationReached)
                    Add("CFGS8012", "unreachable statement after terminating control flow", stmt);
                else if (guaranteedTerminationReached)
                    Add("CFGS8020", "unreachable statement after guaranteed terminating control flow", stmt);

                VisitStatement(stmt);

                if (immediateTerminationReached || guaranteedTerminationReached)
                    continue;

                if (IsImmediateTerminating(stmt))
                    immediateTerminationReached = true;
                else if (DoesStatementAlwaysTerminate(stmt))
                    guaranteedTerminationReached = true;
            }

            if (!isTopLevel)
            {
                ReportUnusedDeclarations(_scopes.Peek());
                _scopes.Pop();
            }
        }

        public void Finish()
        {
            ReportUnusedDeclarations(_scopes.Peek());

            foreach (Declaration function in _functions.Values)
            {
                if (!function.Name.StartsWith('_') &&
                    !string.Equals(function.Name, "main", StringComparison.Ordinal) &&
                    !_calledFunctions.Contains(function.Name) &&
                    !_referencedNames.Contains(function.Name) &&
                    !_externallyReachableFunctions.Contains(function.Name))
                {
                    Add("CFGS8013", $"function '{function.Name}' is never called in this file", function.Line, function.Column);
                }
            }
        }

        private void VisitStatement(Stmt stmt)
        {
            switch (stmt)
            {
                case EmptyStmt:
                case ErrorStmt:
                case YieldStmt:
                case BareImportSyntaxStmt:
                case UseNamespaceStmt:
                case EnumDeclStmt:
                    break;

                case VarDecl v:
                    Declare(v.Name, v.Line, v.Col, DeclarationKind.Local);
                    VisitExpr(v.Value);
                    break;

                case ConstDecl c:
                    Declare(c.Name, c.Line, c.Col, DeclarationKind.Local);
                    VisitExprAllowingMagicNumbers(c.Value);
                    break;

                case DestructureDeclStmt d:
                    VisitExpr(d.Value);
                    if (d.IsConst)
                        DeclarePatternBindingsAllowingMagicNumbers(d.Pattern, DeclarationKind.Local);
                    else
                        DeclarePatternBindings(d.Pattern, DeclarationKind.Local);
                    break;

                case DestructureAssignStmt d:
                    VisitExpr(d.Value);
                    VisitPatternValueExpressions(d.Pattern);
                    UsePatternBindings(d.Pattern);
                    break;

                case FuncDeclStmt f:
                    if (_scopes.Peek().IsTopLevel)
                    {
                        _functions.TryAdd(f.Name, new Declaration(f.Name, f.Line, f.Col, DeclarationKind.Local));
                        if (_namespaceDepth > 0)
                            _externallyReachableFunctions.Add(f.Name);
                    }

                    VisitFunction(f.Name, f.IsAsync, f.Parameters, f.RestParameter, f.ParameterSpecs, f.Body, f.Line, f.Col);
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

                case SliceSetStmt s:
                    VisitExpr(s.Slice);
                    VisitExpr(s.Value);
                    break;

                case PushStmt p:
                    VisitExpr(p.Target);
                    VisitExpr(p.Value);
                    break;

                case SetFieldStmt s:
                    VisitExpr(s.Target);
                    VisitExpr(s.Value);
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
                    // A declaration group (for initializer) binds in the enclosing scope.
                    VisitStatements(b.Statements, isTopLevel: b.IsDeclarationGroup);
                    break;

                case IfStmt i:
                    MaybeReportConstantCondition(i.Condition);
                    MaybeReportEmptyBlock(i.ThenBlock, "if");
                    if (i.ElseBranch is BlockStmt elseBlock)
                        MaybeReportEmptyBlock(elseBlock, "else");
                    VisitExpr(i.Condition);
                    VisitStatement(i.ThenBlock);
                    if (i.ElseBranch is not null)
                        VisitStatement(i.ElseBranch);
                    break;

                case WhileStmt w:
                    MaybeReportConstantCondition(w.Condition);
                    MaybeReportEmptyBlock(w.Body, "while");
                    VisitExpr(w.Condition);
                    VisitStatement(w.Body);
                    break;

                case DoWhileStmt d:
                    if (d.Body is BlockStmt doBlock)
                        MaybeReportEmptyBlock(doBlock, "do");
                    VisitStatement(d.Body);
                    MaybeReportConstantCondition(d.Condition);
                    VisitExpr(d.Condition);
                    break;

                case ForStmt f:
                    if (f.Init is not null)
                        VisitStatement(f.Init);
                    if (f.Condition is not null)
                    {
                        MaybeReportConstantCondition(f.Condition);
                        VisitExpr(f.Condition);
                    }
                    if (f.Increment is not null)
                        VisitStatement(f.Increment);
                    MaybeReportEmptyBlock(f.Body, "for");
                    VisitStatement(f.Body);
                    break;

                case ForeachStmt f:
                    VisitExpr(f.Iterable);
                    if (f.Body is BlockStmt foreachBlock)
                        MaybeReportEmptyBlock(foreachBlock, "foreach");

                    _scopes.Push(new Scope(isTopLevel: false));
                    if (f.TargetPattern is not null)
                    {
                        VisitPatternValueExpressions(f.TargetPattern);
                        if (f.DeclareLocal)
                            DeclarePatternBindings(f.TargetPattern, DeclarationKind.Local);
                        else
                            UsePatternBindings(f.TargetPattern);
                    }
                    else if (!string.IsNullOrWhiteSpace(f.VarName))
                    {
                        if (f.DeclareLocal)
                            Declare(f.VarName, f.Line, f.Col, DeclarationKind.Local);
                        else
                            Use(f.VarName);
                    }

                    VisitStatement(f.Body);
                    ReportUnusedDeclarations(_scopes.Peek());
                    _scopes.Pop();
                    break;

                case MatchStmt m:
                    VisitExpr(m.Expression);
                    foreach (CaseClause caseClause in m.Cases)
                        VisitCaseClause(caseClause);
                    if (m.DefaultCase is not null)
                    {
                        MaybeReportEmptyBlock(m.DefaultCase, "match default");
                        VisitStatement(m.DefaultCase);
                    }
                    break;

                case TryStmt t:
                    MaybeReportEmptyBlock(t.TryBlock, "try");
                    VisitStatement(t.TryBlock);

                    if (t.CatchBlock is not null)
                    {
                        MaybeReportEmptyBlock(t.CatchBlock, "catch");
                        _scopes.Push(new Scope(isTopLevel: false));
                        if (!string.IsNullOrWhiteSpace(t.CatchIdent))
                            Declare(t.CatchIdent!, t.Line, t.Col, DeclarationKind.Local);
                        VisitStatements(t.CatchBlock.Statements, isTopLevel: true);
                        ReportUnusedDeclarations(_scopes.Peek());
                        _scopes.Pop();
                    }

                    if (t.FinallyBlock is not null)
                    {
                        MaybeReportEmptyBlock(t.FinallyBlock, "finally");
                        VisitStatement(t.FinallyBlock);
                    }
                    break;

                case UsingStmt u:
                    VisitExpr(u.Resource);
                    MaybeReportEmptyBlock(u.Body, "using");
                    _scopes.Push(new Scope(isTopLevel: false));
                    if (!string.IsNullOrWhiteSpace(u.BindingName))
                        Declare(u.BindingName!, u.Line, u.Col, DeclarationKind.Local);
                    VisitStatements(u.Body.Statements, isTopLevel: true);
                    ReportUnusedDeclarations(_scopes.Peek());
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

                case NamespaceImportSyntaxStmt n:
                    Declare(n.Alias, n.Line, n.Col, DeclarationKind.Import);
                    break;

                case NamedImportSyntaxStmt n:
                    foreach (ImportBindingSpec binding in n.Imports)
                    {
                        string localName = string.IsNullOrWhiteSpace(binding.LocalName) ? binding.ImportName : binding.LocalName;
                        Declare(localName, n.Line, n.Col, DeclarationKind.Import);
                    }
                    break;

                case DefaultImportSyntaxStmt d:
                    Declare(d.ImportName, d.Line, d.Col, DeclarationKind.Import);
                    break;

                case NativeImportSyntaxStmt n:
                    Declare(n.Alias, n.Line, n.Col, DeclarationKind.Import);
                    break;

                case NamespaceImportAliasStmt n:
                    Declare(n.Alias, n.Line, n.Col, DeclarationKind.Import);
                    break;

                case ImportAliasDeclStmt i:
                    Declare(i.LocalName, i.Line, i.Col, DeclarationKind.Import);
                    break;

                case ClassDeclStmt c:
                    MarkQualifiedPathUsage(c.BaseName);
                    foreach (string interfaceName in c.ImplementedInterfaces)
                        MarkQualifiedPathUsage(interfaceName);
                    foreach (FunctionParameterSpec parameter in c.ConstructorParameterSpecs)
                    {
                        if (parameter.DefaultValue is not null)
                            VisitExpr(parameter.DefaultValue);
                    }
                    foreach (Expr baseCtorArg in c.BaseCtorArgs)
                        VisitExpr(baseCtorArg);

                    foreach ((string fieldName, Expr? value) in c.Fields)
                    {
                        if (c.ConstFields.Contains(fieldName))
                            VisitExprAllowingMagicNumbers(value);
                        else
                            VisitExpr(value);
                    }

                    foreach ((string fieldName, Expr? value) in c.StaticFields)
                    {
                        if (c.StaticConstFields.Contains(fieldName))
                            VisitExprAllowingMagicNumbers(value);
                        else
                            VisitExpr(value);
                    }

                    foreach (FuncDeclStmt method in c.Methods)
                        VisitFunction(method.Name, method.IsAsync, method.Parameters, method.RestParameter, method.ParameterSpecs, method.Body, method.Line, method.Col);
                    foreach (FuncDeclStmt method in c.StaticMethods)
                        VisitFunction(method.Name, method.IsAsync, method.Parameters, method.RestParameter, method.ParameterSpecs, method.Body, method.Line, method.Col);

                    foreach (PropertyDeclStmt property in c.Properties.Concat(c.StaticProperties))
                    {
                        if (property.Initializer is not null)
                            VisitExpr(property.Initializer);

                        foreach (PropertyAccessorDecl accessor in property.Accessors)
                        {
                            if (accessor.Body is not null)
                                VisitStatement(accessor.Body);
                        }
                    }

                    foreach (EnumDeclStmt nestedEnum in c.Enums)
                        VisitStatement(nestedEnum);
                    foreach (ClassDeclStmt nestedClass in c.NestedClasses)
                        VisitStatement(nestedClass);
                    break;

                case InterfaceDeclStmt i:
                    foreach (string baseInterface in i.BaseInterfaces)
                        MarkQualifiedPathUsage(baseInterface);

                    foreach (InterfaceMethodDecl method in i.Methods)
                    {
                        if (method.Body is not null)
                            VisitFunction(method.Name, method.IsAsync, method.Parameters, method.RestParameter, method.ParameterSpecs, method.Body, method.Line, method.Col);
                    }
                    break;

                case ExportStmt e:
                    if (e.Inner is FuncDeclStmt exportedFunction)
                        _externallyReachableFunctions.Add(exportedFunction.Name);
                    VisitStatement(e.Inner);
                    break;
            }
        }

        private void VisitFunction(
            string name,
            IReadOnlyList<string> parameters,
            string? restParameter,
            IReadOnlyList<FunctionParameterSpec>? parameterSpecs,
            BlockStmt body,
            int line,
            int col)
        {
            VisitFunction(name, isAsync: false, parameters, restParameter, parameterSpecs, body, line, col);
        }

        private void VisitFunction(
            string name,
            bool isAsync,
            IReadOnlyList<string> parameters,
            string? restParameter,
            IReadOnlyList<FunctionParameterSpec>? parameterSpecs,
            BlockStmt body,
            int line,
            int col)
        {
            _scopes.Push(new Scope(isTopLevel: false));

            if (parameterSpecs is not null && parameterSpecs.Count > 0)
            {
                foreach (FunctionParameterSpec parameter in parameterSpecs)
                    DeclareParameter(parameter);
            }
            else
            {
                foreach (string parameter in parameters)
                    Declare(parameter, line, col, DeclarationKind.Parameter);
                if (!string.IsNullOrWhiteSpace(restParameter))
                    Declare(restParameter!, line, col, DeclarationKind.Parameter);
            }

            bool hasAwait = ContainsAwait(body);
            if (isAsync && !hasAwait)
                Add("CFGS8014", $"async function '{name}' does not await anything", line, col);

            VisitStatements(body.Statements, isTopLevel: true);
            ReportUnusedDeclarations(_scopes.Peek());
            _scopes.Pop();
        }

        private void DeclareParameter(FunctionParameterSpec parameter)
        {
            if (parameter.DestructurePattern is not null)
                DeclarePatternBindings(parameter.DestructurePattern, DeclarationKind.Parameter);
            else
                Declare(parameter.Name, parameter.Line, parameter.Col, DeclarationKind.Parameter);

            if (parameter.DefaultValue is not null)
                VisitExpr(parameter.DefaultValue);
        }

        private void VisitCaseClause(CaseClause caseClause)
        {
            MaybeReportEmptyBlock(caseClause.Body, "match case");
            _scopes.Push(new Scope(isTopLevel: false));
            VisitPatternValueExpressions(caseClause.Pattern);
            DeclarePatternBindings(caseClause.Pattern, DeclarationKind.Local);
            if (caseClause.Guard is not null)
            {
                MaybeReportConstantCondition(caseClause.Guard);
                VisitExpr(caseClause.Guard);
            }

            VisitStatements(caseClause.Body.Statements, isTopLevel: true);
            ReportUnusedDeclarations(_scopes.Peek());
            _scopes.Pop();
        }

        private void VisitExpr(Expr? expr)
        {
            if (expr is null)
                return;

            switch (expr)
            {
                case NullExpr:
                case StringExpr:
                case CharExpr:
                case BoolExpr:
                case NativeAssemblyExpr:
                    break;

                case InterpolatedStringExpr interpolatedStringExpr:
                    foreach (InterpolatedStringPart part in interpolatedStringExpr.Parts)
                    {
                        if (part is InterpolatedStringExprPart exprPart)
                            VisitExpr(exprPart.Expression);
                    }
                    break;

                case NumberExpr n:
                    MaybeReportMagicNumber(n);
                    break;

                case VarExpr v:
                    // Record the name unconditionally: the function may be declared LATER in the
                    // file (declarations are hoisted, so forward references are legal). Gating on
                    // _functions here made every forward reference a CFGS8013 false positive.
                    // Finish() only consults these sets for known functions, so extra names are free.
                    if (!Use(v.Name))
                        _referencedNames.Add(v.Name);
                    break;

                case CallExpr c:
                    if (c.Target is VarExpr target)
                    {
                        // Same as VarExpr above: track forward calls to functions declared later.
                        if (!Use(target.Name))
                            _calledFunctions.Add(target.Name);
                    }
                    else
                    {
                        VisitExpr(c.Target);
                    }

                    int? typeContextArgIndex = TryGetBuiltinTypeContextArgumentIndex(c);
                    for (int i = 0; i < c.Args.Count; i++)
                    {
                        Expr arg = c.Args[i];
                        if (typeContextArgIndex == i)
                            VisitTypeContextExpr(arg);
                        else
                            VisitExpr(arg);
                    }
                    break;

                case MethodCallExpr m:
                    VisitExpr(m.Target);
                    foreach (Expr arg in m.Args)
                        VisitExpr(arg);
                    break;

                case BinaryExpr b:
                    VisitExpr(b.Left);
                    if (b.Op == TokenType.Is)
                        VisitTypeContextExpr(b.Right);
                    else
                        VisitExpr(b.Right);
                    MaybeReportSuspiciousEquality(b);
                    MaybeReportCharArithmetic(b);
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
                    foreach (Expr item in a.Elements)
                        VisitExpr(item);
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
                    MaybeReportConstantCondition(c.Condition);
                    VisitExpr(c.Condition);
                    VisitExpr(c.ThenExpr);
                    VisitExpr(c.ElseExpr);
                    break;

                case AwaitExpr a:
                    VisitExpr(a.Inner);
                    break;

                case FuncExpr f:
                    VisitFunction("<anonymous>", f.IsAsync, f.Parameters, f.RestParameter, f.ParameterSpecs, f.Body, f.Line, f.Col);
                    break;

                case NamedArgExpr n:
                    VisitExpr(n.Value);
                    break;

                case SpreadArgExpr s:
                    VisitExpr(s.Value);
                    break;

                case NewExpr n:
                    MarkQualifiedPathUsage(n.ClassName);
                    foreach (Expr arg in n.Args)
                        VisitExpr(arg);
                    VisitObjectInitializerEntries(n.Initializers);
                    break;

                case GetFieldExpr g:
                    VisitExpr(g.Target);
                    break;

                case ObjectInitExpr o:
                    VisitExpr(o.Target);
                    VisitObjectInitializerEntries(o.Inits);
                    break;

                case MatchExpr m:
                    VisitExpr(m.Scrutinee);
                    foreach (CaseExprArm arm in m.Arms)
                        VisitCaseExprArm(arm);
                    VisitExpr(m.DefaultArm);
                    break;

                case TryUnwrapExpr t:
                    VisitExpr(t.Inner);
                    break;
            }
        }

        private void VisitObjectInitializerEntries(IEnumerable<ObjectInitializerEntry> entries)
        {
            foreach (ObjectInitializerEntry entry in entries)
            {
                switch (entry)
                {
                    case ObjectMemberInitializerEntry member:
                        if (member.Value is not null)
                            VisitExpr(member.Value);
                        VisitObjectInitializerEntries(member.NestedInitializers);
                        break;

                    case ObjectIndexerInitializerEntry indexer:
                        VisitExpr(indexer.Index);
                        VisitExpr(indexer.Value);
                        break;
                }
            }
        }

        private void VisitCaseExprArm(CaseExprArm arm)
        {
            _scopes.Push(new Scope(isTopLevel: false));
            VisitPatternValueExpressions(arm.Pattern);
            DeclarePatternBindings(arm.Pattern, DeclarationKind.Local);
            if (arm.Guard is not null)
            {
                MaybeReportConstantCondition(arm.Guard);
                VisitExpr(arm.Guard);
            }

            VisitExpr(arm.Body);
            ReportUnusedDeclarations(_scopes.Peek());
            _scopes.Pop();
        }

        private void VisitPatternValueExpressions(MatchPattern pattern)
        {
            switch (pattern)
            {
                case ValueMatchPattern value:
                    VisitExpr(value.Value);
                    break;

                case ArrayMatchPattern arrayPattern:
                    foreach (MatchPattern element in arrayPattern.Elements)
                        VisitPatternValueExpressions(element);
                    break;

                case DictMatchPattern dictPattern:
                    foreach ((_, MatchPattern entryPattern) in dictPattern.Entries)
                        VisitPatternValueExpressions(entryPattern);
                    break;

                case OrMatchPattern orPattern:
                    foreach (MatchPattern alternative in orPattern.Alternatives)
                        VisitPatternValueExpressions(alternative);
                    break;
            }
        }

        private void DeclarePatternBindings(MatchPattern pattern, DeclarationKind kind)
        {
            switch (pattern)
            {
                case BindingMatchPattern binding:
                    Declare(binding.Name, binding.Line, binding.Col, kind);
                    break;

                case ArrayMatchPattern arrayPattern:
                    foreach (MatchPattern element in arrayPattern.Elements)
                        DeclarePatternBindings(element, kind);
                    break;

                case DictMatchPattern dictPattern:
                    foreach ((_, MatchPattern entryPattern) in dictPattern.Entries)
                        DeclarePatternBindings(entryPattern, kind);
                    break;
            }
        }

        private void DeclarePatternBindingsAllowingMagicNumbers(MatchPattern pattern, DeclarationKind kind)
        {
            _magicNumberAllowanceDepth++;
            try
            {
                VisitPatternValueExpressions(pattern);
                DeclarePatternBindings(pattern, kind);
            }
            finally
            {
                _magicNumberAllowanceDepth--;
            }
        }

        private void UsePatternBindings(MatchPattern pattern)
        {
            switch (pattern)
            {
                case BindingMatchPattern binding:
                    Use(binding.Name);
                    break;

                case ArrayMatchPattern arrayPattern:
                    foreach (MatchPattern element in arrayPattern.Elements)
                        UsePatternBindings(element);
                    break;

                case DictMatchPattern dictPattern:
                    foreach ((_, MatchPattern entryPattern) in dictPattern.Entries)
                        UsePatternBindings(entryPattern);
                    break;
            }
        }

        private void VisitExprAllowingMagicNumbers(Expr? expr)
        {
            _magicNumberAllowanceDepth++;
            try
            {
                VisitExpr(expr);
            }
            finally
            {
                _magicNumberAllowanceDepth--;
            }
        }

        private void VisitTypeContextExpr(Expr? expr)
        {
            if (expr is null)
                return;

            if (TryExtractQualifiedPath(expr, out string? path))
            {
                MarkQualifiedPathUsage(path);
                return;
            }

            VisitExpr(expr);
        }

        private void MarkQualifiedPathUsage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            int dotIndex = path.IndexOf('.');
            if (dotIndex < 0)
            {
                // A bare name in type context is a pure type/interface reference; it must not
                // count as a value read of a same-named local that merely shadows the type.
                if (_functions.ContainsKey(path))
                    _referencedNames.Add(path);
                return;
            }

            // A qualified path (e.g. App.Model.Thing) genuinely navigates through its root,
            // so the root keeps counting as a real use (imports, namespace aliases, ...).
            string root = path[..dotIndex];
            if (!Use(root) && _functions.ContainsKey(root))
                _referencedNames.Add(root);
        }

        private static int? TryGetBuiltinTypeContextArgumentIndex(CallExpr callExpr)
        {
            if (callExpr.Target is not VarExpr target)
                return null;

            return target.Name switch
            {
                "implements" when callExpr.Args.Count >= 2 => 1,
                "implements_shape" when callExpr.Args.Count >= 2 => 1,
                "interface_call" when callExpr.Args.Count >= 2 => 1,
                _ => null
            };
        }

        private static bool TryExtractQualifiedPath(Expr expr, out string? path)
        {
            switch (expr)
            {
                case VarExpr varExpr:
                    path = varExpr.Name;
                    return true;

                case IndexExpr indexExpr when indexExpr.Target is not null && indexExpr.Index is StringExpr stringExpr:
                    if (TryExtractQualifiedPath(indexExpr.Target, out string? prefix) && !string.IsNullOrWhiteSpace(prefix))
                    {
                        path = $"{prefix}.{stringExpr.Value}";
                        return true;
                    }

                    break;
            }

            path = null;
            return false;
        }

        private bool ContainsAwait(Node node)
        {
            // yield is a suspension point that REQUIRES an async function, so an async function
            // whose only suspension is a yield must not be flagged as "does not await anything".
            if (node is AwaitExpr or YieldStmt)
                return true;
            if (node is FuncDeclStmt or FuncExpr)
                return false;

            foreach (object child in NodeWalker.EnumerateChildren(node))
            {
                if (child is Node childNode && ContainsAwait(childNode))
                    return true;
            }

            return false;
        }


        private static bool IsImmediateTerminating(Stmt stmt)
            => stmt is ReturnStmt or ThrowStmt or BreakStmt or ContinueStmt;

        private static bool DoesStatementAlwaysTerminate(Stmt stmt)
        {
            if (IsImmediateTerminating(stmt))
                return true;

            return stmt switch
            {
                BlockStmt block => DoesStatementSequenceAlwaysTerminate(block.Statements),
                IfStmt ifStmt => ifStmt.ElseBranch is not null &&
                                 DoesStatementAlwaysTerminate(ifStmt.ThenBlock) &&
                                 DoesStatementAlwaysTerminate(ifStmt.ElseBranch),
                MatchStmt matchStmt => matchStmt.DefaultCase is not null &&
                                       matchStmt.Cases.Count > 0 &&
                                       matchStmt.Cases.All(static c => DoesStatementAlwaysTerminate(c.Body)) &&
                                       DoesStatementAlwaysTerminate(matchStmt.DefaultCase),
                TryStmt tryStmt when tryStmt.FinallyBlock is not null => DoesStatementAlwaysTerminate(tryStmt.FinallyBlock),
                TryStmt tryStmt when tryStmt.CatchBlock is not null => DoesStatementAlwaysTerminate(tryStmt.TryBlock) &&
                                                                       DoesStatementAlwaysTerminate(tryStmt.CatchBlock),
                UsingStmt usingStmt => DoesStatementAlwaysTerminate(usingStmt.Body),
                _ => false
            };
        }

        private static bool DoesStatementSequenceAlwaysTerminate(IEnumerable<Stmt> statements)
        {
            foreach (Stmt stmt in statements)
            {
                if (DoesStatementAlwaysTerminate(stmt))
                    return true;
            }

            return false;
        }

        private void Declare(string name, int line, int col, DeclarationKind kind)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            Scope scope = _scopes.Peek();
            if (scope.Declarations.ContainsKey(name))
                return;

            if (!name.StartsWith('_') && TryFindOuterDeclaration(name, out _))
            {
                string subject = kind switch
                {
                    DeclarationKind.Parameter => "parameter",
                    DeclarationKind.Import => "import",
                    _ => "local"
                };

                Add("CFGS8015", $"{subject} '{name}' shadows an existing declaration from an outer scope", line, col);
            }

            scope.Declarations[name] = new Declaration(name, line, col, kind);
        }

        private bool TryFindOuterDeclaration(string name, out Declaration declaration)
        {
            bool skipCurrent = true;
            foreach (Scope scope in _scopes)
            {
                if (skipCurrent)
                {
                    skipCurrent = false;
                    continue;
                }

                if (scope.Declarations.TryGetValue(name, out declaration!))
                    return true;
            }

            declaration = null!;
            return false;
        }

        private bool Use(string name)
        {
            foreach (Scope scope in _scopes)
            {
                if (scope.Declarations.ContainsKey(name))
                {
                    scope.Used.Add(name);
                    return true;
                }
            }

            return false;
        }

        private void ReportUnusedDeclarations(Scope scope)
        {
            foreach (Declaration declaration in scope.Declarations.Values)
            {
                if (declaration.Name.StartsWith('_') || scope.Used.Contains(declaration.Name))
                    continue;

                switch (declaration.Kind)
                {
                    case DeclarationKind.Import:
                        Add("CFGS8016", $"import '{declaration.Name}' is never used", declaration.Line, declaration.Column);
                        break;

                    case DeclarationKind.Parameter when !scope.IsTopLevel:
                        Add("CFGS8011", $"parameter '{declaration.Name}' is never used", declaration.Line, declaration.Column);
                        break;

                    case DeclarationKind.Local when !scope.IsTopLevel:
                        Add("CFGS8011", $"local '{declaration.Name}' is never used", declaration.Line, declaration.Column);
                        break;
                }
            }
        }

        private void MaybeReportEmptyBlock(BlockStmt block, string context)
        {
            if (block.IsNamespaceScope || block.IsFunctionBody)
                return;

            if (block.Statements.Count == 0 || block.Statements.All(static stmt => stmt is EmptyStmt or ErrorStmt))
                Add("CFGS8017", $"empty {context} block", block);
        }

        private void MaybeReportSuspiciousEquality(BinaryExpr binary)
        {
            if (binary.Op is not (TokenType.Eq or TokenType.Neq))
                return;

            if (TryGetLiteralCategory(binary.Left, out string? leftCategory) &&
                TryGetLiteralCategory(binary.Right, out string? rightCategory) &&
                !string.Equals(leftCategory, rightCategory, StringComparison.Ordinal))
            {
                string outcome = binary.Op == TokenType.Eq ? "false" : "true";
                Add("CFGS8018", $"comparison between {leftCategory} and {rightCategory} is always {outcome}", binary);
                return;
            }

            if (TryEvaluateEqualityComparison(binary.Left, binary.Right, binary.Op, out bool result))
                Add("CFGS8018", $"comparison is always {ToBooleanLiteral(result)}", binary);
        }

        private void MaybeReportCharArithmetic(BinaryExpr binary)
        {
            // Flag only genuine numeric coercions: arithmetic and bitwise operators. Subtraction is
            // excluded because `'9' - '0'` and `c - 'a'` are the idiomatic offset/digit patterns, and
            // comparison, equality, and logical operators are deliberate char ordering, not surprises.
            if (binary.Op is not (
                TokenType.Plus or TokenType.Star or TokenType.Slash or TokenType.Modulo or TokenType.Expo or
                TokenType.bAnd or TokenType.bOr or TokenType.bXor or TokenType.bShiftL or TokenType.bShiftR))
                return;

            bool leftChar = binary.Left is CharExpr;
            bool rightChar = binary.Right is CharExpr;
            if (!leftChar && !rightChar)
                return;

            // Require both operands to be char/number literals so the report never misfires on a char
            // literal combined with a runtime value (for example `'>' + line`, which is concatenation).
            if (binary.Left is not (CharExpr or NumberExpr) || binary.Right is not (CharExpr or NumberExpr))
                return;

            char sample = (leftChar ? (CharExpr)binary.Left : (CharExpr)binary.Right).Value;
            Add(
                "CFGS8022",
                $"char literal '{sample}' coerces to its code point ({(int)sample}) in this arithmetic; write int('{sample}') to make the numeric intent explicit, or use string concatenation for text",
                binary);
        }

        private void MaybeReportConstantCondition(Expr condition)
        {
            if (condition is BinaryExpr { Op: TokenType.Eq or TokenType.Neq })
                return;

            if (TryEvaluateTruthiness(condition, out bool result))
                Add("CFGS8019", $"condition is always {ToBooleanLiteral(result)}", condition);
        }

        private void MaybeReportMagicNumber(NumberExpr number)
        {
            if (_magicNumberAllowanceDepth > 0)
                return;

            if (!TryGetNumericLiteralValue(number, out double value))
                return;

            if (value == 0d || value == 1d || value == -1d)
                return;

            Add("CFGS8021", $"magic number '{FormatNumberLiteral(number.Value)}'; extract it to a named constant if it carries business meaning", number);
        }

        private void Add(string code, string message, Node node)
            => Add(code, message, node.Line, node.Col);

        private void Add(string code, string message, int line, int col)
        {
            if (!_rules.TryGetValue(code, out CfgsLintRuleConfiguration configuration) || !configuration.Enabled)
                return;

            _diagnostics.Add(new CfgsToolDiagnostic(
                code,
                configuration.Severity,
                message,
                _path,
                Math.Max(1, line),
                Math.Max(1, col),
                1));
        }

        private static bool TryEvaluateTruthiness(Expr expr, out bool value)
        {
            switch (expr)
            {
                case NullExpr:
                    value = false;
                    return true;

                case BoolExpr boolean:
                    value = boolean.Value;
                    return true;

                case NumberExpr number when TryGetNumericLiteralValue(number, out double numericValue):
                    value = numericValue != 0d || double.IsNaN(numericValue);
                    return true;

                case StringExpr text:
                    value = text.Value.Length > 0;
                    return true;

                case InterpolatedStringExpr interpolatedStringExpr
                    when interpolatedStringExpr.Parts.All(part => part is InterpolatedStringTextPart):
                    value = interpolatedStringExpr.Parts
                        .OfType<InterpolatedStringTextPart>()
                        .Sum(static part => part.Value.Length) > 0;
                    return true;

                case CharExpr character:
                    value = character.Value != '\0';
                    return true;

                case ArrayExpr array:
                    value = array.Elements.Count > 0;
                    return true;

                case DictExpr dict:
                    value = dict.Pairs.Count > 0;
                    return true;

                case UnaryExpr { Op: TokenType.Not } unary when TryEvaluateTruthiness(unary.Right, out bool innerValue):
                    value = !innerValue;
                    return true;

                case ConditionalExpr conditional when TryEvaluateTruthiness(conditional.Condition, out bool conditionValue):
                    return TryEvaluateTruthiness(conditionValue ? conditional.ThenExpr : conditional.ElseExpr, out value);

                case BinaryExpr { Op: TokenType.AndAnd } andExpr:
                    if (TryEvaluateTruthiness(andExpr.Left, out bool leftAnd))
                    {
                        if (!leftAnd)
                        {
                            value = false;
                            return true;
                        }

                        if (TryEvaluateTruthiness(andExpr.Right, out bool rightAnd))
                        {
                            value = rightAnd;
                            return true;
                        }
                    }
                    break;

                case BinaryExpr { Op: TokenType.OrOr } orExpr:
                    if (TryEvaluateTruthiness(orExpr.Left, out bool leftOr))
                    {
                        if (leftOr)
                        {
                            value = true;
                            return true;
                        }

                        if (TryEvaluateTruthiness(orExpr.Right, out bool rightOr))
                        {
                            value = rightOr;
                            return true;
                        }
                    }
                    break;

                case BinaryExpr binary when TryEvaluateComparison(binary, out bool comparisonResult):
                    value = comparisonResult;
                    return true;
            }

            value = false;
            return false;
        }

        private static bool TryEvaluateComparison(BinaryExpr binary, out bool result)
        {
            if (binary.Op is TokenType.Eq or TokenType.Neq)
                return TryEvaluateEqualityComparison(binary.Left, binary.Right, binary.Op, out result);

            if (!TryGetNumericExprValue(binary.Left, out double left) || !TryGetNumericExprValue(binary.Right, out double right))
            {
                result = false;
                return false;
            }

            result = binary.Op switch
            {
                TokenType.Lt => left < right,
                TokenType.Gt => left > right,
                TokenType.Le => left <= right,
                TokenType.Ge => left >= right,
                _ => false
            };

            return binary.Op is TokenType.Lt or TokenType.Gt or TokenType.Le or TokenType.Ge;
        }

        private static bool TryEvaluateEqualityComparison(Expr left, Expr right, TokenType op, out bool result)
        {
            if (!TryGetComparableLiteral(left, out ComparableLiteral leftLiteral) ||
                !TryGetComparableLiteral(right, out ComparableLiteral rightLiteral))
            {
                result = false;
                return false;
            }

            bool equals;
            if (leftLiteral.Kind != rightLiteral.Kind)
            {
                equals = false;
            }
            else if (leftLiteral.Kind is LiteralKind.Array or LiteralKind.Dictionary)
            {
                result = false;
                return false;
            }
            else
            {
                equals = leftLiteral.Kind switch
                {
                    LiteralKind.Null => true,
                    LiteralKind.Bool => (bool)leftLiteral.Value! == (bool)rightLiteral.Value!,
                    LiteralKind.Number => (double)leftLiteral.Value! == (double)rightLiteral.Value!,
                    LiteralKind.String => string.Equals((string)leftLiteral.Value!, (string)rightLiteral.Value!, StringComparison.Ordinal),
                    LiteralKind.Char => (char)leftLiteral.Value! == (char)rightLiteral.Value!,
                    _ => false
                };
            }

            result = op == TokenType.Eq ? equals : !equals;
            return true;
        }

        private static bool TryGetLiteralCategory(Expr expr, out string? category)
        {
            if (TryGetComparableLiteral(expr, out ComparableLiteral literal))
            {
                category = literal.Kind switch
                {
                    LiteralKind.Null => "null",
                    LiteralKind.Bool => "bool",
                    LiteralKind.Number => "number",
                    LiteralKind.String => "string",
                    LiteralKind.Char => "char",
                    LiteralKind.Array => "array",
                    LiteralKind.Dictionary => "dictionary",
                    _ => null
                };
                return category is not null;
            }

            category = null;
            return false;
        }

        private static bool TryGetComparableLiteral(Expr expr, out ComparableLiteral literal)
        {
            switch (expr)
            {
                case NullExpr:
                    literal = new ComparableLiteral(LiteralKind.Null, null);
                    return true;

                case BoolExpr boolean:
                    literal = new ComparableLiteral(LiteralKind.Bool, boolean.Value);
                    return true;

                case NumberExpr number when TryGetNumericLiteralValue(number, out double numericValue):
                    literal = new ComparableLiteral(LiteralKind.Number, numericValue);
                    return true;

                case StringExpr text:
                    literal = new ComparableLiteral(LiteralKind.String, text.Value);
                    return true;

                case InterpolatedStringExpr interpolatedStringExpr
                    when interpolatedStringExpr.Parts.All(part => part is InterpolatedStringTextPart):
                    literal = new ComparableLiteral(
                        LiteralKind.String,
                        string.Concat(interpolatedStringExpr.Parts.OfType<InterpolatedStringTextPart>().Select(static part => part.Value)));
                    return true;

                case CharExpr character:
                    literal = new ComparableLiteral(LiteralKind.Char, character.Value);
                    return true;

                case ArrayExpr:
                    literal = new ComparableLiteral(LiteralKind.Array, null);
                    return true;

                case DictExpr:
                    literal = new ComparableLiteral(LiteralKind.Dictionary, null);
                    return true;

                case UnaryExpr { Op: TokenType.Plus or TokenType.Minus } unary when TryGetNumericExprValue(unary, out double signedValue):
                    literal = new ComparableLiteral(LiteralKind.Number, signedValue);
                    return true;
            }

            literal = default;
            return false;
        }

        private static bool TryGetNumericExprValue(Expr expr, out double value)
        {
            switch (expr)
            {
                case NumberExpr number:
                    return TryGetNumericLiteralValue(number, out value);

                case UnaryExpr { Op: TokenType.Plus } unary:
                    return TryGetNumericExprValue(unary.Right, out value);

                case UnaryExpr { Op: TokenType.Minus } unary when TryGetNumericExprValue(unary.Right, out double inner):
                    value = -inner;
                    return true;
            }

            value = 0d;
            return false;
        }

        private static bool TryGetNumericLiteralValue(NumberExpr number, out double value)
        {
            try
            {
                value = Convert.ToDouble(number.Value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = 0d;
                return false;
            }
        }

        private static string FormatNumberLiteral(object? value)
            => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "0";

        private static string ToBooleanLiteral(bool value)
            => value ? "true" : "false";

        private enum DeclarationKind
        {
            Local,
            Parameter,
            Import
        }

        private enum LiteralKind
        {
            Null,
            Bool,
            Number,
            String,
            Char,
            Array,
            Dictionary
        }

        private sealed class Scope(bool isTopLevel)
        {
            public bool IsTopLevel { get; } = isTopLevel;
            public Dictionary<string, Declaration> Declarations { get; } = new(StringComparer.Ordinal);
            public HashSet<string> Used { get; } = new(StringComparer.Ordinal);
        }

        private sealed record Declaration(string Name, int Line, int Column, DeclarationKind Kind);

        private readonly record struct ComparableLiteral(LiteralKind Kind, object? Value);
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
        private static readonly string[] GlobalPrefixes =
        [
            "# cfgs-disable",
            "# lint:disable"
        ];

        private static readonly string[] LinePrefixes =
        [
            "# cfgs-disable-line",
            "# lint:disable-line"
        ];

        private static readonly string[] NextLinePrefixes =
        [
            "# cfgs-disable-next-line",
            "# lint:disable-next-line"
        ];

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

                if (TryMatchPrefix(comment, NextLinePrefixes, out string? nextLineCodes))
                {
                    foreach (string code in ParseCodes(nextLineCodes))
                        result.AddLine(i + 2, code);
                    continue;
                }

                if (TryMatchPrefix(comment, LinePrefixes, out string? lineCodes))
                {
                    foreach (string code in ParseCodes(lineCodes))
                        result.AddLine(i + 1, code);
                    continue;
                }

                if (TryMatchPrefix(comment, GlobalPrefixes, out string? globalCodes))
                {
                    foreach (string code in ParseCodes(globalCodes))
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

        private static bool TryMatchPrefix(string comment, IEnumerable<string> prefixes, out string suffix)
        {
            foreach (string prefix in prefixes)
            {
                if (comment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    suffix = comment[prefix.Length..];
                    return true;
                }
            }

            suffix = string.Empty;
            return false;
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
