namespace CFGS_VM.VMCore.Extensions
{
    /// <summary>
    /// Defines the <see cref="CompilerException" />
    /// </summary>
    public sealed class CompilerException(string message, int line, int column, string fileSource) : Exception($"{message}. ( Line : {line}, Column : {column} ) : [Source : '{fileSource}']");

}
