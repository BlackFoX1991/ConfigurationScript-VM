using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace CFGS_VM.Hosting
{
    /// <summary>
    /// Hosts a CFGS script as a Windows service. The script is launched on a background task when the
    /// Service Control Manager (SCM) starts the service; stopping the service cancels a token that the
    /// VM dispatch loop and the script's <c>main</c> entry point observe cooperatively. Lifecycle events
    /// and unhandled script errors are written to the Windows Event Log (source <see cref="EventLogSource"/>),
    /// because a service has no console attached.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class CfgsServiceHost : ServiceBase
    {
        internal const string EventLogSource = "CFGS";

        private readonly string _scriptPath;
        private readonly CancellationTokenSource _cts = new();
        private Task? _runner;

        private CfgsServiceHost(string scriptPath)
        {
            _scriptPath = scriptPath;
            ServiceName = EventLogSource;
            CanStop = true;
            CanShutdown = true;
            AutoLog = true;
        }

        /// <summary>
        /// Entry point for the <c>-as_service</c> CLI command. Blocks (via <see cref="ServiceBase.Run(ServiceBase)"/>)
        /// until the SCM stops the service. Must be launched by the SCM, not from an interactive console.
        /// </summary>
        internal static int Run(string scriptPath)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("[CFGS] -as_service is only supported on Windows.");
                return 1;
            }

            string fullPath = Path.GetFullPath(scriptPath);
            if (!File.Exists(fullPath))
            {
                Console.Error.WriteLine($"[CFGS] service script not found: {fullPath}");
                return 1;
            }

            // When launched interactively (not by the SCM) ServiceBase.Run only prints an unhelpful
            // message and returns success. Detect that up front and give a clear, non-zero result.
            if (Environment.UserInteractive)
            {
                Console.Error.WriteLine("[CFGS] -as_service must be started by the Windows Service Control Manager, not from a console.");
                Console.Error.WriteLine("[CFGS] Register it first:  -install_service <name> -script <script>   then  sc start <name>");
                return 1;
            }

            try
            {
                ServiceBase.Run(new CfgsServiceHost(fullPath));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CFGS] failed to start the service host: {ex.Message}");
                Console.Error.WriteLine("[CFGS] -as_service must be started by the Windows Service Control Manager. Use -install_service to register it.");
                return 1;
            }
        }

        protected override void OnStart(string[] args)
        {
            WriteServiceEvent($"CFGS service starting. Script: {_scriptPath}", EventLogEntryType.Information);
            _runner = Task.Run(() => RunScriptAsync(_cts.Token));
        }

        private async Task RunScriptAsync(CancellationToken ct)
        {
            try
            {
                await Program.RunScriptFileAsync(_scriptPath, debug: false, interactiveDebug: false, ct).ConfigureAwait(false);

                if (!ct.IsCancellationRequested)
                    WriteServiceEvent("CFGS service script completed; the service will stop.", EventLogEntryType.Information);
            }
            catch (OperationCanceledException)
            {
                // Expected when the service is stopped.
            }
            catch (Exception ex)
            {
                WriteServiceEvent($"CFGS service script failed: {ex}", EventLogEntryType.Error);
            }
            finally
            {
                // If the script returned on its own (not because of a stop request), ask the SCM to
                // stop the service so its status reflects that nothing is running anymore.
                if (!ct.IsCancellationRequested)
                {
                    try { Stop(); }
                    catch (InvalidOperationException) { /* already stopping */ }
                }
            }
        }

        protected override void OnStop()
        {
            WriteServiceEvent("CFGS service stopping.", EventLogEntryType.Information);
            _cts.Cancel();

            try
            {
                _runner?.Wait(TimeSpan.FromSeconds(20));
            }
            catch (AggregateException)
            {
                // Swallow cancellation/script faults already reported via the event log.
            }
        }

        protected override void OnShutdown() => OnStop();

        private static void WriteServiceEvent(string message, EventLogEntryType type)
        {
            try
            {
                if (!EventLog.SourceExists(EventLogSource))
                    EventLog.CreateEventSource(EventLogSource, "Application");

                using EventLog log = new EventLog("Application") { Source = EventLogSource };
                log.WriteEntry(message, type);
            }
            catch
            {
                // Event-log failures (e.g. missing rights) must never crash the service host.
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _cts.Dispose();

            base.Dispose(disposing);
        }
    }
}
