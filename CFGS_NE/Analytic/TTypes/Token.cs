public class Token
{
    /// <summary>
    /// Gets the Type
    /// </summary>
    public TokenType Type { get; }

    /// <summary>
    /// Gets the Value
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// Gets the Filename
    /// </summary>
    public string Filename { get; }

    /// <summary>
    /// Gets the Line
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the Column
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Token"/> class.
    /// </summary>
    /// <param name="type">The type<see cref="TokenType"/></param>
    /// <param name="value">The value<see cref="object"/></param>
    /// <param name="line">The line<see cref="int"/></param>
    /// <param name="col">The col<see cref="int"/></param>
    /// <param name="filename">The filename<see cref="string"/></param>
    public Token(TokenType type, object value, int line, int col, string filename)
    {
        Type = type;
        Value = value;
        Line = line;
        Column = col;
        Filename = filename;
    }

    /// <summary>
    /// The ToString
    /// </summary>
    /// <returns>The <see cref="string"/></returns>
    public override string ToString()
    {
        return $"{Type} {Value}";
    }
}
