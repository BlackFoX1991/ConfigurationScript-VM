using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CFGS.Lsp.Tests;

internal static class Program
{
    private static int Main()
    {
        TestCase[] tests =
        [
            InitializeAdvertisesCoreCapabilities,
            DidOpenPublishesDiagnostics,
            DidChangePublishesLatestDiagnosticsAfterDebounce,
            DidChangeAppliesIncrementalRangeEdits,
            CompletionReturnsLanguageItems,
            CompletionReflectsStandardLibraryNamespacePolicy,
            FormattingReturnsDocumentEdit,
            DocumentLinksResolveImportsFromApplicationPluginsDirectory,
            SemanticTokensRangeReturnsSubset,
            HoverFindsFunctionSymbol,
            HoverIncludesQualifiedContainerDetails,
            HoverResolvesThisMemberInsideLambda,
            HoverPrefersTypeSymbolsInTypeContextExpressions,
            AnalyzerKeepsSymbolsAliveAcrossMidFunctionSyntaxErrors,
            AnalyzerReportsMultipleSyntaxDiagnostics,
            DocumentSymbolsUseLastGoodAnalysisAfterCatastrophicLexerFailure,
            HoverUsesLastGoodAnalysisAfterCatastrophicLexerFailure,
            ReferencesUseLastGoodAnalysisAfterCatastrophicLexerFailure,
            LiveLintPublishesStyleDiagnosticsWhenEnabled,
            LiveLintStaysSilentWhenDisabled
        ];

        List<string> failures = [];
        foreach (TestCase test in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {test.Method.Name}");
            }
            catch (Exception ex)
            {
                failures.Add($"{test.Method.Name}: {ex.Message}");
                Console.WriteLine($"FAIL {test.Method.Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"LSP_TEST_SUMMARY:TOTAL={tests.Length}:PASSED={tests.Length - failures.Count}:FAILED={failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void InitializeAdvertisesCoreCapabilities()
    {
        using LspTestClient client = LspTestClient.Start();
        JsonElement result = client.Initialize();
        JsonElement capabilities = result.GetProperty("capabilities");

        AssertTrue(capabilities.GetProperty("hoverProvider").GetBoolean(), "hover provider is not advertised");
        AssertTrue(capabilities.GetProperty("definitionProvider").GetBoolean(), "definition provider is not advertised");
        AssertTrue(capabilities.GetProperty("documentFormattingProvider").GetBoolean(), "formatting provider is not advertised");
        JsonElement textDocumentSync = capabilities.GetProperty("textDocumentSync");
        AssertEqual(2, textDocumentSync.GetProperty("change").GetInt32(), "server must advertise incremental sync");
        AssertTrue(textDocumentSync.GetProperty("save").GetBoolean(), "server must advertise save so didSave triggers the full analysis");
        AssertEqual(JsonValueKind.Object, capabilities.GetProperty("completionProvider").ValueKind, "completion provider is missing");
        JsonElement semanticTokens = capabilities.GetProperty("semanticTokensProvider");
        AssertEqual(JsonValueKind.Object, semanticTokens.ValueKind, "semantic tokens provider is missing");
        AssertTrue(semanticTokens.GetProperty("range").GetBoolean(), "semantic token range support is not advertised");
    }

    private static void DidOpenPublishesDiagnostics()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string uri = CreateDocumentUri("broken.cfs");
        client.DidOpen(uri, "var value = ;\n");

        JsonElement diagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertTrue(diagnostics.GetArrayLength() > 0, "expected diagnostics for broken source");
        AssertFalse(string.IsNullOrWhiteSpace(diagnostics[0].GetProperty("message").GetString()), "diagnostic message is empty");
    }

    private static void LiveLintPublishesStyleDiagnosticsWhenEnabled()
    {
        using LspTestClient client = LspTestClient.StartInitialized(liveLint: true);
        string uri = CreateDocumentUri("live_lint_on.cfs");
        // Trailing whitespace after the first statement is a style lint; the script itself compiles
        // cleanly, so any diagnostic that appears must come from the live linter.
        client.DidOpen(uri, "var greeting = \"hi\"; \nprint(greeting);\n");

        JsonElement diagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertTrue(CountLintDiagnostics(diagnostics) > 0, "expected a cfgs-lint diagnostic when live lint is enabled");
    }

    private static void LiveLintStaysSilentWhenDisabled()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string uri = CreateDocumentUri("live_lint_off.cfs");
        client.DidOpen(uri, "var greeting = \"hi\"; \nprint(greeting);\n");

        JsonElement diagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertEqual(0, CountLintDiagnostics(diagnostics), "expected no cfgs-lint diagnostics when live lint is disabled");
    }

    private static int CountLintDiagnostics(JsonElement diagnostics)
    {
        int count = 0;
        foreach (JsonElement diagnostic in diagnostics.EnumerateArray())
        {
            if (diagnostic.TryGetProperty("source", out JsonElement source) &&
                string.Equals(source.GetString(), "cfgs-lint", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static void CompletionReturnsLanguageItems()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string uri = CreateDocumentUri("completion.cfs");
        client.DidOpen(uri, "\n");
        _ = client.WaitForDiagnostics(uri);

        JsonElement completions = client.SendRequest("textDocument/completion", new
        {
            textDocument = new { uri },
            position = new { line = 0, character = 0 }
        });

        AssertEqual(JsonValueKind.Array, completions.ValueKind, "completion result must be an array");
        AssertTrue(completions.EnumerateArray().Any(item => item.GetProperty("label").GetString() == "func"), "keyword completion 'func' missing");
    }

    private static void CompletionReflectsStandardLibraryNamespacePolicy()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string uri = CreateDocumentUri("stdlib-completion.cfs");
        client.DidOpen(uri, "fs.\n");
        _ = client.WaitForDiagnostics(uri);

        JsonElement memberCompletions = client.SendRequest("textDocument/completion", new
        {
            textDocument = new { uri },
            position = new { line = 0, character = 3 }
        });

        string[] memberLabels = memberCompletions.EnumerateArray()
            .Select(static item => item.GetProperty("label").GetString() ?? string.Empty)
            .ToArray();

        AssertTrue(memberLabels.Contains("open", StringComparer.Ordinal), "fs.open completion missing");
        AssertTrue(memberLabels.Contains("writeAllBytes", StringComparer.Ordinal), "fs.writeAllBytes completion missing");
        AssertFalse(memberLabels.Contains("fopen", StringComparer.Ordinal), "removed fs.fopen alias still appears in completion");
        AssertFalse(memberLabels.Contains("set_workspace", StringComparer.Ordinal), "removed fs.set_workspace alias still appears in completion");

        JsonElement topLevelCompletions = client.SendRequest("textDocument/completion", new
        {
            textDocument = new { uri },
            position = new { line = 1, character = 0 }
        });

        string[] topLevelLabels = topLevelCompletions.EnumerateArray()
            .Select(static item => item.GetProperty("label").GetString() ?? string.Empty)
            .ToArray();

        AssertTrue(topLevelLabels.Contains("fs", StringComparer.Ordinal), "fs namespace completion missing");
        AssertFalse(topLevelLabels.Contains("fopen", StringComparer.Ordinal), "removed global fopen alias still appears in completion");
        AssertFalse(topLevelLabels.Contains("fromjson", StringComparer.Ordinal), "removed global fromjson alias still appears in completion");
    }

    private static void DidChangePublishesLatestDiagnosticsAfterDebounce()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string uri = CreateDocumentUri("change.cfs");
        client.DidOpen(uri, "var value = ;\n");

        JsonElement firstDiagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertTrue(firstDiagnostics.GetArrayLength() > 0, "expected initial diagnostics for broken source");

        client.DidChange(uri, 2, "var value = 1;\n");
        JsonElement nextDiagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertEqual(0, nextDiagnostics.GetArrayLength(), "expected diagnostics to clear after valid debounced change");
    }

    private static void DidChangeAppliesIncrementalRangeEdits()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string uri = CreateDocumentUri("change-range.cfs");
        client.DidOpen(uri, "var value = ;\n");

        JsonElement firstDiagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertTrue(firstDiagnostics.GetArrayLength() > 0, "expected initial diagnostics for broken source");

        client.DidChangeRange(uri, 2, 0, 12, 0, 12, "1");
        JsonElement nextDiagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertEqual(0, nextDiagnostics.GetArrayLength(), "expected diagnostics to clear after incremental range edit");
    }

    private static void FormattingReturnsDocumentEdit()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string uri = CreateDocumentUri("format.cfs");
        client.DidOpen(uri, "func add(a,b){\nvar total=a+b;\n}\n");
        _ = client.WaitForDiagnostics(uri);

        JsonElement edits = client.SendRequest("textDocument/formatting", new
        {
            textDocument = new { uri },
            options = new { tabSize = 4, insertSpaces = true }
        });

        AssertEqual(JsonValueKind.Array, edits.ValueKind, "formatting result must be an array");
        AssertTrue(edits.GetArrayLength() == 1, "formatting should return a single full-document edit");
        AssertTrue(edits[0].GetProperty("newText").GetString()?.Contains("var total = a + b;", StringComparison.Ordinal) == true, "format edit did not normalize spacing");
    }

    private static void DocumentLinksResolveImportsFromApplicationPluginsDirectory()
    {
        string appPluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
        Directory.CreateDirectory(appPluginDirectory);
        string moduleFileName = $"cfgs_lsp_doclink_{Guid.NewGuid():N}.cfs";
        string modulePath = Path.Combine(appPluginDirectory, moduleFileName);
        File.WriteAllText(modulePath, "export const value = 1;\n");

        try
        {
            using LspTestClient client = LspTestClient.StartInitialized();
            string uri = CreateDocumentUri("document-link-import.cfs");
            client.DidOpen(uri, $$"""
import { value } from "{{moduleFileName}}";
""");
            JsonElement diagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
            AssertEqual(0, diagnostics.GetArrayLength(), "expected document-link import sample to analyze cleanly");

            JsonElement links = client.SendRequest("textDocument/documentLink", new
            {
                textDocument = new { uri }
            });

            AssertEqual(JsonValueKind.Array, links.ValueKind, "documentLink result must be an array");
            AssertTrue(links.GetArrayLength() >= 1, "expected at least one document link for the import path");
            string target = links[0].GetProperty("target").GetString() ?? string.Empty;
            AssertEqual(new Uri(Path.GetFullPath(modulePath)).AbsoluteUri, target, "document link did not resolve through the application plugins directory");
        }
        finally
        {
            try
            {
                if (File.Exists(modulePath))
                    File.Delete(modulePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void SemanticTokensRangeReturnsSubset()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string source = """
func add(a, b) {
    var total = a + b;
    return total;
}
""";
        string uri = CreateDocumentUri("semantic-range.cfs");
        client.DidOpen(uri, source);
        _ = client.WaitForDiagnostics(uri);

        JsonElement full = client.SendRequest("textDocument/semanticTokens/full", new
        {
            textDocument = new { uri }
        });

        JsonElement range = client.SendRequest("textDocument/semanticTokens/range", new
        {
            textDocument = new { uri },
            range = new
            {
                start = new { line = 1, character = 0 },
                end = new { line = 1, character = 22 }
            }
        });

        int fullCount = full.GetProperty("data").GetArrayLength();
        int rangeCount = range.GetProperty("data").GetArrayLength();
        AssertTrue(fullCount > 0, "full semantic tokens should not be empty");
        AssertTrue(rangeCount > 0, "range semantic tokens should not be empty");
        AssertTrue(rangeCount < fullCount, "range semantic tokens should be a subset of the full result");
    }

    private static void HoverFindsFunctionSymbol()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string source = """
func add(a, b) {
    return a + b;
}

var answer = add(1, 2);
""";
        string uri = CreateDocumentUri("hover.cfs");
        client.DidOpen(uri, source);
        _ = client.WaitForDiagnostics(uri);

        JsonElement hover = client.SendRequest("textDocument/hover", new
        {
            textDocument = new { uri },
            position = new { line = 4, character = 13 }
        });

        string value = hover.GetProperty("contents").GetProperty("value").GetString() ?? string.Empty;
        AssertTrue(value.Contains("add", StringComparison.Ordinal), "hover does not describe the function symbol");
    }

    private static void HoverIncludesQualifiedContainerDetails()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string source = """
namespace Demo.App {
    class Runner() {
        func run() {
            return 1;
        }
    }
}
""";
        string uri = CreateDocumentUri("hover-details.cfs");
        client.DidOpen(uri, source);
        _ = client.WaitForDiagnostics(uri);

        JsonElement hover = client.SendRequest("textDocument/hover", new
        {
            textDocument = new { uri },
            position = new { line = 2, character = 14 }
        });

        string value = hover.GetProperty("contents").GetProperty("value").GetString() ?? string.Empty;
        AssertTrue(value.Contains("Symbol: `Demo.App.Runner.run`", StringComparison.Ordinal), "hover does not include the qualified symbol name");
        AssertTrue(value.Contains("Namespace: `Demo.App`", StringComparison.Ordinal), "hover does not include the namespace path");
        AssertTrue(value.Contains("Type/Class: `Runner`", StringComparison.Ordinal), "hover does not include the owning type name");
        AssertTrue(value.Contains("Container: `Demo.App.Runner`", StringComparison.Ordinal), "hover does not include the containing namespace/class path");
    }

    private static void HoverResolvesThisMemberInsideLambda()
    {
        // A lambda inside an instance method captures the enclosing receiver (C#-style), so the
        // semantic model must resolve `this.member` inside it for hover/navigation — mirroring the
        // language change that made lexical `this` capture valid.
        using LspTestClient client = LspTestClient.StartInitialized();
        string source = """
class Counter() {
    var total = 0;
    func run() {
        var f = func() { return this.total; };
        return f();
    }
}
""";
        string uri = CreateDocumentUri("hover-lambda-this.cfs");
        client.DidOpen(uri, source);
        _ = client.WaitForDiagnostics(uri);

        JsonElement hover = client.SendRequest("textDocument/hover", new
        {
            textDocument = new { uri },
            position = new { line = 3, character = 39 }
        });

        string value = hover.GetProperty("contents").GetProperty("value").GetString() ?? string.Empty;
        AssertTrue(value.Contains("Counter.total", StringComparison.Ordinal),
            $"hover did not resolve this.total inside a lambda (lexical receiver capture); got: {value}");
    }

    private static void HoverPrefersTypeSymbolsInTypeContextExpressions()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string source = """
namespace Demo.Model;

interface IModel {
    func read();
}

class Runner() {
    func probe(other) {
        var IModel = "shadow";
        var checkIs = other is IModel;
        var checkImplements = implements(other, IModel);
        return interface_call(other, IModel, "read");
    }
}
""";
        string uri = CreateDocumentUri("hover-type-context.cfs");
        client.DidOpen(uri, source);
        JsonElement diagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertEqual(0, diagnostics.GetArrayLength(), "expected type-context hover sample to analyze cleanly");

        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string expectedSymbol = "Symbol: `Demo.Model.IModel`";

        JsonElement isHover = client.SendRequest("textDocument/hover", new
        {
            textDocument = new { uri },
            position = new { line = 9, character = lines[9].LastIndexOf("IModel", StringComparison.Ordinal) + 1 }
        });
        string isValue = isHover.GetProperty("contents").GetProperty("value").GetString() ?? string.Empty;
        AssertTrue(isValue.Contains(expectedSymbol, StringComparison.Ordinal), "`is` type context should resolve the interface symbol, not the shadowing local");

        JsonElement implementsHover = client.SendRequest("textDocument/hover", new
        {
            textDocument = new { uri },
            position = new { line = 10, character = lines[10].LastIndexOf("IModel", StringComparison.Ordinal) + 1 }
        });
        string implementsValue = implementsHover.GetProperty("contents").GetProperty("value").GetString() ?? string.Empty;
        AssertTrue(implementsValue.Contains(expectedSymbol, StringComparison.Ordinal), "`implements` type context should resolve the interface symbol, not the shadowing local");

        JsonElement interfaceCallHover = client.SendRequest("textDocument/hover", new
        {
            textDocument = new { uri },
            position = new { line = 11, character = lines[11].LastIndexOf("IModel", StringComparison.Ordinal) + 1 }
        });
        string interfaceCallValue = interfaceCallHover.GetProperty("contents").GetProperty("value").GetString() ?? string.Empty;
        AssertTrue(interfaceCallValue.Contains(expectedSymbol, StringComparison.Ordinal), "`interface_call` type context should resolve the interface symbol, not the shadowing local");
    }

    private static void AnalyzerKeepsSymbolsAliveAcrossMidFunctionSyntaxErrors()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string source = """
func demo() {
    var first = 1;
    var broken = ;
    var second = 2;
    return second;
}
""";
        string uri = CreateDocumentUri("tolerant-hover.cfs");
        client.DidOpen(uri, source);
        JsonElement diagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertTrue(diagnostics.GetArrayLength() > 0, "expected diagnostics for broken source");

        JsonElement hover = client.SendRequest("textDocument/hover", new
        {
            textDocument = new { uri },
            position = new { line = 4, character = 12 }
        });

        string value = hover.GetProperty("contents").GetProperty("value").GetString() ?? string.Empty;
        AssertTrue(value.Contains("second", StringComparison.Ordinal), "hover should still resolve symbols after a mid-function syntax error");
    }

