using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using System.Text;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
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

        private Stmt ParseNamespaceBodyStatement()
        {
            int startTokenIndex = MarkNodeStart();
            if (_current.Type == TokenType.Public || _current.Type == TokenType.Private || _current.Type == TokenType.Protected)
            {
                MemberVisibility visibility = ParseMemberVisibility(_current.Type);
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Advance();

                if (_current.Type == TokenType.Public || _current.Type == TokenType.Private || _current.Type == TokenType.Protected)
                {
                    throw new ParserException(
                        "duplicate access modifier in namespace member declaration",
                        _current.Line,
                        _current.Column,
                        _current.Filename);
                }

                if (!TryParseModuleScopeStatement(out Stmt visibleStmt) ||
                    !NamespaceMemberVisibilityFacts.TryApply(visibleStmt, visibility, hasExplicitVisibility: true))
                {
                    throw new ParserException(
                        "namespace visibility modifiers support only var/const/func(async func)/class/interface/enum declarations",
                        line,
                        col,
                        file);
                }

                return StampNode(visibleStmt, startTokenIndex);
            }

            if (_current.Type == TokenType.Export)
                throw new ParserException(
                    "export is not allowed inside namespace body",
                    _current.Line, _current.Column, _current.Filename);

            if (_current.Type == TokenType.Import)
                throw new ParserException(
                    "imports are not allowed inside namespace body",
                    _current.Line, _current.Column, _current.Filename);

            if (_current.Type == TokenType.Use)
                throw new ParserException(
                    "use directives are not allowed inside namespace body",
                    _current.Line, _current.Column, _current.Filename);

            if (_current.Type == TokenType.Namespace)
                throw new ParserException(
                    "nested namespace declarations are not supported. Use a qualified namespace name (for example: namespace A.B { ... }).",
                    _current.Line, _current.Column, _current.Filename);

            if (TryParseModuleScopeStatement(out Stmt stmt))
                return StampNode(stmt, startTokenIndex);

            throw new ParserException(
                $"invalid namespace statement {_current.Type}",
                _current.Line, _current.Column, _current.Filename);
        }

        private NamespaceDeclStmt ParseNamespaceDeclStatement(bool allowFileScoped = true)
        {
            int startTokenIndex = MarkNodeStart();
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

            if (_current.Type == TokenType.Semi)
            {
                if (!allowFileScoped)
                {
                    throw new ParserException(
                        "file-scoped namespace must be the first declaration after the import header",
                        line,
                        col,
                        file);
                }

                Eat(TokenType.Semi);

                List<Stmt> fileScopedBodyStatements = new();
                while (_current.Type != TokenType.EOF)
                {
                    try
                    {
                        Stmt bodyStmt = ParseNamespaceBodyStatement();
                        fileScopedBodyStatements.Add(bodyStmt);
                    }
                    catch (Exception ex) when (IsTolerant && ex is ParserException or LexerException)
                    {
                        AddErrorStatement(fileScopedBodyStatements, ex);
                        if (_current.Type is TokenType.Export or TokenType.Import or TokenType.Use or TokenType.Namespace)
                            Advance();
                        RecoverAfterStatementError();
                    }
                }

                if (IsTolerant)
                {
                    try
                    {
                        ValidateTopLevelSymbolUniqueness(fileScopedBodyStatements);
                    }
                    catch (Exception ex) when (ex is ParserException or LexerException)
                    {
                        RecordDiagnostic(ex);
                    }
                }
                else
                {
                    ValidateTopLevelSymbolUniqueness(fileScopedBodyStatements);
                }
                _knownNamespaceRoots.Add(root);
                return StampNode(new NamespaceDeclStmt(parts, fileScopedBodyStatements, line, col, file, isFileScoped: true), startTokenIndex);
            }

            Eat(TokenType.LBrace);

            List<Stmt> bodyStmts = new();
            while (_current.Type != TokenType.RBrace)
            {
                if (_current.Type == TokenType.EOF)
                {
                    if (!IsTolerant)
                        throw new ParserException("expected '}' before end of file in namespace body", _current.Line, _current.Column, _current.Filename);

                    RecordDiagnostic(new ParserException("expected '}' before end of file in namespace body", _current.Line, _current.Column, _current.Filename));
                    break;
                }

                try
                {
                    Stmt bodyStmt = ParseNamespaceBodyStatement();
                    bodyStmts.Add(bodyStmt);
                }
                catch (Exception ex) when (IsTolerant && ex is ParserException or LexerException)
                {
                    AddErrorStatement(bodyStmts, ex);
                    if (_current.Type is TokenType.Export or TokenType.Import or TokenType.Use or TokenType.Namespace)
                        Advance();
                    RecoverAfterStatementError(TokenType.RBrace);
                }
            }

            if (_current.Type == TokenType.RBrace)
                Eat(TokenType.RBrace);
            else if (!IsTolerant)
                Eat(TokenType.RBrace);

            if (IsTolerant)
            {
                try
                {
                    ValidateTopLevelSymbolUniqueness(bodyStmts);
                }
                catch (Exception ex) when (ex is ParserException or LexerException)
                {
                    RecordDiagnostic(ex);
                }
            }
            else
            {
                ValidateTopLevelSymbolUniqueness(bodyStmts);
            }
            _knownNamespaceRoots.Add(root);
            return StampNode(new NamespaceDeclStmt(parts, bodyStmts, line, col, file), startTokenIndex);
        }
    }
}
