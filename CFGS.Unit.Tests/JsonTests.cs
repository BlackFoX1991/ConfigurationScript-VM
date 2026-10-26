using CFGS_VM.Hosting;
using Xunit;

namespace CFGS.Unit.Tests;

public sealed class JsonTests
{
    [Fact]
    public async Task JsonAliasesRoundTripBigIntegersAsNumbers()
    {
        CfgsScript script = new CfgsScriptEngine().CompileSource("<xunit-json-bigint>", """
var number = bigint("1267650600228229401496703205377");
var encoded = json.to({"number": number}, false);
var decoded = json.from(encoded)["number"];
var kind = typeof(decoded);
var equal = decoded == number;
""");

        await script.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal("{\"number\":1267650600228229401496703205377}", script.GetGlobal<string>("encoded"));
        Assert.Equal("BigInt", script.GetGlobal<string>("kind"));
        Assert.True(script.GetGlobal<bool>("equal"));
    }

    [Theory]
    [InlineData("var a = []; a[] = a; json.to(a, false);")]
    [InlineData("var d = {}; d[\"self\"] = d; json.to(d, false);")]
    public async Task JsonAliasReportsCyclesAsRuntimeErrors(string source)
    {
        CfgsScript script = new CfgsScriptEngine().CompileSource("<xunit-json-cycle>", source);

        CfgsScriptException error = await Assert.ThrowsAsync<CfgsScriptException>(
            () => script.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal("CFGS3000", error.Code);
    }
}
