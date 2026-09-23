using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Lowering
{
    /// <summary>
    /// Lowers namespace declarations. A namespace exists only at compile time, so a declaration becomes a
    /// block that is marked as a namespace scope and carries the qualified path. The block opens no runtime
    /// scope and builds no namespace object: the compiler declares every member in its own global binding
    /// (see <c>NamespaceRuntimeBindingNames</c>), and the resolver has already bound every reference to a
    /// member, qualified or not, to that binding.
    /// </summary>
    internal sealed class NamespaceLowerer
    {
        public List<Stmt> Lower(List<Stmt> statements)
        {
            List<Stmt> lowered = new(statements.Count);
            foreach (Stmt stmt in statements)
                lowered.Add(LowerTopLevelStatement(stmt));

            return lowered;
        }

        private static Stmt LowerTopLevelStatement(Stmt stmt)
        {
            return stmt switch
            {
                NamespaceDeclStmt ns => new BlockStmt(new List<Stmt>(ns.BodyStatements), ns.Line, ns.Col, ns.OriginFile)
                {
                    IsNamespaceScope = true,
                    NamespacePath = string.Join(".", ns.Parts)
                },
                ImportAliasDeclStmt aliasDecl => new VarDecl(
                    aliasDecl.LocalName,
                    new VarExpr(aliasDecl.ImportName, aliasDecl.Line, aliasDecl.Col, aliasDecl.OriginFile),
                    aliasDecl.Line,
                    aliasDecl.Col,
                    aliasDecl.OriginFile),
                _ => stmt
            };
        }
    }
}
