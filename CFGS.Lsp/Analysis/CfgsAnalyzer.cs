using System.Text;
using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;

namespace CFGS.Lsp;

internal sealed class CfgsAnalyzer
{
    public static readonly string[] SemanticTokenTypes =
    [
        "namespace",
        "class",
        "enum",
        "enumMember",
        "function",
        "method",
        "parameter",
        "variable"
    ];

    public static readonly string[] SemanticTokenModifiers =
    [
        "declaration",
        "readonly"
    ];

    private static readonly string[] Keywords =
    [
        "var", "const", "if", "else", "delete", "while", "do", "for", "foreach", "in",
        "break", "continue", "func", "return", "match", "case", "default", "try", "catch",
        "finally", "throw", "class", "enum", "new", "null", "true", "false", "import",
        "export", "namespace", "from", "as", "static", "public", "private", "protected",
        "async", "await", "yield", "out"
    ];

    public CfgsAnalysisResult Analyze(string documentUri, string sourceText, IReadOnlyDictionary<string, string>? openDocumentSources = null)
    {
        string origin = GetDocumentOrigin(documentUri);
        SourceResolver sources = new(documentUri, origin, sourceText, openDocumentSources);
        Dictionary<string, List<CfgsDiagnostic>> diagnosticsByUri = CreateUriMap<List<CfgsDiagnostic>>();

        try
        {
            using CurrentDirectoryScope _ = CurrentDirectoryScope.ForDocument(origin);

            Lexer lexer = new(origin, sourceText);
            Parser parser = new(lexer, _ => { }, loadImportSource: sources.GetOverlaySourceText);
            List<Stmt> ast = parser.Parse();

            Compiler compiler = new(origin);
            compiler.Compile(ast);

            SymbolCollector collector = new(sources, origin, documentUri);
            collector.Collect(ast);
            CfgsSemanticModel semanticModel = new CfgsSemanticModelBuilder(documentUri, origin, sourceText, openDocumentSources).Build(ast);

            EnsureDocumentEntry(diagnosticsByUri, documentUri);
            return new CfgsAnalysisResult(documentUri, origin, sourceText, diagnosticsByUri, collector.DocumentSymbols, collector.AllSymbols, semanticModel);
        }
        catch (LexerException ex)
        {
            AddExceptionDiagnostic(diagnosticsByUri, sources, documentUri, ex.Filename, ex.Line, ex.Column, ex.RawMessage, ex.Category);
        }
        catch (ParserException ex)
        {
            AddExceptionDiagnostic(diagnosticsByUri, sources, documentUri, ex.Filename, ex.Line, ex.Column, ex.RawMessage, ex.Category);
        }
        catch (CompilerException ex)
        {
            AddExceptionDiagnostic(diagnosticsByUri, sources, documentUri, ex.FileSource, ex.Line, ex.Column, ex.RawMessage, ex.Category);
        }
        catch (Exception ex)
        {
            AddExceptionDiagnostic(diagnosticsByUri, sources, documentUri, origin, 1, 1, ex.Message, "SystemError");
        }

        EnsureDocumentEntry(diagnosticsByUri, documentUri);
        return new CfgsAnalysisResult(documentUri, origin, sourceText, diagnosticsByUri, [], [], new CfgsSemanticModel([], new Dictionary<string, CfgsSymbol>(), []));
    }