    private static void AnalyzerReportsMultipleSyntaxDiagnostics()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string source = """
func demo() {
    var a = ;
    var b = ;
    return 1;
}
""";
        string uri = CreateDocumentUri("multi-diagnostics.cfs");
        client.DidOpen(uri, source);
        JsonElement diagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertTrue(diagnostics.GetArrayLength() >= 2, "expected multiple syntax diagnostics, not only the first failure");
    }

    private static void DocumentSymbolsUseLastGoodAnalysisAfterCatastrophicLexerFailure()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string source = """
func add(a, b) {
    return a + b;
}

var answer = add(1, 2);
""";
        string uri = CreateDocumentUri("last-good-symbols.cfs");
        client.DidOpen(uri, source);
        JsonElement initialDiagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertEqual(0, initialDiagnostics.GetArrayLength(), "expected the baseline source to analyze cleanly");

        client.DidChange(uri, 2, "@\n" + source);
        JsonElement diagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertTrue(diagnostics.GetArrayLength() > 0, "expected diagnostics after catastrophic lexer failure");

        JsonElement symbols = client.SendRequest("textDocument/documentSymbol", new
        {
            textDocument = new { uri }
        });

        AssertEqual(JsonValueKind.Array, symbols.ValueKind, "documentSymbol result must be an array");
        AssertTrue(symbols.EnumerateArray().Any(item => item.GetProperty("name").GetString() == "add"), "document symbols should fall back to the last good analysis");
    }

    private static void HoverUsesLastGoodAnalysisAfterCatastrophicLexerFailure()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string source = """
func add(a, b) {
    return a + b;
}

var answer = add(1, 2);
""";
        string uri = CreateDocumentUri("last-good-hover.cfs");
        client.DidOpen(uri, source);
        JsonElement initialDiagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertEqual(0, initialDiagnostics.GetArrayLength(), "expected the baseline source to analyze cleanly");

        client.DidChange(uri, 2, "@\n" + source);
        JsonElement diagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertTrue(diagnostics.GetArrayLength() > 0, "expected diagnostics after catastrophic lexer failure");

        JsonElement hover = client.SendRequest("textDocument/hover", new
        {
            textDocument = new { uri },
            position = new { line = 5, character = 13 }
        });

        string value = hover.GetProperty("contents").GetProperty("value").GetString() ?? string.Empty;
        AssertTrue(value.Contains("add", StringComparison.Ordinal), "hover should resolve against the last good analysis even after current symbols disappear");
    }

    private static void ReferencesUseLastGoodAnalysisAfterCatastrophicLexerFailure()
    {
        using LspTestClient client = LspTestClient.StartInitialized();
        string source = """
func add(a, b) {
    return a + b;
}

var answer = add(1, 2);
""";
        string uri = CreateDocumentUri("last-good-references.cfs");
        client.DidOpen(uri, source);
        JsonElement initialDiagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertEqual(0, initialDiagnostics.GetArrayLength(), "expected the baseline source to analyze cleanly");

        client.DidChange(uri, 2, "@\n" + source);
        JsonElement diagnostics = client.WaitForDiagnostics(uri).GetProperty("diagnostics");
        AssertTrue(diagnostics.GetArrayLength() > 0, "expected diagnostics after catastrophic lexer failure");

        JsonElement references = client.SendRequest("textDocument/references", new
        {
            textDocument = new { uri },
            position = new { line = 5, character = 13 },
            context = new { includeDeclaration = true }
        });

        AssertEqual(JsonValueKind.Array, references.ValueKind, "references result must be an array");
        AssertTrue(references.GetArrayLength() >= 2, "references should still include declaration and usage from the last good analysis");
    }

    private static string CreateDocumentUri(string fileName)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cfgs-lsp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new Uri(Path.Combine(directory, fileName)).AbsoluteUri;
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}. Expected '{expected}', got '{actual}'.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
        => AssertTrue(!condition, message);

    private delegate void TestCase();
}

