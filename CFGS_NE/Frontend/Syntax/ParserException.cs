namespace CFGS_VM.Analytic.Ex
{
    /// <summary>
    /// Defines the <see cref="ParserException" />
    /// </summary>
    public sealed class ParserException : Exception
    {
        /// <summary>
        /// Gets the Category
        /// </summary>
        public string Category { get; } = "ParseError";

        /// <summary>
        /// Gets the RawMessage
        /// </summary>
        public string RawMessage { get; }

        /// <summary>
        /// Gets the stable diagnostic code when the parser supplied one.
        /// </summary>
        public string? DiagnosticCode { get; }

        /// <summary>
        /// Gets the Line
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// Gets the Column
        /// </summary>
        public int Column { get; }

        /// <summary>
        /// Gets the Filename
        /// </summary>
        public string Filename { get; }

        /// <summary>
        /// Gets the source span length when known.
        /// </summary>
        public int? SpanLength { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ParserException"/> class.
        /// </summary>
        /// <param name="message">The message<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="column">The column<see cref="int"/></param>
        /// <param name="filename">The filename<see cref="string"/></param>
        public ParserException(string message, int line, int column, string filename, int? spanLength = null, string? diagnosticCode = null)
            : base(message)
        {
            RawMessage = message;
            DiagnosticCode = string.IsNullOrWhiteSpace(diagnosticCode) ? null : diagnosticCode.Trim();
            Line = line;
            Column = column;
            Filename = filename;
            SpanLength = spanLength is > 0 ? spanLength : null;
        }
    }
}
