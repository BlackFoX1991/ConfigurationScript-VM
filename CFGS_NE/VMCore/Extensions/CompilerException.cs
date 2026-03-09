namespace CFGS_VM.VMCore.Extensions
{
    /// <summary>
    /// Defines the <see cref="CompilerException" />
    /// </summary>
    public sealed class CompilerException : Exception
    {
        /// <summary>
        /// Gets the Category
        /// </summary>
        public string Category { get; } = "CompileError";

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
        public string FileSource { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompilerException"/> class.
        /// </summary>
        /// <param name="message">The message<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="column">The column<see cref="int"/></param>
        /// <param name="fileSource">The fileSource<see cref="string"/></param>
        public CompilerException(string message, int line, int column, string fileSource)
            : base(message)
        {
            RawMessage = message;
            Line = line;
            Column = column;
            FileSource = fileSource;
        }
    }
}