internal sealed class LspTestClient : IDisposable
{
    private readonly Process _process;
    private readonly Stream _stdout;
    private readonly Stream _stdin;
    private readonly ConcurrentQueue<JsonElement> _notifications = new();
    private int _nextId;
    private bool _disposed;

    private LspTestClient(Process process)
    {
        _process = process;
        _stdout = process.StandardOutput.BaseStream;
        _stdin = process.StandardInput.BaseStream;
    }

    public static LspTestClient Start()
    {
        string serverPath = Path.Combine(AppContext.BaseDirectory, "CFGS.Lsp.dll");
        if (!File.Exists(serverPath))
            throw new FileNotFoundException("CFGS.Lsp.dll was not found in the test output directory.", serverPath);

        Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { serverPath },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("failed to start CFGS.Lsp");

        return new LspTestClient(process);
    }

    public static LspTestClient StartInitialized(bool liveLint = false)
    {
        LspTestClient client = Start();
        client.Initialize(liveLint);
        client.SendNotification("initialized", new { });
        return client;
    }

    public JsonElement Initialize(bool liveLint = false)
    {
        string root = Path.Combine(Path.GetTempPath(), "cfgs-lsp-tests-root");
        Directory.CreateDirectory(root);
        return SendRequest("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri = new Uri(root).AbsoluteUri,
            capabilities = new { },
            initializationOptions = new { liveLint }
        });
    }

    public void DidOpen(string uri, string text)
    {
        SendNotification("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri,
                languageId = "cfgs",
                version = 1,
                text
            }
        });
    }

    public void DidChange(string uri, int version, string text)
    {
        SendNotification("textDocument/didChange", new
        {
            textDocument = new
            {
                uri,
                version
            },
            contentChanges = new[]
            {
                new
                {
                    text
                }
            }
        });
    }

    public void DidChangeRange(string uri, int version, int startLine, int startCharacter, int endLine, int endCharacter, string text)
    {
        SendNotification("textDocument/didChange", new
        {
            textDocument = new
            {
                uri,
                version
            },
            contentChanges = new[]
            {
                new
                {
                    range = new
                    {
                        start = new { line = startLine, character = startCharacter },
                        end = new { line = endLine, character = endCharacter }
                    },
                    text
                }
            }
        });
    }

    public JsonElement WaitForDiagnostics(string uri)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            JsonElement message = ReadMessage();
            if (message.TryGetProperty("method", out JsonElement method) &&
                method.GetString() == "textDocument/publishDiagnostics")
            {
                JsonElement parameters = message.GetProperty("params");
                if (string.Equals(parameters.GetProperty("uri").GetString(), uri, StringComparison.OrdinalIgnoreCase))
                    return parameters.Clone();
            }

            _notifications.Enqueue(message.Clone());
        }

        throw new TimeoutException($"timed out waiting for diagnostics for {uri}");
    }

    public JsonElement SendRequest(string method, object parameters)
    {
        int id = Interlocked.Increment(ref _nextId);
        WriteMessage(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        });

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            JsonElement message = ReadMessage();
            if (message.TryGetProperty("id", out JsonElement responseId) && responseId.GetInt32() == id)
            {
                if (message.TryGetProperty("error", out JsonElement error))
                    throw new InvalidOperationException(error.GetProperty("message").GetString());

                return message.TryGetProperty("result", out JsonElement result)
                    ? result.Clone()
                    : JsonDocument.Parse("null").RootElement.Clone();
            }

            _notifications.Enqueue(message.Clone());
        }

        throw new TimeoutException($"timed out waiting for LSP response to {method}");
    }

    public void SendNotification(string method, object parameters)
    {
        WriteMessage(new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        });
    }

    private void WriteMessage(object payload)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        _stdin.Write(header);
        _stdin.Write(body);
        _stdin.Flush();
    }

    private JsonElement ReadMessage()
    {
        string header = ReadHeader();
        int length = ParseContentLength(header);
        byte[] body = ReadExactly(length);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private string ReadHeader()
    {
        List<byte> bytes = [];
        while (true)
        {
            int next = _stdout.ReadByte();
            if (next < 0)
                throw new EndOfStreamException("LSP server closed stdout.");

            bytes.Add((byte)next);
            int count = bytes.Count;
            if (count >= 4 &&
                bytes[count - 4] == '\r' &&
                bytes[count - 3] == '\n' &&
                bytes[count - 2] == '\r' &&
                bytes[count - 1] == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }
    }

    private byte[] ReadExactly(int length)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = _stdout.Read(buffer, offset, length - offset);
            if (read <= 0)
                throw new EndOfStreamException("LSP server closed stdout while sending a message body.");
            offset += read;
        }

        return buffer;
    }

    private static int ParseContentLength(string header)
    {
        foreach (string line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && string.Equals(line[..colon], "Content-Length", StringComparison.OrdinalIgnoreCase))
                return int.Parse(line[(colon + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }

        throw new FormatException("LSP message header did not contain Content-Length.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            SendRequest("shutdown", new { });
            SendNotification("exit", new { });
        }
        catch
        {
        }

        if (!_process.WaitForExit(1000))
            _process.Kill(entireProcessTree: true);

        _process.Dispose();
    }
}
