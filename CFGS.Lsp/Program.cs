namespace CFGS.Lsp;

internal static class Program
{
    public static async Task<int> Main()
    {
        CfgsAnalyzer analyzer = new();
        LspServer server = new(analyzer);
        await server.RunAsync();
        return Environment.ExitCode;
    }
}
