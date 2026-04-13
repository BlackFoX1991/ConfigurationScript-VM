using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using System.Text;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        /// <summary>
        /// The ParseNamespacePath
        /// </summary>
        /// <returns>The <see cref="List{string}"/></returns>
        private List<string> ParseNamespacePath()
        {
            List<string> parts = new();

            while (true)
            {
                Token partToken = Expect(
                    TokenType.Ident,
                    parts.Count == 0 ? "expected namespace name" : "expected identifier in namespace name");

                string part = partToken.Value?.ToString() ?? string.Empty;
                if (Lexer.Keywords.ContainsKey(part))
                    throw new ParserException($"invalid symbol declaration name '{part}'", partToken.Line, partToken.Column, partToken.Filename);

                parts.Add(part);
                if (!Match(TokenType.Dot))
                    break;
            }

            return parts;
        }

        /// <summary>
        /// The ParseQualifiedTypeName
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        private string ParseQualifiedTypeName()
        {
            StringBuilder sb = new();
            while (true)
            {
                Token partToken = Expect(
                    TokenType.Ident,
                    sb.Length == 0 ? "expected type name" : "expected identifier in type name");

                string part = partToken.Value?.ToString() ?? string.Empty;
                if (Lexer.Keywords.ContainsKey(part))
                    throw new ParserException($"invalid symbol declaration name '{part}'", partToken.Line, partToken.Column, partToken.Filename);

                if (sb.Length > 0)
                    sb.Append('.');
                sb.Append(part);

                if (!Match(TokenType.Dot))
                    break;
            }

            return sb.ToString();
        }

        /// <summary>
        /// The ParseNamespaceBodyStatement
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseNamespaceBodyStatement()
        {
            if (_current.Type == TokenType.Export)
                throw new ParserException(
                    "export is not allowed inside namespace body",
                    _current.Line, _current.Column, _current.Filename);

            if (_current.Type == TokenType.Import)
                throw new ParserException(
                    "imports are not allowed inside namespace body",
                    _current.Line, _current.Column, _current.Filename);

            if (_current.Type == TokenType.Namespace)
                throw new ParserException(
                    "nested namespace declarations are not supported. Use a qualified namespace name (for example: namespace A.B { ... }).",
                    _current.Line, _current.Column, _current.Filename);

            if (TryParseModuleScopeStatement(out Stmt stmt))
                return stmt;

            throw new ParserException(
                $"invalid namespace statement {_current.Type}",
                _current.Line, _current.Column, _current.Filename);
        }

        /// <summary>
        /// The ParseNamespaceDeclStatements
        /// </summary>
        /// <returns>The <see cref="NamespaceDeclStmt"/></returns>
        private NamespaceDeclStmt ParseNamespaceDeclStatement()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Namespace);

            List<string> parts = ParseNamespacePath();
            string root = parts[0];

            if (_knownTopLevelKinds.TryGetValue(root, out string? rootKind) && !_knownNamespaceRoots.Contains(root))
            {
                string label = TopLevelSymbolFacts.NamespaceRootConflictLabel(rootKind);
                throw new ParserException(
                    $"namespace root '{root}' conflicts with existing {label} '{root}'",
                    line, col, file);
            }

            Eat(TokenType.LBrace);

            List<Stmt> bodyStmts = new();
            while (_current.Type != TokenType.RBrace)
            {
                if (_current.Type == TokenType.EOF)
                    throw new ParserException("expected '}' before end of file in namespace body", _current.Line, _current.Column, _current.Filename);

                Stmt bodyStmt = ParseNamespaceBodyStatement();
                bodyStmts.Add(bodyStmt);
            }

            Eat(TokenType.RBrace);

            ValidateTopLevelSymbolUniqueness(bodyStmts);
            _knownNamespaceRoots.Add(root);
            return new NamespaceDeclStmt(parts, bodyStmts, line, col, file);
        }
    }
}
