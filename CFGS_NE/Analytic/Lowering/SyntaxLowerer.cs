using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Lowering
{
    public sealed class SyntaxLowerer
    {
        private readonly NamespaceLowerer _namespaceLowerer = new();
        private readonly ParamLowerer _paramLowerer = new();

        public List<Stmt> Lower(List<Stmt> statements)
        {
            List<Stmt> namespaceLowered = _namespaceLowerer.Lower(statements);
            return _paramLowerer.Lower(namespaceLowered);
        }
    }
}
