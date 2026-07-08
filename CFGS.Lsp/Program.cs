namespace CFGS.Lsp;

internal static class Program
{
    public static async Task<int> Main()
    {
        // Der Sprachserver laeuft als Kindprozess von VS Code. Ohne diese Absicherungen wuerde jede
        // unerwartete Ausnahme (ein fehlerhaftes Protokoll-Frame, ein Sonderfall in einer Feature-Anfrage,
        // eine nebenlaeufige Analyse) den Prozess still beenden - fuer den Nutzer sieht das so aus, als sei
        // der LSP "abgekackt": keine Diagnosen, keine Referenzen mehr. Alles Unerwartete wird deshalb nach
        // stderr geloggt (landet im VS-Code-Ausgabekanal "CFGS Language Server"), damit ein kuenftiger
        // Absturz konkret diagnostizierbar ist statt spurlos.
        AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
        {
            try { Console.Error.WriteLine($"[CFGS-LSP] UNHANDLED EXCEPTION: {e.ExceptionObject}"); }
            catch { /* stderr kaputt: dann laesst sich nichts mehr melden */ }
        };

        TaskScheduler.UnobservedTaskException += static (_, e) =>
        {
            try { Console.Error.WriteLine($"[CFGS-LSP] UNOBSERVED TASK EXCEPTION: {e.Exception}"); }
            catch { }
            // Als beobachtet markieren, damit eine verwaiste Hintergrund-Task den Prozess nicht mitreisst.
            e.SetObserved();
        };

        try
        {
            CfgsAnalyzer analyzer = new();
            LspServer server = new(analyzer);
            await server.RunAsync();
            return Environment.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CFGS-LSP] FATAL: language server terminated: {ex}");
            return 1;
        }
    }
}
