using System.Text;
using System.Text.Json;

namespace CFGS_VM.VMCore.Extensions
{
    /// <summary>
    /// Defines the <see cref="ErrorTracker" />
    /// </summary>
    internal static class ErrorTracker
    {
        /// <summary>
        /// Defines the LogFileName
        /// </summary>
        private const string LogFileName = "error_tracking.jsonl";

        /// <summary>
        /// Defines the Sync
        /// </summary>
        private static readonly object Sync = new();

        /// <summary>
        /// Defines the Utf8NoBom
        /// </summary>
        private static readonly UTF8Encoding Utf8NoBom = new(false);

        /// <summary>
        /// The Track
        /// </summary>
        /// <param name="ex">The ex<see cref="Exception"/></param>
        /// <param name="phase">The phase<see cref="string"/></param>
        /// <param name="sourceName">The sourceName<see cref="string?"/></param>
        /// <returns>The <see cref="string?"/></returns>
        public static string? Track(Exception ex, string phase, string? sourceName = null)
        {
            string errorId = Guid.NewGuid().ToString("N");

            if (string.Equals(Environment.GetEnvironmentVariable("CFGS_DISABLE_ERROR_TRACKING"), "1", StringComparison.Ordinal))
                return null;

            bool tracked = false;
            try
            {
                ErrorDiagnostic diag = ErrorDiagnostics.FromException(ex, sourceName);

                ErrorTrackingEntry entry = new()
                {
                    ErrorId = errorId,
                    TimestampUtc = DateTime.UtcNow,
                    Phase = phase,
                    SourceName = sourceName,
                    Category = diag.Category,
                    ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                    Message = diag.Message,
                    SourceFile = diag.SourceFile,
                    Line = diag.Line,
                    Column = diag.Column,
                    LanguageStack = diag.LanguageStack,
                    StackTrace = diag.ManagedStack,
                    InnerExceptionChain = BuildInnerExceptionChain(ex),
                    RuntimeVersion = Environment.Version.ToString(),
                    ProcessId = Environment.ProcessId
                };

                string logPath = ResolveLogPath();
                string json = JsonSerializer.Serialize(entry);

                lock (Sync)
                {
                    File.AppendAllText(logPath, json + Environment.NewLine, Utf8NoBom);
                }
                tracked = true;
            }
            catch
            {
                // Error tracking must never interfere with the normal error flow.
            }

            return tracked ? errorId : null;
        }

        /// <summary>
        /// The ResolveLogPath
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        private static string ResolveLogPath()
        {
            string? configuredPath = Environment.GetEnvironmentVariable("CFGS_ERROR_TRACKING_PATH");
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string fullConfiguredPath = Path.GetFullPath(configuredPath);
                string? dir = Path.GetDirectoryName(fullConfiguredPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                return fullConfiguredPath;
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string preferredBaseDir = !string.IsNullOrWhiteSpace(localAppData)
                ? Path.Combine(localAppData, "Configuration Language")
                : Path.Combine(Path.GetTempPath(), "Configuration Language");

            try
            {
                Directory.CreateDirectory(preferredBaseDir);
                return Path.Combine(preferredBaseDir, LogFileName);
            }
            catch
            {
                string tempBaseDir = Path.Combine(Path.GetTempPath(), "Configuration Language");
                Directory.CreateDirectory(tempBaseDir);
                return Path.Combine(tempBaseDir, LogFileName);
            }
        }

        /// <summary>
        /// The BuildInnerExceptionChain
        /// </summary>
        /// <param name="ex">The ex<see cref="Exception"/></param>
        /// <returns>The <see cref="string?"/></returns>
        private static string? BuildInnerExceptionChain(Exception ex)
        {
            if (ex.InnerException is null)
                return null;

            StringBuilder sb = new();
            Exception? current = ex.InnerException;

            while (current is not null)
            {
                if (sb.Length > 0)
                    sb.Append(" --> ");
                sb.Append(current.GetType().Name);
                sb.Append(": ");
                sb.Append(current.Message);
                current = current.InnerException;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Defines the <see cref="ErrorTrackingEntry" />
        /// </summary>
        private sealed class ErrorTrackingEntry
        {
            /// <summary>
            /// Gets or sets the ErrorId
            /// </summary>
            public string ErrorId { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the TimestampUtc
            /// </summary>
            public DateTime TimestampUtc { get; set; }

            /// <summary>
            /// Gets or sets the Phase
            /// </summary>
            public string Phase { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the SourceName
            /// </summary>
            public string? SourceName { get; set; }

            /// <summary>
            /// Gets or sets the Category
            /// </summary>
            public string Category { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the ExceptionType
            /// </summary>
            public string ExceptionType { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the Message
            /// </summary>
            public string Message { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the SourceFile
            /// </summary>
            public string? SourceFile { get; set; }

            /// <summary>
            /// Gets or sets the Line
            /// </summary>
            public int? Line { get; set; }

            /// <summary>
            /// Gets or sets the Column
            /// </summary>
            public int? Column { get; set; }

            /// <summary>
            /// Gets or sets the LanguageStack
            /// </summary>
            public string? LanguageStack { get; set; }

            /// <summary>
            /// Gets or sets the StackTrace
            /// </summary>
            public string? StackTrace { get; set; }

            /// <summary>
            /// Gets or sets the InnerExceptionChain
            /// </summary>
            public string? InnerExceptionChain { get; set; }

            /// <summary>
            /// Gets or sets the RuntimeVersion
            /// </summary>
            public string RuntimeVersion { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the ProcessId
            /// </summary>
            public int ProcessId { get; set; }
        }
    }
}
