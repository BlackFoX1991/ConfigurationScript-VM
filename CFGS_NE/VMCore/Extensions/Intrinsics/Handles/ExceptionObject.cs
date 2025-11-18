namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    public sealed class ExceptionObject
    {
        /// <summary>
        /// Gets the Type
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets the Message
        /// </summary>
        public string eMessage { get; }

        /// <summary>
        /// Gets the File
        /// </summary>
        public string File { get; }

        /// <summary>
        /// Gets the Line
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// Gets the Col
        /// </summary>
        public int Col { get; }

        /// <summary>
        /// Gets the Stack
        /// </summary>
        public string Stack { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExceptionObject"/> class.
        /// </summary>
        /// <param name="type">The type<see cref="string"/></param>
        /// <param name="message">The message<see cref="string"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="stack">The stack<see cref="string"/></param>
        public ExceptionObject(string type, string message, string file, int line, int col, string stack = "")
        {
            Type = type;
            eMessage = message;
            File = file;
            Line = line;
            Col = col;
            Stack = stack;
        }

        /// <summary>
        /// The ToString
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        public override string ToString()
        {
            return $"{eMessage}\n{Type}\n -> {File} at {Line}, position {Col}.";
        }
    }
}

