using System.Text;
using System.Text.RegularExpressions;
using CFGS_VM.Analytic;
using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Lowering;
using CFGS_VM.Analytic.Modules;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;

namespace CFGS.Lsp;

internal sealed class CfgsAnalyzer
{
    private static readonly HashSet<string> NonSymbolKeywords = new(StringComparer.Ordinal)
    {
        "var", "const", "if", "else", "delete", "while", "do", "for", "foreach", "in", "is",
        "break", "continue", "func", "return", "match", "case", "default", "try", "catch",
        "finally", "throw", "class", "interface", "enum", "new", "null", "true", "false", "import",
        "export", "namespace", "from", "as", "static", "public", "private", "protected",
        "async", "await", "yield", "out", "using", "defer", "override"
    };

    public static readonly string[] SemanticTokenTypes =
    [
        "namespace",
        "class",
        "interface",
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
        "readonly",
        "static",
        "async",
        "defaultLibrary"
    ];

    private static readonly string[] Keywords =
    [
        "var", "const", "if", "else", "delete", "while", "do", "for", "foreach", "in", "is",
        "break", "continue", "func", "return", "match", "case", "default", "try", "catch",
        "finally", "throw", "class", "interface", "enum", "new", "null", "true", "false", "import",
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
            FrontendPipeline frontendPipeline = new(
                loadPluginDll: _ => { },
                loadImportSource: sources.GetOverlaySourceText,
                workingDirectory: FrontendPipeline.TryGetWorkingDirectory(origin));
            List<Stmt> ast = frontendPipeline.BuildLoweredAst(origin, sourceText);

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
        return new CfgsAnalysisResult(documentUri, origin, sourceText, diagnosticsByUri, [], [], new CfgsSemanticModel([], new Dictionary<string, CfgsSymbol>(), [], [], []));
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
        if (TryGetNonSymbolKeywordAt(result.SourceText, line, character, out _))
            return [];

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
        if (TryGetNonSymbolKeywordAt(result.SourceText, line, character, out _))
            return null;

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
                resolvedSymbol.Signature,
                resolvedSymbol.ValueTypeQualifiedName,
                resolvedSymbol.CallTargetSymbolId))
            .ToList();
    }

    public IReadOnlyList<CfgsSymbol> FindWorkspaceSymbols(CfgsAnalysisResult result, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return result.AllSymbols
            .Concat(result.SemanticModel.Symbols)
            .Where(symbol => !symbol.IsSynthetic &&
                             !string.IsNullOrWhiteSpace(symbol.Name) &&
                             symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .GroupBy(symbol => (symbol.QualifiedName, symbol.Uri), StringTupleComparer.Instance)
            .Select(static group => group.First())
            .OrderBy(static symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<CfgsSymbol> FindTypeDefinition(CfgsAnalysisResult result, int line, int character)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        if (symbol is null)
            return [];

        string? typeQualifiedName = FindSymbolValueType(result, symbol);
        if (string.IsNullOrWhiteSpace(typeQualifiedName))
            return [];

        return result.SemanticModel.Symbols
            .Where(s => string.Equals(s.QualifiedName, typeQualifiedName, StringComparison.Ordinal) && s.Kind is 5 or 10 or 11)
            .Take(1)
            .Concat(result.AllSymbols
                .Where(s => string.Equals(s.QualifiedName, typeQualifiedName, StringComparison.Ordinal) && s.Kind is 5 or 10 or 11))
            .GroupBy(s => s.QualifiedName, StringComparer.Ordinal)
            .Select(static g => g.First())
            .ToList();
    }

    public IReadOnlyList<CfgsSymbol> FindImplementations(CfgsAnalysisResult result, int line, int character)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        return symbol is null ? [] : FindImplementations(result, symbol);
    }

    public IReadOnlyList<CfgsSymbol> FindImplementations(CfgsAnalysisResult result, CfgsSymbol symbol)
    {
        CfgsSymbol? resolvedSymbol = FindEquivalentSemanticSymbol(result, symbol) ?? symbol;

        if (resolvedSymbol.Kind is 5 or 11)
            return FindTypeImplementations(result, resolvedSymbol);

        if (resolvedSymbol.Kind == 6)
        {
            string? containingTypeQualifiedName = TryGetContainingTypeQualifiedName(resolvedSymbol.QualifiedName);
            if (!string.IsNullOrWhiteSpace(containingTypeQualifiedName))
            {
                CfgsSymbol? containingType = result.AllSymbols
                    .Concat(result.SemanticModel.Symbols)
                    .FirstOrDefault(s =>
                        string.Equals(s.QualifiedName, containingTypeQualifiedName, StringComparison.Ordinal) &&
                        s.Kind is 5 or 11);

                if (containingType is not null)
                {
                    List<CfgsSymbol> members = FindTypeImplementations(result, containingType)
                        .SelectMany(candidateType => result.AllSymbols
                            .Concat(result.SemanticModel.Symbols)
                            .Where(s =>
                                s.Kind == 6 &&
                                !s.IsSynthetic &&
                                string.Equals(
                                    s.QualifiedName,
                                    $"{candidateType.QualifiedName}.{resolvedSymbol.Name}",
                                    StringComparison.Ordinal)))
                        .GroupBy(s => s.QualifiedName, StringComparer.Ordinal)
                        .Select(static g => g.First())
                        .ToList();

                    if (members.Count > 0)
                        return members;
                }
            }

            string methodName = resolvedSymbol.Name;
            return result.AllSymbols
                .Concat(result.SemanticModel.Symbols)
                .Where(s => s.Kind == 6 && !s.IsSynthetic &&
                            string.Equals(s.Name, methodName, StringComparison.Ordinal) &&
                            !string.Equals(s.QualifiedName, resolvedSymbol.QualifiedName, StringComparison.Ordinal))
                .GroupBy(s => s.QualifiedName, StringComparer.Ordinal)
                .Select(static g => g.First())
                .ToList();
        }

        return [];
    }

    public CfgsCallHierarchyItem? PrepareCallHierarchy(CfgsAnalysisResult result, int line, int character)
    {
        CfgsSymbol? symbol = ResolveSymbol(result, line, character);
        symbol = ResolveCallTargetSymbol(result, symbol);
        if (symbol is null || symbol.IsSynthetic || symbol.Kind is not (6 or 12))
        {
            if (!TryFindResolvedCallableSymbol(result, line, character, static s => !s.IsSynthetic && s.Kind is 6 or 12, out symbol))
                return null;
        }

        if (symbol is null)
            return null;

        return new CfgsCallHierarchyItem(symbol.Name, symbol.Kind, symbol.Uri, symbol.Range, symbol.SelectionRange, symbol.Detail);
    }

    public IReadOnlyList<CfgsCallHierarchyIncomingCall> FindIncomingCalls(CfgsAnalysisResult result, CfgsCallHierarchyItem item)
    {
        CfgsSymbol? target = result.SemanticModel.Symbols
            .FirstOrDefault(s => string.Equals(s.Name, item.Name, StringComparison.Ordinal) &&
                                 string.Equals(s.Uri, item.Uri, GetPathComparison()) &&
                                 s.SelectionRange.Equals(item.SelectionRange) &&
                                 s.Kind is 6 or 12);

        if (target is null || string.IsNullOrWhiteSpace(target.Id))
            return [];

        Dictionary<string, (CfgsSymbol Caller, List<RangeInfo> Ranges)> callers = new(StringComparer.Ordinal);
        foreach (CfgsResolvedCallSite callSite in GetResolvedCallSites(result))
        {
            IReadOnlyList<CfgsSymbol> calledSymbols = ResolveCallSiteSymbols(result, callSite, static s => s.Kind is 6 or 12);
            if (!calledSymbols.Any(calledSymbol => string.Equals(calledSymbol.Id, target.Id, StringComparison.Ordinal)))
                continue;

            CfgsSymbol? caller = FindEnclosingFunction(result, callSite.Uri, callSite.Range.Start.Line, callSite.Range.Start.Character);
            if (caller is null)
                continue;

            string key = $"{caller.Uri}|{caller.QualifiedName}";
            if (!callers.TryGetValue(key, out var entry))
            {
                entry = (caller, []);
                callers[key] = entry;
            }
            if (!entry.Ranges.Contains(callSite.Range))
                entry.Ranges.Add(callSite.Range);
        }

        return callers.Values
            .Select(entry => new CfgsCallHierarchyIncomingCall(
                new CfgsCallHierarchyItem(entry.Caller.Name, entry.Caller.Kind, entry.Caller.Uri, entry.Caller.Range, entry.Caller.SelectionRange, entry.Caller.Detail),
                entry.Ranges))
            .ToList();
    }

    public IReadOnlyList<CfgsCallHierarchyOutgoingCall> FindOutgoingCalls(CfgsAnalysisResult result, CfgsCallHierarchyItem item)
    {
        CfgsSymbol? source = result.SemanticModel.Symbols
            .FirstOrDefault(s => string.Equals(s.Name, item.Name, StringComparison.Ordinal) &&
                                 string.Equals(s.Uri, item.Uri, GetPathComparison()) &&
                                 s.SelectionRange.Equals(item.SelectionRange) &&
                                 s.Kind is 6 or 12);

        if (source is null || string.IsNullOrWhiteSpace(source.Id))
            return [];

        CfgsResolvedOccurrence? decl = result.SemanticModel.Occurrences
            .FirstOrDefault(o => string.Equals(o.SymbolId, source.Id, StringComparison.Ordinal) && o.IsDeclaration);

        if (decl is null)
            return [];

        Dictionary<string, (CfgsSymbol Callee, List<RangeInfo> Ranges)> callees = new(StringComparer.Ordinal);
        foreach (CfgsResolvedCallSite callSite in GetResolvedCallSites(result))
        {
            if (!string.Equals(callSite.Uri, decl.Uri, GetPathComparison()))
                continue;

            CfgsSymbol? enclosing = FindEnclosingFunction(result, callSite.Uri, callSite.Range.Start.Line, callSite.Range.Start.Character);
            if (enclosing is null || !string.Equals(enclosing.Id, source.Id, StringComparison.Ordinal))
                continue;

            foreach (CfgsSymbol calledSymbol in ResolveCallSiteSymbols(result, callSite, static s => s.Kind is 6 or 12))
            {
                string key = $"{calledSymbol.Uri}|{calledSymbol.QualifiedName}";
                if (!callees.TryGetValue(key, out var entry))
                {
                    entry = (calledSymbol, []);
                    callees[key] = entry;
                }

                if (!entry.Ranges.Contains(callSite.Range))
                    entry.Ranges.Add(callSite.Range);
            }

        }

        return callees.Values
            .Select(entry => new CfgsCallHierarchyOutgoingCall(
                new CfgsCallHierarchyItem(entry.Callee.Name, entry.Callee.Kind, entry.Callee.Uri, entry.Callee.Range, entry.Callee.SelectionRange, entry.Callee.Detail),
                entry.Ranges))
            .ToList();
    }

    public IReadOnlyList<CfgsInlayHint> GetInlayHints(CfgsAnalysisResult result, RangeInfo range)
    {
        List<CfgsInlayHint> hints = [];

        foreach (CfgsResolvedCallSite callSite in GetResolvedCallSites(result, range))
        {
            IReadOnlyList<CfgsSignature> signatures = ResolveCallSiteSymbols(result, callSite, static s => s.Signature is not null && s.Kind is 6 or 12 or 5)
                .Select(static symbol => symbol.Signature!)
                .GroupBy(static signature => signature.Label, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToList();
            if (signatures.Count == 0)
                continue;

            int callArgIndex = SignatureLocator.TryFindCallArgPositions(result.SourceText, callSite.Range.Start.Line, callSite.Range.End.Character);
            if (callArgIndex < 0)
                continue;

            IReadOnlyList<(PositionInfo Position, int ArgIndex)> argPositions =
                SignatureLocator.GetArgumentPositions(result.SourceText, callSite.Range.Start.Line, callArgIndex);

            foreach ((PositionInfo position, int argIndex) in argPositions)
            {
                string? label = TryGetConsensusParameterLabel(signatures, argIndex);
                if (label is not null)
                    hints.Add(new CfgsInlayHint(position, $"{label}:", CfgsInlayHintKind.Parameter));
            }
        }

        return hints;
    }

    public IReadOnlyList<CfgsCodeLens> GetCodeLenses(CfgsAnalysisResult result)
    {
        List<CfgsCodeLens> lenses = [];

        foreach (CfgsSymbol symbol in result.DocumentSymbols)
            CollectCodeLenses(result, symbol, lenses);

        return lenses;
    }

    public IReadOnlyList<CfgsDocumentLink> GetDocumentLinks(CfgsAnalysisResult result)
    {
        List<CfgsDocumentLink> links = [];
        string[] lines = result.SourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            (string RawPath, int StartChar, int EndChar)? importPath = TryMatchImportPath(lines[i]);
            if (importPath is null)
                continue;

            string? resolvedUri = TryResolveImportUri(result.Origin, importPath.Value.RawPath);
            if (resolvedUri is null)
                continue;

            links.Add(new CfgsDocumentLink(
                new RangeInfo(new PositionInfo(i, importPath.Value.StartChar), new PositionInfo(i, importPath.Value.EndChar)),
                resolvedUri));
        }

        return links;
    }

    public IReadOnlyList<CfgsSelectionRange> GetSelectionRanges(CfgsAnalysisResult result, IReadOnlyList<PositionInfo> positions)
    {
        List<CfgsSelectionRange> ranges = [];

        foreach (PositionInfo position in positions)
        {
            string? identifier = TextLocator.GetIdentifierAt(result.SourceText, position.Line, position.Character);
            RangeInfo wordRange = identifier is not null
                ? TextLocator.CreateRange(result.SourceText, position.Line + 1, position.Character + 1, identifier)
                : new RangeInfo(position, new PositionInfo(position.Line, position.Character + 1));

            string[] lines = result.SourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            RangeInfo lineRange = position.Line < lines.Length
                ? new RangeInfo(new PositionInfo(position.Line, 0), new PositionInfo(position.Line, lines[position.Line].Length))
                : wordRange;

            CfgsSymbol? enclosing = FindSmallestEnclosingSymbol(result.DocumentSymbols, position.Line);
            CfgsSelectionRange? parent = null;

            RangeInfo fullRange = new(new PositionInfo(0, 0), new PositionInfo(Math.Max(lines.Length - 1, 0), lines.Length > 0 ? lines[^1].Length : 0));
            CfgsSelectionRange fullDocRange = new(fullRange, null);

            if (enclosing is not null)
            {
                CfgsSelectionRange symbolRange = new(enclosing.Range, fullDocRange);
                CfgsSelectionRange lineRangeNode = new(lineRange, symbolRange);
                parent = new CfgsSelectionRange(wordRange, lineRangeNode);
            }
            else
            {
                CfgsSelectionRange lineRangeNode = new(lineRange, fullDocRange);
                parent = new CfgsSelectionRange(wordRange, lineRangeNode);
            }

            ranges.Add(parent);
        }

        return ranges;
    }

    public IReadOnlyList<CfgsTextEdit> FormatDocument(CfgsAnalysisResult result, int tabSize, bool insertSpaces)
    {
        string[] lines = result.SourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        string indent = insertSpaces ? new string(' ', tabSize) : "\t";
        List<CfgsTextEdit> edits = [];
        int depth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (trimmed.StartsWith('}') ||
                trimmed.StartsWith("+#", StringComparison.Ordinal))
                depth = Math.Max(depth - 1, 0);

            string expectedIndent = string.Concat(Enumerable.Repeat(indent, depth));
            string formatted = expectedIndent + trimmed;

            if (!string.Equals(lines[i], formatted, StringComparison.Ordinal))
            {
                edits.Add(new CfgsTextEdit(
                    result.DocumentUri,
                    new RangeInfo(new PositionInfo(i, 0), new PositionInfo(i, lines[i].Length)),
                    formatted));
            }

            if (trimmed.EndsWith('{') ||
                trimmed.StartsWith("#+", StringComparison.Ordinal))
                depth++;
        }

        return edits;
    }

    public IReadOnlyList<CfgsCompletionItem> GetCompletions(CfgsAnalysisResult result, int line, int character)
    {
        MemberCompletionRequest? request = TryFindMemberCompletionRequest(result.SourceText, line, character);
        if (request is not null)
        {
            IReadOnlyList<CfgsCompletionItem> semanticItems = GetSemanticDotCompletions(result, request.Value);
            if (semanticItems.Count > 0)
                return semanticItems;
        }

        string? prefix = GetDotPrefix(result.SourceText, line, character);
        if (prefix is not null)
            return GetDotCompletions(result, prefix);

        return GetCompletions(result);
    }

    public bool ShouldIncludeSnippetCompletions(CfgsAnalysisResult result, int line, int character)
        => TryFindMemberCompletionRequest(result.SourceText, line, character) is null &&
           GetDotPrefix(result.SourceText, line, character) is null;

    public IReadOnlyList<CfgsCompletionItem> GetSnippetCompletions()
    {
        return
        [
            new CfgsCompletionItem("if", 15, "if statement", "if (${1:condition}) {\n\t$0\n}"),
            new CfgsCompletionItem("ife", 15, "if-else statement", "if (${1:condition}) {\n\t$2\n} else {\n\t$0\n}"),
            new CfgsCompletionItem("for", 15, "for loop", "for (var ${1:i} = 0; ${1:i} < ${2:count}; ${1:i}++) {\n\t$0\n}"),
            new CfgsCompletionItem("foreach", 15, "foreach loop", "foreach (var ${1:item} in ${2:collection}) {\n\t$0\n}"),
            new CfgsCompletionItem("while", 15, "while loop", "while (${1:condition}) {\n\t$0\n}"),
            new CfgsCompletionItem("func", 15, "function declaration", "func ${1:name}(${2:params}) {\n\t$0\n}"),
            new CfgsCompletionItem("class", 15, "class declaration", "class ${1:Name}(${2:params}) {\n\t$0\n}"),
            new CfgsCompletionItem("interface", 15, "interface declaration", "interface ${1:IName} {\n\tfunc ${2:name}(${3:params});\n\t$0\n}"),
            new CfgsCompletionItem("try", 15, "try-catch block", "try {\n\t$1\n} catch (${2:err}) {\n\t$0\n}"),
            new CfgsCompletionItem("match", 15, "match expression", "match (${1:expr}) {\n\tcase ${2:pattern} => $0\n}"),
            new CfgsCompletionItem("import", 15, "import statement", "import { ${1:name} } from \"${2:./module.cfs}\""),
            new CfgsCompletionItem("export", 15, "export declaration", "export ${0}"),
            new CfgsCompletionItem("async", 15, "async function", "async func ${1:name}(${2:params}) {\n\t$0\n}"),
        ];
    }

    public CfgsSignatureHelpResult? FindSignatureHelp(CfgsAnalysisResult result, int line, int character)
    {
        SignatureRequest? request = SignatureLocator.TryFindSignatureRequest(result.SourceText, line, character);
        if (request is null)
            return null;

        IReadOnlyList<CfgsSignature> signatures = FindResolvedCallSite(result, request.Value.TargetLine, request.Value.TargetCharacter) is CfgsResolvedCallSite callSite
            ? ResolveCallSiteSymbols(result, callSite, static s => s.Signature is not null)
                .Select(static symbol => symbol.Signature!)
                .GroupBy(static signature => signature.Label, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToList()
            : FindResolvedCallableSymbols(result, request.Value.TargetLine, request.Value.TargetCharacter, static s => s.Signature is not null)
                .Select(static symbol => symbol.Signature!)
                .GroupBy(static signature => signature.Label, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToList();

        if (signatures.Count == 0)
            return null;

        int maxParameters = signatures.Max(static signature => signature.Parameters.Count);
        int activeParameter = Math.Min(request.Value.ActiveParameter, Math.Max(maxParameters - 1, 0));
        return new CfgsSignatureHelpResult(signatures, 0, activeParameter);
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
        CfgsSymbolIdentity identity = CfgsSymbolIdentity.From(symbol);
        if (!string.IsNullOrWhiteSpace(symbol.Id) && result.SemanticModel.SymbolsById.TryGetValue(symbol.Id!, out CfgsSymbol? exact) && identity.Matches(exact))
            return exact;

        return result.SemanticModel.Symbols.FirstOrDefault(candidate => identity.Matches(candidate));
    }

    private static CfgsSymbol? ResolveCallTargetSymbol(CfgsAnalysisResult result, CfgsSymbol? symbol)
    {
        if (symbol is null)
            return null;

        HashSet<string> seen = new(StringComparer.Ordinal);
        CfgsSymbol current = symbol;

        while (!string.IsNullOrWhiteSpace(current.CallTargetSymbolId) &&
               seen.Add(current.Id ?? current.QualifiedName) &&
               result.SemanticModel.SymbolsById.TryGetValue(current.CallTargetSymbolId!, out CfgsSymbol? next))
        {
            current = next;
        }

        return current;
    }

    private static HashSet<string> CollectCallTargetSymbolIds(CfgsAnalysisResult result, CfgsSymbol target)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(target.Id))
            ids.Add(target.Id!);

        foreach (CfgsSymbol symbol in result.SemanticModel.Symbols)
        {
            CfgsSymbol? resolved = ResolveCallTargetSymbol(result, symbol);
            if (resolved is null || !string.Equals(resolved.Id, target.Id, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrWhiteSpace(symbol.Id))
                ids.Add(symbol.Id!);
        }

        return ids;
    }

    private static IReadOnlyList<CfgsResolvedCallSite> GetResolvedCallSites(CfgsAnalysisResult result, RangeInfo? range = null)
    {
        return result.SemanticModel.CallSites
            .Where(callSite =>
                string.Equals(callSite.Uri, result.DocumentUri, GetPathComparison()) &&
                (range is null ||
                 (callSite.Range.Start.Line >= range.Value.Start.Line &&
                  callSite.Range.Start.Line <= range.Value.End.Line)))
            .ToList();
    }

    private static CfgsResolvedCallSite? FindResolvedCallSite(CfgsAnalysisResult result, int line, int character)
        => result.SemanticModel.CallSites.FirstOrDefault(callSite =>
            string.Equals(callSite.Uri, result.DocumentUri, GetPathComparison()) &&
            Contains(callSite.Range, line, character));

    private static IReadOnlyList<CfgsSymbol> ResolveCallSiteSymbols(CfgsAnalysisResult result, CfgsResolvedCallSite callSite, Func<CfgsSymbol, bool> predicate)
        => callSite.SymbolIds
            .Select(symbolId => result.SemanticModel.SymbolsById.GetValueOrDefault(symbolId))
            .Where(symbol => symbol is not null && predicate(symbol))
            .Cast<CfgsSymbol>()
            .GroupBy(static symbol => symbol.Id ?? symbol.QualifiedName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();

    private static string? TryGetConsensusParameterLabel(IReadOnlyList<CfgsSignature> signatures, int argIndex)
    {
        string? label = null;
        foreach (CfgsSignature signature in signatures)
        {
            if (argIndex >= signature.Parameters.Count)
                return null;

            string candidate = signature.Parameters[argIndex];
            if (label is null)
            {
                label = candidate;
                continue;
            }

            if (!string.Equals(label, candidate, StringComparison.Ordinal))
                return null;
        }

        return label;
    }

    private static IReadOnlyList<CfgsSymbol> FindResolvedCallableSymbols(CfgsAnalysisResult result, int line, int character, Func<CfgsSymbol, bool> predicate)
    {
        List<CfgsSymbol> symbols = [];

        foreach (CfgsResolvedOccurrence occurrence in result.SemanticModel.Occurrences)
        {
            if (!string.Equals(occurrence.Uri, result.DocumentUri, GetPathComparison()))
                continue;

            if (!Contains(occurrence.Range, line, character))
                continue;

            if (!result.SemanticModel.SymbolsById.TryGetValue(occurrence.SymbolId, out CfgsSymbol? occurrenceSymbol))
                continue;

            CfgsSymbol? callable = ResolveCallTargetSymbol(result, occurrenceSymbol);
            if (callable is not null && predicate(callable))
                symbols.Add(callable);
        }

        return symbols
            .GroupBy(static symbol => symbol.Id ?? symbol.QualifiedName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
    }

    private static bool TryFindResolvedCallableSymbol(CfgsAnalysisResult result, int line, int character, Func<CfgsSymbol, bool> predicate, out CfgsSymbol? symbol)
    {
        symbol = FindResolvedCallableSymbols(result, line, character, predicate).FirstOrDefault();
        return symbol is not null;
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

    private static bool TryGetNonSymbolKeywordAt(string sourceText, int line, int character, out string? keyword)
    {
        keyword = TextLocator.GetIdentifierAt(sourceText, line, character);
        return !string.IsNullOrWhiteSpace(keyword) && NonSymbolKeywords.Contains(keyword);
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
            11 => 8,
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
            11 => 2,
            10 => 3,
            22 => 4,
            12 => 5,
            6 => 6,
            13 when IsParameterSymbol(symbol) => 7,
            13 => 8,
            14 => 8,
            _ => -1
        };

        if (tokenType < 0)
        {
            span = default;
            return false;
        }

        int modifiers = 0;
        if (isDeclaration)
            modifiers |= 1 << 0; // declaration
        if (symbol.Kind == 14)
            modifiers |= 1 << 1; // readonly
        if (symbol.Detail.Contains("static", StringComparison.OrdinalIgnoreCase))
            modifiers |= 1 << 2; // static
        if (symbol.Detail.StartsWith("async ", StringComparison.Ordinal))
            modifiers |= 1 << 3; // async

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

    private static IReadOnlyList<CfgsSymbol> FindTypeImplementations(CfgsAnalysisResult result, CfgsSymbol typeSymbol)
    {
        HashSet<string> candidates = new(StringComparer.Ordinal)
        {
            typeSymbol.Name,
            typeSymbol.QualifiedName
        };

        return result.AllSymbols
            .Concat(result.SemanticModel.Symbols)
            .Where(candidate => (typeSymbol.Kind == 11 ? candidate.Kind is 5 or 11 : candidate.Kind == 5) &&
                                !candidate.IsSynthetic &&
                                !string.Equals(candidate.QualifiedName, typeSymbol.QualifiedName, StringComparison.Ordinal) &&
                                DeclaresTypeReference(candidate.Detail, candidates))
            .GroupBy(candidate => candidate.QualifiedName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
    }

    private static string? TryGetContainingTypeQualifiedName(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return null;

        int lastDot = qualifiedName.LastIndexOf('.');
        return lastDot > 0 ? qualifiedName[..lastDot] : null;
    }

    private static bool DeclaresTypeReference(string detail, HashSet<string> candidates)
        => ExtractDeclaredTypeReferences(detail).Any(candidates.Contains);

    private static IEnumerable<string> ExtractDeclaredTypeReferences(string detail)
    {
        int colonIndex = detail.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex < 0 || colonIndex >= detail.Length - 1)
            yield break;

        string inheritanceClause = detail[(colonIndex + 1)..];
        foreach (string typeName in inheritanceClause.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(typeName))
                yield return typeName;
        }
    }

    private static string? FindSymbolValueType(CfgsAnalysisResult result, CfgsSymbol symbol)
    {
        if (symbol.Kind is 5 or 10 or 11)
            return symbol.QualifiedName;

        if (!string.IsNullOrWhiteSpace(symbol.ValueTypeQualifiedName))
            return symbol.ValueTypeQualifiedName;

        if (symbol.Detail.StartsWith("var ", StringComparison.Ordinal) || symbol.Detail.StartsWith("const ", StringComparison.Ordinal))
        {
            foreach (CfgsSymbol candidate in result.SemanticModel.Symbols)
            {
                if (candidate.Kind is 5 or 10 or 11 &&
                    !string.IsNullOrWhiteSpace(candidate.QualifiedName) &&
                    symbol.QualifiedName.StartsWith(candidate.QualifiedName + ".", StringComparison.Ordinal))
                    return candidate.QualifiedName;
            }
        }

        return null;
    }

    private static CfgsSymbol? FindEnclosingFunction(CfgsAnalysisResult result, string uri, int line, int character)
    {
        CfgsSymbol? best = null;
        int bestSize = int.MaxValue;

        foreach (CfgsSymbol symbol in result.SemanticModel.Symbols)
        {
            if (symbol.Kind is not (6 or 12))
                continue;

            if (!string.Equals(symbol.Uri, uri, GetPathComparison()))
                continue;

            if (!Contains(symbol.Range, line, character))
                continue;

            int size = (symbol.Range.End.Line - symbol.Range.Start.Line) * 10000 +
                       (symbol.Range.End.Character - symbol.Range.Start.Character);
            if (size < bestSize)
            {
                bestSize = size;
                best = symbol;
            }
        }

        return best;
    }

    private static CfgsSymbol? FindSmallestEnclosingSymbol(IReadOnlyList<CfgsSymbol> symbols, int line)
    {
        foreach (CfgsSymbol symbol in symbols)
        {
            if (symbol.Range.Start.Line <= line && symbol.Range.End.Line >= line)
            {
                CfgsSymbol? child = FindSmallestEnclosingSymbol(symbol.Children, line);
                return child ?? symbol;
            }
        }

        return null;
    }

    private void CollectCodeLenses(CfgsAnalysisResult result, CfgsSymbol symbol, List<CfgsCodeLens> lenses)
    {
        if (symbol.Kind is 5 or 6 or 10 or 12 && !symbol.IsSynthetic)
        {
            int refCount = 0;
            CfgsSymbol? resolved = FindEquivalentSemanticSymbol(result, symbol);
            if (resolved is not null && !string.IsNullOrWhiteSpace(resolved.Id))
            {
                refCount = result.SemanticModel.Occurrences
                    .Count(o => string.Equals(o.SymbolId, resolved.Id, StringComparison.Ordinal) && !o.IsDeclaration);
            }

            lenses.Add(new CfgsCodeLens(symbol.SelectionRange, $"{refCount} reference{(refCount == 1 ? "" : "s")}"));
        }

        foreach (CfgsSymbol child in symbol.Children)
            CollectCodeLenses(result, child, lenses);
    }

    private static string? TryResolveImportUri(string origin, string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        if (Uri.TryCreate(rawPath, UriKind.Absolute, out Uri? absoluteImportUri))
        {
            if (!absoluteImportUri.IsFile)
                return absoluteImportUri.AbsoluteUri;

            string absoluteImportPath = Path.GetFullPath(absoluteImportUri.LocalPath);
            return File.Exists(absoluteImportPath) ? new Uri(absoluteImportPath).AbsoluteUri : null;
        }

        string? sourceDir = Path.GetDirectoryName(origin);
        if (string.IsNullOrWhiteSpace(sourceDir))
            return null;

        string candidatePath = Path.IsPathFullyQualified(rawPath)
            ? rawPath
            : Path.Combine(sourceDir, rawPath.Replace('/', Path.DirectorySeparatorChar));
        string fullPath = Path.GetFullPath(candidatePath);

        return File.Exists(fullPath) ? new Uri(fullPath).AbsoluteUri : null;
    }

    private static (string RawPath, int StartChar, int EndChar)? TryMatchImportPath(string lineText)
    {
        foreach (string pattern in new[]
                 {
                     @"\bfrom\s+""(?<path>[^""]+)""",
                     @"\bfrom\s+'(?<path>[^']+)'",
                     @"^\s*import\s+""(?<path>[^""]+)""",
                     @"^\s*import\s+'(?<path>[^']+)'"
                 })
        {
            Match match = Regex.Match(lineText, pattern);
            if (!match.Success)
                continue;

            Group pathGroup = match.Groups["path"];
            return (pathGroup.Value, pathGroup.Index, pathGroup.Index + pathGroup.Length);
        }

        return null;
    }

    private static MemberCompletionRequest? TryFindMemberCompletionRequest(string sourceText, int line, int character)
    {
        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (line < 0 || line >= lines.Length)
            return null;

        string lineText = lines[line];
        int cursor = Math.Min(Math.Max(character, 0), lineText.Length);
        int memberStart = cursor;
        while (memberStart > 0 && IsIdentifierChar(lineText[memberStart - 1]))
            memberStart--;

        int dotIndex = memberStart - 1;
        if (dotIndex < 0 || dotIndex >= lineText.Length || lineText[dotIndex] != '.')
            return null;

        string typedPrefix = lineText[memberStart..cursor];
        return new MemberCompletionRequest(line, memberStart, typedPrefix);
    }

    private IReadOnlyList<CfgsCompletionItem> GetSemanticDotCompletions(CfgsAnalysisResult result, MemberCompletionRequest request)
    {
        List<CfgsResolvedCompletionTarget> candidates = result.SemanticModel.CompletionTargets
            .Where(target =>
                string.Equals(target.Uri, result.DocumentUri, GetPathComparison()) &&
                target.End.Line == request.TargetLine &&
                target.End.Character == request.TargetCharacter)
            .ToList();

        if (candidates.Count == 0)
            return [];

        return candidates
            .SelectMany(static target => target.Items)
            .Where(item =>
                string.IsNullOrWhiteSpace(request.TypedPrefix) ||
                item.Label.StartsWith(request.TypedPrefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(static item => item.Label, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? GetDotPrefix(string sourceText, int line, int character)
    {
        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (line < 0 || line >= lines.Length)
            return null;

        string lineText = lines[line];
        int dotIndex = character - 1;
        if (dotIndex < 0 || dotIndex >= lineText.Length || lineText[dotIndex] != '.')
            return null;

        int end = dotIndex;
        int start = end - 1;
        while (start >= 0 && (char.IsLetterOrDigit(lineText[start]) || lineText[start] == '_' || lineText[start] == '.'))
            start--;
        start++;

        string prefix = lineText[start..end];
        return string.IsNullOrWhiteSpace(prefix) ? null : prefix;
    }

    private IReadOnlyList<CfgsCompletionItem> GetDotCompletions(CfgsAnalysisResult result, string prefix)
    {
        string searchPrefix = prefix + ".";

        List<CfgsCompletionItem> items = [];

        foreach (CfgsSymbol symbol in result.AllSymbols.Concat(result.SemanticModel.Symbols))
        {
            if (symbol.IsSynthetic)
                continue;

            if (!symbol.QualifiedName.StartsWith(searchPrefix, StringComparison.Ordinal))
                continue;

            string remainder = symbol.QualifiedName[searchPrefix.Length..];
            if (remainder.Contains('.', StringComparison.Ordinal))
                continue;

            if (items.All(item => !string.Equals(item.Label, remainder, StringComparison.Ordinal)))
                items.Add(new CfgsCompletionItem(remainder, ToCompletionKind(symbol.Kind), symbol.Detail));
        }

        return items.OrderBy(static item => item.Label, StringComparer.Ordinal).ToList();
    }

    private static bool IsIdentifierChar(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_';

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.Ordinal) &&
               string.Equals(x.Item2, y.Item2, GetPathComparison());

        public int GetHashCode((string, string) obj)
        {
            StringComparer ordinal = StringComparer.Ordinal;
            StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            return HashCode.Combine(ordinal.GetHashCode(obj.Item1), pathComparer.GetHashCode(obj.Item2));
        }
    }

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
                    AddFlattenedSymbol(namespaceSymbol!);
                    if (IsCurrentDocument(block.OriginFile))
                        DocumentSymbols.Add(namespaceSymbol!);
                    continue;
                }

                if (stmt is VarDecl syntheticRoot && IsSyntheticNamespaceRoot(syntheticRoot))
                    continue;

                if (TryCreateSymbol(stmt, null, out CfgsSymbol? symbol))
                {
                    AddFlattenedSymbol(symbol!);
                    if (IsCurrentDocument(symbol!.OriginFile))
                        DocumentSymbols.Add(symbol);
                }
            }
        }

        private void AddFlattenedSymbol(CfgsSymbol symbol)
        {
            AllSymbols.Add(symbol);

            foreach (CfgsSymbol child in symbol.Children)
                AddFlattenedSymbol(child);
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

                case InterfaceDeclStmt @interface:
                    symbol = CreateInterfaceSymbol(@interface, containerName);
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

            foreach (KeyValuePair<string, Expr?> field in classDecl.Fields)
            {
                bool isConst = classDecl.ConstFields.Contains(field.Key);
                int kind = isConst ? 14 : 13;
                string prefix = isConst ? "const" : "var";
                children.Add(CreateSimpleSymbol(field.Key, qualifiedName, classDecl.Line, classDecl.Col, classDecl.OriginFile, kind, $"{prefix} {field.Key}"));
            }

            foreach (KeyValuePair<string, Expr?> field in classDecl.StaticFields)
            {
                bool isConst = classDecl.StaticConstFields.Contains(field.Key);
                int kind = isConst ? 14 : 13;
                string prefix = isConst ? "static const" : "static var";
                children.Add(CreateSimpleSymbol(field.Key, qualifiedName, classDecl.Line, classDecl.Col, classDecl.OriginFile, kind, $"{prefix} {field.Key}"));
            }

            children.AddRange(classDecl.Methods.Select(method => CreateFunctionSymbol(method, qualifiedName, isMethod: true)));
            children.AddRange(classDecl.StaticMethods.Select(method => CreateFunctionSymbol(method, qualifiedName, isMethod: true)));
            children.AddRange(classDecl.Enums.Select(@enum => CreateEnumSymbol(@enum, qualifiedName)));
            children.AddRange(classDecl.NestedClasses.Select(nested => CreateClassSymbol(nested, qualifiedName)));

            string header = BuildClassHeader(classDecl);

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

        private CfgsSymbol CreateInterfaceSymbol(InterfaceDeclStmt interfaceDecl, string? containerName)
        {
            string qualifiedName = Qualify(containerName, interfaceDecl.Name);
            List<CfgsSymbol> children =
                interfaceDecl.Methods
                    .Select(method => CreateInterfaceMethodSymbol(method, qualifiedName))
                    .ToList();

            string header = BuildInterfaceHeader(interfaceDecl);

            return CreateSymbol(
                interfaceDecl.Name,
                qualifiedName,
                interfaceDecl.Line,
                interfaceDecl.Col,
                interfaceDecl.OriginFile,
                11,
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

        private CfgsSymbol CreateInterfaceMethodSymbol(InterfaceMethodDecl method, string? containerName)
        {
            string qualifiedName = Qualify(containerName, method.Name);
            string signature = BuildInterfaceMethodSignature(method);
            return CreateSymbol(
                method.Name,
                qualifiedName,
                method.Line,
                method.Col,
                method.OriginFile,
                6,
                signature,
                signature,
                []);
        }

        private CfgsSymbol CreateFunctionSymbol(FuncDeclStmt func, string? containerName, bool isMethod)
        {
            string qualifiedName = Qualify(containerName, func.Name);
            string signature = SignatureDisplay.BuildFunctionLabel(func.Name, func.IsAsync, func.Parameters, func.ParameterSpecs, func.RestParameter);
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

        private static string BuildClassHeader(ClassDeclStmt classDecl)
        {
            StringBuilder sb = new();
            sb.Append($"class {classDecl.Name}({string.Join(", ", classDecl.Parameters)})");

            List<string> inheritedTypes = [];
            if (!string.IsNullOrWhiteSpace(classDecl.BaseName))
                inheritedTypes.Add(classDecl.BaseName);
            inheritedTypes.AddRange(classDecl.ImplementedInterfaces.Where(static iface => !string.IsNullOrWhiteSpace(iface)));

            if (inheritedTypes.Count > 0)
                sb.Append($" : {string.Join(", ", inheritedTypes)}");

            return sb.ToString();
        }

        private static string BuildInterfaceHeader(InterfaceDeclStmt interfaceDecl)
        {
            StringBuilder sb = new();
            sb.Append($"interface {interfaceDecl.Name}");
            if (interfaceDecl.BaseInterfaces.Count > 0)
                sb.Append($" : {string.Join(", ", interfaceDecl.BaseInterfaces)}");

            return sb.ToString();
        }

        private static string BuildInterfaceMethodSignature(InterfaceMethodDecl method)
            => SignatureDisplay.BuildFunctionLabel(method.Name, method.IsAsync, method.Parameters, parameterSpecs: null, method.RestParameter);

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
    string Detail,
    string? InsertText = null);

internal readonly record struct MemberCompletionRequest(
    int TargetLine,
    int TargetCharacter,
    string TypedPrefix);

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
    CfgsSignature? Signature = null,
    string? ValueTypeQualifiedName = null,
    string? CallTargetSymbolId = null);

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
        if (string.IsNullOrEmpty(token))
            return -1;

        int searchIndex = Math.Min(Math.Max(minIndex, 0), lineText.Length);
        int found = FindValidIdentifierMatch(lineText, token, searchIndex, lineText.Length);
        if (found >= 0)
            return found;

        return searchIndex > 0
            ? FindValidIdentifierMatch(lineText, token, 0, searchIndex)
            : -1;
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
        int bestDirectionScore = int.MaxValue;
        (int lineIndex, int charIndex)? best = null;

        for (int i = 0; i < lines.Length; i++)
        {
            int found = FindIdentifier(lines[i], token, 0);
            if (found < 0)
                continue;

            int distance = Math.Abs(i - expectedLine);
            int directionScore = i < expectedLine ? 1 : 0;
            if (distance < bestDistance || (distance == bestDistance && directionScore < bestDirectionScore))
            {
                bestDistance = distance;
                bestDirectionScore = directionScore;
                best = (i, found);
            }
        }

        return best;
    }

    private static int FindValidIdentifierMatch(string lineText, string token, int startIndex, int endExclusive)
    {
        int limit = Math.Min(Math.Max(endExclusive, 0), lineText.Length);
        int searchIndex = Math.Min(Math.Max(startIndex, 0), limit);

        while (searchIndex <= limit)
        {
            int found = lineText.IndexOf(token, searchIndex, StringComparison.Ordinal);
            if (found < 0 || found >= limit)
                return -1;

            if (IsValidIdentifierMatch(lineText, found, token.Length))
                return found;

            searchIndex = found + 1;
        }

        return -1;
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

internal sealed record CfgsCallHierarchyItem(
    string Name,
    int Kind,
    string Uri,
    RangeInfo Range,
    RangeInfo SelectionRange,
    string Detail);

internal sealed record CfgsCallHierarchyIncomingCall(
    CfgsCallHierarchyItem From,
    IReadOnlyList<RangeInfo> FromRanges);

internal sealed record CfgsCallHierarchyOutgoingCall(
    CfgsCallHierarchyItem To,
    IReadOnlyList<RangeInfo> FromRanges);

internal sealed record CfgsInlayHint(
    PositionInfo Position,
    string Label,
    CfgsInlayHintKind Kind);

internal enum CfgsInlayHintKind
{
    Type = 1,
    Parameter = 2
}

internal sealed record CfgsCodeLens(
    RangeInfo Range,
    string CommandTitle);

internal sealed record CfgsDocumentLink(
    RangeInfo Range,
    string Target);

internal sealed record CfgsSelectionRange(
    RangeInfo Range,
    CfgsSelectionRange? Parent);
