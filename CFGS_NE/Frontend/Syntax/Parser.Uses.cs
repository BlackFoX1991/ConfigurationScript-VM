using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        /// <summary>
        /// Parses header-only use directives.
        /// </summary>
        /// <param name="stmts">The target statement list.</param>
        private void ParseUseHeader(List<Stmt> stmts)
        {
            while (_current.Type == TokenType.Use)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;

                Eat(TokenType.Use);
                List<string> parts = ParseNamespacePath();
                Eat(TokenType.Semi);
                stmts.Add(new UseNamespaceStmt(parts, line, col, file));
            }
        }
    }
}
