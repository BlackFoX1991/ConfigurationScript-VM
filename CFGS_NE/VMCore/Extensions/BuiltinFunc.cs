// BuiltinFunc.cs
namespace CFGS_VM.VMCore.Extension
{
    // A delegate type for your built-in functions
    public delegate object BuiltinFunctionDelegate(object[] args);

    public sealed class BuiltinFunc
    {
        public string Name { get; }
        public BuiltinFunctionDelegate Function { get; }

        public BuiltinFunc(string name, BuiltinFunctionDelegate function)
        {
            Name = name;
            Function = function;
        }
    }
}