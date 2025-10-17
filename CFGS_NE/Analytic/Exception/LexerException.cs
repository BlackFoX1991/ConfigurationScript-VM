
/// <summary>
/// Defines the <see cref="LexerException" />
/// </summary>
public sealed class LexerException(string message, int line, int column, string filename) : Exception($"{message}. ( Line : {line}, Column : {column} ) : [Source : '{filename}']");
