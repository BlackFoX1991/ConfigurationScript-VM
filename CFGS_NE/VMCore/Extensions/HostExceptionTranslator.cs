using System.Collections.Generic;
using System.Reflection;

namespace CFGS_VM.VMCore.Extensions
{
    /// <summary>
    /// Translates raw CLR exceptions that surface from runtime operations into clean,
    /// language-native CFGS runtime diagnostics.
    ///
    /// Numeric coercion, checked casts, parsing, and collection helpers can throw framework
    /// exceptions (<see cref="OverflowException"/>, <see cref="InvalidCastException"/>,
    /// <see cref="FormatException"/>, and so on) whose messages leak .NET wording and .NET type
    /// names to CFGS users. When one of those reaches an execution boundary it is a runtime fault
    /// of the running script, not a host/system failure, so it MUST be reported as a
    /// <c>RuntimeError</c> in the <c>CFGS3xxx</c> range rather than a raw <c>SystemError</c>.
    ///
    /// This translator is the shared safety net applied at every error boundary. Genuinely external
    /// faults (I/O, host services, unknown invariants) are left untranslated and continue to be
    /// reported as system errors.
    /// </summary>
    internal static class HostExceptionTranslator
    {
        /// <summary>The exception object <c>type</c> string used for translated runtime faults.</summary>
        public const string RuntimeErrorType = "RuntimeError";

        /// <summary>
        /// Attempts to translate a CLR exception into a clean CFGS runtime message and diagnostic code.
        /// </summary>
        /// <param name="ex">The raw CLR exception.</param>
        /// <param name="message">The clean, language-native message (no "Runtime error:" prefix).</param>
        /// <param name="code">The stable CFGS3xxx diagnostic code.</param>
        /// <returns><c>true</c> when the exception is a recognized script-level runtime fault.</returns>
        public static bool TryTranslate(Exception ex, out string message, out string code)
        {
            switch (ex)
            {
                case DivideByZeroException:
                    message = "division by zero";
                    code = "CFGS3007";
                    return true;

                case OverflowException:
                    message = "arithmetic overflow: numeric value is outside the representable range of the target type";
                    code = "CFGS3006";
                    return true;

                case InvalidCastException ice:
                    message = DescribeInvalidCast(ice);
                    code = "CFGS3008";
                    return true;

                case FormatException:
                    message = "invalid numeric format: the text is not a valid number";
                    code = "CFGS3009";
                    return true;

                case KeyNotFoundException:
                    message = "key not found in dictionary";
                    code = "CFGS3010";
                    return true;

                // IndexOutOfRangeException is standalone; ArgumentOutOfRangeException derives from
                // ArgumentException, so it is matched before the generic ArgumentException case below.
                case IndexOutOfRangeException:
                case ArgumentOutOfRangeException:
                    message = "index out of range";
                    code = "CFGS3002";
                    return true;

                case NullReferenceException:
                    message = "null reference";
                    code = "CFGS3012";
                    return true;

                // InvalidOperationException is used broadly, so translate only the numeric-coercion
                // shapes raised by the value conversion helpers; leave unrecognized ones as system
                // faults rather than mislabeling an internal invariant as a script error.
                case InvalidOperationException when TryTranslateNumericCoercion(ex.Message, out string numericMessage):
                    message = numericMessage;
                    code = "CFGS3011";
                    return true;

                default:
                    message = string.Empty;
                    code = string.Empty;
                    return false;
            }
        }

        /// <summary>
        /// Unwraps reflection and aggregate wrappers so the meaningful inner fault is reported.
        /// A native call routed through reflection surfaces the real fault inside a
        /// <see cref="TargetInvocationException"/> whose own message ("Exception has been thrown by
        /// the target of an invocation.") is useless to a CFGS user.
        /// </summary>
        public static Exception Unwrap(Exception ex)
        {
            if (ex is TargetInvocationException tie && tie.InnerException is not null)
                return Unwrap(tie.InnerException);
            if (ex is AggregateException agg && agg.InnerExceptions.Count == 1 && agg.InnerExceptions[0] is not null)
                return Unwrap(agg.InnerExceptions[0]);
            return ex;
        }

        /// <summary>
        /// Produces the human-facing detail for a native interop failure: a clean CFGS phrasing when
        /// the unwrapped fault is a recognized runtime fault, otherwise the unwrapped .NET message.
        /// </summary>
        public static string DescribeHostFailure(Exception ex)
        {
            Exception inner = Unwrap(ex);
            if (TryTranslate(inner, out string translated, out _))
                return translated;
            return string.IsNullOrWhiteSpace(inner.Message) ? "unspecified host failure" : inner.Message;
        }

        private static string DescribeInvalidCast(InvalidCastException ice)
            => string.IsNullOrWhiteSpace(ice.Message)
                ? "invalid type conversion"
                : "invalid type conversion: the value cannot be converted to the required type";

        /// <summary>
        /// Recognizes the numeric coercion messages produced by the value conversion helpers
        /// (for example "Not numeric: String", "Not integral: Boolean", or the floating-point/decimal
        /// coercion guards) and rewrites them into language-native wording, preserving the offending
        /// .NET type name when the helper embedded one.
        /// </summary>
        private static bool TryTranslateNumericCoercion(string? rawMessage, out string message)
        {
            string raw = rawMessage?.Trim() ?? string.Empty;

            if (TryExtractAfter(raw, "Not numeric:", out string numericType))
            {
                message = numericType.Length == 0
                    ? "value is not numeric"
                    : $"value of type '{numericType}' is not numeric";
                return true;
            }

            if (TryExtractAfter(raw, "Not integral:", out string integralType))
            {
                message = integralType.Length == 0
                    ? "value is not an integer"
                    : $"value of type '{integralType}' is not an integer";
                return true;
            }

            if (raw.StartsWith("Cannot coerce floating-point to BigInteger", StringComparison.Ordinal))
            {
                message = "cannot convert a floating-point value to an integer without explicit rounding";
                return true;
            }

            if (raw.StartsWith("Cannot coerce decimal to BigInteger", StringComparison.Ordinal))
            {
                message = "cannot convert a decimal value to an integer without explicit rounding";
                return true;
            }

            message = string.Empty;
            return false;
        }

        private static bool TryExtractAfter(string raw, string prefix, out string remainder)
        {
            if (raw.StartsWith(prefix, StringComparison.Ordinal))
            {
                remainder = raw[prefix.Length..].Trim();
                // The helpers use "null" as a sentinel; keep it as an empty descriptor so callers emit
                // the generic phrasing rather than "value of type 'null'".
                if (string.Equals(remainder, "null", StringComparison.OrdinalIgnoreCase))
                    remainder = string.Empty;
                return true;
            }

            remainder = string.Empty;
            return false;
        }
    }
}