    public IReadOnlyList<CfgsCompletionItem> GetCompletions(CfgsAnalysisResult result)
    {
        Dictionary<string, CfgsCompletionItem> items = new(StringComparer.Ordinal);

        foreach (string keyword in Keywords)
            items[keyword] = new CfgsCompletionItem(keyword, 14, "keyword");

        foreach (CfgsSymbol symbol in result.AllSymbols)
        {
            if (symbol.IsSynthetic)
                continue;

            if (!items.ContainsKey(symbol.Name))
                items[symbol.Name] = new CfgsCompletionItem(symbol.Name, ToCompletionKind(symbol.Kind), symbol.Detail);
        }

        foreach (CfgsSymbol symbol in result.SemanticModel.Symbols)
        {
            if (symbol.IsSynthetic)
                continue;

            if (!items.ContainsKey(symbol.Name))
                items[symbol.Name] = new CfgsCompletionItem(symbol.Name, ToCompletionKind(symbol.Kind), symbol.Detail);
        }

        return items.Values.OrderBy(static item => item.Label, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<int> GetSemanticTokens(CfgsAnalysisResult result)
    {
        List<CfgsSemanticTokenSpan> spans = [];

        foreach (CfgsResolvedOccurrence occurrence in result.SemanticModel.Occurrences)
        {
            if (!string.Equals(occurrence.Uri, result.DocumentUri, GetPathComparison()))
                continue;

            if (!result.SemanticModel.SymbolsById.TryGetValue(occurrence.SymbolId, out CfgsSymbol? symbol))
                continue;

            if (!TryMapSemanticToken(symbol, occurrence.IsDeclaration, out CfgsSemanticTokenSpan span))
                continue;

            spans.Add(span);
        }

        foreach (CfgsSymbol symbol in result.DocumentSymbols)
        {
            if (!string.Equals(symbol.Uri, result.DocumentUri, GetPathComparison()))
                continue;

            if (symbol.Kind != 3)
                continue;

            if (TryMapSemanticToken(symbol, isDeclaration: true, out CfgsSemanticTokenSpan span))
                spans.Add(span);
        }

        List<CfgsSemanticTokenSpan> ordered = spans
            .GroupBy(static span => (span.Line, span.Character, span.Length, span.TokenType, span.TokenModifiers))
            .Select(static group => group.First())
            .OrderBy(static span => span.Line)
            .ThenBy(static span => span.Character)
            .ThenBy(static span => span.Length)
            .ToList();

        List<int> data = [];
        int previousLine = 0;
        int previousCharacter = 0;
        bool hasPrevious = false;

        foreach (CfgsSemanticTokenSpan span in ordered)
        {
            int deltaLine = hasPrevious ? span.Line - previousLine : span.Line;
            int deltaCharacter = !hasPrevious || deltaLine != 0
                ? span.Character
                : span.Character - previousCharacter;

            data.Add(deltaLine);
            data.Add(deltaCharacter);
            data.Add(span.Length);
            data.Add(span.TokenType);
            data.Add(span.TokenModifiers);

            previousLine = span.Line;
            previousCharacter = span.Character;
            hasPrevious = true;
        }

        return data;
    }

    public IReadOnlyList<CfgsSymbol> FindDefinitions(CfgsAnalysisResult result, int line, int character)
    {
        if (TryFindResolvedSymbol(result, line, character, out CfgsSymbol? resolved))
            return [resolved!];

        string? qualifiedName = TextLocator.GetQualifiedIdentifierAt(result.SourceText, line, character);
        if (!string.IsNullOrWhiteSpace(qualifiedName))
        {
            List<CfgsSymbol> qualifiedMatches =
                result.AllSymbols
                    .Where(symbol => string.Equals(symbol.QualifiedName, qualifiedName, StringComparison.Ordinal))
                    .ToList();

            if (qualifiedMatches.Count > 0)
                return qualifiedMatches;
        }

        string? identifier = TextLocator.GetIdentifierAt(result.SourceText, line, character);
        if (string.IsNullOrWhiteSpace(identifier))
            return [];

        List<CfgsSymbol> sameFileMatches =
            result.AllSymbols
                .Where(symbol =>
                    string.Equals(symbol.Name, identifier, StringComparison.Ordinal) &&
                    string.Equals(symbol.Uri, result.DocumentUri, StringComparison.Ordinal))
                .ToList();

        if (sameFileMatches.Count > 0)
            return sameFileMatches;

        return result.AllSymbols
            .Where(symbol => string.Equals(symbol.Name, identifier, StringComparison.Ordinal))
            .ToList();
    }

    public CfgsSymbol? FindHoverSymbol(CfgsAnalysisResult result, int line, int character)
        => FindDefinitions(result, line, character).FirstOrDefault();

    public CfgsSymbol? ResolveSymbol(CfgsAnalysisResult result, int line, int character)
    {
        if (TryFindResolvedSymbol(result, line, character, out CfgsSymbol? resolved))
            return resolved;

        return FindDefinitions(result, line, character).FirstOrDefault();
    }

    public IReadOnlyList<CfgsSymbol> FindDocumentHighlights(CfgsAnalysisResult result, int line, int character)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        if (symbol is null)
            return [];

        return FindReferences(result, symbol, includeDeclaration: true)
            .Where(reference => string.Equals(reference.Uri, result.DocumentUri, GetPathComparison()))
            .ToList();
    }

    public CfgsPrepareRenameResult? PrepareRename(CfgsAnalysisResult result, int line, int character)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        if (symbol is null || symbol.IsSynthetic)
            return null;

        return new CfgsPrepareRenameResult(symbol.SelectionRange, symbol.Name);
    }

