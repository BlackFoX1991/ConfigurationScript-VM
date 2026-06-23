using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        internal static bool TryGetNamespaceScopePath(BlockStmt block, out string namespacePath)
        {
            namespacePath = string.Empty;

            if (block.Statements.Count == 0)
                return false;

            if (block.Statements[0] is not VarDecl nsVar)
                return false;

            if (!nsVar.Name.StartsWith("__ns_scope_", StringComparison.Ordinal))
                return false;

            if (nsVar.Value == null)
                return false;

            return TryExtractQualifiedPath(nsVar.Value, out namespacePath);
        }

        private static bool TryExtractQualifiedPath(Expr expr, out string path)
        {
            switch (expr)
            {
                case VarExpr ve:
                    path = ve.Name;
                    return !string.IsNullOrWhiteSpace(path);

                case IndexExpr idx when idx.Target != null && idx.Index is StringExpr seg:
                    {
                        if (!TryExtractQualifiedPath(idx.Target, out string prefix))
                        {
                            path = string.Empty;
                            return false;
                        }

                        if (string.IsNullOrWhiteSpace(seg.Value))
                        {
                            path = string.Empty;
                            return false;
                        }

                        path = $"{prefix}.{seg.Value}";
                        return true;
                    }

                default:
                    path = string.Empty;
                    return false;
            }
        }
    }
}
