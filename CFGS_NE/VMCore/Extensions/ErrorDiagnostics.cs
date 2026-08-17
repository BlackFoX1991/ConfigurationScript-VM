using CFGS_VM.Analytic.Ex;
using System.Reflection;

namespace CFGS_VM.VMCore.Extensions
{
    internal readonly record struct ErrorDiagnostic(
        string Code,
        string Category,
        string Message,
        string? SourceFile,
        int? Line,
        int? Column,
        int? SpanLength,
        string? Help,
        string? LanguageStack,
        string? ManagedStack)
    {
        /// <summary>
        /// Gets a value indicating whether HasLocation
        /// </summary>
        public bool HasLocation => !string.IsNullOrWhiteSpace(SourceFile) || Line.HasValue || Column.HasValue;
    }

    internal static class ErrorDiagnostics
    {
        public static ErrorDiagnostic FromException(Exception ex, string? fallbackSourceName = null)
        {
            Exception effective = Unwrap(ex);

            ErrorDiagnostic diag = effective switch
            {
                VMException vm => new ErrorDiagnostic(
                    Code: FirstNonEmpty(vm.DiagnosticCode, InferCode(vm.Category, vm.RawMessage))!,
                    Category: vm.Category,
                    Message: NormalizeMessage(vm.RawMessage),
                    SourceFile: FirstNonEmpty(vm.FileSource, fallbackSourceName),
                    Line: vm.Line >= 0 ? vm.Line : null,
                    Column: vm.Column >= 0 ? vm.Column : null,
                    SpanLength: InferSpanLength(vm.RawMessage),
                    Help: InferHelp(vm.Category, vm.RawMessage),
                    LanguageStack: NormalizeStack(vm.LanguageStackTrace),
                    ManagedStack: effective.StackTrace),

                ParserException p => new ErrorDiagnostic(
                    Code: FirstNonEmpty(p.DiagnosticCode, InferCode(p.Category, p.RawMessage))!,
                    Category: p.Category,
                    Message: NormalizeMessage(p.RawMessage),
                    SourceFile: FirstNonEmpty(p.Filename, fallbackSourceName),
                    Line: p.Line >= 0 ? p.Line : null,
                    Column: p.Column >= 0 ? p.Column : null,
                    SpanLength: p.SpanLength ?? InferSpanLength(p.RawMessage),
                    Help: InferHelp(p.Category, p.RawMessage),
                    LanguageStack: null,
                    ManagedStack: effective.StackTrace),

                LexerException l => new ErrorDiagnostic(
                    Code: FirstNonEmpty(l.DiagnosticCode, InferCode(l.Category, l.RawMessage))!,
                    Category: l.Category,
                    Message: NormalizeMessage(l.RawMessage),
                    SourceFile: FirstNonEmpty(l.Filename, fallbackSourceName),
                    Line: l.Line >= 0 ? l.Line : null,
                    Column: l.Column >= 0 ? l.Column : null,
                    SpanLength: l.SpanLength ?? InferSpanLength(l.RawMessage),
                    Help: InferHelp(l.Category, l.RawMessage),
                    LanguageStack: null,
                    ManagedStack: effective.StackTrace),

                CompilerException c => new ErrorDiagnostic(
                    Code: FirstNonEmpty(c.DiagnosticCode, InferCode(c.Category, c.RawMessage))!,
                    Category: c.Category,
                    Message: NormalizeMessage(c.RawMessage),
                    SourceFile: FirstNonEmpty(c.FileSource, fallbackSourceName),
                    Line: c.Line >= 0 ? c.Line : null,
                    Column: c.Column >= 0 ? c.Column : null,
                    SpanLength: c.SpanLength ?? InferSpanLength(c.RawMessage),
                    Help: InferHelp(c.Category, c.RawMessage),
                    LanguageStack: null,
                    ManagedStack: effective.StackTrace),

                _ => new ErrorDiagnostic(
                    Code: "CFGS9000",
                    Category: "SystemError",
                    Message: NormalizeMessage(effective.Message),
                    SourceFile: fallbackSourceName,
                    Line: null,
                    Column: null,
                    SpanLength: null,
                    Help: null,
                    LanguageStack: null,
                    ManagedStack: effective.StackTrace)
            };

            if (!diag.HasLocation && !string.IsNullOrWhiteSpace(fallbackSourceName))
                diag = diag with { SourceFile = fallbackSourceName };

            if (diag.SpanLength is null)
                diag = diag with { SpanLength = InferSourceSpanLength(diag.SourceFile, diag.Line, diag.Column) };

            return diag;
        }

