using System.Text;

namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    /// <summary>
    /// A mutable string accumulator. Repeated <c>s = s + x</c> rebuilds the whole string on every step
    /// (O(n²) for a growing string); a string builder appends in place (amortized O(n)). Created by the
    /// <c>strbuilder()</c> builtin and materialized to a plain string with <c>.str()</c> / <c>.toString()</c>.
    /// </summary>
    public sealed class StringBuilderHandle
    {
        private readonly StringBuilder _sb;

        public StringBuilderHandle() => _sb = new StringBuilder();

        public StringBuilderHandle(string initial) => _sb = new StringBuilder(initial);

        /// <summary>Appends the (already stringified) text and returns this builder for chaining.</summary>
        public StringBuilderHandle Append(string s)
        {
            _sb.Append(s);
            return this;
        }

        public int Length => _sb.Length;

        public StringBuilderHandle Clear()
        {
            _sb.Clear();
            return this;
        }

        /// <summary>Materializes the accumulated text into an immutable string.</summary>
        public string Build() => _sb.ToString();

        public override string ToString() => _sb.ToString();
    }
}
