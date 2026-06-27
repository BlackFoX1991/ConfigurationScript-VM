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
            FormatterPreservesMergeOperators,
            FormatterHandlesTernarySpacing,
            FormatterIgnoresBracesInsideStringsAndComments,
            FormatterPreservesCommentOnlyFiles,
            FormatterPreservesHeaderTrailingAndSuppressionComments,
            FormatterPreservesCommentsInsideClassesAndEnums,
            FormatterAstPathFormatsImportsAndObjectInitializers,
            FormatterAstPathFormatsClassesAndProperties,
            FormatterAstPathFormatsInterfacesAndEnums,
            FormatterWrapsLongCallsCollectionsAndConditions,
            FormatterWrapsLongNewExpressionsAndInitializers,
            FormatterPreservesShortUsingSyntaxViaFallback,
            LinterReportsStyleAndAstDiagnostics,
            LinterFixesOnlySafeStyleIssues,
            LinterSupportsSuppressions,
            LinterReportsExpandedRuleSet,
            LinterSuppressesExpandedRuleSet,
            LinterStrictPresetEnablesAdditionalRules,
            LinterDoesNotFlagExportedOrNamespacedApiFunctionsAsUnused,
            CliLintJsonProducesStableDiagnosticShape,
            CliLintHonorsToolingLintConfig,
            HostingFileIoPermissionScopeAppliesToInvocations,
            HostingDefaultsDenyFileIo,
            RunAsyncCanDeferAndThenAutoInvokeMainOnce,
            WorkspaceJailRejectsEscapingPaths,
            HostingSerializesConcurrentInvocations,
            AsyncHotStartInheritsVmPolicy,
            AsyncHotStartInheritsWorkspaceJail,
            HostCanConstructClassesAndAccessMembers,
            WorkspaceIsVmScoped,
            PluginLoadRejectsMismatchedPackageLockHash,
            HostingPreservesStructuredDiagnosticCode,
            CfbLoaderRejectsCorruptElementCounts,
            NativeInteropConvertsClosuresBigIntegersAndCollections,
            NativeInteropGuardsCyclicConversion,
            CfbRoundTripsSurrogateCharOperand,
            CfbRejectsCorruptDecimalOperand,
            JsonSerializesBigIntegerAsNumber,
            LoopClosureCaptureMatchesCSharp,
            ForeachDestructuringAndBreakContinueStillWork,
            CompoundAndIncrementEvaluateLValueOnce,
            NullConcatenatesAsNull,
            ShiftsPromoteInsteadOfMasking,
            SliceAssignmentRequiresMatchingLength,
            SortAndIndexOfUseNumericComparison,
            MinMaxHandleMixedNumericKinds
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

    private static void FormatterPreservesMergeOperators()
    {
        string source = """
func main(){
var a={"x":1}<<<{"x":2};
var b={"x":1}>>>{"x":2};
return a;
}
""";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertTrue(actual.Contains("<<<", StringComparison.Ordinal), "formatter must keep the '<<<' operator intact");
        AssertTrue(actual.Contains(">>>", StringComparison.Ordinal), "formatter must keep the '>>>' operator intact");
        AssertFalse(actual.Contains("<< <", StringComparison.Ordinal), "formatter must not split '<<<' into '<< <'");
        AssertFalse(actual.Contains(">> >", StringComparison.Ordinal), "formatter must not split '>>>' into '>> >'");
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

    private static void FormatterPreservesCommentOnlyFiles()
    {
        string source = """
# headline

#+
block comment
+#
""" + "\n";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertEqual(NormalizeNewlines(source), NormalizeNewlines(actual), "formatter should preserve comment-only documents");
    }

    private static void FormatterPreservesHeaderTrailingAndSuppressionComments()
    {
        string source = """
# file header
func main(){ # keep after brace
# cfgs-disable-next-line CFGS8001
var value=1; # keep trailing

return value;
}
# file footer
""";

        string expected = """
# file header
func main(){ # keep after brace
    # cfgs-disable-next-line CFGS8001
    var value = 1; # keep trailing

    return value;
}
# file footer
""" + "\n";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertEqual(NormalizeNewlines(expected), NormalizeNewlines(actual), "formatter should preserve header, trailing and suppression comments on the AST path");
    }

    private static void FormatterPreservesCommentsInsideClassesAndEnums()
    {
        string source = """
class Box(){
# before field
var value=1; # field comment

enum Mode{
Off,
# keep enum comment
On=3
}
}
""";

        string expected = """
class Box(){
    # before field
    var value = 1; # field comment

    enum Mode{
        Off,
        # keep enum comment
        On = 3
    }
}
""" + "\n";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertEqual(NormalizeNewlines(expected), NormalizeNewlines(actual), "formatter should preserve comments inside class and enum bodies");
    }

    private static void FormatterAstPathFormatsImportsAndObjectInitializers()
    {
        string source = """
import{answer as ans,Box}from "mod.cfs";
func main(){
var box=new Box(1){value:2};
return ans;
}
""";

        string expected = """
import { answer as ans, Box } from "mod.cfs";
func main(){
    var box = new Box(1) { value: 2 };
    return ans;
}
""" + "\n";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertEqual(NormalizeNewlines(expected), NormalizeNewlines(actual), "AST formatter should normalize imports and object initializers structurally");
    }

    private static void FormatterPreservesShortUsingSyntaxViaFallback()
    {
        string source = """
func main(){
using var resource=newResource();
return resource;
}
""";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertTrue(actual.Contains("using var resource = newResource();", StringComparison.Ordinal), "formatter must preserve short using syntax instead of rewriting it through lowered AST");
        AssertFalse(actual.Contains("using(var resource = newResource())", StringComparison.Ordinal), "formatter must not expand short using declarations into block using syntax");
    }

    private static void FormatterAstPathFormatsClassesAndProperties()
    {
        string source = """
export class Box(v):Base(v),IThing{
public static var shared=1;
private var _name="";
property name{
get{return this._name;}
private set(next){this._name=next;}
}
static property version{get;set;}=1;
enum Mode{Off,On=3,Auto}
class Inner(){
func run(){return Box.version;}
}
protected static func build(seed){return new Box(seed){name:"x"};}
}
""";

        string expected = """
export class Box(v) : Base(v), IThing{
    public static var shared = 1;
    private var _name = "";
    property name{
        get{
            return this._name;
        }
        private set(next){
            this._name = next;
        }
    }
    static property version { get; set; } = 1;
    enum Mode{
        Off,
        On = 3,
        Auto
    }
    class Inner(){
        func run(){
            return Box.version;
        }
    }
    protected static func build(seed){
        return new Box(seed) { name: "x" };
    }
}
""" + "\n";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertEqual(NormalizeNewlines(expected), NormalizeNewlines(actual), "AST formatter should handle class members, nested types and properties structurally");
    }

    private static void FormatterAstPathFormatsInterfacesAndEnums()
    {
        string source = """
export interface IThing:IBase,IRunnable{
property name{get;set;}
static property kind{get;}
index get(key);
index set(key,value);
async func load(id);
func label(){return this.name;}
}
export enum Mode{A=1,B,C=4}
""";

        string expected = """
export interface IThing : IBase, IRunnable{
    property name { get; set; }
    static property kind { get; }
    index get(key);
    index set(key, value);
    async func load(id);
    func label(){
        return this.name;
    }
}
export enum Mode{
    A = 1,
    B,
    C = 4
}
""" + "\n";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false)).Text;
        AssertEqual(NormalizeNewlines(expected), NormalizeNewlines(actual), "AST formatter should handle interface contracts and enum canonicalization structurally");
    }

    private static void FormatterWrapsLongCallsCollectionsAndConditions()
    {
        string source = """
func main(){
var values=[alpha,beta,gamma,delta];
var mapping={"alpha":alphaValue,"beta":betaValue,"gamma":gammaValue};
var result=combine(firstValue,secondValue,thirdValue,fourthValue);
if(firstCondition&&secondCondition&&thirdCondition){
return result;
}
}
""";

        string expected = """
func main(){
    var values = [
        alpha,
        beta,
        gamma,
        delta
    ];
    var mapping = {
        "alpha": alphaValue,
        "beta": betaValue,
        "gamma": gammaValue
    };
    var result = combine(
        firstValue,
        secondValue,
        thirdValue,
        fourthValue
    );
    if(firstCondition &&
        secondCondition &&
        thirdCondition){
        return result;
    }
}
""" + "\n";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false, MaxLineWidth: 40)).Text;
        AssertEqual(NormalizeNewlines(expected), NormalizeNewlines(actual), "AST formatter should wrap long calls, collections and logical conditions when max line width is constrained");
    }

    private static void FormatterWrapsLongNewExpressionsAndInitializers()
    {
        string source = """
func main(){
var box=new Box(firstValue,secondValue,thirdValue){primary:firstValue,secondary:secondValue,tertiary:thirdValue};
return box;
}
""";

        string expected = """
func main(){
    var box = new Box(
        firstValue,
        secondValue,
        thirdValue
    ) {
        primary: firstValue,
        secondary: secondValue,
        tertiary: thirdValue
    };
    return box;
}
""" + "\n";

        string actual = CfgsFormatter.Format(source, new CfgsFormatOptions(4, UseTabs: false, MaxLineWidth: 40)).Text;
        AssertEqual(NormalizeNewlines(expected), NormalizeNewlines(actual), "AST formatter should wrap long constructor calls and object initializers when max line width is constrained");
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
# cfgs-disable-next-line CFGS8013
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

    private static void LinterReportsExpandedRuleSet()
    {
        (_, string file) = WriteTempCfgsProject(
            """
import dep from "./d.cfs";

func main(flag){
    var value = 1;
    func inner(value){
        if(1 == "1"){
            return value;
        }else{
            return value + 1;
        }
        var after = value;
    }

    if(flag){
    }

    return inner(value);
}
""",
            additionalFiles: new Dictionary<string, string>
            {
                ["d.cfs"] = """
export const dep = 1;
"""
            });

        CfgsLintResult result = CfgsLinter.LintFile(file, new CfgsLintOptions(), global::Program.Version);
        string[] codes = result.Diagnostics.Select(static d => d.Code).Distinct(StringComparer.Ordinal).ToArray();

        AssertContains(codes, "CFGS8015", "shadowing diagnostic missing");
        AssertContains(codes, "CFGS8016", "unused import diagnostic missing");
        AssertContains(codes, "CFGS8017", "empty block diagnostic missing");
        AssertContains(codes, "CFGS8018", "suspicious equality diagnostic missing");
        AssertContains(codes, "CFGS8020", "guaranteed-termination unreachable diagnostic missing");
    }

    private static void LinterSuppressesExpandedRuleSet()
    {
        (_, string file) = WriteTempCfgsProject(
            """
import dep from "./d.cfs"; # cfgs-disable-line CFGS8016

func shadow(flag){
    var value = 1;
    if(flag){
        var value = 1; # cfgs-disable-line CFGS8015
        value = value + 1;
    }

    if(flag){ # cfgs-disable-line CFGS8017
    }

    if(1 == "1"){ # cfgs-disable-line CFGS8018
        return value;
    }

    return value;
}

func strictOnly(){
    if([]){ # cfgs-disable-line CFGS8019
        return 1;
    }

    return 42; # cfgs-disable-line CFGS8021
}

func terminates(flag){
    if(flag){
        return 1;
    }else{
        return 1;
    }

    var after = 0; # cfgs-disable-line CFGS8020
}
""",
            additionalFiles: new Dictionary<string, string>
            {
                ["d.cfs"] = """
export const dep = 1;
"""
            });

        CfgsLintResult result = CfgsLinter.LintFile(
            file,
            new CfgsLintOptions { Preset = CfgsLintPreset.Strict },
            global::Program.Version);

        string[] codes = result.Diagnostics.Select(static d => d.Code).Distinct(StringComparer.Ordinal).ToArray();

        AssertFalse(codes.Contains("CFGS8015", StringComparer.Ordinal), "cfgs-disable-line did not suppress CFGS8015");
        AssertFalse(codes.Contains("CFGS8016", StringComparer.Ordinal), "cfgs-disable-line did not suppress CFGS8016");
        AssertFalse(codes.Contains("CFGS8017", StringComparer.Ordinal), "cfgs-disable-line did not suppress CFGS8017");
        AssertFalse(codes.Contains("CFGS8018", StringComparer.Ordinal), "cfgs-disable-line did not suppress CFGS8018");
        AssertFalse(codes.Contains("CFGS8019", StringComparer.Ordinal), "cfgs-disable-line did not suppress CFGS8019");
        AssertFalse(codes.Contains("CFGS8020", StringComparer.Ordinal), "cfgs-disable-line did not suppress CFGS8020");
        AssertFalse(codes.Contains("CFGS8021", StringComparer.Ordinal), "cfgs-disable-line did not suppress CFGS8021");
    }

    private static void LinterStrictPresetEnablesAdditionalRules()
    {
        string file = WriteTempCfgs("""
func main(){
    if([]){
        return 1;
    }

    return 42;
}
""");

        CfgsLintResult recommended = CfgsLinter.LintFile(file, new CfgsLintOptions(), global::Program.Version);
        CfgsLintResult strict = CfgsLinter.LintFile(
            file,
            new CfgsLintOptions { Preset = CfgsLintPreset.Strict },
            global::Program.Version);

        string[] recommendedCodes = recommended.Diagnostics.Select(static d => d.Code).Distinct(StringComparer.Ordinal).ToArray();
        string[] strictCodes = strict.Diagnostics.Select(static d => d.Code).Distinct(StringComparer.Ordinal).ToArray();

        AssertFalse(recommendedCodes.Contains("CFGS8019", StringComparer.Ordinal), "recommended preset must not enable CFGS8019");
        AssertFalse(recommendedCodes.Contains("CFGS8021", StringComparer.Ordinal), "recommended preset must not enable CFGS8021");
        AssertContains(strictCodes, "CFGS8019", "strict preset must enable CFGS8019");
        AssertContains(strictCodes, "CFGS8021", "strict preset must enable CFGS8021");
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

    private static void CliLintHonorsToolingLintConfig()
    {
        (_, string file) = WriteTempCfgsProject(
            """
import dep from "./d.cfs";

func main(){
    if([]){
        return 1;
    }

    var note = "1234567890";
    return 42;
}
""",
            cfgsJson: """
{
  "tooling": {
    "maxLineLength": 12,
    "lint": {
      "preset": "strict",
      "maxLineLength": 8,
      "rules": {
        "CFGS8003": "warning",
        "CFGS8016": false,
        "CFGS8021": {
          "severity": "error"
        }
      }
    }
  }
}
""",
            additionalFiles: new Dictionary<string, string>
            {
                ["d.cfs"] = """
export const dep = 1;
"""
            });

        string vmAssembly = typeof(global::Program).Assembly.Location;

        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { vmAssembly, "-lint", file, "--json", "--max-line-length", "30" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("failed to start dotnet process");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);

        AssertEqual(1, process.ExitCode, $"lint command should surface strict diagnostics: {stderr}");

        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        string[] codes = root.EnumerateArray()
            .Select(static d => d.GetProperty("code").GetString() ?? string.Empty)
            .ToArray();

        AssertFalse(codes.Contains("CFGS8003", StringComparer.Ordinal), "CLI max-line-length must override manifest values");
        AssertFalse(codes.Contains("CFGS8016", StringComparer.Ordinal), "tooling.lint rule disable must suppress CFGS8016");
        AssertContains(codes, "CFGS8019", "strict preset from tooling.lint must enable CFGS8019");
        AssertContains(codes, "CFGS8021", "strict preset from tooling.lint must enable CFGS8021");

        JsonElement magicNumberDiagnostic = root.EnumerateArray()
            .First(static d => string.Equals(d.GetProperty("code").GetString(), "CFGS8021", StringComparison.Ordinal));
        AssertEqual("error", magicNumberDiagnostic.GetProperty("severity").GetString(), "tooling.lint severity override must apply to CFGS8021");
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

    private static void RunAsyncCanDeferAndThenAutoInvokeMainOnce()
    {
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<host-auto-main>", """
var counter = 0;

func main() {
    counter = counter + 1;
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();
        AssertEqual(0, script.GetGlobal<int>("counter"), "top-level load should not auto-run main when autoInvokeMain=false");

        script.RunAsync().GetAwaiter().GetResult();
        AssertEqual(1, script.GetGlobal<int>("counter"), "a later RunAsync() should still auto-run main once after a manual top-level load");

        script.RunAsync().GetAwaiter().GetResult();
        AssertEqual(1, script.GetGlobal<int>("counter"), "auto-main should not run more than once per script instance");
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

    private static void AsyncHotStartInheritsWorkspaceJail()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "cfgs-hot-jail-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = workspace,
            CompilerVersion = global::Program.Version
        };
        engine.Permissions.AllowFileIO = true;
        engine.Permissions.RestrictFileIoToWorkspace = true;

        CfgsScript script = engine.CompileSource("<host-hot-jail>", """
async func resolveAsync(path) {
    yield;
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

        string inside = script.InvokeAsync<string>("resolveAsync", "sub/file.bin").GetAwaiter().GetResult() ?? "";
        AssertTrue(
            inside.StartsWith(Path.GetFullPath(workspace), StringComparison.OrdinalIgnoreCase),
            "async hot-start child VM must keep contained paths inside the workspace");

        bool rejected = false;
        try
        {
            script.InvokeAsync<string>("resolveAsync", "../escape.bin").GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex.ToString().Contains("workspace", StringComparison.OrdinalIgnoreCase))
        {
            rejected = true;
        }

        AssertTrue(rejected, "async hot-start child VM did not inherit RestrictFileIoToWorkspace=true");
    }

    private static void HostCanConstructClassesAndAccessMembers()
    {
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };
        engine.SetService("currentUser", "Ada");

        CfgsScript script = engine.CompileSource("<host-class-access>", """
class Person {
    public var name = "";

    func init(name) {
        this.name = name;
    }

    public func greet(prefix = "Hello") {
        return prefix + ", " + this.name;
    }
}

func currentUserName() {
    return currentUser;
}

func readCurrentPerson() {
    return currentPerson.name;
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();

        object person = script.NewAsync("Person", "Ada").GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("host NewAsync returned null");
        string? initialName = script.GetMember<string>(person, "name");
        AssertEqual("Ada", initialName, "host GetMember returned the wrong initial value");

        script.SetMember(person, "name", "Grace");
        string? updatedName = script.GetMember<string>(person, "name");
        AssertEqual("Grace", updatedName, "host SetMember did not update the script instance");

        string? greeting = script.InvokeMemberAsync<string>(person, "greet", "Hi").GetAwaiter().GetResult();
        AssertEqual("Hi, Grace", greeting, "host InvokeMemberAsync returned the wrong method result");

        script.SetGlobal("currentPerson", person);
        string? currentPerson = script.InvokeAsync<string>("readCurrentPerson").GetAwaiter().GetResult();
        AssertEqual("Grace", currentPerson, "host SetGlobal did not expose the script instance back to CFGS");

        string? currentUser = script.InvokeAsync<string>("currentUserName").GetAwaiter().GetResult();
        AssertEqual("Ada", currentUser, "engine.SetService did not inject a host global");
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
        => WriteTempCfgsProject(source).File;

    private static (string Directory, string File) WriteTempCfgsProject(
        string source,
        string? cfgsJson = null,
        IReadOnlyDictionary<string, string>? additionalFiles = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cfgs-tooling-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string file = Path.Combine(directory, "test.cfs");
        File.WriteAllText(file, NormalizeNewlines(source));

        if (!string.IsNullOrWhiteSpace(cfgsJson))
            File.WriteAllText(Path.Combine(directory, "cfgs.json"), NormalizeNewlines(cfgsJson));

        if (additionalFiles is not null)
        {
            foreach ((string relativePath, string content) in additionalFiles)
            {
                string targetPath = Path.Combine(directory, relativePath);
                string? targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);

                File.WriteAllText(targetPath, NormalizeNewlines(content));
            }
        }

        return (directory, file);
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

    private static void NativeInteropConvertsClosuresBigIntegersAndCollections()
    {
        string fixtureAssembly = typeof(NativeFixture).Assembly.Location;

        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            NativeAssemblyBaseDirectory = Path.GetDirectoryName(fixtureAssembly)!,
            CompilerVersion = global::Program.Version
        };
        engine.Permissions.AllowNativeInterop = true;

        CfgsScript script = engine.CompileSource("<native-interop>", $$"""
import native "{{Path.GetFileName(fixtureAssembly)}}" as Fx;

func applyClosures() {
    var f1 = func(x) { return "one:" + x; };
    var f2 = func(x) { return "two:" + x; };
    var r1 = Fx.NativeFixture.Apply(f1, "a");
    var r2 = Fx.NativeFixture.Apply(f2, "a");
    var r1b = Fx.NativeFixture.Apply(f1, "b");
    return r1 + "|" + r2 + "|" + r1b;
}

func bigToDouble() {
    var big = 2 ** 100;
    return Fx.NativeFixture.AsDouble(big);
}

func joinArray() {
    return Fx.NativeFixture.JoinStrings(["a", "b", "c"]);
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();

        // B1: two distinct closures and a re-applied first closure must each dispatch to the
        // correct function. The old hash-code cache could collide and return a foreign delegate.
        string applied = script.InvokeAsync<string>("applyClosures").GetAwaiter().GetResult() ?? "";
        AssertEqual("one:a|two:a|one:b", applied, "native delegate cache dispatched closures incorrectly (B1)");

        // B4: a CFGS BigInteger (2**100) flowing into a native double parameter.
        double big = Convert.ToDouble(script.InvokeAsync<object>("bigToDouble").GetAwaiter().GetResult());
        AssertTrue(big > 1e30 && big < 1.3e30, "BigInteger did not convert to a native double parameter (B4)");

        // B5: a CFGS array flowing into a native List<string> parameter.
        string joined = script.InvokeAsync<string>("joinArray").GetAwaiter().GetResult() ?? "";
        AssertEqual("a,b,c", joined, "CFGS array did not convert to a native List<T> parameter (B5)");
    }

    private static void NativeInteropGuardsCyclicConversion()
    {
        string fixtureAssembly = typeof(NativeFixture).Assembly.Location;

        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            NativeAssemblyBaseDirectory = Path.GetDirectoryName(fixtureAssembly)!,
            CompilerVersion = global::Program.Version
        };
        engine.Permissions.AllowNativeInterop = true;

        CfgsScript script = engine.CompileSource("<native-interop-cycle>", $$"""
import native "{{Path.GetFileName(fixtureAssembly)}}" as Fx;

func triggerCycle() {
    return Fx.NativeFixture.SelfReferencingList();
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();

        // B3: converting a self-referencing CLR collection back to CFGS must surface a clean,
        // catchable error rather than recursing into an unrecoverable StackOverflowException.
        // Reaching the assertion at all proves the process survived the conversion.
        bool guarded = false;
        try
        {
            script.InvokeAsync<object>("triggerCycle").GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex.ToString().Contains("cyclic", StringComparison.OrdinalIgnoreCase))
        {
            guarded = true;
        }

        AssertTrue(guarded, "cyclic native value graph must surface a clean error instead of crashing (B3)");
    }

    private static void CfbRoundTripsSurrogateCharOperand()
    {
        // R3/R2: a lone surrogate char literal is accepted by the lexer and runs in the interpreter,
        // so it must also survive a .cfb save/load round-trip. The old UTF-8 char writer crashed on it.
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<cfb-char>", "var c = '\\uD800';");
        string path = Path.Combine(Path.GetTempPath(), $"cfgs_char_{Guid.NewGuid():N}.cfb");
        try
        {
            CfbFile.Save(script.CompiledScript, path);
            CompiledScript loaded = CfbFile.Load(path);
            bool found = loaded.Instructions.Any(i => i.Operand is char ch && ch == '\uD800');
            AssertTrue(found, "surrogate char operand did not round-trip through .cfb (R3/R2)");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void CfbRejectsCorruptDecimalOperand()
    {
        // R1: a decimal operand with invalid scale/flag bits must fail with the loader's documented
        // InvalidDataException, not a raw ArgumentException from the decimal constructor.
        string path = Path.Combine(Path.GetTempPath(), $"cfgs_decimal_{Guid.NewGuid():N}.cfb");
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
                writer.Write(0);             // plugin count
                writer.Write(0);             // function count
                writer.Write(1);             // instruction count
                writer.Write((int)CFGS_VM.VMCore.Command.OpCode.PUSH_NULL); // any valid opcode
                writer.Write((byte)5);       // OperandKind.Decimal
                writer.Write(0);             // lo
                writer.Write(0);             // mid
                writer.Write(0);             // hi
                writer.Write(29 << 16);      // flags: scale 29 is out of the valid 0..28 range
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

            AssertTrue(threw, "CfbFile.Load must reject an invalid decimal operand with InvalidDataException (R1)");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void JsonSerializesBigIntegerAsNumber()
    {
        // J1: BigInteger must serialize as a bare JSON number, consistent with int/long.
        System.Numerics.BigInteger big = System.Numerics.BigInteger.Pow(2, 100);
        string json = VM.JsonStringify(Value.FromObject(big));
        AssertEqual("1267650600228229401496703205376", json, "BigInteger must serialize as a bare JSON number (J1)");
    }

    private static void LoopClosureCaptureMatchesCSharp()
    {
        // S1: foreach gives a fresh per-iteration binding (closures see the iteration value), while a
        // C-style for keeps one shared, mutating counter (closures see the final value) — exactly C#.
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<loop-capture>", """
func foreachCapture() {
    var fns = [];
    foreach (x in [1, 2, 3]) {
        fns[] = func() { return x; };
    }
    return ("" + fns[0]()) + "," + ("" + fns[1]()) + "," + ("" + fns[2]());
}

func forCapture() {
    var fns = [];
    for (var i = 0; i < 3; i = i + 1) {
        fns[] = func() { return i; };
    }
    return ("" + fns[0]()) + "," + ("" + fns[1]()) + "," + ("" + fns[2]());
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();

        string foreachResult = script.InvokeAsync<string>("foreachCapture").GetAwaiter().GetResult() ?? "";
        AssertEqual("1,2,3", foreachResult, "foreach must give each iteration a fresh binding (C# semantics, S1)");

        string forResult = script.InvokeAsync<string>("forCapture").GetAwaiter().GetResult() ?? "";
        AssertEqual("3,3,3", forResult, "C-style for must keep one shared mutating counter (C# semantics)");
    }

    private static void ForeachDestructuringAndBreakContinueStillWork()
    {
        // The per-iteration foreach scope (S1) moved the loop-variable / destructuring-binding
        // declarations inside the loop, so re-verify the touched paths: destructuring, break/continue,
        // and that destructuring bindings also get fresh per-iteration bindings for captured closures.
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<foreach-destructure>", """
func destructure() {
    var pairs = [[1, 2], [3, 4], [5, 6]];
    var sum = 0;
    foreach (var [x, y] in pairs) {
        sum = sum + x + y;
    }
    return sum;
}

func breakContinue() {
    var total = 0;
    foreach (n in [1, 2, 3, 4, 5]) {
        if (n == 2) { continue; }
        if (n == 5) { break; }
        total = total + n;
    }
    return total;
}

func destructureCapture() {
    var fns = [];
    foreach (var [x, y] in [[1, 10], [2, 20]]) {
        fns[] = func() { return x + y; };
    }
    return ("" + fns[0]()) + "," + ("" + fns[1]());
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();

        AssertEqual(21, Convert.ToInt32(script.InvokeAsync<object>("destructure").GetAwaiter().GetResult()),
            "foreach array destructuring broke after the per-iteration scope change");
        AssertEqual(8, Convert.ToInt32(script.InvokeAsync<object>("breakContinue").GetAwaiter().GetResult()),
            "foreach break/continue broke after the per-iteration scope change");
        AssertEqual("11,22", script.InvokeAsync<string>("destructureCapture").GetAwaiter().GetResult() ?? "",
            "foreach destructuring bindings must also be fresh per iteration (S1)");
    }

    private static void CompoundAndIncrementEvaluateLValueOnce()
    {
        // C1: compound/increment assignment must evaluate the lvalue's target/index sub-expressions
        // exactly once. A side-effecting index previously ran twice (prefix: three times) and wrote
        // to a different slot than it read.
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<lvalue-once>", """
func compoundOnce() {
    var arr = [0, 0, 0, 0]; var seq = 0;
    func nx() { var r = seq; seq = seq + 1; return r; }
    arr[nx()] += 100;
    return ("" + arr[0]) + "," + ("" + arr[1]) + ",seq=" + ("" + seq);
}

func postfixOnce() {
    var arr = [10, 10, 10]; var c = 0;
    func ix() { var r = c; c = c + 1; return r; }
    arr[ix()]++;
    return ("" + arr[0]) + "," + ("" + arr[1]) + ",c=" + ("" + c);
}

func prefixOnce() {
    var arr = [5, 5, 5]; var c = 0;
    func ix() { c = c + 1; return 0; }
    var res = ++arr[ix()];
    return ("" + arr[0]) + ",res=" + ("" + res) + ",c=" + ("" + c);
}

func nestedOnce() {
    var grid = [[1, 1], [1, 1]]; var ic = 0; var jc = 0;
    func ri() { ic = ic + 1; return 0; }
    func rj() { jc = jc + 1; return 1; }
    grid[ri()][rj()] += 100;
    return ("" + grid[0][1]) + ",ic=" + ("" + ic) + ",jc=" + ("" + jc);
}

func memberOnce() {
    var box = {"v": 1}; var calls = 0;
    func getBox() { calls = calls + 1; return box; }
    getBox().v += 10;
    return ("" + box["v"]) + ",calls=" + ("" + calls);
}

func simpleRegression() {
    var a = [1, 2, 3]; var k = 1;
    a[k] += 10;
    a[0] += 100;
    return ("" + a[0]) + "," + ("" + a[1]) + "," + ("" + a[2]);
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();

        AssertEqual("100,0,seq=1", script.InvokeAsync<string>("compoundOnce").GetAwaiter().GetResult() ?? "",
            "compound assignment evaluated the index more than once / wrote the wrong slot (C1)");
        AssertEqual("11,10,c=1", script.InvokeAsync<string>("postfixOnce").GetAwaiter().GetResult() ?? "",
            "postfix ++ evaluated the index more than once (C1)");
        AssertEqual("6,res=6,c=1", script.InvokeAsync<string>("prefixOnce").GetAwaiter().GetResult() ?? "",
            "prefix ++ evaluated the index more than once (C1)");
        AssertEqual("101,ic=1,jc=1", script.InvokeAsync<string>("nestedOnce").GetAwaiter().GetResult() ?? "",
            "nested index compound evaluated sub-indices more than once (C1)");
        AssertEqual("11,calls=1", script.InvokeAsync<string>("memberOnce").GetAwaiter().GetResult() ?? "",
            "member compound evaluated the receiver more than once (C1)");
        AssertEqual("101,12,3", script.InvokeAsync<string>("simpleRegression").GetAwaiter().GetResult() ?? "",
            "compound assignment with a simple index regressed (C1)");
    }

    private static void NullConcatenatesAsNull()
    {
        // C2: null in '+' string concatenation renders as "null", consistent with str(null)/print(null).
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<null-concat>", """
func concatNulls() {
    return ("x" + null) + "|" + (null + "y") + "|" + ("a" + null + "b") + "|" + ("" + null);
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();
        AssertEqual("xnull|nully|anullb|null", script.InvokeAsync<string>("concatNulls").GetAwaiter().GetResult() ?? "",
            "null must concatenate as \"null\" (C2)");
    }

    private static void ShiftsPromoteInsteadOfMasking()
    {
        // C3: '<<' and '>>' must auto-promote like +,-,*,** instead of masking the shift count
        // (& 0x1F / & 0x3F) and letting native int/long overflow wrap.
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<shifts>", """
func shl() {
    return ("" + (1 << 31)) + "," + ("" + (1 << 32)) + "," + ("" + (1 << 40))
        + "," + ("" + (1 << 63)) + "," + ("" + (1 << 64)) + "," + ("" + (3 << 33));
}
func shr() {
    return ("" + (256 >> 40)) + "," + ("" + (5 >> 64)) + "," + ("" + (-8 >> 1));
}
func big() { return "" + ((2 ** 70) << 2); }
func small() {
    return ("" + (1 << 10)) + "," + ("" + (1024 >> 3)) + "," + ("" + (7 << 2)) + "," + ("" + (1 << 30));
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();

        AssertEqual("2147483648,4294967296,1099511627776,9223372036854775808,18446744073709551616,25769803776",
            script.InvokeAsync<string>("shl").GetAwaiter().GetResult() ?? "",
            "left shift masked the count / wrapped instead of promoting (C3)");
        AssertEqual("0,0,-4", script.InvokeAsync<string>("shr").GetAwaiter().GetResult() ?? "",
            "right shift masked the count instead of collapsing to 0 / arithmetic shift (C3)");
        AssertEqual("4722366482869645213696", script.InvokeAsync<string>("big").GetAwaiter().GetResult() ?? "",
            "BigInteger shift regressed (C3)");
        AssertEqual("1024,128,28,1073741824", script.InvokeAsync<string>("small").GetAwaiter().GetResult() ?? "",
            "small in-range shifts must be unchanged (C3 regression)");
    }

    private static void SliceAssignmentRequiresMatchingLength()
    {
        // C4: slice assignment must require the slice width to equal the number of supplied values,
        // instead of silently overwriting min(width, count) and dropping the rest.
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<slice-set>", """
func exact() {
    var a = [0, 1, 2, 3, 4];
    a[1~3] = [7, 7];
    return ("" + a[0]) + ("" + a[1]) + ("" + a[2]) + ("" + a[3]) + ("" + a[4]);
}
func tooLong() {
    var a = [0, 1, 2, 3, 4];
    try { a[1~3] = [7, 7, 7]; return "no-error"; } catch (e) { return "threw"; }
}
func tooShort() {
    var a = [0, 1, 2, 3, 4];
    try { a[1~4] = [5]; return "no-error"; } catch (e) { return "threw"; }
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();

        AssertEqual("07734", script.InvokeAsync<string>("exact").GetAwaiter().GetResult() ?? "",
            "slice assignment with matching length regressed (C4)");
        AssertEqual("threw", script.InvokeAsync<string>("tooLong").GetAwaiter().GetResult() ?? "",
            "slice assignment with too many values must throw (C4)");
        AssertEqual("threw", script.InvokeAsync<string>("tooShort").GetAwaiter().GetResult() ?? "",
            "slice assignment with too few values must throw (C4)");
    }

    private static void SortAndIndexOfUseNumericComparison()
    {
        // C5/C6: sort() and indexOf()/includes() must compare by numeric value across CLR kinds.
        // CFGS auto-promotes (int->long->BigInteger), so naturally-mixed arrays previously threw
        // from int.CompareTo(long) (sort) or missed cross-kind values (indexOf via CLR Equals).
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<numeric-collections>", """
func sortMixedIntLong() { return "" + [5, 2 ** 40, 3].sort(); }
func sortMixedBig() { return "" + [2 ** 70, 5, 100].sort(); }
func sortMixedDouble() { return "" + [3, 1.5, 2].sort(); }
func sortPureInt() { return "" + [3, 1, 2].sort(); }
func sortStrings() { return "" + ["banana", "apple", "cherry"].sort(); }
func indexOfCrossKind() {
    var f = [2 ** 40 / (2 ** 40), 99];
    return ("" + f.indexOf(1)) + "," + ("" + f.includes(1));
}
func indexOfPlain() {
    return ("" + [10, 20, 30].indexOf(20)) + "," + ("" + [10, 20].indexOf(99));
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();

        AssertEqual("[3, 5, 1099511627776]", script.InvokeAsync<string>("sortMixedIntLong").GetAwaiter().GetResult() ?? "",
            "sort must order mixed int/long numerically instead of throwing (C5)");
        AssertEqual("[5, 100, 1180591620717411303424]", script.InvokeAsync<string>("sortMixedBig").GetAwaiter().GetResult() ?? "",
            "sort must order mixed int/BigInteger numerically (C5)");
        AssertEqual("[1.5, 2, 3]", script.InvokeAsync<string>("sortMixedDouble").GetAwaiter().GetResult() ?? "",
            "sort must order mixed int/double numerically (C5)");
        AssertEqual("[1, 2, 3]", script.InvokeAsync<string>("sortPureInt").GetAwaiter().GetResult() ?? "",
            "pure-int sort regressed (C5)");
        AssertEqual("[apple, banana, cherry]", script.InvokeAsync<string>("sortStrings").GetAwaiter().GetResult() ?? "",
            "string sort regressed (C5)");
        AssertEqual("0,true", script.InvokeAsync<string>("indexOfCrossKind").GetAwaiter().GetResult() ?? "",
            "indexOf/includes must use numeric equality across CLR kinds (C6)");
        AssertEqual("1,-1", script.InvokeAsync<string>("indexOfPlain").GetAwaiter().GetResult() ?? "",
            "plain indexOf regressed (C6)");
    }

    private static void MinMaxHandleMixedNumericKinds()
    {
        // min/max compared via `dynamic a < b`, which threw on e.g. double vs BigInteger. They now
        // use the same numeric-aware comparison.
        CfgsScriptEngine engine = new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            CompilerVersion = global::Program.Version
        };

        CfgsScript script = engine.CompileSource("<min-max>", """
func r() {
    return ("" + math.min(5, 2 ** 40)) + "," + ("" + math.max(5, 2 ** 40))
        + "," + ("" + math.min(3, 1.5)) + "," + ("" + math.max(2 ** 70, 3.0));
}
""");

        script.RunAsync(autoInvokeMain: false).GetAwaiter().GetResult();
        AssertEqual("5,1099511627776,1.5,1180591620717411303424",
            script.InvokeAsync<string>("r").GetAwaiter().GetResult() ?? "",
            "min/max must compare mixed numeric kinds without throwing");
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