        public static string FormatHeadline(ErrorDiagnostic diag)
            => $"{diag.Code}: [{diag.Category}] {diag.Message}";

        public static string? FormatLocation(ErrorDiagnostic diag)
        {
            if (!diag.HasLocation)
                return null;

            string source = string.IsNullOrWhiteSpace(diag.SourceFile) ? "<unknown>" : diag.SourceFile;
            string line = diag.Line.HasValue ? diag.Line.Value.ToString() : "?";
            string col = diag.Column.HasValue ? diag.Column.Value.ToString() : "?";
            return $"{source}:{line}:{col}";
        }

        public static string? FormatSourceSpan(ErrorDiagnostic diag)
        {
            if (string.IsNullOrWhiteSpace(diag.SourceFile) || !diag.Line.HasValue)
                return null;

            string sourceFile = diag.SourceFile!;
            if (!File.Exists(sourceFile))
                return null;

            string? lineText = ReadLine(sourceFile, diag.Line.Value);
            if (lineText is null)
                return null;

            int lineNo = Math.Max(1, diag.Line.Value);
            int col = Math.Max(1, diag.Column ?? 1);
            int spanLength = Math.Max(1, diag.SpanLength ?? 1);
            int markerStart = Math.Min(Math.Max(0, col - 1), lineText.Length);
            int markerLength = Math.Min(spanLength, Math.Max(1, lineText.Length - markerStart));
            string lineNoText = lineNo.ToString();
            string gutter = new(' ', lineNoText.Length);

            return
                $"  {gutter} |\n" +
                $"  {lineNoText} | {lineText}\n" +
                $"  {gutter} | {new string(' ', markerStart)}{new string('^', markerLength)}";
        }

        public static string? FormatHelp(ErrorDiagnostic diag)
        {
            if (string.IsNullOrWhiteSpace(diag.Help))
                return null;

            return $"= help: {diag.Help}";
        }

        public static string? FormatLanguageStack(ErrorDiagnostic diag)
            => NormalizeStack(diag.LanguageStack);

