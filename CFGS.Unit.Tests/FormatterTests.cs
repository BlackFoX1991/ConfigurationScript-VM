using CFGS_VM.Tooling;
using Xunit;

namespace CFGS.Unit.Tests;

public sealed class FormatterTests
{
    [Fact]
    public void FormattingIsIdempotentAndPreservesBracesInStrings()
    {
        const string source = "func render(){\nvar text=\"{literal}\";\nreturn text;\n}\n";
        CfgsFormatOptions options = new();

        string once = CfgsFormatter.Format(source, options).Text;
        string twice = CfgsFormatter.Format(once, options).Text;

        Assert.Equal(once, twice);
        Assert.Contains("\"{literal}\"", once);
    }
}