    public IReadOnlyList<CfgsSymbol> FindReferences(CfgsAnalysisResult result, int line, int character, bool includeDeclaration)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        return symbol is null ? [] : FindReferences(result, symbol, includeDeclaration);
    }

    public IReadOnlyList<CfgsSymbol> FindReferences(CfgsAnalysisResult result, CfgsSymbol symbol, bool includeDeclaration)
    {
        CfgsSymbol? resolvedSymbol = FindEquivalentSemanticSymbol(result, symbol);
        if (resolvedSymbol is null || string.IsNullOrWhiteSpace(resolvedSymbol.Id))
            return [];

        IEnumerable<CfgsResolvedOccurrence> occurrences = result.SemanticModel.Occurrences
            .Where(occurrence => string.Equals(occurrence.SymbolId, resolvedSymbol.Id, StringComparison.Ordinal));

        if (!includeDeclaration)
            occurrences = occurrences.Where(static occurrence => !occurrence.IsDeclaration);

        return occurrences
            .Select(occurrence => new CfgsSymbol(
                resolvedSymbol.Name,
                resolvedSymbol.QualifiedName,
                resolvedSymbol.Kind,
                occurrence.Uri,
                resolvedSymbol.OriginFile,
                occurrence.Range,
                occurrence.Range,
                resolvedSymbol.Detail,
                resolvedSymbol.HoverText,
                [],
                resolvedSymbol.IsSynthetic,
                resolvedSymbol.Id,
                resolvedSymbol.Signature))
            .ToList();
    }

    public CfgsSignatureHelpResult? FindSignatureHelp(CfgsAnalysisResult result, int line, int character)
    {
        SignatureRequest? request = SignatureLocator.TryFindSignatureRequest(result.SourceText, line, character);
        if (request is null)
            return null;

        if (!TryFindResolvedSymbol(result, request.Value.TargetLine, request.Value.TargetCharacter, out CfgsSymbol? symbol))
            return null;

        if (symbol?.Signature is null)
            return null;

        int activeParameter = Math.Min(request.Value.ActiveParameter, Math.Max(symbol.Signature.Parameters.Count - 1, 0));
        return new CfgsSignatureHelpResult([symbol.Signature], 0, activeParameter);
    }

    public CfgsRenameResult? RenameSymbol(CfgsAnalysisResult result, int line, int character, string newName)
    {
        if (!IsValidIdentifier(newName))
            return null;

        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        return symbol is null ? null : RenameSymbol(result, symbol, newName);
    }

    public CfgsRenameResult? RenameSymbol(CfgsAnalysisResult result, CfgsSymbol symbol, string newName)
    {
        if (!IsValidIdentifier(newName))
            return null;

        CfgsSymbol? resolvedSymbol = FindEquivalentSemanticSymbol(result, symbol);
        if (resolvedSymbol is null || string.IsNullOrWhiteSpace(resolvedSymbol.Id))
            return null;

        List<CfgsTextEdit> edits = result.SemanticModel.Occurrences
            .Where(occurrence => string.Equals(occurrence.SymbolId, resolvedSymbol.Id, StringComparison.Ordinal))
            .Select(occurrence => new CfgsTextEdit(occurrence.Uri, occurrence.Range, newName))
            .ToList();

        return edits.Count == 0 ? null : new CfgsRenameResult(edits);
    }

    public bool SupportsWorkspaceSymbolMatching(CfgsSymbol symbol)
        => !symbol.IsSynthetic &&
           !string.IsNullOrWhiteSpace(symbol.QualifiedName) &&
           !symbol.QualifiedName.Contains('#', StringComparison.Ordinal);

    private static CfgsSymbol? FindEquivalentSemanticSymbol(CfgsAnalysisResult result, CfgsSymbol symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol.Id) && result.SemanticModel.SymbolsById.TryGetValue(symbol.Id!, out CfgsSymbol? exact))
            return exact;

        CfgsSymbolIdentity identity = CfgsSymbolIdentity.From(symbol);
        return result.SemanticModel.Symbols.FirstOrDefault(candidate => identity.Matches(candidate));
    }

    private static bool TryFindResolvedSymbol(CfgsAnalysisResult result, int line, int character, out CfgsSymbol? symbol)
    {
        foreach (CfgsResolvedOccurrence occurrence in result.SemanticModel.Occurrences)
        {
            if (!string.Equals(occurrence.Uri, result.DocumentUri, GetPathComparison()))
                continue;

            if (!Contains(occurrence.Range, line, character))
                continue;

            if (result.SemanticModel.SymbolsById.TryGetValue(occurrence.SymbolId, out symbol))
                return true;
        }

        symbol = null;
        return false;
    }

    private static bool Contains(RangeInfo range, int line, int character)
    {
        if (line < range.Start.Line || line > range.End.Line)
            return false;

        if (line == range.Start.Line && character < range.Start.Character)
            return false;

        if (line == range.End.Line && character > range.End.Character)
            return false;

        return true;
    }

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsValidIdentifier(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return false;

        if (!char.IsLetter(newName[0]) && newName[0] != '_')
            return false;

        return newName.All(static ch => char.IsLetterOrDigit(ch) || ch == '_');
    }

    private static int ToCompletionKind(int symbolKind)
        => symbolKind switch
        {
            5 => 7,
            6 => 2,
            10 => 13,
            12 => 3,
            13 => 6,
            14 => 21,
            22 => 20,
            _ => 1
        };

    private static bool TryMapSemanticToken(CfgsSymbol symbol, bool isDeclaration, out CfgsSemanticTokenSpan span)
    {
        int tokenType = symbol.Kind switch
        {
            3 => 0,
            5 => 1,
            10 => 2,
            22 => 3,
            12 => 4,
            6 => 5,
            13 when IsParameterSymbol(symbol) => 6,
            13 => 7,
            14 => 7,
            _ => -1
        };

        if (tokenType < 0)
        {
            span = default;
            return false;
        }

        int modifiers = 0;
        if (isDeclaration)
            modifiers |= 1 << 0;
        if (symbol.Kind == 14)
            modifiers |= 1 << 1;

        span = new CfgsSemanticTokenSpan(
            symbol.SelectionRange.Start.Line,
            symbol.SelectionRange.Start.Character,
            Math.Max(symbol.SelectionRange.End.Character - symbol.SelectionRange.Start.Character, 1),
            tokenType,
            modifiers);

        return true;
    }

    private static bool IsParameterSymbol(CfgsSymbol symbol)
        => symbol.Detail.Contains("parameter", StringComparison.Ordinal);

    private static void EnsureDocumentEntry(Dictionary<string, List<CfgsDiagnostic>> diagnosticsByUri, string documentUri)
    {
        if (!diagnosticsByUri.ContainsKey(documentUri))
            diagnosticsByUri[documentUri] = [];
    }

    private static void AddExceptionDiagnostic(
        Dictionary<string, List<CfgsDiagnostic>> diagnosticsByUri,
        SourceResolver sources,
        string fallbackDocumentUri,
        string? origin,
        int line,
        int column,
        string message,
        string code)
    {
        string uri = sources.ToDocumentUri(origin, fallbackDocumentUri);
        RangeInfo range = TextLocator.CreateRange(
            sources.TryGetSource(origin, out string? text) ? text : null,
            line,
            column,
            null);

        if (!diagnosticsByUri.TryGetValue(uri, out List<CfgsDiagnostic>? list))
        {
            list = [];
            diagnosticsByUri[uri] = list;
        }

        list.Add(new CfgsDiagnostic(range, message.Trim(), 1, "cfgs", code));
    }

    private static string GetDocumentOrigin(string documentUri)
    {
        string? path = SourceResolver.TryGetLocalPath(documentUri);
        return path ?? documentUri;
    }

    private static Dictionary<string, T> CreateUriMap<T>()
        => OperatingSystem.IsWindows()
            ? new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, T>(StringComparer.Ordinal);

    private sealed class SymbolCollector
    {
        private readonly SourceResolver _sources;
        private readonly string _documentOrigin;
        private readonly string _documentUri;
        private readonly HashSet<(int Line, int Column, string RootName)> _syntheticNamespaceRoots = new();

        public SymbolCollector(SourceResolver sources, string documentOrigin, string documentUri)
        {
            _sources = sources;
            _documentOrigin = NormalizeOrigin(documentOrigin);
            _documentUri = documentUri;
        }

        public List<CfgsSymbol> DocumentSymbols { get; } = [];

        public List<CfgsSymbol> AllSymbols { get; } = [];

        public void Collect(IEnumerable<Stmt> statements)
        {
            foreach (Stmt stmt in statements)
            {
                if (stmt is BlockStmt block && block.IsNamespaceScope && TryCreateNamespaceSymbol(block, out CfgsSymbol? namespaceSymbol))
                {
                    AllSymbols.Add(namespaceSymbol!);
                    if (IsCurrentDocument(block.OriginFile))
                        DocumentSymbols.Add(namespaceSymbol!);
                    continue;
                }

                if (stmt is VarDecl syntheticRoot && IsSyntheticNamespaceRoot(syntheticRoot))
                    continue;

                if (TryCreateSymbol(stmt, null, out CfgsSymbol? symbol))
                {
                    AllSymbols.Add(symbol!);
                    if (IsCurrentDocument(symbol!.OriginFile))
                        DocumentSymbols.Add(symbol);
                }
            }
        }

        private bool IsSyntheticNamespaceRoot(VarDecl decl)
            => decl.Value is DictExpr &&
               _syntheticNamespaceRoots.Contains((decl.Line, decl.Col, decl.Name));

        private bool TryCreateNamespaceSymbol(BlockStmt block, out CfgsSymbol? namespaceSymbol)
        {
            namespaceSymbol = null;

            if (!TryExtractNamespaceInfo(block, out string? namespacePath, out string? tempName))
                return false;

            string rootName = namespacePath!.Split('.')[0];
            _syntheticNamespaceRoots.Add((block.Line, block.Col, rootName));

            List<CfgsSymbol> children = [];
            foreach (Stmt stmt in EnumerateNamespaceBodyStatements(block, tempName!))
            {
                if (TryCreateSymbol(stmt, namespacePath, out CfgsSymbol? child))
                    children.Add(child!);
            }

            string? source = _sources.GetSourceText(block.OriginFile);
            RangeInfo selectionRange = TextLocator.CreateRange(source, block.Line, block.Col, namespacePath);
            namespaceSymbol = new CfgsSymbol(
                namespacePath,
                namespacePath,
                3,
                _sources.ToDocumentUri(block.OriginFile, _documentUri),
                block.OriginFile,
                selectionRange,
                selectionRange,
                "namespace",
                $"```cfgs\nnamespace {namespacePath}\n```",
                children,
                IsSynthetic: false);

            foreach (CfgsSymbol child in children)
                AllSymbols.Add(child);

            return true;
        }

        private IEnumerable<Stmt> EnumerateNamespaceBodyStatements(BlockStmt block, string tempName)
        {
            foreach (Stmt stmt in block.Statements.Skip(1))
            {
                if (stmt is AssignExprStmt assign &&
                    assign.Target is IndexExpr target &&
                    target.Target is VarExpr varExpr &&
                    string.Equals(varExpr.Name, tempName, StringComparison.Ordinal) &&
                    assign.Value is VarExpr)
                {
                    continue;
                }

                yield return stmt;
            }
        }

        private bool TryExtractNamespaceInfo(BlockStmt block, out string? namespacePath, out string? tempName)
        {
            namespacePath = null;
            tempName = null;

            if (block.Statements.FirstOrDefault() is not VarDecl tempDecl)
                return false;

            tempName = tempDecl.Name;
            namespacePath = TextLocator.TryExtractQualifiedPath(tempDecl.Value);
            return !string.IsNullOrWhiteSpace(namespacePath);
        }

        private bool TryCreateSymbol(Stmt stmt, string? containerName, out CfgsSymbol? symbol)
        {
            symbol = null;
            Stmt effective = stmt is ExportStmt export ? export.Inner : stmt;

            switch (effective)
            {
                case FuncDeclStmt func:
                    symbol = CreateFunctionSymbol(func, containerName, isMethod: false);
                    return true;

                case ClassDeclStmt @class:
                    symbol = CreateClassSymbol(@class, containerName);
                    return true;

                case EnumDeclStmt @enum:
                    symbol = CreateEnumSymbol(@enum, containerName);
                    return true;

                case VarDecl variable:
                    symbol = CreateSimpleSymbol(variable.Name, containerName, variable.Line, variable.Col, variable.OriginFile, 13, $"var {variable.Name}");
                    return true;

                case ConstDecl constant:
                    symbol = CreateSimpleSymbol(constant.Name, containerName, constant.Line, constant.Col, constant.OriginFile, 14, $"const {constant.Name}");
                    return true;

                default:
                    return false;
            }
        }

        private CfgsSymbol CreateClassSymbol(ClassDeclStmt classDecl, string? containerName)
        {
            string qualifiedName = Qualify(containerName, classDecl.Name);
            List<CfgsSymbol> children = [];

            children.AddRange(classDecl.Methods.Select(method => CreateFunctionSymbol(method, qualifiedName, isMethod: true)));
            children.AddRange(classDecl.StaticMethods.Select(method => CreateFunctionSymbol(method, qualifiedName, isMethod: true)));
            children.AddRange(classDecl.Enums.Select(@enum => CreateEnumSymbol(@enum, qualifiedName)));
            children.AddRange(classDecl.NestedClasses.Select(nested => CreateClassSymbol(nested, qualifiedName)));

            string header = $"class {classDecl.Name}({string.Join(", ", classDecl.Parameters)})";
            if (!string.IsNullOrWhiteSpace(classDecl.BaseName))
                header += $" : {classDecl.BaseName}";

            return CreateSymbol(
                classDecl.Name,
                qualifiedName,
                classDecl.Line,
                classDecl.Col,
                classDecl.OriginFile,
                5,
                header,
                header,
                children);
        }

        private CfgsSymbol CreateEnumSymbol(EnumDeclStmt enumDecl, string? containerName)
        {
            string qualifiedName = Qualify(containerName, enumDecl.Name);
            List<CfgsSymbol> children =
                enumDecl.Members
                    .Select(member => CreateSimpleSymbol(member.Name, qualifiedName, member.Line, member.Col, member.OriginFile, 22, $"enum member {member.Name}"))
                    .ToList();

            return CreateSymbol(
                enumDecl.Name,
                qualifiedName,
                enumDecl.Line,
                enumDecl.Col,
                enumDecl.OriginFile,
                10,
                $"enum {enumDecl.Name}",
                $"enum {enumDecl.Name}",
                children);
        }

        private CfgsSymbol CreateFunctionSymbol(FuncDeclStmt func, string? containerName, bool isMethod)
        {
            string qualifiedName = Qualify(containerName, func.Name);
            string prefix = func.IsAsync ? "async func" : "func";
            string signature = $"{prefix} {func.Name}({string.Join(", ", func.Parameters)})";
            return CreateSymbol(
                func.Name,
                qualifiedName,
                func.Line,
                func.Col,
                func.OriginFile,
                isMethod ? 6 : 12,
                signature,
                signature,
                []);
        }

        private CfgsSymbol CreateSimpleSymbol(string name, string? containerName, int line, int column, string originFile, int kind, string detail)
            => CreateSymbol(name, Qualify(containerName, name), line, column, originFile, kind, detail, detail, []);

        private CfgsSymbol CreateSymbol(
            string name,
            string qualifiedName,
            int line,
            int column,
            string originFile,
            int kind,
            string detail,
            string hoverHeader,
            List<CfgsSymbol> children)
        {
            string? source = _sources.GetSourceText(originFile);
            RangeInfo selectionRange = TextLocator.CreateRange(source, line, column, name);
            string uri = _sources.ToDocumentUri(originFile, _documentUri);

            return new CfgsSymbol(
                name,
                qualifiedName,
                kind,
                uri,
                originFile,
                selectionRange,
                selectionRange,
                detail,
                CreateHoverText(hoverHeader, uri),
                children,
                IsSynthetic: false);
        }

        private static string CreateHoverText(string header, string uri)
        {
            StringBuilder sb = new();
            sb.AppendLine("```cfgs");
            sb.AppendLine(header);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.Append("Defined in `");
            sb.Append(uri);
            sb.Append('`');
            return sb.ToString();
        }

        private bool IsCurrentDocument(string originFile)
            => string.Equals(NormalizeOrigin(originFile), _documentOrigin, GetPathComparison());

        private static string Qualify(string? containerName, string name)
            => string.IsNullOrWhiteSpace(containerName) ? name : $"{containerName}.{name}";

        private static StringComparison GetPathComparison()
            => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static string NormalizeOrigin(string origin)
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                return Path.GetFullPath(uri.LocalPath);

            if (Path.IsPathFullyQualified(origin))
                return Path.GetFullPath(origin);

            return origin;
        }
    }

    private sealed class SourceResolver
    {
        private readonly string _documentUri;
        private readonly string _documentOrigin;
        private readonly Dictionary<string, string> _sourceCache = OperatingSystem.IsWindows()
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.Ordinal);

        public SourceResolver(string documentUri, string documentOrigin, string documentText, IReadOnlyDictionary<string, string>? openDocumentSources)
        {
            _documentUri = documentUri;
            _documentOrigin = NormalizeOrigin(documentOrigin);
            _sourceCache[_documentOrigin] = documentText;
            _sourceCache[_documentUri] = documentText;
            RegisterOverlaySources(openDocumentSources);
        }

        public string? GetOverlaySourceText(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return null;

            string normalized = NormalizeOrigin(origin);
            return _sourceCache.TryGetValue(normalized, out string? cached) ? cached : null;
        }

        public bool TryGetSource(string? origin, out string? text)
        {
            text = GetSourceText(origin);
            return text is not null;
        }

        public string? GetSourceText(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return null;

            string normalized = NormalizeOrigin(origin);
            if (_sourceCache.TryGetValue(normalized, out string? cached))
                return cached;

            if (File.Exists(normalized))
            {
                string source = File.ReadAllText(normalized);
                _sourceCache[normalized] = source;
                return source;
            }

            return null;
        }

        public string ToDocumentUri(string? origin, string fallbackDocumentUri)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return fallbackDocumentUri;

            string normalized = NormalizeOrigin(origin);
            if (string.Equals(normalized, _documentOrigin, GetPathComparison()))
                return _documentUri;

            if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
                return uri.IsFile ? new Uri(Path.GetFullPath(uri.LocalPath)).AbsoluteUri : uri.AbsoluteUri;

            if (Path.IsPathFullyQualified(origin) || File.Exists(origin))
                return new Uri(Path.GetFullPath(origin)).AbsoluteUri;

            return fallbackDocumentUri;
        }

        public static string? TryGetLocalPath(string uri)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
            {
                if (parsed.IsFile)
                    return Path.GetFullPath(parsed.LocalPath);

                return null;
            }

            if (Path.IsPathFullyQualified(uri))
                return Path.GetFullPath(uri);

            return null;
        }

        private static string NormalizeOrigin(string origin)
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                return Path.GetFullPath(uri.LocalPath);

            if (Path.IsPathFullyQualified(origin))
                return Path.GetFullPath(origin);

            return origin;
        }

        private void RegisterOverlaySources(IReadOnlyDictionary<string, string>? openDocumentSources)
        {
            if (openDocumentSources is null)
                return;

            foreach ((string key, string value) in openDocumentSources)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                _sourceCache[NormalizeOrigin(key)] = value;
            }
        }

        private static StringComparison GetPathComparison()
            => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _previousDirectory;

        private CurrentDirectoryScope(string previousDirectory)
        {
            _previousDirectory = previousDirectory;
        }

        public static CurrentDirectoryScope ForDocument(string origin)
        {
            string previousDirectory = Environment.CurrentDirectory;
            string? localPath = SourceResolver.TryGetLocalPath(origin) ?? (Path.IsPathFullyQualified(origin) ? Path.GetFullPath(origin) : null);
            string? nextDirectory = localPath is null ? null : Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(nextDirectory) && Directory.Exists(nextDirectory))
                Environment.CurrentDirectory = nextDirectory;

            return new CurrentDirectoryScope(previousDirectory);
        }

        public void Dispose()
        {
            Environment.CurrentDirectory = _previousDirectory;
        }
    }
}