        private static Exception Unwrap(Exception ex)
        {
            if (ex is AggregateException agg && agg.InnerExceptions.Count == 1 && agg.InnerExceptions[0] is not null)
                return Unwrap(agg.InnerExceptions[0]);
            if (ex is TargetInvocationException tie && tie.InnerException is not null)
                return Unwrap(tie.InnerException);
            return ex;
        }

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
        /// Falls back to category/message inference for throw sites that carry no explicit
        /// diagnostic code. Internal rather than private so the LSP shares this one ladder: the
        /// specification requires editor diagnostics to match the CLI, which a second copy cannot
        /// guarantee.
        /// </summary>
        internal static string InferCode(string category, string? message)
        {
            string m = NormalizeMessage(message).ToLowerInvariant();

            if (m.Contains(".cfb", StringComparison.Ordinal) ||
                m.Contains("binary", StringComparison.Ordinal) ||
                m.Contains("bytecode", StringComparison.Ordinal))
                return "CFGS5000";

            if (category.Equals("LexError", StringComparison.OrdinalIgnoreCase))
                return "CFGS0001";

            if (category.Equals("ParseError", StringComparison.OrdinalIgnoreCase))
            {
                if (m.Contains("expected", StringComparison.Ordinal))
                    return "CFGS1001";
                if (m.Contains("import", StringComparison.Ordinal))
                    return "CFGS4001";
                if (m.Contains("duplicate", StringComparison.Ordinal))
                    return "CFGS1002";
                return "CFGS1000";
            }

            if (category.Equals("CompileError", StringComparison.OrdinalIgnoreCase))
            {
                if (m.Contains("dot access", StringComparison.Ordinal) ||
                    m.Contains("bracket access", StringComparison.Ordinal))
                    return "CFGS2003";
                if (m.Contains("receiver", StringComparison.Ordinal))
                    return "CFGS2004";
                if (m.Contains("unknown", StringComparison.Ordinal))
                    return "CFGS2001";
                if (m.Contains("inaccessible", StringComparison.Ordinal))
                    return "CFGS2002";
                if (m.Contains("duplicate", StringComparison.Ordinal))
                    return "CFGS2005";
                if (m.Contains("override", StringComparison.Ordinal))
                    return "CFGS2006";
                if (m.Contains("assignment", StringComparison.Ordinal) ||
                    m.Contains("assign target", StringComparison.Ordinal) ||
                    m.Contains("read-only", StringComparison.Ordinal))
                    return "CFGS2007";
                if (m.Contains("invalid static member access", StringComparison.Ordinal) ||
                    m.Contains("invalid instance member access", StringComparison.Ordinal) ||
                    m.Contains("member is static", StringComparison.Ordinal) ||
                    m.Contains("member is instance", StringComparison.Ordinal))
                    return "CFGS2008";
                if (m.Contains("reserved", StringComparison.Ordinal))
                    return "CFGS2009";
                if (m.Contains("interface", StringComparison.Ordinal))
                    return "CFGS2010";
                if (m.Contains("constructor", StringComparison.Ordinal) ||
                    m.Contains("base ctor", StringComparison.Ordinal))
                    return "CFGS2011";
                if (m.Contains("property", StringComparison.Ordinal) ||
                    m.Contains("backing-field", StringComparison.Ordinal) ||
                    m.Contains("accessor", StringComparison.Ordinal))
                    return "CFGS2012";
                if (m.Contains("ambiguous", StringComparison.Ordinal))
                    return "CFGS2013";
                if (m.Contains("async", StringComparison.Ordinal) ||
                    m.Contains("await", StringComparison.Ordinal) ||
                    m.Contains("yield", StringComparison.Ordinal))
                    return "CFGS2014";
                if (m.Contains("argument", StringComparison.Ordinal) ||
                    m.Contains("parameter", StringComparison.Ordinal) ||
                    m.Contains("spread", StringComparison.Ordinal) ||
                    m.Contains("rest", StringComparison.Ordinal) ||
                    m.Contains("default", StringComparison.Ordinal))
                    return "CFGS2015";
                if (m.Contains("namespace", StringComparison.Ordinal))
                    return "CFGS2016";
                return "CFGS2000";
            }

            if (category.Equals("RuntimeError", StringComparison.OrdinalIgnoreCase))
            {
                if (m.Contains("undefined variable", StringComparison.Ordinal))
                    return "CFGS3001";
                if (m.Contains("not indexable", StringComparison.Ordinal))
                    return "CFGS3003";
                if (m.Contains("index", StringComparison.Ordinal))
                    return "CFGS3002";
                if (m.Contains("argument", StringComparison.Ordinal))
                    return "CFGS3004";
                return "CFGS3000";
            }

            return "CFGS9000";
        }

