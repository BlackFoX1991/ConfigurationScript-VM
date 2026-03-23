namespace CFGS_VM.VMCore.Extensions
{
    /// <summary>
    /// Defines the <see cref="VMException" />
    /// </summary>
    public sealed class VMException : Exception
    {
        /// <summary>
        /// Gets the Category
        /// </summary>
        public string Category { get; } = "RuntimeError";

        /// <summary>
        /// Gets the RawMessage
        /// </summary>
        public string RawMessage { get; }

        /// <summary>
        /// Gets the Line
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// Gets the Column
        /// </summary>
        public int Column { get; }

        /// <summary>
        /// Gets the FileSource
        /// </summary>
        public string? FileSource { get; }

        /// <summary>
        /// Gets the LanguageStackTrace
        /// </summary>
        public string? LanguageStackTrace { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="VMException"/> class.
        /// </summary>
        /// <param name="message">The message<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="column">The column<see cref="int"/></param>
        /// <param name="fileSource">The fileSource<see cref="string?"/></param>
        /// <param name="dbg">The dbg<see cref="bool"/></param>
        /// <param name="dbStream">The dbStream<see cref="MemoryStream"/></param>
        /// <param name="languageStackTrace">The languageStackTrace<see cref="string?"/></param>
        public VMException(
            string message,
            int line,
            int column,
            string? fileSource,
            bool dbg,
            MemoryStream dbStream,
            string? languageStackTrace = null)
            : base(NormalizeMessage(message))
        {
            RawMessage = NormalizeMessage(message);
            Line = line;
            Column = column;
            FileSource = fileSource;
            LanguageStackTrace = string.IsNullOrWhiteSpace(languageStackTrace) ? null : languageStackTrace;
        }

        public VMException(
            string message,
            int line,
            int column,
            string? fileSource,
            bool dbg,
            MemoryStream dbStream,
            Exception innerException,
            string? languageStackTrace = null)
            : base(NormalizeMessage(message), innerException)
        {
            RawMessage = NormalizeMessage(message);
            Line = line;
            Column = column;
            FileSource = fileSource;
            LanguageStackTrace = string.IsNullOrWhiteSpace(languageStackTrace) ? null : languageStackTrace;
        }

        /// <summary>
        /// The NormalizeMessage
        /// </summary>
        /// <param name="message">The message<see cref="string"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string NormalizeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "runtime error";
            return message.Trim();
        }
    }
}
