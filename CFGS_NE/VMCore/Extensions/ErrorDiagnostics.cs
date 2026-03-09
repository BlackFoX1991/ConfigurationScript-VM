using CFGS_VM.Analytic.Ex;
using System.Reflection;

namespace CFGS_VM.VMCore.Extensions
{
    /// <summary>
    /// Defines the <see cref="ErrorDiagnostic" />
    /// </summary>
    internal readonly record struct ErrorDiagnostic(
        string Category,
        string Message,
        string? SourceFile,
        int? Line,
        int? Column,
        string? LanguageStack,
        string? ManagedStack)
    {
        /// <summary>
        /// Gets a value indicating whether HasLocation
        /// </summary>
        public bool HasLocation => !string.IsNullOrWhiteSpace(SourceFile) || Line.HasValue || Column.HasValue;
    }

    /// <summary>
    /// Defines the <see cref="ErrorDiagnostics" />
    /// </summary>
    internal static class ErrorDiagnostics
    {
        /// <summary>
        /// The FromException
        /// </summary>
        /// <param name="ex">The ex<see cref="Exception"/></param>
        /// <param name="fallbackSourceName">The fallbackSourceName<see cref="string?"/></param>
        /// <returns>The <see cref="ErrorDiagnostic"/></returns>
        public static ErrorDiagnostic FromException(Exception ex, string? fallbackSourceName = null)
        {
            Exception effective = Unwrap(ex);

            ErrorDiagnostic diag = effective switch
            {
                VMException vm => new ErrorDiagnostic(
                    Category: vm.Category,
                    Message: NormalizeMessage(vm.RawMessage),
                    SourceFile: FirstNonEmpty(vm.FileSource, fallbackSourceName),
                    Line: vm.Line >= 0 ? vm.Line : null,
                    Column: vm.Column >= 0 ? vm.Column : null,
                    LanguageStack: NormalizeStack(vm.LanguageStackTrace),
                    ManagedStack: effective.StackTrace),

                ParserException p => new ErrorDiagnostic(
                    Category: p.Category,
                    Message: NormalizeMessage(p.RawMessage),
                    SourceFile: FirstNonEmpty(p.Filename, fallbackSourceName),
                    Line: p.Line >= 0 ? p.Line : null,
                    Column: p.Column >= 0 ? p.Column : null,
                    LanguageStack: null,
                    ManagedStack: effective.StackTrace),

                LexerException l => new ErrorDiagnostic(
                    Category: l.Category,
                    Message: NormalizeMessage(l.RawMessage),
                    SourceFile: FirstNonEmpty(l.Filename, fallbackSourceName),
                    Line: l.Line >= 0 ? l.Line : null,
                    Column: l.Column >= 0 ? l.Column : null,
                    LanguageStack: null,
                    ManagedStack: effective.StackTrace),

                CompilerException c => new ErrorDiagnostic(
                    Category: c.Category,
                    Message: NormalizeMessage(c.RawMessage),
                    SourceFile: FirstNonEmpty(c.FileSource, fallbackSourceName),
                    Line: c.Line >= 0 ? c.Line : null,
                    Column: c.Column >= 0 ? c.Column : null,
                    LanguageStack: null,
                    ManagedStack: effective.StackTrace),

                _ => new ErrorDiagnostic(
                    Category: "SystemError",
                    Message: NormalizeMessage(effective.Message),
                    SourceFile: fallbackSourceName,
                    Line: null,
                    Column: null,
                    LanguageStack: null,
                    ManagedStack: effective.StackTrace)
            };

            if (!diag.HasLocation && !string.IsNullOrWhiteSpace(fallbackSourceName))
                diag = diag with { SourceFile = fallbackSourceName };

            return diag;
        }

        /// <summary>
        /// The FormatHeadline
        /// </summary>
        /// <param name="diag">The diag<see cref="ErrorDiagnostic"/></param>
        /// <returns>The <see cref="string"/></returns>
        public static string FormatHeadline(ErrorDiagnostic diag)
            => $"[{diag.Category}] {diag.Message}";

        /// <summary>
        /// The FormatLocation
        /// </summary>
        /// <param name="diag">The diag<see cref="ErrorDiagnostic"/></param>
        /// <returns>The <see cref="string?"/></returns>
        public static string? FormatLocation(ErrorDiagnostic diag)
        {
            if (!diag.HasLocation)
                return null;

            string source = string.IsNullOrWhiteSpace(diag.SourceFile) ? "<unknown>" : diag.SourceFile;
            string line = diag.Line.HasValue ? diag.Line.Value.ToString() : "?";
            string col = diag.Column.HasValue ? diag.Column.Value.ToString() : "?";
            return $"{source}:{line}:{col}";
        }

        /// <summary>
        /// The FormatLanguageStack
        /// </summary>
        /// <param name="diag">The diag<see cref="ErrorDiagnostic"/></param>
        /// <returns>The <see cref="string?"/></returns>
        public static string? FormatLanguageStack(ErrorDiagnostic diag)
            => NormalizeStack(diag.LanguageStack);

        /// <summary>
        /// The Unwrap
        /// </summary>
        /// <param name="ex">The ex<see cref="Exception"/></param>
        /// <returns>The <see cref="Exception"/></returns>
        private static Exception Unwrap(Exception ex)
        {
            if (ex is AggregateException agg && agg.InnerExceptions.Count == 1 && agg.InnerExceptions[0] is not null)
                return Unwrap(agg.InnerExceptions[0]);
            if (ex is TargetInvocationException tie && tie.InnerException is not null)
                return Unwrap(tie.InnerException);
            return ex;
        }

        /// <summary>
        /// The NormalizeMessage
        /// </summary>
        /// <param name="message">The message<see cref="string?"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string NormalizeMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "unspecified error";

            string m = message.Trim();

            const string runtimePrefix = "Runtime error:";
            if (m.StartsWith(runtimePrefix, StringComparison.OrdinalIgnoreCase))
                m = m[runtimePrefix.Length..].TrimStart();

            return m;
        }

        /// <summary>
        /// The NormalizeStack
        /// </summary>
        /// <param name="stack">The stack<see cref="string?"/></param>
        /// <returns>The <see cref="string?"/></returns>
        private static string? NormalizeStack(string? stack)
        {
            if (string.IsNullOrWhiteSpace(stack))
                return null;
            return stack.Trim();
        }

        /// <summary>
        /// The FirstNonEmpty
        /// </summary>
        /// <param name="a">The a<see cref="string?"/></param>
        /// <param name="b">The b<see cref="string?"/></param>
        /// <returns>The <see cref="string?"/></returns>
        private static string? FirstNonEmpty(string? a, string? b)
        {
            if (!string.IsNullOrWhiteSpace(a)) return a;
            if (!string.IsNullOrWhiteSpace(b)) return b;
            return null;
        }
    }
}
