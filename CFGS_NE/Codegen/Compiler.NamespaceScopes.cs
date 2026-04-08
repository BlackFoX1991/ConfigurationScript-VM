using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// The TryGetNamespaceScopePath
        /// </summary>
        /// <param name="block">The block<see cref="BlockStmt"/></param>
        /// <param name="namespacePath">The namespacePath<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
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

        /// <summary>
        /// The TryExtractQualifiedPath
        /// </summary>
        /// <param name="expr">The expr<see cref="Expr"/></param>
        /// <param name="path">The path<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
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
