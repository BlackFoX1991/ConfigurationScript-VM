using System.Diagnostics;
using System.Text.Json;
using CFGS_VM.Tooling;

namespace CFGS.Tooling.Tests;

internal static class Program
{
    private static int Main()
    {
        TestCase[] tests =
        [
            FormatterIsIdempotent,
            FormatterNormalizesIndentationAndSpacing,
            FormatterKeepsUnaryOperatorsTight,
            FormatterHandlesTernarySpacing,
            FormatterIgnoresBracesInsideStringsAndComments,
            LinterReportsStyleAndAstDiagnostics,
            LinterFixesOnlySafeStyleIssues,
            LinterSupportsSuppressions,
            LinterDoesNotFlagExportedOrNamespacedApiFunctionsAsUnused,
            CliLintJsonProducesStableDiagnosticShape
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

        Console.WriteLine($"TOOLING_TEST_SUMMARY:TOTAL={tests.Length}:PASSED={tests.Length - failures.Count}:FAILED={failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void FormatterIsIdempotent()
    {
        string source = """
func add(a,b){
var total=a+b;
if(total>0){
return total;
}
return 0;
}
""";

        CfgsFormatOptions options = new(4, UseTabs: false);
        string once = CfgsFormatter.Format(source, options).Text;
        string twice = CfgsFormatter.Format(once, options).Text;

        AssertEqual(once, twice, "formatter must be idempotent");
    }

    private static void FormatterNormalizesIndentationAndSpacing()
    {
        string source = """
func add(a,b){
var total=a+b;
if(total>0){
return total;
}
}
""";

        string expected = """
func add(a, b){
    var total = a + b;
    if(total > 0){
        return total;
    }
}
""" + "\n";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertEqual(NormalizeNewlines(expected), NormalizeNewlines(actual), "formatter output changed unexpectedly");
    }

    private static void LinterReportsStyleAndAstDiagnostics()
    {
        string file = WriteTempCfgs("""
func unusedFunc(){
    var unused = 1;    
    return 1;
    var unreachable = 2;
}
""");

        CfgsLintResult result = CfgsLinter.LintFile(file, new CfgsLintOptions(), global::Program.Version);
        string[] codes = result.Diagnostics.Select(static d => d.Code).OrderBy(static c => c, StringComparer.Ordinal).ToArray();

        AssertContains(codes, "CFGS8001", "trailing whitespace diagnostic missing");
        AssertContains(codes, "CFGS8011", "unused local diagnostic missing");
        AssertContains(codes, "CFGS8012", "unreachable statement diagnostic missing");
        AssertContains(codes, "CFGS8013", "unused top-level function diagnostic missing");
    }

    private static void FormatterKeepsUnaryOperatorsTight()
    {
        string source = """
func check(flag){
if(!flag){
return -1;
}
return +1;
}
""";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertTrue(actual.Contains("if(!flag){", StringComparison.Ordinal), "formatter must not insert a space into unary ! expressions");
        AssertTrue(actual.Contains("return -1;", StringComparison.Ordinal), "formatter must keep unary minus tight");
        AssertTrue(actual.Contains("return +1;", StringComparison.Ordinal), "formatter must keep unary plus tight");
    }

    private static void FormatterHandlesTernarySpacing()
    {
        string source = "var label=active?\"yes\":\"no\";\n";
        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertEqual("var label = active ? \"yes\": \"no\";\n", NormalizeNewlines(actual), "formatter should space conditional operator predictably");
    }

    private static void FormatterIgnoresBracesInsideStringsAndComments()
    {
        string source = """
func render(){
var text="{not a block}";
#+
comment with { and }
+#
if(true){
return text;
}
}
""";

        string expected = """
func render(){
    var text = "{not a block}";
    #+
    comment with { and }
    +#
    if(true){
        return text;
    }
}
""" + "\n";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertEqual(NormalizeNewlines(expected), NormalizeNewlines(actual), "formatter should ignore braces inside strings and block comments");
    }

    private static void LinterFixesOnlySafeStyleIssues()
    {
        string source = "func main(){    \n\tvar value = 1;\n\n\n\n}\n";
        string file = WriteTempCfgs(source);

        CfgsLintResult result = CfgsLinter.LintFile(file, new CfgsLintOptions(Fix: true), global::Program.Version);

        AssertTrue(result.Fixed, "linter did not report a safe style fix");
        AssertTrue(result.FixedText is not null, "linter did not return fixed text");
        AssertFalse(result.FixedText!.Contains('\t'), "fix should convert leading tabs to spaces");
        AssertFalse(result.FixedText.Contains("    \n", StringComparison.Ordinal), "fix should remove trailing whitespace");
        AssertFalse(result.FixedText.Contains("\n\n\n", StringComparison.Ordinal), "fix should collapse excessive blank lines");
        AssertTrue(result.FixedText.EndsWith(Environment.NewLine, StringComparison.Ordinal), "fix should keep a final newline");
    }

    private static void LinterSupportsSuppressions()
    {
        string file = WriteTempCfgs("""
# lint:disable-next-line CFGS8013
func helper(){
    var unused = 1; # lint:disable-line CFGS8011
    return 1;
}
""");

        CfgsLintResult result = CfgsLinter.LintFile(file, new CfgsLintOptions(), global::Program.Version);
        string[] codes = result.Diagnostics.Select(static d => d.Code).ToArray();

        AssertFalse(codes.Contains("CFGS8013", StringComparer.Ordinal), "next-line suppression did not suppress CFGS8013");
        AssertFalse(codes.Contains("CFGS8011", StringComparer.Ordinal), "line suppression did not suppress CFGS8011");
    }

    private static void LinterDoesNotFlagExportedOrNamespacedApiFunctionsAsUnused()
    {
        string file = WriteTempCfgs("""
export func exportedApi(){
    return 1;
}

namespace Demo.Tools {
    func namespacedApi(){
        return 2;
    }
}

func privateHelper(){
    return 3;
}
""");

        CfgsLintResult result = CfgsLinter.LintFile(file, new CfgsLintOptions(), global::Program.Version);
        string[] messages = result.Diagnostics
            .Where(static d => d.Code == "CFGS8013")
            .Select(static d => d.Message)
            .ToArray();

        AssertFalse(messages.Any(static m => m.Contains("exportedApi", StringComparison.Ordinal)), "exported API function should not be reported as unused");
        AssertFalse(messages.Any(static m => m.Contains("namespacedApi", StringComparison.Ordinal)), "namespaced API function should not be reported as unused");
        AssertTrue(messages.Any(static m => m.Contains("privateHelper", StringComparison.Ordinal)), "private unused helper should still be reported");
    }

    private static void CliLintJsonProducesStableDiagnosticShape()
    {
        string file = WriteTempCfgs("func neverCalled(){\n    return 1;\n}\n");
        string vmAssembly = typeof(global::Program).Assembly.Location;

        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { vmAssembly, "-lint", file, "--json" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("failed to start dotnet process");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);

        AssertEqual(0, process.ExitCode, $"lint JSON command failed: {stderr}");

        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        AssertEqual(JsonValueKind.Array, root.ValueKind, "lint JSON root must be an array");
        AssertTrue(root.GetArrayLength() >= 1, "lint JSON should contain at least one diagnostic");

        JsonElement first = root[0];
        AssertTrue(first.TryGetProperty("code", out JsonElement code), "diagnostic code missing");
        AssertTrue(first.TryGetProperty("severity", out JsonElement severity), "diagnostic severity missing");
        AssertTrue(first.TryGetProperty("message", out JsonElement message), "diagnostic message missing");
        AssertTrue(first.TryGetProperty("source", out JsonElement source), "diagnostic source missing");
        AssertTrue(first.TryGetProperty("line", out JsonElement line), "diagnostic line missing");
        AssertTrue(first.TryGetProperty("column", out JsonElement column), "diagnostic column missing");
        AssertTrue(first.TryGetProperty("spanLength", out JsonElement spanLength), "diagnostic spanLength missing");

        AssertEqual("CFGS8013", code.GetString(), "unexpected diagnostic code");
        AssertEqual("info", severity.GetString(), "unexpected diagnostic severity");
        AssertFalse(string.IsNullOrWhiteSpace(message.GetString()), "diagnostic message must not be empty");
        AssertEqual(Path.GetFullPath(file), Path.GetFullPath(source.GetString() ?? string.Empty), "diagnostic source mismatch");
        AssertTrue(line.GetInt32() > 0, "line must be 1-based");
        AssertTrue(column.GetInt32() > 0, "column must be 1-based");
        AssertTrue(spanLength.GetInt32() > 0, "spanLength must be positive");
    }

    private static string WriteTempCfgs(string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cfgs-tooling-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "test.cfs");
        File.WriteAllText(file, NormalizeNewlines(source));
        return file;
    }

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static void AssertContains(IEnumerable<string> values, string expected, string message)
    {
        if (!values.Contains(expected, StringComparer.Ordinal))
            throw new InvalidOperationException(message);
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