        private static string? InferHelp(string category, string? message)
        {
            string m = NormalizeMessage(message);
            string lower = m.ToLowerInvariant();

            if (lower.Contains("class members require dot access", StringComparison.Ordinal) ||
                lower.Contains("class static members require dot access", StringComparison.Ordinal) ||
                lower.Contains("not indexable; use dot access", StringComparison.Ordinal))
                return "use dot syntax for class members, for example C.member or instance.member; bracket access is only for indexable values such as arrays and dictionaries";

            if (lower.Contains("unknown static member", StringComparison.Ordinal) ||
                lower.Contains("unknown instance member", StringComparison.Ordinal) ||
                lower.Contains("unknown initializer member", StringComparison.Ordinal))
                return "check the member name spelling and whether the member is declared as instance or static";

            if (lower.Contains("invalid static member access", StringComparison.Ordinal))
                return "access instance members through an object instance, not through the class";

            if (lower.Contains("invalid instance member access", StringComparison.Ordinal))
                return "access static members through the class or type receiver, not through an instance";

            if (lower.Contains("inaccessible", StringComparison.Ordinal))
                return "check the member visibility; private and protected members are only visible from allowed class contexts";

            if (lower.Contains("invalid receiver usage", StringComparison.Ordinal))
                return "`this`, `type`, `super`, and `outer` are only available in their documented class/member contexts";

            if (lower.Contains("expected identifier after '.'", StringComparison.Ordinal))
                return "dot access requires an identifier after '.', for example object.member";

            if (lower.Contains("undefined variable", StringComparison.Ordinal))
                return "declare the variable before reading it, or check the name spelling";

            if (lower.Contains("positional argument cannot follow named", StringComparison.Ordinal))
                return "place all positional arguments before named arguments";

            if (lower.Contains("spread non-array", StringComparison.Ordinal) ||
                lower.Contains("cannot spread", StringComparison.Ordinal))
                return "spread arguments require an array value";

            return null;
        }

        private static int? InferSpanLength(string? message)
        {
            string m = NormalizeMessage(message);
            int first = m.IndexOf('\'');
            if (first >= 0)
            {
                int second = m.IndexOf('\'', first + 1);
                if (second > first + 1)
                    return second - first - 1;
            }

            return null;
        }

        private static int? InferSourceSpanLength(string? path, int? line, int? column)
        {
            if (string.IsNullOrWhiteSpace(path) || !line.HasValue || !column.HasValue)
                return null;
            if (!File.Exists(path))
                return null;

            string? lineText = ReadLine(path, line.Value);
            if (lineText is null)
                return null;

            int start = Math.Min(Math.Max(column.Value - 1, 0), lineText.Length);
            if (start >= lineText.Length)
                return 1;

            char first = lineText[start];
            if (char.IsLetterOrDigit(first) || first == '_')
            {
                int end = start + 1;
                while (end < lineText.Length && (char.IsLetterOrDigit(lineText[end]) || lineText[end] == '_'))
                    end++;
                return Math.Max(1, end - start);
            }

            if (first == '"' || first == '\'')
            {
                int end = start + 1;
                while (end < lineText.Length)
                {
                    if (lineText[end] == first && lineText[end - 1] != '\\')
                        return Math.Max(1, end - start + 1);
                    end++;
                }
            }

            if (start + 1 < lineText.Length)
            {
                string two = lineText.Substring(start, 2);
                if (two is "==" or "!=" or "<=" or ">=" or "<<" or ">>" or "&&" or "||" or "++" or "--" or "+=" or "-=" or "*=" or "/=" or "%=" or "**" or "??")
                    return 2;
            }

            return 1;
        }

        private static string? ReadLine(string path, int line)
        {
            if (line <= 0)
                return null;

            try
            {
                using StreamReader reader = new(path);
                for (int current = 1; ; current++)
                {
                    string? text = reader.ReadLine();
                    if (text is null)
                        return null;
                    if (current == line)
                        return text;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string? NormalizeStack(string? stack)
        {
            if (string.IsNullOrWhiteSpace(stack))
                return null;
            return stack.Trim();
        }

        private static string? FirstNonEmpty(string? a, string? b)
        {
            if (!string.IsNullOrWhiteSpace(a)) return a;
            if (!string.IsNullOrWhiteSpace(b)) return b;
            return null;
        }
    }
}
