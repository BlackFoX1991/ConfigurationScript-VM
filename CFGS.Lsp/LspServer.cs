using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CFGS_VM.Tooling;

namespace CFGS.Lsp;

internal sealed class LspServer
{
    private static readonly Regex MissingImportPathRegex = new("^import path not found '(?<path>[^']+)'", RegexOptions.Compiled);
    private static readonly Regex MissingExportRegex = new("^Could not find export '(?<name>[^']+)' in import '(?<path>[^']+)'$", RegexOptions.Compiled);
    // Erfasst den Pfad-String eines Import-Statements (bare/named/default), damit bei einer Aenderung an
    // einer importierten Datei genau die offenen Dateien neu analysiert werden, die sie importieren.
    private static readonly Regex ImportPathLineRegex = new(@"^\s*import\b[^""]*""(?<path>[^""]+)""", RegexOptions.Compiled);
    private readonly CfgsAnalyzer _analyzer;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly JsonSerializerOptions _jsonOptions = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingAnalysisByUri = OperatingSystem.IsWindows()
        ? new(StringComparer.OrdinalIgnoreCase)
        : new(StringComparer.Ordinal);
    // Second, longer-debounced track that runs the full analysis WITH the codegen pass once the user pauses
    // typing (or on save). didChange only runs the fast, codegen-free analysis; this catches the deeper
    // compile diagnostics without paying for a full compile on every keystroke.
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingFullAnalysisByUri = OperatingSystem.IsWindows()
        ? new(StringComparer.OrdinalIgnoreCase)
        : new(StringComparer.Ordinal);
    // Threadsicher, weil lese-only Requests (references, rename, codeLens/resolve, ...) jetzt nebenlaeufig
    // zum seriellen Notification-Strom laufen und dabei ueber EnsureDocument in diese Map schreiben koennen.
    private readonly ConcurrentDictionary<string, DocumentState> _documents = OperatingSystem.IsWindows()
        ? new(StringComparer.OrdinalIgnoreCase)
        : new(StringComparer.Ordinal);

    private static readonly TimeSpan DidChangeAnalysisDebounce = TimeSpan.FromMilliseconds(150);
    // The full (codegen) analysis waits until typing has paused for this long, so it runs at most once per
    // burst instead of on every keystroke.
    private static readonly TimeSpan FullAnalysisIdleDebounce = TimeSpan.FromMilliseconds(700);
    private string? _workspaceRootPath;
    private bool _shutdownRequested;
    // Wird beim initialize aus den Client-Faehigkeiten gelesen: unterstuetzt der Client die dynamische
    // Registrierung von Datei-Watchern, registriert der Server nach "initialized" einen **/*.cfs-Watcher,
    // damit didChangeWatchedFiles auch fuer nicht offene, importierte Dateien eintrifft.
    private bool _supportsDynamicWatchedFiles;
    // Fortlaufende id fuer server-initiierte Requests (z.B. client/registerCapability).
    private int _nextServerRequestId;

    // Live-Lint (Style + AST-Regeln beim Tippen) ist per Client-Option schaltbar und standardmaessig aus,
    // damit Tools/Tests, die die Option nicht senden, nur Compile-/Semantik-Diagnosen sehen. Die VS-Code-
    // Extension setzt initializationOptions.liveLint (Default an, ueber cfgs.lint.live abschaltbar).
    private bool _liveLintEnabled;

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
            string? json;
            try
            {
                json = await ReadMessageAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Ein Lesefehler bedeutet einen desynchronisierten/kaputten Eingabestrom - daraus laesst sich
                // die Frame-Grenze nicht mehr sicher wiederherstellen, also sauber beenden statt endlos auf
                // Muell zu laufen.
                await TryLogAsync($"[CFGS-LSP] input stream error, shutting down: {ex.Message}");
                break;
            }

            if (json is null)
                break;

            // Eine einzelne fehlerhafte oder unerwartete Nachricht (kaputtes JSON, ein Sonderfall in einem
            // Handler) darf NIE den gesamten Server beenden. Frueher lief RunAsync ungeschuetzt: eine Ausnahme
            // hier riss den Prozess mit und der Nutzer sah "der LSP ist tot" (keine Diagnosen/Referenzen mehr).
            // Der Frame wurde bereits vollstaendig gelesen, daher ist der Strom weiter synchron und wir koennen
            // mit der naechsten Nachricht fortfahren.
            try
            {
                string method;
                bool hasId;
                using (JsonDocument peek = JsonDocument.Parse(json))
                {
                    method = peek.RootElement.TryGetProperty("method", out JsonElement m) ? m.GetString() ?? string.Empty : string.Empty;
                    hasId = peek.RootElement.TryGetProperty("id", out _);
                }

                // Reihenfolge-kritische Nachrichten (alle Notifications inkl. didChange, plus der
                // initialize/shutdown-Handshake) werden weiterhin seriell verarbeitet, damit Textzustand und
                // Lebenszyklus konsistent bleiben. Alle uebrigen id-Requests sind lesend und potenziell
                // workspace-weit (references, rename, codeLens/resolve, workspace/symbol, callHierarchy); sie
                // laufen nebenlaeufig, damit ein langsamer Scan nicht die nachfolgenden Nachrichten blockiert.
                if (MustHandleInline(method, hasId))
                {
                    using JsonDocument document = JsonDocument.Parse(json);
                    if (!await HandleMessageAsync(document.RootElement, cancellationToken))
                        break;
                }
                else
                {
                    DispatchRequestInBackground(json, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await TryLogAsync($"[CFGS-LSP] error handling message, continuing: {ex.Message}");
            }
        }
    }

    private static async Task TryLogAsync(string message)
    {
        try
        {
            await Console.Error.WriteLineAsync(message);
        }
        catch
        {
            // Wenn selbst stderr nicht mehr geht, gibt es keinen sinnvollen Ausweg mehr.
        }
    }

    private static bool MustHandleInline(string method, bool hasId)
    {
        // Notifications (kein id) muessen in Reihenfolge bleiben; initialize/shutdown sind der Handshake.
        if (!hasId)
            return true;

        return method is "initialize" or "shutdown";
    }

