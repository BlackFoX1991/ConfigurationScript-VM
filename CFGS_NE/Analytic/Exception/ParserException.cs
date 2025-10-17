namespace CFGS_VM.Analytic.Ex
{
    /// <summary>
    /// Defines the <see cref="ParserException" />
    /// </summary>
    public sealed class ParserException(string message, int line, int column, string filename) : Exception($"{message}. ( Line : {line}, Column : {column} ) : [Source : '{filename}']");
}