internal sealed record CfgsAnalysisResult(
    string DocumentUri,
    string Origin,
    string SourceText,
    IReadOnlyDictionary<string, List<CfgsDiagnostic>> DiagnosticsByUri,
    IReadOnlyList<CfgsSymbol> DocumentSymbols,
    IReadOnlyList<CfgsSymbol> AllSymbols,
    CfgsSemanticModel SemanticModel);

internal sealed record CfgsDiagnostic(
    RangeInfo Range,
    string Message,
    int Severity,
    string Source,
    string Code);

internal sealed record CfgsCompletionItem(
    string Label,
    int Kind,
    string Detail);

internal readonly record struct CfgsSemanticTokenSpan(
    int Line,
    int Character,
    int Length,
    int TokenType,
    int TokenModifiers);

internal sealed record CfgsTextEdit(
    string Uri,
    RangeInfo Range,
    string NewText);

internal sealed record CfgsRenameResult(
    IReadOnlyList<CfgsTextEdit> Edits);

internal sealed record CfgsPrepareRenameResult(
    RangeInfo Range,
    string Placeholder);

internal readonly record struct CfgsSymbolIdentity(
    string QualifiedName,
    int Kind,
    string Uri,
    RangeInfo SelectionRange)
{
    public static CfgsSymbolIdentity From(CfgsSymbol symbol)
        => new(symbol.QualifiedName, symbol.Kind, symbol.Uri, symbol.SelectionRange);

    public bool Matches(CfgsSymbol symbol)
        => Kind == symbol.Kind &&
           string.Equals(QualifiedName, symbol.QualifiedName, StringComparison.Ordinal) &&
           string.Equals(Uri, symbol.Uri, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
           SelectionRange.Equals(symbol.SelectionRange);
}

internal sealed record CfgsSymbol(
    string Name,
    string QualifiedName,
    int Kind,
    string Uri,
    string OriginFile,
    RangeInfo Range,
    RangeInfo SelectionRange,
    string Detail,
    string HoverText,
    IReadOnlyList<CfgsSymbol> Children,
    bool IsSynthetic,
    string? Id = null,
    CfgsSignature? Signature = null);

internal readonly record struct PositionInfo(int Line, int Character);

internal readonly record struct RangeInfo(PositionInfo Start, PositionInfo End);

internal static class TextLocator
{
    public static RangeInfo CreateRange(string? sourceText, int line, int column, string? token)
    {
        int startLine = Math.Max(line - 1, 0);
        int startCharacter = Math.Max(column - 1, 0);
        int endCharacter = startCharacter + Math.Max(token?.Length ?? 1, 1);

        if (sourceText is null)
            return new RangeInfo(new PositionInfo(startLine, startCharacter), new PositionInfo(startLine, endCharacter));

        string[] lines = SplitLines(sourceText);
        if (startLine >= lines.Length)
            return new RangeInfo(new PositionInfo(startLine, startCharacter), new PositionInfo(startLine, endCharacter));

        string lineText = lines[startLine];
        if (!string.IsNullOrWhiteSpace(token))
        {
            int found = FindIdentifier(lineText, token, startCharacter);
            if (found >= 0)
            {
                startCharacter = found;
                endCharacter = found + token.Length;
            }
            else
            {
                (int lineIndex, int charIndex)? nearby = FindIdentifierNearby(lines, startLine, token);
                if (nearby is not null)
                {
                    startLine = nearby.Value.lineIndex;
                    lineText = lines[startLine];
                    startCharacter = nearby.Value.charIndex;
                    endCharacter = startCharacter + token.Length;
                }
            }
        }

        endCharacter = Math.Min(Math.Max(endCharacter, startCharacter + 1), Math.Max(lineText.Length, startCharacter + 1));
        return new RangeInfo(new PositionInfo(startLine, startCharacter), new PositionInfo(startLine, endCharacter));
    }

    public static string? GetIdentifierAt(string sourceText, int line, int character)
    {
        if (!TryGetLine(sourceText, line, out string lineText))
            return null;

        int index = ClampIndex(lineText, character);
        if (index == lineText.Length && index > 0)
            index--;

        if (!IsIdentifierChar(At(lineText, index)) && index > 0 && IsIdentifierChar(lineText[index - 1]))
            index--;

        if (!IsIdentifierChar(At(lineText, index)))
            return null;

        int start = index;
        while (start > 0 && IsIdentifierChar(lineText[start - 1]))
            start--;

        int end = index;
        while (end < lineText.Length && IsIdentifierChar(lineText[end]))
            end++;

        return lineText[start..end];
    }

    public static string? GetQualifiedIdentifierAt(string sourceText, int line, int character)
    {
        if (!TryGetLine(sourceText, line, out string lineText))
            return null;

        int index = ClampIndex(lineText, character);
        if (index == lineText.Length && index > 0)
            index--;

        char current = At(lineText, index);
        if (!IsIdentifierChar(current) && current != '.' && index > 0)
            index--;

        if (!IsIdentifierChar(At(lineText, index)) && At(lineText, index) != '.')
            return null;

        int start = index;
        while (start > 0 && IsQualifiedIdentifierChar(lineText[start - 1]))
            start--;

        int end = index;
        while (end < lineText.Length && IsQualifiedIdentifierChar(lineText[end]))
            end++;

        string candidate = lineText[start..end].Trim('.');
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        string[] parts = candidate.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.All(static part => part.All(IsIdentifierChar)) ? string.Join(".", parts) : null;
    }

    public static string? TryExtractQualifiedPath(Expr? expr)
    {
        return expr switch
        {
            VarExpr varExpr => varExpr.Name,
            IndexExpr indexExpr when indexExpr.Target is not null && indexExpr.Index is StringExpr stringExpr
                => AppendQualifiedPath(TryExtractQualifiedPath(indexExpr.Target), stringExpr.Value),
            _ => null
        };
    }

    private static string? AppendQualifiedPath(string? left, string right)
        => string.IsNullOrWhiteSpace(left) ? null : $"{left}.{right}";

    private static bool TryGetLine(string sourceText, int line, out string lineText)
    {
        string[] lines = SplitLines(sourceText);
        if (line < 0 || line >= lines.Length)
        {
            lineText = string.Empty;
            return false;
        }

        lineText = lines[line];
        return true;
    }

    private static string[] SplitLines(string sourceText)
        => sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static int FindIdentifier(string lineText, string token, int minIndex)
    {
        int searchIndex = Math.Min(Math.Max(minIndex, 0), lineText.Length);
        int found = lineText.IndexOf(token, searchIndex, StringComparison.Ordinal);
        if (IsValidIdentifierMatch(lineText, found, token.Length))
            return found;

        found = lineText.IndexOf(token, StringComparison.Ordinal);
        return IsValidIdentifierMatch(lineText, found, token.Length) ? found : -1;
    }

    private static bool IsValidIdentifierMatch(string lineText, int start, int length)
    {
        if (start < 0)
            return false;

        bool leftOk = start == 0 || !IsIdentifierChar(lineText[start - 1]);
        int end = start + length;
        bool rightOk = end >= lineText.Length || !IsIdentifierChar(lineText[end]);
        return leftOk && rightOk;
    }

    private static (int lineIndex, int charIndex)? FindIdentifierNearby(string[] lines, int expectedLine, string token)
    {
        int bestDistance = int.MaxValue;
        (int lineIndex, int charIndex)? best = null;

        for (int i = Math.Max(0, expectedLine - 2); i <= Math.Min(lines.Length - 1, expectedLine + 2); i++)
        {
            int found = FindIdentifier(lines[i], token, 0);
            if (found < 0)
                continue;

            int distance = Math.Abs(i - expectedLine);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (i, found);
            }
        }

        return best;
    }

    private static int ClampIndex(string text, int index)
        => Math.Min(Math.Max(index, 0), text.Length);

    private static char At(string text, int index)
        => index < 0 || index >= text.Length ? '\0' : text[index];

    private static bool IsQualifiedIdentifierChar(char ch)
        => IsIdentifierChar(ch) || ch == '.';

    private static bool IsIdentifierChar(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_';
}