    private void DispatchRequestInBackground(string json, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                await HandleMessageAsync(document.RootElement, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await TrySendRequestFailureAsync(json, ex, cancellationToken);
            }
        }, CancellationToken.None);
    }

    private async Task TrySendRequestFailureAsync(string json, Exception ex, CancellationToken cancellationToken)
    {
        // Ein Hintergrund-Request darf nicht stumm haengen bleiben: dem Client eine JSON-RPC-Fehlerantwort
        // fuer die ausstehende id schicken, damit VS Code die Anfrage nicht bis zum Timeout offen haelt.
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("id", out JsonElement idElement))
                await SendErrorResponseAsync(idElement, -32603, ex.Message, cancellationToken);
        }
        catch
        {
            // Best effort: ein Fehler beim Melden des Fehlers darf den Server nicht zusaetzlich stoeren.
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
                    _liveLintEnabled = TryGetLiveLintOption(parameters);
                    _supportsDynamicWatchedFiles = TryGetDynamicWatchedFilesSupport(parameters);
                    if (hasId)
                    {
                        await SendResponseAsync(idElement, new
                        {
                            capabilities = new
                            {
                                // Object form so we can advertise `save`: clients then send didSave, which
                                // triggers the full (compile) analysis. openClose + incremental change as before.
                                textDocumentSync = new
                                {
                                    openClose = true,
                                    change = 2,
                                    save = true
                                },
                                definitionProvider = true,
                                typeDefinitionProvider = true,
                                implementationProvider = true,
                                hoverProvider = true,
                                documentHighlightProvider = true,
                                referencesProvider = true,
                                codeActionProvider = new
                                {
                                    codeActionKinds = new[] { "quickfix", "refactor" }
                                },
                                renameProvider = new
                                {
                                    prepareProvider = true
                                },
                                documentSymbolProvider = true,
                                workspaceSymbolProvider = true,
                                foldingRangeProvider = true,
                                selectionRangeProvider = true,
                                documentFormattingProvider = true,
                                documentLinkProvider = new
                                {
                                    resolveProvider = false
                                },
                                codeLensProvider = new
                                {
                                    resolveProvider = true
                                },
                                inlayHintProvider = true,
                                callHierarchyProvider = true,
                                signatureHelpProvider = new
                                {
                                    triggerCharacters = new[] { "(", "," },
                                    retriggerCharacters = new[] { "," }
                                },
                                completionProvider = new
                                {
                                    resolveProvider = false,
                                    triggerCharacters = new[] { "." }
                                },
                                semanticTokensProvider = new
                                {
                                    legend = new
                                    {
                                        tokenTypes = CfgsAnalyzer.SemanticTokenTypes,
                                        tokenModifiers = CfgsAnalyzer.SemanticTokenModifiers
                                    },
                                    range = true,
                                    full = true
                                }
                            },
                            serverInfo = new
                            {
                                name = "CFGS Language Server",
                                version = "0.2.0"
                            }
                        }, cancellationToken);
                    }
                    return true;

                case "initialized":
                    await RegisterFileWatchersAsync(cancellationToken);
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

                case "textDocument/didSave":
                    HandleDidSave(parameters, cancellationToken);
                    return true;

                case "textDocument/documentSymbol":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        await SendResponseAsync(idElement, analysis.DocumentSymbols.Select(ToDocumentSymbolPayload).ToList(), cancellationToken);
                    }
                    return true;

                case "textDocument/foldingRange":
                    if (hasId)
                    {
                        string uri = GetTextDocumentUri(parameters);
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        List<object> ranges = new();
                        CollectFoldingRanges(analysis.DocumentSymbols, ranges);
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
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        IReadOnlyList<CfgsSymbol> definitions = _analyzer.FindDefinitions(analysis, line, character);
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
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        CfgsSymbol? symbol = _analyzer.FindHoverSymbol(analysis, line, character);
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
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        IReadOnlyList<CfgsSymbol> highlights = _analyzer.FindDocumentHighlights(analysis, line, character);
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
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        CfgsSymbol? symbol = _analyzer.ResolveSymbol(analysis, line, character);
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
                        (int line, int character) = GetPosition(parameters);
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        List<object> items = _analyzer.GetCompletions(analysis, line, character)
                            .Select(ToCompletionPayload).ToList();
                        if (_analyzer.ShouldIncludeSnippetCompletions(analysis, line, character))
                            items.AddRange(_analyzer.GetSnippetCompletions().Select(ToSnippetCompletionPayload));
                        await SendResponseAsync(idElement, items, cancellationToken);
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

                case "textDocument/semanticTokens/range":
                    if (hasId)
                    {
                        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
                        RangeInfo requestRange = ToRangeInfo(parameters.GetProperty("range"));
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        IReadOnlyList<int> data = _analyzer.GetSemanticTokens(analysis, requestRange);
                        await SendResponseAsync(idElement, new { data }, cancellationToken);
                    }
                    return true;

                case "textDocument/typeDefinition":
                    if (hasId)
                    {
                        string uri = GetTextDocumentUri(parameters);
                        (int line, int character) = GetPosition(parameters);
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        IReadOnlyList<CfgsSymbol> typeDefs = _analyzer.FindTypeDefinition(analysis, line, character);
                        object? payload = typeDefs.Count == 0 ? null : typeDefs.Select(ToLocationPayload).ToList();
                        await SendResponseAsync(idElement, payload, cancellationToken);
                    }
                    return true;

                case "textDocument/implementation":
                    if (hasId)
                    {
                        string uri = GetTextDocumentUri(parameters);
                        (int line, int character) = GetPosition(parameters);
                        DocumentState state = EnsureDocument(uri);
                        IReadOnlyList<CfgsSymbol> implementations = FindWorkspaceImplementations(state, line, character);
                        object? payload = implementations.Count == 0 ? null : implementations.Select(ToLocationPayload).ToList();
                        await SendResponseAsync(idElement, payload, cancellationToken);
                    }
                    return true;

                case "workspace/symbol":
                    if (hasId)
                    {
                        string query = parameters.TryGetProperty("query", out JsonElement queryEl) ? queryEl.GetString() ?? string.Empty : string.Empty;
                        IReadOnlyList<object> workspaceSymbols = FindWorkspaceSymbols(query);
                        await SendResponseAsync(idElement, workspaceSymbols.Count == 0 ? null : workspaceSymbols, cancellationToken);
                    }
                    return true;

                case "textDocument/formatting":
                    if (hasId)
                    {
                        string uri = GetTextDocumentUri(parameters);
                        int tabSize = 4;
                        bool insertSpaces = true;
                        if (parameters.TryGetProperty("options", out JsonElement options))
                        {
                            if (options.TryGetProperty("tabSize", out JsonElement ts))
                                tabSize = ts.GetInt32();
                            if (options.TryGetProperty("insertSpaces", out JsonElement isp))
                                insertSpaces = isp.GetBoolean();
                        }
                        DocumentState state = EnsureDocument(uri);
                        IReadOnlyList<CfgsTextEdit> formatEdits = _analyzer.FormatDocument(state.Analysis, tabSize, insertSpaces);
                        await SendResponseAsync(idElement, formatEdits.Count == 0 ? null : formatEdits.Select(ToTextEditPayload).ToList(), cancellationToken);
                    }
                    return true;

                case "textDocument/documentLink":
                    if (hasId)
                    {
                        string uri = GetTextDocumentUri(parameters);
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        IReadOnlyList<CfgsDocumentLink> links = _analyzer.GetDocumentLinks(analysis);
                        await SendResponseAsync(idElement, links.Count == 0 ? null : links.Select(ToDocumentLinkPayload).ToList(), cancellationToken);
                    }
                    return true;

                case "textDocument/selectionRange":
                    if (hasId)
                    {
                        string uri = GetTextDocumentUri(parameters);
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        List<PositionInfo> positions = [];
                        if (parameters.TryGetProperty("positions", out JsonElement positionsEl))
                        {
                            foreach (JsonElement pos in positionsEl.EnumerateArray())
                            {
                                int pl = pos.TryGetProperty("line", out JsonElement pll) ? pll.GetInt32() : 0;
                                int pc = pos.TryGetProperty("character", out JsonElement pcc) ? pcc.GetInt32() : 0;
                                positions.Add(new PositionInfo(pl, pc));
                            }
                        }
                        IReadOnlyList<CfgsSelectionRange> selectionRanges = _analyzer.GetSelectionRanges(analysis, positions);
                        await SendResponseAsync(idElement, selectionRanges.Select(ToSelectionRangePayload).ToList(), cancellationToken);
                    }
                    return true;

                case "textDocument/codeLens":
                    if (hasId)
                    {
                        string uri = GetTextDocumentUri(parameters);
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        IReadOnlyList<CfgsCodeLens> codeLenses = _analyzer.GetCodeLenses(analysis);
                        await SendResponseAsync(idElement, codeLenses.Select(lens => ToUnresolvedCodeLensPayload(uri, lens)).ToList(), cancellationToken);
                    }
                    return true;

                case "codeLens/resolve":
                    if (hasId)
                    {
                        await SendResponseAsync(idElement, ResolveCodeLens(parameters), cancellationToken);
                    }
                    return true;

                case "textDocument/inlayHint":
                    if (hasId)
                    {
                        string uri = GetTextDocumentUri(parameters);
                        RangeInfo hintRange = parameters.TryGetProperty("range", out JsonElement rangeEl)
                            ? ToRangeInfo(rangeEl)
                            : new RangeInfo(new PositionInfo(0, 0), new PositionInfo(int.MaxValue, 0));
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        IReadOnlyList<CfgsInlayHint> inlayHints = _analyzer.GetInlayHints(analysis, hintRange);
                        await SendResponseAsync(idElement, inlayHints.Count == 0 ? null : inlayHints.Select(ToInlayHintPayload).ToList(), cancellationToken);
                    }
                    return true;

                case "textDocument/prepareCallHierarchy":
                    if (hasId)
                    {
                        string uri = GetTextDocumentUri(parameters);
                        (int line, int character) = GetPosition(parameters);
                        DocumentState state = EnsureDocument(uri);
                        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
                        CfgsCallHierarchyItem? item = _analyzer.PrepareCallHierarchy(analysis, line, character);
                        await SendResponseAsync(idElement, item is null ? null : new[] { ToCallHierarchyItemPayload(item) }, cancellationToken);
                    }
                    return true;

                case "callHierarchy/incomingCalls":
                    if (hasId)
                    {
                        CfgsCallHierarchyItem callItem = ParseCallHierarchyItem(parameters.GetProperty("item"));
                        IReadOnlyList<CfgsCallHierarchyIncomingCall> incoming = FindWorkspaceIncomingCalls(callItem);
                        await SendResponseAsync(idElement, incoming.Count == 0 ? null : incoming.Select(ToIncomingCallPayload).ToList(), cancellationToken);
                    }
                    return true;

                case "callHierarchy/outgoingCalls":
                    if (hasId)
                    {
                        CfgsCallHierarchyItem callItem = ParseCallHierarchyItem(parameters.GetProperty("item"));
                        IReadOnlyList<CfgsCallHierarchyOutgoingCall> outgoing = FindWorkspaceOutgoingCalls(callItem);
                        await SendResponseAsync(idElement, outgoing.Count == 0 ? null : outgoing.Select(ToOutgoingCallPayload).ToList(), cancellationToken);
                    }
                    return true;

                case "workspace/didChangeWatchedFiles":
                    HandleDidChangeWatchedFiles(parameters, cancellationToken);
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
        CancelAllPendingAnalysis(uri);
        // Initial diagnostics for a freshly opened file are the full set (including the compile pass).
        await AnalyzeAndPublishAsync(state, cancellationToken, runCompilePass: true);

        // Opening a file makes its (possibly unsaved) buffer the authoritative source for anyone importing
        // it, so open importers may now resolve differently: refresh them.
        string? openedPath = TryGetLocalPath(uri);
        if (openedPath is not null)
            ReanalyzeImportersOf(openedPath, uri, cancellationToken);
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
            state.Text = ApplyContentChanges(state.Text, contentChanges);

        // Fast diagnostics right away; the deeper compile check runs once typing pauses.
        ScheduleFastAnalysis(state, cancellationToken);
        ScheduleFullAnalysis(state, cancellationToken, FullAnalysisIdleDebounce);
    }

    private void HandleDidSave(JsonElement parameters, CancellationToken cancellationToken)
    {
        string uri = GetTextDocumentUri(parameters);
        if (_documents.TryGetValue(uri, out DocumentState? state))
        {
            // Saving is an explicit request for the deep (compile) diagnostics, so run the full analysis
            // promptly rather than waiting out the idle debounce.
            ScheduleFullAnalysis(state, cancellationToken, TimeSpan.FromMilliseconds(50));
        }

        // Saving a file changes what everyone importing it sees on disk. Refresh the open importers so a
        // "import path not found" / "unknown namespace" no longer lingers after the imported file is fixed.
        string? savedPath = TryGetLocalPath(uri);
        if (savedPath is not null)
            ReanalyzeImportersOf(savedPath, uri, cancellationToken);
    }

    private async Task HandleDidCloseAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string uri = GetTextDocumentUri(parameters);
        CancelAllPendingAnalysis(uri);
        if (_documents.TryRemove(uri, out DocumentState? state))
        {
            foreach (string publishedUri in state.PublishedDiagnosticUris)
                await PublishDiagnosticsAsync(publishedUri, [], cancellationToken);
        }
        else
        {
            await PublishDiagnosticsAsync(uri, [], cancellationToken);
        }

        // Closing a file drops its buffer overlay; open importers now resolve it from disk instead, which
        // can change their diagnostics.
        string? closedPath = TryGetLocalPath(uri);
        if (closedPath is not null)
            ReanalyzeImportersOf(closedPath, uri, cancellationToken);
    }

    private void HandleDidChangeWatchedFiles(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("changes", out JsonElement changes) ||
            changes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement change in changes.EnumerateArray())
        {
            string? changedUri = change.TryGetProperty("uri", out JsonElement uriElement) ? uriElement.GetString() : null;
            string? changedPath = changedUri is null ? null : TryGetLocalPath(changedUri);
            if (changedPath is not null)
                ReanalyzeImportersOf(changedPath, changedUri, cancellationToken);
        }
    }

    // Wenn sich eine Datei aendert (Save, externe Aenderung, oeffnen/schliessen), stimmen die Diagnosen der
    // offenen Dateien, die sie importieren, nicht mehr - der LSP-Analyzer bekommt Aenderungen an anderen
    // Dateien sonst nicht mit. Hier werden genau diese Importeure erneut (voll) analysiert.
    private void ReanalyzeImportersOf(string changedFullPath, string? excludeUri, CancellationToken cancellationToken)
    {
        foreach (DocumentState importer in GetOpenDocumentsImporting(changedFullPath, excludeUri))
        {
            // Der Text des Importeurs selbst ist unveraendert, sein Ergebnis aber nicht. Deshalb den
            // Text-Kurzschluss in AnalyzeAndPublishAsync invalidieren, damit die volle Analyse wirklich neu
            // laeuft und die (Compile-)Diagnosen neu veroeffentlicht werden.
            InvalidateAnalysisCache(importer);
            ScheduleFullAnalysis(importer, cancellationToken, TimeSpan.FromMilliseconds(50));
        }
    }

    private IEnumerable<DocumentState> GetOpenDocumentsImporting(string changedFullPath, string? excludeUri)
    {
        string normalizedChanged;
        try
        {
            normalizedChanged = Path.GetFullPath(changedFullPath);
        }
        catch
        {
            yield break;
        }

        foreach (DocumentState state in _documents.Values)
        {
            if (excludeUri is not null && GetUriComparer().Equals(state.Uri, excludeUri))
                continue;

            if (DocumentImportsPath(state, normalizedChanged))
                yield return state;
        }
    }

    private static bool DocumentImportsPath(DocumentState state, string changedFullPath)
    {
        string? importerPath = TryGetLocalPath(state.Uri);
        if (importerPath is null)
            return false;

        string? importerDirectory = Path.GetDirectoryName(importerPath);
        if (string.IsNullOrEmpty(importerDirectory))
            return false;

        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (string line in state.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            Match match = ImportPathLineRegex.Match(line);
            if (!match.Success)
                continue;

            string rawPath = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(rawPath) ||
                rawPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                rawPath.Contains("://", StringComparison.Ordinal))
            {
                continue;
            }

            string candidate = Path.IsPathFullyQualified(rawPath)
                ? rawPath
                : Path.Combine(importerDirectory, rawPath.Replace('/', Path.DirectorySeparatorChar));

            string resolved;
            try
            {
                resolved = Path.GetFullPath(candidate);
            }
            catch
            {
                continue;
            }

            if (string.Equals(resolved, changedFullPath, pathComparison))
                return true;
        }

        return false;
    }

    private static void InvalidateAnalysisCache(DocumentState state)
    {
        state.AnalyzedText = null;
        state.AnalyzedTextWithCompile = null;
    }

    private async Task RegisterFileWatchersAsync(CancellationToken cancellationToken)
    {
        if (!_supportsDynamicWatchedFiles)
            return;

        // Ohne Datei-Watcher erfaehrt der Server nie von Aenderungen an importierten Dateien, die NICHT im
        // Editor offen sind - dann bleibt z.B. "import path not found" stehen, obwohl die Zieldatei laengst
        // angelegt wurde. Diese dynamische Registrierung laesst den Client didChangeWatchedFiles fuer alle
        // *.cfs schicken.
        await SendRequestAsync("client/registerCapability", new
        {
            registrations = new[]
            {
                new
                {
                    id = "cfgs-watched-files",
                    method = "workspace/didChangeWatchedFiles",
                    registerOptions = new
                    {
                        watchers = new[]
                        {
                            new { globPattern = "**/*.cfs" }
                        }
                    }
                }
            }
        }, cancellationToken);
    }

    private static bool TryGetDynamicWatchedFilesSupport(JsonElement parameters)
    {
        return parameters.ValueKind == JsonValueKind.Object &&
               parameters.TryGetProperty("capabilities", out JsonElement capabilities) &&
               capabilities.TryGetProperty("workspace", out JsonElement workspace) &&
               workspace.TryGetProperty("didChangeWatchedFiles", out JsonElement watchedFiles) &&
               watchedFiles.TryGetProperty("dynamicRegistration", out JsonElement dynamicRegistration) &&
               dynamicRegistration.ValueKind == JsonValueKind.True;
    }

    private DocumentState EnsureDocument(string uri)
    {
        // GetOrAdd statt TryGetValue+Insert: unter Nebenlaeufigkeit koennen zwei Requests dieselbe noch
        // ungeoeffnete Datei gleichzeitig anfordern; die Analyse ist seiteneffektfrei, also ist ein
        // seltener Doppel-Lauf harmlos und nur eine Instanz landet in der Map.
        return _documents.GetOrAdd(uri, u =>
        {
            string text = LoadDocumentText(u);
            DocumentState created = new(u, text, 0)
            {
                Analysis = _analyzer.Analyze(u, text, GetOpenDocumentSources(), _workspaceRootPath)
            };
            created.PublishedDiagnosticUris = created.Analysis.DiagnosticsByUri.Keys.ToHashSet(GetUriComparer());
            return created;
        });
    }

    private static string LoadDocumentText(string uri)
    {
        string? localPath = TryGetLocalPath(uri);
        if (localPath is not null && File.Exists(localPath))
            return File.ReadAllText(localPath);

        return string.Empty;
    }

    private static bool IsAlreadyAnalyzed(DocumentState state, string text, bool runCompilePass)
        => runCompilePass
            ? string.Equals(text, state.AnalyzedTextWithCompile, StringComparison.Ordinal)
            : string.Equals(text, state.AnalyzedText, StringComparison.Ordinal);

    private async Task AnalyzeAndPublishAsync(DocumentState state, CancellationToken cancellationToken, bool runCompilePass)
    {
        // Unveraenderter Text (bereits in der benoetigten Tiefe analysiert) -> nichts zu tun. Schneller
        // Vorab-Check ohne Lock, um Contention im Normalfall zu vermeiden.
        if (IsAlreadyAnalyzed(state, state.Text, runCompilePass))
            return;

        // Die schnelle (didChange) und die volle (idle/save) Analyse derselben Datei werden serialisiert,
        // damit sich ihre state-Mutationen und Publishes nicht ueberschneiden.
        await state.AnalysisLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string text = state.Text;
            if (IsAlreadyAnalyzed(state, text, runCompilePass))
                return;

            CfgsAnalysisResult analysis = _analyzer.Analyze(
                state.Uri, text, GetOpenDocumentSources(), _workspaceRootPath, runCompilePass, cancellationToken);
            state.AnalyzedText = text;
            if (runCompilePass)
                state.AnalyzedTextWithCompile = text;
            state.Analysis = analysis;
            if (IsSuccessfulInteractiveAnalysis(analysis))
                state.LastSuccessfulAnalysis = analysis;

            // Live-Lint (Style + AST-Regeln) fuer die editierte Datei: laeuft im selben debounced Analyse-Pfad
            // wie die Semantik-Diagnosen, also erscheinen Lint-Hinweise schon beim Tippen. Der Compile-Pass des
            // Linters wird ausgelassen, weil die Analyse (im vollen Lauf) diese Diagnosen bereits liefert.
            IReadOnlyList<CfgsDiagnostic> lintDiagnostics = ComputeLintDiagnostics(state.Uri, text);

            HashSet<string> nextUris = analysis.DiagnosticsByUri.Keys.ToHashSet(GetUriComparer());
            nextUris.Add(state.Uri);

            bool publishedOwnUri = false;
            foreach ((string uri, List<CfgsDiagnostic> diagnostics) in analysis.DiagnosticsByUri)
            {
                if (GetUriComparer().Equals(uri, state.Uri))
                {
                    await PublishDiagnosticsAsync(uri, MergeDiagnostics(diagnostics, lintDiagnostics), cancellationToken);
                    publishedOwnUri = true;
                }
                else
                {
                    await PublishDiagnosticsAsync(uri, diagnostics, cancellationToken);
                }
            }

            if (!publishedOwnUri)
                await PublishDiagnosticsAsync(state.Uri, lintDiagnostics, cancellationToken);

            foreach (string staleUri in state.PublishedDiagnosticUris.Except(nextUris, GetUriComparer()))
                await PublishDiagnosticsAsync(staleUri, [], cancellationToken);

            state.PublishedDiagnosticUris = nextUris;
        }
        finally
        {
            state.AnalysisLock.Release();
        }
    }

    private static List<CfgsDiagnostic> MergeDiagnostics(
        IReadOnlyList<CfgsDiagnostic> compile,
        IReadOnlyList<CfgsDiagnostic> lint)
    {
        if (lint.Count == 0)
            return compile as List<CfgsDiagnostic> ?? compile.ToList();

        List<CfgsDiagnostic> merged = new(compile.Count + lint.Count);
        merged.AddRange(compile);
        merged.AddRange(lint);
        return merged;
    }

    private IReadOnlyList<CfgsDiagnostic> ComputeLintDiagnostics(string uri, string text)
    {
        // Nur echte Dateien linten: der Style-Pass ist am Pfad verankert; fuer untitled-Buffer wird
        // uebersprungen. Ein Linter-Fehler darf niemals die Editor-Diagnosen brechen -> defensiv gefangen.
        if (!_liveLintEnabled)
            return [];

        string? localPath = TryGetLocalPath(uri);
        if (localPath is null)
            return [];

        try
        {
            CfgsLintResult result = CfgsLinter.LintSource(localPath, text, new CfgsLintOptions());
            List<CfgsDiagnostic> mapped = new(result.Diagnostics.Count);
            foreach (CfgsToolDiagnostic diagnostic in result.Diagnostics)
                mapped.Add(ToLintDiagnostic(diagnostic));
            return mapped;
        }
        catch
        {
            return [];
        }
    }

    private static CfgsDiagnostic ToLintDiagnostic(CfgsToolDiagnostic diagnostic)
    {
        int line = Math.Max(0, diagnostic.Line - 1);
        int column = Math.Max(0, diagnostic.Column - 1);
        int span = Math.Max(1, diagnostic.SpanLength);
        RangeInfo range = new(new PositionInfo(line, column), new PositionInfo(line, column + span));

        // Tool-Severity ist umgekehrt zur LSP-Severity: Error=3/Warning=2/Info=1 -> LSP 1/2/3.
        int severity = diagnostic.Severity switch
        {
            CfgsToolDiagnosticSeverity.Error => 1,
            CfgsToolDiagnosticSeverity.Warning => 2,
            _ => 3
        };

        return new CfgsDiagnostic(range, diagnostic.Message, severity, "cfgs-lint", diagnostic.Code);
    }

    // Fast, codegen-free analysis on every edit (responsive live diagnostics).
    private void ScheduleFastAnalysis(DocumentState state, CancellationToken cancellationToken)
        => ScheduleAnalysis(state, cancellationToken, _pendingAnalysisByUri, DidChangeAnalysisDebounce, runCompilePass: false);

    // Full analysis (including the codegen pass) once typing has paused, or on save.
    private void ScheduleFullAnalysis(DocumentState state, CancellationToken cancellationToken, TimeSpan delay)
        => ScheduleAnalysis(state, cancellationToken, _pendingFullAnalysisByUri, delay, runCompilePass: true);

    private void ScheduleAnalysis(
        DocumentState state,
        CancellationToken cancellationToken,
        ConcurrentDictionary<string, CancellationTokenSource> track,
        TimeSpan delay,
        bool runCompilePass)
    {
        CancelPending(track, state.Uri);
        CancellationTokenSource pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        track[state.Uri] = pending;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, pending.Token);
                await AnalyzeAndPublishAsync(state, pending.Token, runCompilePass);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // Eine gescheiterte Analyse darf weder den Prozess mitreissen noch stumm verschwinden (sonst
                // "hoeren" die Diagnosen fuer diese Datei einfach auf). Loggen und weitermachen; der naechste
                // Tastendruck stoesst eine neue Analyse an.
                await TryLogAsync($"[CFGS-LSP] analysis failed for {state.Uri}: {ex.Message}");
            }
            finally
            {
                if (track.TryGetValue(state.Uri, out CancellationTokenSource? current) &&
                    ReferenceEquals(current, pending))
                {
                    track.TryRemove(state.Uri, out _);
                }

                pending.Dispose();
            }
        }, CancellationToken.None);
    }

    private void CancelAllPendingAnalysis(string uri)
    {
        CancelPending(_pendingAnalysisByUri, uri);
        CancelPending(_pendingFullAnalysisByUri, uri);
    }

    private static void CancelPending(ConcurrentDictionary<string, CancellationTokenSource> track, string uri)
    {
        if (track.TryRemove(uri, out CancellationTokenSource? pending))
        {
            pending.Cancel();
            pending.Dispose();
        }
    }

    private static CfgsAnalysisResult GetInteractiveAnalysis(DocumentState state)
    {
        if (!ShouldUseLastSuccessfulAnalysis(state.Analysis) || state.LastSuccessfulAnalysis is null)
            return state.Analysis;

        return state.LastSuccessfulAnalysis with { SourceText = state.Text };
    }

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
        CfgsAnalysisResult anchorAnalysis = GetInteractiveAnalysis(anchorState);
        if (!_analyzer.SupportsWorkspaceSymbolMatching(symbol))
            return _analyzer.FindReferences(anchorAnalysis, symbol, includeDeclaration);

        return EnumerateWorkspaceStates(anchorState.Uri)
            .SelectMany(state => _analyzer.FindReferences(GetInteractiveAnalysis(state), symbol, includeDeclaration))
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

    private IReadOnlyList<CfgsSymbol> FindWorkspaceImplementations(DocumentState anchorState, int line, int character)
    {
        CfgsAnalysisResult anchorAnalysis = GetInteractiveAnalysis(anchorState);
        CfgsSymbol? symbol = _analyzer.ResolveSymbol(anchorAnalysis, line, character);
        if (symbol is null)
            return [];

        return EnumerateWorkspaceStates(anchorState.Uri)
            .SelectMany(state => _analyzer.FindImplementations(GetInteractiveAnalysis(state), symbol))
            .GroupBy(s => (s.QualifiedName, s.Uri))
            .Select(static g => g.First())
            .ToList();
    }

    private IReadOnlyList<object> FindWorkspaceSymbols(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        List<object> results = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (DocumentState state in _documents.Values)
        {
            foreach (CfgsSymbol symbol in _analyzer.FindWorkspaceSymbols(GetInteractiveAnalysis(state), query))
            {
                string key = $"{symbol.QualifiedName}|{symbol.Uri}";
                if (seen.Add(key))
                    results.Add(ToWorkspaceSymbolPayload(symbol));
            }
        }

        foreach (string uri in EnumerateWorkspaceDocumentUris(_documents.Values.FirstOrDefault()?.Uri ?? string.Empty))
        {
            if (_documents.ContainsKey(uri))
                continue;

            DocumentState state = EnsureDocument(uri);
            foreach (CfgsSymbol symbol in _analyzer.FindWorkspaceSymbols(GetInteractiveAnalysis(state), query))
            {
                string key = $"{symbol.QualifiedName}|{symbol.Uri}";
                if (seen.Add(key))
                    results.Add(ToWorkspaceSymbolPayload(symbol));
            }
        }

        return results;
    }

    private IReadOnlyList<CfgsCallHierarchyIncomingCall> FindWorkspaceIncomingCalls(CfgsCallHierarchyItem item)
    {
        Dictionary<string, (CfgsCallHierarchyItem Caller, List<RangeInfo> Ranges)> merged = new(StringComparer.Ordinal);

        foreach (DocumentState state in EnumerateWorkspaceStates(item.Uri))
        {
            foreach (CfgsCallHierarchyIncomingCall call in _analyzer.FindIncomingCalls(GetInteractiveAnalysis(state), item))
            {
                string key = $"{call.From.Uri}|{call.From.SelectionRange.Start.Line}:{call.From.SelectionRange.Start.Character}|{call.From.Name}";
                if (!merged.TryGetValue(key, out var entry))
                {
                    entry = (call.From, []);
                    merged[key] = entry;
                }
                entry.Ranges.AddRange(call.FromRanges);
            }
        }

        return merged.Values
            .Select(e => new CfgsCallHierarchyIncomingCall(e.Caller, e.Ranges))
            .ToList();
    }

    private IReadOnlyList<CfgsCallHierarchyOutgoingCall> FindWorkspaceOutgoingCalls(CfgsCallHierarchyItem item)
    {
        Dictionary<string, (CfgsCallHierarchyItem Callee, List<RangeInfo> Ranges)> merged = new(StringComparer.Ordinal);

        foreach (DocumentState state in EnumerateWorkspaceStates(item.Uri))
        {
            foreach (CfgsCallHierarchyOutgoingCall call in _analyzer.FindOutgoingCalls(GetInteractiveAnalysis(state), item))
            {
                string key = $"{call.To.Uri}|{call.To.SelectionRange.Start.Line}:{call.To.SelectionRange.Start.Character}|{call.To.Name}";
                if (!merged.TryGetValue(key, out var entry))
                {
                    entry = (call.To, []);
                    merged[key] = entry;
                }
                entry.Ranges.AddRange(call.FromRanges);
            }
        }

        return merged.Values
            .Select(e => new CfgsCallHierarchyOutgoingCall(e.Callee, e.Ranges))
            .ToList();
    }

    private static CfgsCallHierarchyItem ParseCallHierarchyItem(JsonElement element)
    {
        string name = element.GetProperty("name").GetString() ?? string.Empty;
        int kind = element.GetProperty("kind").GetInt32();
        string uri = element.GetProperty("uri").GetString() ?? string.Empty;
        RangeInfo range = ToRangeInfo(element.GetProperty("range"));
        RangeInfo selectionRange = ToRangeInfo(element.GetProperty("selectionRange"));
        string detail = element.TryGetProperty("detail", out JsonElement detailEl) ? detailEl.GetString() ?? string.Empty : string.Empty;
        return new CfgsCallHierarchyItem(name, kind, uri, range, selectionRange, detail);
    }

    private IReadOnlyList<object> GetCodeActions(DocumentState state, string uri, RangeInfo requestRange)
    {
        List<CfgsCodeAction> actions = [];

        if (state.Analysis.DiagnosticsByUri.TryGetValue(uri, out List<CfgsDiagnostic>? diagnostics) && diagnostics.Count > 0)
        {
            foreach (CfgsDiagnostic diagnostic in diagnostics)
            {
                if (!Intersects(diagnostic.Range, requestRange))
                    continue;

                AddQuickFixes(actions, state, diagnostic);
            }
        }

        AddRefactorings(actions, state, uri, requestRange);

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

    private static void AddRefactorings(List<CfgsCodeAction> actions, DocumentState state, string uri, RangeInfo requestRange)
    {
        if (requestRange.Start.Line == requestRange.End.Line && requestRange.Start.Character == requestRange.End.Character)
            return;

        string selectedText = ExtractText(state.Text, requestRange);
        if (string.IsNullOrWhiteSpace(selectedText))
            return;

        string lineEnding = DetectLineEnding(state.Text);
        string trimmed = selectedText.Trim();

        if (!trimmed.Contains('\n', StringComparison.Ordinal))
        {
            string varName = "__extracted";
            string insertLine = $"var {varName} = {trimmed};{lineEnding}";
            int insertAtLine = requestRange.Start.Line;
            string[] lines = state.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            string indentation = insertAtLine < lines.Length
                ? new string(lines[insertAtLine].TakeWhile(char.IsWhiteSpace).ToArray())
                : string.Empty;

            actions.Add(new CfgsCodeAction(
                "Extract to variable",
                "refactor",
                [],
                new CfgsWorkspaceEdit([], [
                    new CfgsTextEdit(uri, new RangeInfo(new PositionInfo(insertAtLine, 0), new PositionInfo(insertAtLine, 0)), indentation + insertLine),
                    new CfgsTextEdit(uri, requestRange, varName)
                ])));
        }

        actions.Add(new CfgsCodeAction(
            "Surround with try-catch",
            "refactor",
            [],
            new CfgsWorkspaceEdit([], [
                new CfgsTextEdit(uri, requestRange,
                    $"try {{{lineEnding}\t{trimmed}{lineEnding}}} catch (err) {{{lineEnding}\t# handle error{lineEnding}}}")
            ])));
    }

    private static string ExtractText(string sourceText, RangeInfo range)
    {
        string[] lines = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (range.Start.Line >= lines.Length)
            return string.Empty;

        if (range.Start.Line == range.End.Line)
        {
            string line = lines[range.Start.Line];
            int start = Math.Min(range.Start.Character, line.Length);
            int end = Math.Min(range.End.Character, line.Length);
            return line[start..end];
        }

        StringBuilder sb = new();
        for (int i = range.Start.Line; i <= Math.Min(range.End.Line, lines.Length - 1); i++)
        {
            string line = lines[i];
            if (i == range.Start.Line)
                sb.Append(line[Math.Min(range.Start.Character, line.Length)..]);
            else if (i == range.End.Line)
                sb.Append(line[..Math.Min(range.End.Character, line.Length)]);
            else
                sb.Append(line);

            if (i < range.End.Line)
                sb.Append('\n');
        }

        return sb.ToString();
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

    private static string ApplyContentChanges(string currentText, JsonElement contentChanges)
    {
        string text = currentText;
        foreach (JsonElement change in contentChanges.EnumerateArray())
        {
            string newText = change.TryGetProperty("text", out JsonElement textEl) ? textEl.GetString() ?? string.Empty : string.Empty;
            if (!change.TryGetProperty("range", out JsonElement rangeElement))
            {
                text = newText;
                continue;
            }

            RangeInfo range = ToRangeInfo(rangeElement);
            text = ApplyRangeChange(text, range, newText);
        }

        return text;
    }

    private static string ApplyRangeChange(string sourceText, RangeInfo range, string newText)
    {
        int startOffset = GetOffset(sourceText, range.Start);
        int endOffset = GetOffset(sourceText, range.End);
        if (startOffset > endOffset)
            (startOffset, endOffset) = (endOffset, startOffset);

        return sourceText[..startOffset] + newText + sourceText[endOffset..];
    }

    private static int GetOffset(string text, PositionInfo position)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int targetLine = Math.Max(0, position.Line);
        int targetCharacter = Math.Max(0, position.Character);
        int line = 0;
        int character = 0;
        int index = 0;

        while (index < text.Length && line < targetLine)
        {
            if (text[index] == '\r')
            {
                index++;
                if (index < text.Length && text[index] == '\n')
                    index++;
                line++;
                character = 0;
                continue;
            }

            if (text[index] == '\n')
            {
                index++;
                line++;
                character = 0;
                continue;
            }

            index++;
            character++;
        }

        while (index < text.Length && character < targetCharacter && text[index] is not '\r' and not '\n')
        {
            index++;
            character++;
        }

        return index;
    }

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
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLength))
            {
                contentLength = parsedLength;
            }
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

    // Server-initiierter Request (z.B. client/registerCapability). Die Antwort des Clients traegt nur eine
    // id und kein "method" und wird von HandleMessageAsync gefahrlos ignoriert - wir muessen sie nicht
    // abwarten.
    private Task SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
        => SendPayloadAsync(new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _nextServerRequestId),
            method,
            @params = parameters
        }, cancellationToken);

    private async Task SendPayloadAsync(object payload, CancellationToken cancellationToken)
    {
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
        byte[] headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {jsonBytes.Length}\r\n\r\n");

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _output.WriteAsync(headerBytes, cancellationToken);
            await _output.WriteAsync(jsonBytes, cancellationToken);
            await _output.FlushAsync(cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
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
        detail = item.Detail,
        insertText = item.InsertText
    };

    private static object ToSnippetCompletionPayload(CfgsCompletionItem item) => new
    {
        label = item.Label,
        kind = item.Kind,
        detail = item.Detail,
        insertText = item.InsertText,
        insertTextFormat = 2
    };

    private static object ToWorkspaceSymbolPayload(CfgsSymbol symbol) => new
    {
        name = symbol.Name,
        kind = symbol.Kind,
        location = ToLocationPayload(symbol)
    };

    private static object ToDocumentLinkPayload(CfgsDocumentLink link) => new
    {
        range = ToRangePayload(link.Range),
        target = link.Target
    };

    private static object ToUnresolvedCodeLensPayload(string uri, CfgsCodeLens lens) => new
    {
        range = ToRangePayload(lens.Range),
        data = new
        {
            uri,
            line = lens.Range.Start.Line,
            character = lens.Range.Start.Character
        }
    };

    // Lazily computes the workspace-wide reference count for a code lens and attaches a
    // navigation command (peek references) so the user can jump to each reference.
    private object ResolveCodeLens(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
            return new { command = new { title = "0 references", command = string.Empty } };

        string uri = data.GetProperty("uri").GetString() ?? string.Empty;
        int line = data.GetProperty("line").GetInt32();
        int character = data.GetProperty("character").GetInt32();

        DocumentState state = EnsureDocument(uri);
        CfgsAnalysisResult analysis = GetInteractiveAnalysis(state);
        CfgsSymbol? symbol = _analyzer.ResolveSymbol(analysis, line, character);
        IReadOnlyList<CfgsSymbol> references = symbol is null
            ? []
            : FindWorkspaceReferences(state, symbol, includeDeclaration: false);

        int count = references.Count;
        string title = $"{count} reference{(count == 1 ? "" : "s")}";

        if (count == 0)
            return new { command = new { title, command = string.Empty } };

        return new
        {
            command = new
            {
                title,
                command = "editor.action.showReferences",
                arguments = new object[]
                {
                    uri,
                    new { line, character },
                    references.Select(ToLocationPayload).ToList()
                }
            }
        };
    }

    private static object ToInlayHintPayload(CfgsInlayHint hint) => new
    {
        position = new
        {
            line = hint.Position.Line,
            character = hint.Position.Character
        },
        label = hint.Label,
        kind = (int)hint.Kind,
        paddingRight = true
    };

    private static object ToCallHierarchyItemPayload(CfgsCallHierarchyItem item) => new
    {
        name = item.Name,
        kind = item.Kind,
        uri = item.Uri,
        range = ToRangePayload(item.Range),
        selectionRange = ToRangePayload(item.SelectionRange),
        detail = item.Detail
    };

    private static object ToIncomingCallPayload(CfgsCallHierarchyIncomingCall call) => new
    {
        from = ToCallHierarchyItemPayload(call.From),
        fromRanges = call.FromRanges.Select(ToRangePayload).ToList()
    };

    private static object ToOutgoingCallPayload(CfgsCallHierarchyOutgoingCall call) => new
    {
        to = ToCallHierarchyItemPayload(call.To),
        fromRanges = call.FromRanges.Select(ToRangePayload).ToList()
    };

    private static object ToSelectionRangePayload(CfgsSelectionRange range)
    {
        object result = new
        {
            range = ToRangePayload(range.Range)
        };

        if (range.Parent is not null)
        {
            result = new
            {
                range = ToRangePayload(range.Range),
                parent = ToSelectionRangePayload(range.Parent)
            };
        }

        return result;
    }

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

    private static bool TryGetLiveLintOption(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty("initializationOptions", out JsonElement options) &&
            options.ValueKind == JsonValueKind.Object &&
            options.TryGetProperty("liveLint", out JsonElement liveLint))
        {
            return liveLint.ValueKind == JsonValueKind.True;
        }

        return false;
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
            Analysis = new CfgsAnalysisResult(uri, uri, text, new Dictionary<string, List<CfgsDiagnostic>>(), [], [], new CfgsSemanticModel([], new Dictionary<string, CfgsSymbol>(), [], [], []));
            LastSuccessfulAnalysis = null;
            PublishedDiagnosticUris = new HashSet<string>(GetUriComparer());
        }

        public string Uri { get; }

        public string Text { get; set; }

        public int Version { get; set; }

        public CfgsAnalysisResult Analysis { get; set; }

        public CfgsAnalysisResult? LastSuccessfulAnalysis { get; set; }

        public HashSet<string> PublishedDiagnosticUris { get; set; }

        // Text, der zur zuletzt veroeffentlichten Analyse gefuehrt hat. Aendert sich der Text zwischen
        // zwei Analyse-Laeufen nicht (z.B. Save ohne Aenderung, redundante didChange-Sequenzen), wird der
        // komplette Re-Build uebersprungen.
        public string? AnalyzedText { get; set; }

        // Text, der zuletzt MIT dem Codegen-Pass (voller Compile) analysiert wurde. Getrennt von
        // AnalyzedText, weil die schnelle Live-Analyse den Codegen ueberspringt: der Idle-/Save-Lauf soll
        // denselben Text trotzdem einmal voll pruefen, obwohl die schnelle Analyse ihn schon gesehen hat.
        public string? AnalyzedTextWithCompile { get; set; }

        // Serialisiert die schnelle (didChange) und die volle (idle/save) Analyse derselben Datei, damit sich
        // ihre state-Mutationen und Publishes nicht ueberschneiden.
        public SemaphoreSlim AnalysisLock { get; } = new(1, 1);
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
