using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CFGS.Lsp;

internal sealed class LspServer
{
    private static readonly Regex MissingImportPathRegex = new("^import path not found '(?<path>[^']+)'", RegexOptions.Compiled);
    private static readonly Regex MissingExportRegex = new("^Could not find export '(?<name>[^']+)' in import '(?<path>[^']+)'$", RegexOptions.Compiled);
    private readonly CfgsAnalyzer _analyzer;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly JsonSerializerOptions _jsonOptions = new();
    private readonly Dictionary<string, DocumentState> _documents = OperatingSystem.IsWindows()
        ? new(StringComparer.OrdinalIgnoreCase)
        : new(StringComparer.Ordinal);

    private string? _workspaceRootPath;
    private bool _shutdownRequested;

    public LspServer(CfgsAnalyzer analyzer)
    {
        _analyzer = analyzer;
        _input = Console.OpenStandardInput();
        _output = Console.OpenStandardOutput();
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? json = await ReadMessageAsync(cancellationToken);
            if (json is null)
                break;

            using JsonDocument document = JsonDocument.Parse(json);
            if (!await HandleMessageAsync(document.RootElement, cancellationToken))
                break;
        }
    }

    private async Task<bool> HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("method", out JsonElement methodElement))
            return true;

        string method = methodElement.GetString() ?? string.Empty;
        bool hasId = message.TryGetProperty("id", out JsonElement idElement);
        JsonElement parameters = message.TryGetProperty("params", out JsonElement paramsElement)
            ? paramsElement
            : default;

        try
        {
            switch (method)
            {
                case "initialize":
                    _workspaceRootPath = TryGetWorkspaceRootPath(parameters);
                    if (hasId)
                    {
                        await SendResponseAsync(idElement, new
                        {
                            capabilities = new
                            {
                                textDocumentSync = 1,
                                definitionProvider = true,
                                hoverProvider = true,
                                documentHighlightProvider = true,
                                referencesProvider = true,
                                codeActionProvider = new
                                {
                                    codeActionKinds = new[] { "quickfix" }
                                },
                                renameProvider = new
                                {
                                    prepareProvider = true
                                },
                                documentSymbolProvider = true,
                                foldingRangeProvider = true,
                                signatureHelpProvider = new
                                {
                                    triggerCharacters = new[] { "(", "," },
                                    retriggerCharacters = new[] { "," }
                                },
                                completionProvider = new
                                {
                                    resolveProvider = false
                                },
                                semanticTokensProvider = new
                                {
                                    legend = new
                                    {
                                        tokenTypes = CfgsAnalyzer.SemanticTokenTypes,
                                        tokenModifiers = CfgsAnalyzer.SemanticTokenModifiers
                                    },
                                    full = true
                                }
                            },
                            serverInfo = new
                            {
                                name = "CFGS Language Server",
                                version = "0.1.0"
                            }
                        }, cancellationToken);
                    }
                    return true;

                case "initialized":
                    return true;

                case "shutdown":
                    _shutdownRequested = true;
                    if (hasId)
                        await SendResponseAsync(idElement, null, cancellationToken);
                    return true;

                case "exit":
                    Environment.ExitCode = _shutdownRequested ? 0 : 1;
                    return false;

                case "textDocument/didOpen":
                    await HandleDidOpenAsync(parameters, cancellationToken);
                    return true;

                case "textDocument/didChange":
                    await HandleDidChangeAsync(parameters, cancellationToken);
                    return true;

                case "textDocument/didClose":
                    await HandleDidCloseAsync(parameters, cancellationToken);
                    return true;

                case "textDocument/documentSymbol":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        DocumentState state = EnsureDocument(uri);
                        await SendResponseAsync(idElement, state.Analysis.DocumentSymbols.Select(ToDocumentSymbolPayload).ToList(), cancellationToken);
                    }
                    return true;

                case "textDocument/foldingRange":
                    if (hasId)
                    {
                        string uri = GetTextDocumentUri(parameters);
                        DocumentState state = EnsureDocument(uri);
                        List<object> ranges = new();
                        CollectFoldingRanges(state.Analysis.DocumentSymbols, ranges);
                        await SendResponseAsync(idElement, ranges, cancellationToken);
                    }
                    return true;

                case "textDocument/definition":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        JsonElement position = parameters.GetProperty("position");
                        int line = position.GetProperty("line").GetInt32();
                        int character = position.GetProperty("character").GetInt32();

                        DocumentState state = EnsureDocument(uri);
                        IReadOnlyList<CfgsSymbol> definitions = _analyzer.FindDefinitions(state.Analysis, line, character);
                        object? payload = definitions.Count == 0
                            ? null
                            : definitions.Select(ToLocationPayload).ToList();

                        await SendResponseAsync(idElement, payload, cancellationToken);
                    }
                    return true;

                case "textDocument/hover":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        JsonElement position = parameters.GetProperty("position");
                        int line = position.GetProperty("line").GetInt32();
                        int character = position.GetProperty("character").GetInt32();

                        DocumentState state = EnsureDocument(uri);
                        CfgsSymbol? symbol = _analyzer.FindHoverSymbol(state.Analysis, line, character);
                        object? payload = symbol is null
                            ? null
                            : new
                            {
                                contents = new
                                {
                                    kind = "markdown",
                                    value = symbol.HoverText
                                },
                                range = ToRangePayload(symbol.SelectionRange)
                            };

                        await SendResponseAsync(idElement, payload, cancellationToken);
                    }
                    return true;

                case "textDocument/documentHighlight":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        JsonElement position = parameters.GetProperty("position");
                        int line = position.GetProperty("line").GetInt32();
                        int character = position.GetProperty("character").GetInt32();

                        DocumentState state = EnsureDocument(uri);
                        IReadOnlyList<CfgsSymbol> highlights = _analyzer.FindDocumentHighlights(state.Analysis, line, character);
                        object? payload = highlights.Count == 0
                            ? null
                            : highlights.Select(static highlight => new
                            {
                                range = ToRangePayload(highlight.SelectionRange),
                                kind = 1
                            }).ToList();

                        await SendResponseAsync(idElement, payload, cancellationToken);
                    }
                    return true;

                case "textDocument/references":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        JsonElement position = parameters.GetProperty("position");
                        bool includeDeclaration = parameters.GetProperty("context").GetProperty("includeDeclaration").GetBoolean();
                        int line = position.GetProperty("line").GetInt32();
                        int character = position.GetProperty("character").GetInt32();

                        DocumentState state = EnsureDocument(uri);
                        CfgsSymbol? symbol = _analyzer.ResolveSymbol(state.Analysis, line, character);
                        IReadOnlyList<CfgsSymbol> references = symbol is null
                            ? []
                            : FindWorkspaceReferences(state, symbol, includeDeclaration);
                        object? payload = references.Count == 0
                            ? null
                            : references.Select(ToLocationPayload).ToList();

                        await SendResponseAsync(idElement, payload, cancellationToken);
                    }
                    return true;

                case "textDocument/codeAction":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        RangeInfo requestRange = ToRangeInfo(parameters.GetProperty("range"));

                        DocumentState state = EnsureDocument(uri);
                        IReadOnlyList<object> codeActions = GetCodeActions(state, uri, requestRange);
                        await SendResponseAsync(idElement, codeActions.Count == 0 ? null : codeActions, cancellationToken);
                    }
                    return true;

                case "textDocument/signatureHelp":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        JsonElement position = parameters.GetProperty("position");
                        int line = position.GetProperty("line").GetInt32();
                        int character = position.GetProperty("character").GetInt32();

                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        CfgsSignatureHelpResult? signatureHelp = _analyzer.FindSignatureHelp(analysis, line, character);
                        object? payload = signatureHelp is null
                            ? null
                            : new
                            {
                                signatures = signatureHelp.Signatures.Select(ToSignaturePayload).ToList(),
                                activeSignature = signatureHelp.ActiveSignature,
                                activeParameter = signatureHelp.ActiveParameter
                            };

                        await SendResponseAsync(idElement, payload, cancellationToken);
                    }
                    return true;

                case "textDocument/rename":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        JsonElement position = parameters.GetProperty("position");
                        string newName = parameters.GetProperty("newName").GetString() ?? string.Empty;
                        int line = position.GetProperty("line").GetInt32();
                        int character = position.GetProperty("character").GetInt32();

                        DocumentState state = EnsureDocument(uri);
                        CfgsSymbol? symbol = _analyzer.ResolveSymbol(state.Analysis, line, character);
                        CfgsRenameResult? rename = symbol is null
                            ? null
                            : RenameWorkspaceSymbol(state, symbol, newName);
                        object? payload = rename is null
                            ? null
                            : new
                            {
                                changes = rename.Edits
                                    .GroupBy(edit => edit.Uri, StringComparer.OrdinalIgnoreCase)
                                    .ToDictionary(
                                        group => group.Key,
                                        group => group.Select(ToTextEditPayload).ToList(),
                                        StringComparer.OrdinalIgnoreCase)
                            };

                        await SendResponseAsync(idElement, payload, cancellationToken);
                    }
                    return true;

                case "textDocument/prepareRename":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        JsonElement position = parameters.GetProperty("position");
                        int line = position.GetProperty("line").GetInt32();
                        int character = position.GetProperty("character").GetInt32();

                        DocumentState state = EnsureDocument(uri);
                        CfgsPrepareRenameResult? rename = _analyzer.PrepareRename(state.Analysis, line, character);
                        object? payload = rename is null
                            ? null
                            : new
                            {
                                range = ToRangePayload(rename.Range),
                                placeholder = rename.Placeholder
                            };

                        await SendResponseAsync(idElement, payload, cancellationToken);
                    }
                    return true;

                case "textDocument/completion":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        IReadOnlyList<CfgsCompletionItem> completions = _analyzer.GetCompletions(analysis);
                        await SendResponseAsync(idElement, completions.Select(ToCompletionPayload).ToList(), cancellationToken);
                    }
                    return true;

                case "textDocument/semanticTokens/full":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        IReadOnlyList<int> data = _analyzer.GetSemanticTokens(analysis);
                        await SendResponseAsync(idElement, new { data }, cancellationToken);
                    }
                    return true;

                default:
                    if (hasId)
                        await SendErrorResponseAsync(idElement, -32601, $"Method not found: {method}", cancellationToken);
                    return true;
            }
        }
        catch (Exception ex)
        {
            if (hasId)
                await SendErrorResponseAsync(idElement, -32603, ex.Message, cancellationToken);

            return true;
        }
    }

    private static string GetTextDocumentUri(JsonElement parameters)
    {
        if (parameters.TryGetProperty("textDocument", out JsonElement td) &&
            td.TryGetProperty("uri", out JsonElement u))
            return u.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static (int line, int character) GetPosition(JsonElement parameters)
    {
        if (parameters.TryGetProperty("position", out JsonElement pos))
        {
            int line = pos.TryGetProperty("line", out JsonElement l) ? l.GetInt32() : 0;
            int character = pos.TryGetProperty("character", out JsonElement c) ? c.GetInt32() : 0;
            return (line, character);
        }
        return (0, 0);
    }

    private async Task HandleDidOpenAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetProperty("textDocument", out JsonElement textDocument))
            return;
        string uri = textDocument.TryGetProperty("uri", out JsonElement uriEl) ? uriEl.GetString() ?? string.Empty : string.Empty;
        int version = textDocument.TryGetProperty("version", out JsonElement versionElement) ? versionElement.GetInt32() : 0;
        string text = textDocument.TryGetProperty("text", out JsonElement textEl) ? textEl.GetString() ?? string.Empty : string.Empty;

        DocumentState state = new(uri, text, version);
        _documents[uri] = state;
        await AnalyzeAndPublishAsync(state, cancellationToken);
    }

    private async Task HandleDidChangeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!parameters.TryGetProperty("textDocument", out JsonElement textDocument))
            return;
        string uri = textDocument.TryGetProperty("uri", out JsonElement uriEl) ? uriEl.GetString() ?? string.Empty : string.Empty;
        DocumentState state = EnsureDocument(uri);

        if (textDocument.TryGetProperty("version", out JsonElement versionElement))
            state.Version = versionElement.GetInt32();

        if (parameters.TryGetProperty("contentChanges", out JsonElement contentChanges) && contentChanges.GetArrayLength() > 0)
        {
            if (contentChanges[0].TryGetProperty("text", out JsonElement textEl))
                state.Text = textEl.GetString() ?? string.Empty;
        }

        await AnalyzeAndPublishAsync(state, cancellationToken);
    }

    private async Task HandleDidCloseAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string uri = GetTextDocumentUri(parameters);
        if (_documents.Remove(uri, out DocumentState? state))
        {
            foreach (string publishedUri in state.PublishedDiagnosticUris)
                await PublishDiagnosticsAsync(publishedUri, [], cancellationToken);
        }
        else
        {
            await PublishDiagnosticsAsync(uri, [], cancellationToken);
        }
    }

    private DocumentState EnsureDocument(string uri)
    {
        if (_documents.TryGetValue(uri, out DocumentState? existing))
            return existing;

        string text = LoadDocumentText(uri);
        DocumentState created = new(uri, text, 0)
        {
            Analysis = _analyzer.Analyze(uri, text, GetOpenDocumentSources())
        };
        created.PublishedDiagnosticUris = created.Analysis.DiagnosticsByUri.Keys.ToHashSet(GetUriComparer());
        _documents[uri] = created;
        return created;
    }

    private static string LoadDocumentText(string uri)
    {
        string? localPath = TryGetLocalPath(uri);
        if (localPath is not null && File.Exists(localPath))
            return File.ReadAllText(localPath);

        return string.Empty;
    }

    private async Task AnalyzeAndPublishAsync(DocumentState state, CancellationToken cancellationToken)
    {
        CfgsAnalysisResult analysis = _analyzer.Analyze(state.Uri, state.Text, GetOpenDocumentSources());
        state.Analysis = analysis;
        if (IsSuccessfulInteractiveAnalysis(analysis))
            state.LastSuccessfulAnalysis = analysis;

        HashSet<string> nextUris = analysis.DiagnosticsByUri.Keys.ToHashSet(GetUriComparer());
        nextUris.Add(state.Uri);

        foreach ((string uri, List<CfgsDiagnostic> diagnostics) in analysis.DiagnosticsByUri)
            await PublishDiagnosticsAsync(uri, diagnostics, cancellationToken);

        if (!analysis.DiagnosticsByUri.ContainsKey(state.Uri))
            await PublishDiagnosticsAsync(state.Uri, [], cancellationToken);

        foreach (string staleUri in state.PublishedDiagnosticUris.Except(nextUris, GetUriComparer()))
            await PublishDiagnosticsAsync(staleUri, [], cancellationToken);

        state.PublishedDiagnosticUris = nextUris;
    }

    private static CfgsAnalysisResult GetInteractiveAnalysis(DocumentState state)
        => ShouldUseLastSuccessfulAnalysis(state.Analysis) && state.LastSuccessfulAnalysis is not null
            ? state.LastSuccessfulAnalysis
            : state.Analysis;

    private static bool ShouldUseLastSuccessfulAnalysis(CfgsAnalysisResult analysis)
        => !HasAnyDiagnostics(analysis)
            ? false
            : analysis.AllSymbols.Count == 0 && analysis.SemanticModel.Symbols.Count == 0;

    private static bool IsSuccessfulInteractiveAnalysis(CfgsAnalysisResult analysis)
        => !HasAnyDiagnostics(analysis);

    private static bool HasAnyDiagnostics(CfgsAnalysisResult analysis)
        => analysis.DiagnosticsByUri.Values.Any(static diagnostics => diagnostics.Count > 0);

    private IReadOnlyDictionary<string, string> GetOpenDocumentSources()
    {
        Dictionary<string, string> sources = OperatingSystem.IsWindows()
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.Ordinal);

        foreach (DocumentState state in _documents.Values)
        {
            sources[state.Uri] = state.Text;
            string? localPath = TryGetLocalPath(state.Uri);
            if (!string.IsNullOrWhiteSpace(localPath))
                sources[localPath!] = state.Text;
        }

        return sources;
    }

    private IReadOnlyList<CfgsSymbol> FindWorkspaceReferences(DocumentState anchorState, CfgsSymbol symbol, bool includeDeclaration)
    {
        if (!_analyzer.SupportsWorkspaceSymbolMatching(symbol))
            return _analyzer.FindReferences(anchorState.Analysis, symbol, includeDeclaration);

        return EnumerateWorkspaceStates(anchorState.Uri)
            .SelectMany(state => _analyzer.FindReferences(state.Analysis, symbol, includeDeclaration))
            .GroupBy(reference => new ReferenceKey(reference.Uri, reference.SelectionRange), GetReferenceKeyComparer())
            .Select(group => group.First())
            .OrderBy(reference => reference.Uri, GetUriOrderComparer())
            .ThenBy(reference => reference.SelectionRange.Start.Line)
            .ThenBy(reference => reference.SelectionRange.Start.Character)
            .ToList();
    }

    private CfgsRenameResult? RenameWorkspaceSymbol(DocumentState anchorState, CfgsSymbol symbol, string newName)
    {
        if (!_analyzer.SupportsWorkspaceSymbolMatching(symbol))
            return _analyzer.RenameSymbol(anchorState.Analysis, symbol, newName);

        List<CfgsTextEdit> edits = EnumerateWorkspaceStates(anchorState.Uri)
            .Select(state => _analyzer.RenameSymbol(state.Analysis, symbol, newName))
            .Where(rename => rename is not null)
            .SelectMany(rename => rename!.Edits)
            .GroupBy(edit => new ReferenceKey(edit.Uri, edit.Range), GetReferenceKeyComparer())
            .Select(group => group.First())
            .OrderBy(edit => edit.Uri, GetUriOrderComparer())
            .ThenBy(edit => edit.Range.Start.Line)
            .ThenBy(edit => edit.Range.Start.Character)
            .ToList();

        return edits.Count == 0 ? null : new CfgsRenameResult(edits);
    }

    private IReadOnlyList<object> GetCodeActions(DocumentState state, string uri, RangeInfo requestRange)
    {
        if (!state.Analysis.DiagnosticsByUri.TryGetValue(uri, out List<CfgsDiagnostic>? diagnostics) || diagnostics.Count == 0)
            return [];

        List<CfgsCodeAction> actions = [];
        foreach (CfgsDiagnostic diagnostic in diagnostics)
        {
            if (!Intersects(diagnostic.Range, requestRange))
                continue;

            AddQuickFixes(actions, state, diagnostic);
        }

        return actions
            .GroupBy(static action => action.Title, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select(ToCodeActionPayload)
            .ToList();
    }

    private void AddQuickFixes(List<CfgsCodeAction> actions, DocumentState state, CfgsDiagnostic diagnostic)
    {
        if (!string.Equals(diagnostic.Code, "ParseError", StringComparison.Ordinal))
            return;

        if (string.Equals(diagnostic.Message, "expected string after 'from' in import statement", StringComparison.Ordinal))
        {
            actions.Add(new CfgsCodeAction(
                "Insert import path string",
                "quickfix",
                [diagnostic],
                new CfgsWorkspaceEdit([], [new CfgsTextEdit(state.Uri, diagnostic.Range, "\"./module.cfs\"")])));
        }

        if (TryCreateMissingImportModuleQuickFix(state, diagnostic, out CfgsCodeAction? createModuleAction))
            actions.Add(createModuleAction!);

        if (TryCreateMissingExportQuickFix(state, diagnostic, out CfgsCodeAction? createExportAction))
            actions.Add(createExportAction!);
    }

    private bool TryCreateMissingImportModuleQuickFix(DocumentState state, CfgsDiagnostic diagnostic, out CfgsCodeAction? action)
    {
        action = null;

        Match match = MissingImportPathRegex.Match(diagnostic.Message);
        if (!match.Success)
            return false;

        string rawPath = match.Groups["path"].Value;
        if (!TryResolveLocalImportPath(state.Uri, rawPath, out string targetPath, out string targetUri))
            return false;

        string lineEnding = DetectLineEnding(state.Text);
        string moduleText = BuildMissingImportModuleStub(state.Text, diagnostic.Range.Start.Line, lineEnding);
        List<CfgsTextEdit> edits = moduleText.Length == 0
            ? []
            : [new CfgsTextEdit(targetUri, new RangeInfo(new PositionInfo(0, 0), new PositionInfo(0, 0)), moduleText)];

        action = new CfgsCodeAction(
            $"Create missing module '{Path.GetFileName(targetPath)}'",
            "quickfix",
            [diagnostic],
            new CfgsWorkspaceEdit([new CfgsCreateFile(targetUri)], edits));
        return true;
    }

    private bool TryCreateMissingExportQuickFix(DocumentState state, CfgsDiagnostic diagnostic, out CfgsCodeAction? action)
    {
        action = null;

        Match match = MissingExportRegex.Match(diagnostic.Message);
        if (!match.Success)
            return false;

        string exportName = match.Groups["name"].Value;
        string rawPath = match.Groups["path"].Value;
        if (!TryResolveLocalImportPath(state.Uri, rawPath, out string targetPath, out string targetUri))
            return false;

        string existingText = GetDocumentText(targetUri);
        string lineEnding = DetectLineEnding(existingText);
        string prefix = existingText.Length == 0
            ? string.Empty
            : EndsWithNewLine(existingText)
                ? string.Empty
                : lineEnding;
        string stub = $"{prefix}export const {exportName} = null;{lineEnding}";

        action = new CfgsCodeAction(
            $"Create export '{exportName}' in '{Path.GetFileName(targetPath)}'",
            "quickfix",
            [diagnostic],
            new CfgsWorkspaceEdit([], [new CfgsTextEdit(targetUri, CreateAppendRange(existingText), stub)]));
        return true;
    }

    private string GetDocumentText(string uri)
        => _documents.TryGetValue(uri, out DocumentState? state)
            ? state.Text
            : LoadDocumentText(uri);

    private bool TryResolveLocalImportPath(string sourceUri, string rawPath, out string targetPath, out string targetUri)
    {
        targetPath = string.Empty;
        targetUri = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPath) || rawPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return false;

        if (Uri.TryCreate(rawPath, UriKind.Absolute, out Uri? absoluteImportUri) && !absoluteImportUri.IsFile)
            return false;

        string? sourcePath = TryGetLocalPath(sourceUri);
        if (string.IsNullOrWhiteSpace(sourcePath))
            return false;

        string candidatePath = Path.IsPathFullyQualified(rawPath)
            ? rawPath
            : Path.Combine(Path.GetDirectoryName(sourcePath)!, rawPath.Replace('/', Path.DirectorySeparatorChar));
        string fullPath = Path.GetFullPath(candidatePath);
        if (!IsPathWithinWorkspace(sourceUri, fullPath))
            return false;

        targetPath = fullPath;
        targetUri = new Uri(fullPath).AbsoluteUri;
        return true;
    }

    private bool IsPathWithinWorkspace(string anchorUri, string targetPath)
    {
        string? workspaceRoot = ResolveWorkspaceRootPath(anchorUri);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return false;

        string relative = Path.GetRelativePath(workspaceRoot, targetPath);
        return !relative.StartsWith("..", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
               && !Path.IsPathFullyQualified(relative);
    }

    private static string BuildMissingImportModuleStub(string sourceText, int line, string lineEnding)
    {
        List<string> exportNames = ExtractImportedSymbolNames(sourceText, line);
        if (exportNames.Count == 0)
            return string.Empty;

        return string.Join(lineEnding, exportNames.Select(static name => $"export const {name} = null;")) + lineEnding;
    }

    private static List<string> ExtractImportedSymbolNames(string sourceText, int line)
    {
        string lineText = GetLineText(sourceText, line);
        if (string.IsNullOrWhiteSpace(lineText))
            return [];

        Match namedListMatch = Regex.Match(lineText, @"^\s*import\s*\{\s*(?<imports>[^}]+)\}\s*from\b");
        if (namedListMatch.Success)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (string part in namedListMatch.Groups["imports"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string importName = part.Split(new[] { " as " }, 2, StringSplitOptions.TrimEntries)[0];
                if (!string.IsNullOrWhiteSpace(importName))
                    names.Add(importName);
            }

            return names.OrderBy(static name => name, StringComparer.Ordinal).ToList();
        }

        Match singleImportMatch = Regex.Match(lineText, @"^\s*import\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+from\b");
        if (singleImportMatch.Success)
            return [singleImportMatch.Groups["name"].Value];

        return [];
    }

    private static string GetLineText(string sourceText, int line)
    {
        string normalized = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = normalized.Split('\n');
        return line >= 0 && line < lines.Length ? lines[line] : string.Empty;
    }

    private static string DetectLineEnding(string text)
        => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : Environment.NewLine;

    private static bool EndsWithNewLine(string text)
        => text.EndsWith("\r\n", StringComparison.Ordinal) || text.EndsWith('\n');

    private static RangeInfo CreateAppendRange(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new RangeInfo(new PositionInfo(0, 0), new PositionInfo(0, 0));

        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = normalized.Split('\n');
        int line = lines.Length - 1;
        int character = lines[line].Length;
        return new RangeInfo(new PositionInfo(line, character), new PositionInfo(line, character));
    }

    private static bool Intersects(RangeInfo left, RangeInfo right)
        => ComparePositions(left.End, right.Start) >= 0 && ComparePositions(right.End, left.Start) >= 0;

    private static int ComparePositions(PositionInfo left, PositionInfo right)
    {
        int lineComparison = left.Line.CompareTo(right.Line);
        return lineComparison != 0 ? lineComparison : left.Character.CompareTo(right.Character);
    }

    private IEnumerable<DocumentState> EnumerateWorkspaceStates(string anchorUri)
    {
        HashSet<string> seen = new(GetUriComparer());

        foreach (DocumentState openState in _documents.Values)
        {
            if (seen.Add(openState.Uri))
                yield return openState;
        }

        foreach (string uri in EnumerateWorkspaceDocumentUris(anchorUri))
        {
            if (!seen.Add(uri))
                continue;

            yield return EnsureDocument(uri);
        }
    }

    private IEnumerable<string> EnumerateWorkspaceDocumentUris(string anchorUri)
    {
        string? rootPath = ResolveWorkspaceRootPath(anchorUri);
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            yield break;

        foreach (string filePath in EnumerateCfgsFiles(rootPath!))
            yield return new Uri(filePath).AbsoluteUri;
    }

    private string? ResolveWorkspaceRootPath(string anchorUri)
    {
        if (!string.IsNullOrWhiteSpace(_workspaceRootPath) && Directory.Exists(_workspaceRootPath))
            return _workspaceRootPath;

        string? localPath = TryGetLocalPath(anchorUri);
        string? documentDirectory = localPath is null ? null : Path.GetDirectoryName(localPath);
        return !string.IsNullOrWhiteSpace(documentDirectory) && Directory.Exists(documentDirectory) ? documentDirectory : null;
    }

    private static IEnumerable<string> EnumerateCfgsFiles(string rootPath)
    {
        Stack<string> pending = new();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (string directory in childDirectories)
            {
                string name = Path.GetFileName(directory);
                if (ShouldSkipDirectory(name))
                    continue;

                pending.Push(directory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, "*.cfs");
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
                yield return Path.GetFullPath(file);
        }
    }

    private static bool ShouldSkipDirectory(string name)
        => name.Equals(".git", StringComparison.OrdinalIgnoreCase)
           || name.Equals(".vs", StringComparison.OrdinalIgnoreCase)
           || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
           || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
           || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
           || name.Equals("dist", StringComparison.OrdinalIgnoreCase);

    private async Task PublishDiagnosticsAsync(string uri, IReadOnlyList<CfgsDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        await SendNotificationAsync("textDocument/publishDiagnostics", new
        {
            uri,
            diagnostics = diagnostics.Select(ToDiagnosticPayload).ToList()
        }, cancellationToken);
    }

    private async Task<string?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        int contentLength = -1;

        while (true)
        {
            string? line = await ReadHeaderLineAsync(cancellationToken);
            if (line is null)
                return null;

            if (line.Length == 0)
                break;

            int separator = line.IndexOf(':');
            if (separator < 0)
                continue;

            string name = line[..separator];
            string value = line[(separator + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(value, CultureInfo.InvariantCulture);
        }

        if (contentLength < 0)
            return null;

        byte[] content = new byte[contentLength];
        await ReadExactlyAsync(content, cancellationToken);
        return Encoding.UTF8.GetString(content);
    }

    private async Task<string?> ReadHeaderLineAsync(CancellationToken cancellationToken)
    {
        List<byte> bytes = [];

        while (true)
        {
            byte[] buffer = new byte[1];
            int read = await _input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                if (bytes.Count == 0)
                    return null;
                break;
            }

            byte current = buffer[0];
            if (current == (byte)'\n')
                break;

            if (current != (byte)'\r')
                bytes.Add(current);
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private async Task ReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await _input.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of LSP input stream.");
            offset += read;
        }
    }

    private Task SendResponseAsync(JsonElement id, object? result, CancellationToken cancellationToken)
        => SendPayloadAsync(new
        {
            jsonrpc = "2.0",
            id = JsonSerializer.Deserialize<object>(id.GetRawText(), _jsonOptions),
            result
        }, cancellationToken);

    private Task SendErrorResponseAsync(JsonElement id, int code, string message, CancellationToken cancellationToken)
        => SendPayloadAsync(new
        {
            jsonrpc = "2.0",
            id = JsonSerializer.Deserialize<object>(id.GetRawText(), _jsonOptions),
            error = new
            {
                code,
                message
            }
        }, cancellationToken);

    private Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
        => SendPayloadAsync(new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        }, cancellationToken);

    private async Task SendPayloadAsync(object payload, CancellationToken cancellationToken)
    {
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
        byte[] headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {jsonBytes.Length}\r\n\r\n");

        await _output.WriteAsync(headerBytes, cancellationToken);
        await _output.WriteAsync(jsonBytes, cancellationToken);
        await _output.FlushAsync(cancellationToken);
    }

    private static object ToDiagnosticPayload(CfgsDiagnostic diagnostic) => new
    {
        range = ToRangePayload(diagnostic.Range),
        severity = diagnostic.Severity,
        source = diagnostic.Source,
        code = diagnostic.Code,
        message = diagnostic.Message
    };

    private static object ToDocumentSymbolPayload(CfgsSymbol symbol) => new
    {
        name = symbol.Name,
        detail = symbol.Detail,
        kind = symbol.Kind,
        range = ToRangePayload(symbol.Range),
        selectionRange = ToRangePayload(symbol.SelectionRange),
        children = symbol.Children.Select(ToDocumentSymbolPayload).ToList()
    };

    private static void CollectFoldingRanges(IReadOnlyList<CfgsSymbol> symbols, List<object> ranges)
    {
        foreach (CfgsSymbol symbol in symbols)
        {
            int startLine = symbol.Range.Start.Line;
            int endLine = symbol.Range.End.Line;
            if (endLine > startLine)
            {
                ranges.Add(new
                {
                    startLine,
                    endLine,
                    kind = "region"
                });
            }
            CollectFoldingRanges(symbol.Children, ranges);
        }
    }

    private static object ToLocationPayload(CfgsSymbol symbol) => new
    {
        uri = symbol.Uri,
        range = ToRangePayload(symbol.SelectionRange)
    };

    private static object ToCompletionPayload(CfgsCompletionItem item) => new
    {
        label = item.Label,
        kind = item.Kind,
        detail = item.Detail
    };

    private static object ToSignaturePayload(CfgsSignature signature) => new
    {
        label = signature.Label,
        parameters = signature.Parameters.Select(parameter => new
        {
            label = parameter
        }).ToList()
    };

    private static object ToTextEditPayload(CfgsTextEdit edit) => new
    {
        range = ToRangePayload(edit.Range),
        newText = edit.NewText
    };

    private static object ToCodeActionPayload(CfgsCodeAction action) => new
    {
        title = action.Title,
        kind = action.Kind,
        diagnostics = action.Diagnostics.Select(ToDiagnosticPayload).ToList(),
        edit = ToWorkspaceEditPayload(action.Edit)
    };

    private static object ToWorkspaceEditPayload(CfgsWorkspaceEdit edit)
    {
        if (edit.CreateFiles.Count > 0)
        {
            List<object> documentChanges = [];
            foreach (CfgsCreateFile createFile in edit.CreateFiles)
            {
                documentChanges.Add(new
                {
                    kind = "create",
                    uri = createFile.Uri,
                    options = new
                    {
                        ignoreIfExists = true,
                        overwrite = false
                    }
                });
            }

            foreach (IGrouping<string, CfgsTextEdit> group in edit.TextEdits.GroupBy(static change => change.Uri, GetUriOrderComparer()))
            {
                documentChanges.Add(new
                {
                    textDocument = new
                    {
                        uri = group.Key,
                        version = (int?)null
                    },
                    edits = group.Select(ToTextEditPayload).ToList()
                });
            }

            return new
            {
                documentChanges
            };
        }

        return new
        {
            changes = edit.TextEdits
                .GroupBy(static change => change.Uri, GetUriOrderComparer())
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(ToTextEditPayload).ToList(),
                    GetUriOrderComparer())
        };
    }

    private static object ToRangePayload(RangeInfo range) => new
    {
        start = new
        {
            line = range.Start.Line,
            character = range.Start.Character
        },
        end = new
        {
            line = range.End.Line,
            character = range.End.Character
        }
    };

    private static RangeInfo ToRangeInfo(JsonElement range)
    {
        JsonElement start = range.GetProperty("start");
        JsonElement end = range.GetProperty("end");
        return new RangeInfo(
            new PositionInfo(start.GetProperty("line").GetInt32(), start.GetProperty("character").GetInt32()),
            new PositionInfo(end.GetProperty("line").GetInt32(), end.GetProperty("character").GetInt32()));
    }

    private static string? TryGetLocalPath(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) && parsed.IsFile)
            return Path.GetFullPath(parsed.LocalPath);

        return Path.IsPathFullyQualified(uri) ? Path.GetFullPath(uri) : null;
    }

    private static string? TryGetWorkspaceRootPath(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Undefined || parameters.ValueKind == JsonValueKind.Null)
            return null;

        if (parameters.TryGetProperty("workspaceFolders", out JsonElement workspaceFolders) &&
            workspaceFolders.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement folder in workspaceFolders.EnumerateArray())
            {
                string? candidate = folder.TryGetProperty("uri", out JsonElement uriElement)
                    ? TryGetLocalPath(uriElement.GetString() ?? string.Empty)
                    : folder.TryGetProperty("path", out JsonElement pathElement)
                        ? pathElement.GetString()
                        : null;

                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        if (parameters.TryGetProperty("rootUri", out JsonElement rootUriElement))
        {
            string? rootPath = TryGetLocalPath(rootUriElement.GetString() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath))
                return Path.GetFullPath(rootPath);
        }

        if (parameters.TryGetProperty("rootPath", out JsonElement rootPathElement))
        {
            string? rootPath = rootPathElement.GetString();
            if (!string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath))
                return Path.GetFullPath(rootPath);
        }

        return null;
    }

    private static IEqualityComparer<string> GetUriComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparer GetUriOrderComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static IEqualityComparer<ReferenceKey> GetReferenceKeyComparer()
        => ReferenceKeyComparer.Instance;

    private sealed record CfgsCodeAction(
        string Title,
        string Kind,
        IReadOnlyList<CfgsDiagnostic> Diagnostics,
        CfgsWorkspaceEdit Edit);

    private sealed record CfgsWorkspaceEdit(
        IReadOnlyList<CfgsCreateFile> CreateFiles,
        IReadOnlyList<CfgsTextEdit> TextEdits);

    private sealed record CfgsCreateFile(string Uri);

    private sealed class DocumentState
    {
        public DocumentState(string uri, string text, int version)
        {
            Uri = uri;
            Text = text;
            Version = version;
            Analysis = new CfgsAnalysisResult(uri, uri, text, new Dictionary<string, List<CfgsDiagnostic>>(), [], [], new CfgsSemanticModel([], new Dictionary<string, CfgsSymbol>(), []));
            LastSuccessfulAnalysis = null;
            PublishedDiagnosticUris = new HashSet<string>(GetUriComparer());
        }

        public string Uri { get; }

        public string Text { get; set; }

        public int Version { get; set; }

        public CfgsAnalysisResult Analysis { get; set; }

        public CfgsAnalysisResult? LastSuccessfulAnalysis { get; set; }

        public HashSet<string> PublishedDiagnosticUris { get; set; }
    }

    private readonly record struct ReferenceKey(string Uri, RangeInfo Range);

    private sealed class ReferenceKeyComparer : IEqualityComparer<ReferenceKey>
    {
        public static readonly ReferenceKeyComparer Instance = new();

        public bool Equals(ReferenceKey x, ReferenceKey y)
            => string.Equals(x.Uri, y.Uri, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
               && x.Range.Equals(y.Range);

        public int GetHashCode(ReferenceKey obj)
        {
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            return HashCode.Combine(comparer.GetHashCode(obj.Uri), obj.Range);
        }
    }
}
