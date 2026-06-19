using CFGS_VM.Analytic.Ex;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.Hosting
{
    public sealed class CfgsScriptException : Exception
    {
        public string Code { get; }

        public string Category { get; }

        public string? SourceFile { get; }

        public int? Line { get; }

        public int? Column { get; }

        public string? LanguageStack { get; }

        private CfgsScriptException(
            string code,
            string category,
            string message,
            string? sourceFile,
            int? line,
            int? column,
            string? languageStack,
            Exception innerException)
            : base(message, innerException)
        {
            Code = code;
            Category = category;
            SourceFile = sourceFile;
            Line = line;
            Column = column;
            LanguageStack = string.IsNullOrWhiteSpace(languageStack) ? null : languageStack;
        }

        public static CfgsScriptException From(Exception exception, string? fallbackSource = null)
        {
            if (exception is CfgsScriptException scriptException)
                return scriptException;

            Exception effective = exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
                ? aggregate.InnerExceptions[0]
                : exception;

            if (effective is OperationCanceledException canceled)
            {
                return new CfgsScriptException(
                    "CFGS9100",
                    "Canceled",
                    canceled.Message,
                    fallbackSource,
                    null,
                    null,
                    null,
                    effective);
            }

            ErrorDiagnostic diagnostic = ErrorDiagnostics.FromException(effective, fallbackSource);
            return new CfgsScriptException(
                diagnostic.Code,
                diagnostic.Category,
                diagnostic.Message,
                diagnostic.SourceFile,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.LanguageStack,
                effective);
        }

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    }
}
