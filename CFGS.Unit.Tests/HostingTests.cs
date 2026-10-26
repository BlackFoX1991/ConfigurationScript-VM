using CFGS_VM.Hosting;
using Xunit;

namespace CFGS.Unit.Tests;

public sealed class HostingTests
{
    [Fact]
    public async Task TopLevelRunsOnceAndMainIsExplicit()
    {
        CfgsScript script = new CfgsScriptEngine().CompileSource("<xunit-host-main>", """
var count = 0;
func main() { count = count + 1; }
""");

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await script.RunAsync(cancellationToken);
        await script.RunAsync(cancellationToken);
        Assert.Equal(0, script.GetGlobal<int>("count"));

        await script.InvokeAsync("main", null, cancellationToken);
        Assert.Equal(1, script.GetGlobal<int>("count"));
    }

    [Fact]
    public async Task AsyncMemberAccessSupportsCancellation()
    {
        CfgsScript script = new CfgsScriptEngine().CompileSource("<xunit-host-member>", """
class Box() { property value { get; set; } = 1; }
""");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await script.RunAsync(cancellationToken);
        object box = (await script.NewAsync("Box", null, cancellationToken))!;

        await script.SetMemberAsync(box, "value", 7, cancellationToken);
        Assert.Equal(7, await script.GetMemberAsync<int>(box, "value", cancellationToken));

        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        CfgsScriptException error = await Assert.ThrowsAsync<CfgsScriptException>(
            () => script.GetMemberAsync(box, "value", canceled.Token));
        Assert.Equal("CFGS9100", error.Code);
    }
}
