/// <summary>
/// Defines the <see cref="LexerException" />
/// </summary>
public sealed class LexerException : Exception
{
    /// <summary>
    /// Gets the Category
    /// </summary>
    public string Category { get; } = "LexError";

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
    /// Gets the Filename
    /// </summary>
    public string Filename { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LexerException"/> class.
    /// </summary>
    /// <param name="message">The message<see cref="string"/></param>
    /// <param name="line">The line<see cref="int"/></param>
    /// <param name="column">The column<see cref="int"/></param>
    /// <param name="filename">The filename<see cref="string"/></param>
    public LexerException(string message, int line, int column, string filename)
        : base(message)
    {
        RawMessage = message;
        Line = line;
        Column = column;
        Filename = filename;
    }
}
