using System.Diagnostics;
using System.Text.Json;
using CFGS_VM.Hosting;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Plugin;
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
            CliLintJsonProducesStableDiagnosticShape,
            HostingFileIoPermissionScopeAppliesToInvocations,
            HostingDefaultsDenyFileIo,
            WorkspaceJailRejectsEscapingPaths,
            HostingSerializesConcurrentInvocations,
            AsyncHotStartInheritsVmPolicy,
            WorkspaceIsVmScoped,
            PluginLoadRejectsMismatchedPackageLockHash,
            HostingPreservesStructuredDiagnosticCode,
            CfbLoaderRejectsCorruptElementCounts
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

    private static void HostingFileIoPermissionScopeAppliesToInvocations()
    {
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };
        engine.Permissions.AllowFileIO = false;

        CfgsScript script = engine.CompileSource("<host-permissions>", """
func is_file_allowed() {
    return probeAllowFileIO();
}
""");
        script.Runtime.Builtins.Register(new BuiltinDescriptor(
            "probeAllowFileIO",
            0,
            0,
            (args, instr) => VM.AllowFileIO));

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();
        bool allowedDuringInvoke = script.InvokeAsync<bool>("is_file_allowed").GetAwaiter().GetResult();

        AssertFalse(allowedDuringInvoke, "host InvokeAsync did not apply the configured file I/O permission scope");
        AssertTrue(VM.AllowFileIO, "host InvokeAsync did not restore the outer VM file I/O scope");
    }

    private static void HostingDefaultsDenyFileIo()
    {
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        // No permissions configured: the embedding surface must be fail-closed.
        AssertFalse(engine.Permissions.AllowFileIO, "AllowFileIO must default to false");
        AssertFalse(engine.Permissions.AllowNativeInterop, "AllowNativeInterop must default to false");
        AssertFalse(engine.Permissions.AllowPluginLoading, "AllowPluginLoading must default to false");

        CfgsScript script = engine.CompileSource("<defaults>", """
func is_file_allowed() {
    return probeAllowFileIO();
}
""");
        script.Runtime.Builtins.Register(new BuiltinDescriptor(
            "probeAllowFileIO",
            0,
            0,
            (args, instr) => VM.AllowFileIO));

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();
        bool allowed = script.InvokeAsync<bool>("is_file_allowed").GetAwaiter().GetResult();

        AssertFalse(allowed, "file I/O must be denied by default when no permissions are granted");
    }

    private static void WorkspaceJailRejectsEscapingPaths()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "cfgs-jail-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = workspace,
            CompilerVersion = global::Program.Version
        };
        engine.Permissions.AllowFileIO = true;
        engine.Permissions.RestrictFileIoToWorkspace = true;

        CfgsScript script = engine.CompileSource("<jail>", """
func resolve(path) {
    return resolveWorkspaceHost(path);
}
""");
        script.Runtime.Builtins.Register(new BuiltinDescriptor(
            "resolveWorkspaceHost",
            1,
            1,
            (args, instr) =>
            {
                VM vm = VM.CurrentVm ?? throw new InvalidOperationException("missing current VM");
                return vm.ResolveWorkspacePath(args[0]?.ToString() ?? string.Empty);
            }));

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();

        string inside = script.InvokeAsync<string>("resolve", "sub/file.bin").GetAwaiter().GetResult() ?? "";
        AssertTrue(
            inside.StartsWith(Path.GetFullPath(workspace), StringComparison.OrdinalIgnoreCase),
            "a contained path must resolve inside the workspace");

        bool rejected = false;
        try
        {
            script.InvokeAsync<string>("resolve", "../escape.bin").GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex.ToString().Contains("workspace", StringComparison.OrdinalIgnoreCase))
        {
            rejected = true;
        }

        AssertTrue(rejected, "a path escaping the workspace must be rejected when RestrictFileIoToWorkspace is enabled");
    }

    private static void HostingSerializesConcurrentInvocations()
    {
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<host-concurrency>", """
async func gated(value) {
    markStart();
    yield;
    yield;
    markEnd();
    return value;
}
""");

        object sync = new();
        int active = 0;
        int maxActive = 0;
        script.Runtime.Builtins.Register(new BuiltinDescriptor(
            "markStart",
            0,
            0,
            (args, instr) =>
            {
                lock (sync)
                {
                    active++;
                    maxActive = Math.Max(maxActive, active);
                }

                return null!;
            }));
        script.Runtime.Builtins.Register(new BuiltinDescriptor(
            "markEnd",
            0,
            0,
            (args, instr) =>
            {
                lock (sync)
                    active--;
                return null!;
            }));

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();
        Task<object?>[] calls =
        [
            script.InvokeAsync("gated", 1),
            script.InvokeAsync("gated", 2),
            script.InvokeAsync("gated", 3)
        ];

        object?[] results = Task.WhenAll(calls).GetAwaiter().GetResult();
        AssertEqual(1, maxActive, "concurrent host invocations should be serialized per script instance");
        AssertEqual(1, Convert.ToInt32(results[0]), "first invocation returned wrong value");
        AssertEqual(2, Convert.ToInt32(results[1]), "second invocation returned wrong value");
        AssertEqual(3, Convert.ToInt32(results[2]), "third invocation returned wrong value");
    }

    private static void AsyncHotStartInheritsVmPolicy()
    {
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };
        engine.Permissions.AllowNativeInterop = false;

        CfgsScript script = engine.CompileSource("<host-child-policy>", """
async func checkPolicy() {
    yield;
    return isNativeInteropAllowed();
}
""");
        script.Runtime.Builtins.Register(new BuiltinDescriptor(
            "isNativeInteropAllowed",
            0,
            0,
            (args, instr) => VM.CurrentVm?.AllowNativeInterop ?? true));

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();
        bool allowed = script.InvokeAsync<bool>("checkPolicy").GetAwaiter().GetResult();

        AssertFalse(allowed, "async hot-start child VM did not inherit AllowNativeInterop=false");
    }

    private static void WorkspaceIsVmScoped()
    {
        string processDirectory = Directory.GetCurrentDirectory();
        string workspace = Path.Combine(Path.GetTempPath(), "cfgs-workspace-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = processDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<workspace-scope>", """
func writeInWorkspace(path) {
    setWorkspaceHost(path);
    writeWorkspaceFileHost("local.bin");
    return getWorkspaceHost();
}
""");
        script.Runtime.Builtins.Register(new BuiltinDescriptor(
            "setWorkspaceHost",
            1,
            1,
            (args, instr) =>
            {
                VM vm = VM.CurrentVm ?? throw new InvalidOperationException("missing current VM");
                vm.WorkingDirectory = args[0]?.ToString() ?? string.Empty;
                return null!;
            }));
        script.Runtime.Builtins.Register(new BuiltinDescriptor(
            "getWorkspaceHost",
            0,
            0,
            (args, instr) => VM.CurrentVm?.WorkingDirectory ?? string.Empty));
        script.Runtime.Builtins.Register(new BuiltinDescriptor(
            "writeWorkspaceFileHost",
            1,
            1,
            (args, instr) =>
            {
                VM vm = VM.CurrentVm ?? throw new InvalidOperationException("missing current VM");
                File.WriteAllBytes(vm.ResolveWorkspacePath(args[0]?.ToString() ?? string.Empty), [65, 66, 67]);
                return null!;
            }));

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();
        string actualWorkspace = script.InvokeAsync<string>("writeInWorkspace", workspace).GetAwaiter().GetResult() ?? "";

        AssertEqual(Path.GetFullPath(workspace), Path.GetFullPath(actualWorkspace), "script workspace did not update");
        AssertEqual(processDirectory, Directory.GetCurrentDirectory(), "VM workspace changes must not change the process current directory");
        AssertTrue(File.Exists(Path.Combine(workspace, "local.bin")), "relative file write did not use VM workspace");
    }

    private static void PluginLoadRejectsMismatchedPackageLockHash()
    {
        string root = Path.Combine(Path.GetTempPath(), "cfgs-lock-test", Guid.NewGuid().ToString("N"));
        string pluginDirectory = Path.Combine(root, ".cfgs", "plugins");
        Directory.CreateDirectory(pluginDirectory);

        string sourceDll = typeof(global::Program).Assembly.Location;
        string pluginDll = Path.Combine(pluginDirectory, Path.GetFileName(sourceDll));
        File.Copy(sourceDll, pluginDll);
        File.WriteAllText(Path.Combine(root, "cfgs.lock.json"), $$"""
{
  "lockfileVersion": 1,
  "generatedAtUtc": "2026-06-19T00:00:00.0000000+00:00",
  "plugins": [
    {
      "name": "hash-test",
      "version": "1.0.0",
      "type": "plugin",
      "source": "source.dll",
      "installedPath": ".cfgs/plugins/{{Path.GetFileName(sourceDll)}}",
      "sha256": "0000",
      "includeDependencies": true
    }
  ]
}
""");

        try
        {
            new VM().LoadPlugin(pluginDll);
            throw new InvalidOperationException("plugin load should have failed for mismatched lockfile hash");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("lockfile hash mismatch", StringComparison.OrdinalIgnoreCase))
        {
            // Expected.
        }
    }

    private static void HostingPreservesStructuredDiagnosticCode()
    {
        CompilerException compilerException = new(
            "custom semantic failure",
            2,
            4,
            "structured.cfs",
            spanLength: 3,
            diagnosticCode: "CFGS2999");

        CfgsScriptException scriptException = CfgsScriptException.From(compilerException);

        AssertEqual("CFGS2999", scriptException.Code, "host exception did not preserve explicit diagnostic code");
        AssertEqual("CompileError", scriptException.Category, "host exception category changed unexpectedly");
        AssertEqual(2, scriptException.Line, "host exception line changed unexpectedly");
        AssertEqual(4, scriptException.Column, "host exception column changed unexpectedly");
    }

    private static string WriteTempCfgs(string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cfgs-tooling-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "test.cfs");
        File.WriteAllText(file, NormalizeNewlines(source));
        return file;
    }

    private static void CfbLoaderRejectsCorruptElementCounts()
    {
        // A hostile/corrupt .cfb could declare an enormous element count and force the loader into a
        // huge allocation (OutOfMemoryException) before reading any data. The loader must instead
        // reject out-of-range counts with a clean InvalidDataException. We hand-craft a valid header
        // followed by a bogus plugin count of int.MaxValue.
        string path = Path.Combine(Path.GetTempPath(), $"cfgs_cfb_corrupt_{Guid.NewGuid():N}.cfb");
        try
        {
            using (FileStream fs = File.Create(path))
            using (BinaryWriter writer = new(fs, System.Text.Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("CFB1"));
                writer.Write(CompiledScript.CurrentBytecodeVersion);
                writer.Write("test");        // name
                writer.Write(string.Empty);  // sourceHash
                writer.Write("1.0");         // compilerVersion
                writer.Write(false);         // shouldAutoInvokeMain
                writer.Write(int.MaxValue);  // malicious plugin count
            }

            bool threw = false;
            try
            {
                _ = CfbFile.Load(path);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }

            AssertTrue(threw, "CfbFile.Load must reject an out-of-range element count with InvalidDataException");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
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
