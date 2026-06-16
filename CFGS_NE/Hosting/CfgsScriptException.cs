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

            return effective switch
            {
                VMException vm => new CfgsScriptException(
                    InferCode(vm.Category),
                    vm.Category,
                    vm.RawMessage,
                    FirstNonEmpty(vm.FileSource, fallbackSource),
                    vm.Line >= 0 ? vm.Line : null,
                    vm.Column >= 0 ? vm.Column : null,
                    vm.LanguageStackTrace,
                    effective),

                CompilerException compiler => new CfgsScriptException(
                    InferCode(compiler.Category),
                    compiler.Category,
                    compiler.RawMessage,
                    FirstNonEmpty(compiler.FileSource, fallbackSource),
                    compiler.Line >= 0 ? compiler.Line : null,
                    compiler.Column >= 0 ? compiler.Column : null,
                    null,
                    effective),

                ParserException parser => new CfgsScriptException(
                    InferCode(parser.Category),
                    parser.Category,
                    parser.RawMessage,
                    FirstNonEmpty(parser.Filename, fallbackSource),
                    parser.Line >= 0 ? parser.Line : null,
                    parser.Column >= 0 ? parser.Column : null,
                    null,
                    effective),

                LexerException lexer => new CfgsScriptException(
                    InferCode(lexer.Category),
                    lexer.Category,
                    lexer.RawMessage,
                    FirstNonEmpty(lexer.Filename, fallbackSource),
                    lexer.Line >= 0 ? lexer.Line : null,
                    lexer.Column >= 0 ? lexer.Column : null,
                    null,
                    effective),

                OperationCanceledException canceled => new CfgsScriptException(
                    "CFGS9100",
                    "Canceled",
                    canceled.Message,
                    fallbackSource,
                    null,
                    null,
                    null,
                    effective),

                _ => new CfgsScriptException(
                    "CFGS9000",
                    "SystemError",
                    string.IsNullOrWhiteSpace(effective.Message) ? effective.GetType().Name : effective.Message,
                    fallbackSource,
                    null,
                    null,
                    null,
                    effective)
            };
        }

        private static string InferCode(string category)
            => category switch
            {
                "LexerError" => "CFGS1000",
                "ParserError" => "CFGS2000",
                "CompileError" => "CFGS3000",
                "RuntimeError" => "CFGS5000",
                "Canceled" => "CFGS9100",
                _ => "CFGS9000"
            };

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    }
}
