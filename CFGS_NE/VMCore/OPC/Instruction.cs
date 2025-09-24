public class Instruction
{
    /// <summary>
    /// Gets the Code
    /// </summary>
    public OpCode Code { get; }

    /// <summary>
    /// Gets the Operand
    /// </summary>
    public object? Operand { get; }

    /// <summary>
    /// Gets the Line
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the Col
    /// </summary>
    public int Col { get; }

    /// <summary>
    /// Gets the OriginFile
    /// </summary>
    public string OriginFile { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Instruction"/> class.
    /// </summary>
    /// <param name="code">The code<see cref="OpCode"/></param>
    /// <param name="operand">The operand<see cref="object?"/></param>
    /// <param name="line">The line<see cref="int"/></param>
    /// <param name="col">The col<see cref="int"/></param>
    /// <param name="originFile">The originFile<see cref="string"/></param>
    public Instruction(OpCode code, object? operand = null, int line = -1, int col = -1, string originFile = null)
    {
        Code = code;
        Operand = operand;
        Line = line;
        Col = col;
        OriginFile = originFile;
    }

    /// <summary>
    /// The ToString
    /// </summary>
    /// <returns>The <see cref="string"/></returns>
    public override string ToString()
    {
        return Operand != null
            ? $"{Code} {Operand}"
            : $"{Code}";
    }
}
