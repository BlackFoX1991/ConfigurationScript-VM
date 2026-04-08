using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        /// <summary>
        /// Parses the import header into syntax-only import statements.
        /// </summary>
        /// <param name="stmts">The target statement list.</param>
        private void ParseImportHeader(List<Stmt> stmts)
        {
            if (_current.Type != TokenType.Import)
                return;

            while (_current.Type == TokenType.Import)
            {
                Eat(TokenType.Import);

                if (_current.Type == TokenType.String)
                {
                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;

                    string path = _current.Value?.ToString() ?? string.Empty;
                    Eat(TokenType.String);
                    stmts.Add(new BareImportSyntaxStmt(path, line, col, file));
                }
                else if (_current.Type == TokenType.Star)
                {
                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;

                    Eat(TokenType.Star);
                    Eat(TokenType.As);

                    Token aliasToken = Expect(TokenType.Ident, "expected identifier after 'as' in import statement");
                    string alias = aliasToken.Value?.ToString() ?? string.Empty;
                    if (Lexer.Keywords.ContainsKey(alias))
                        throw new ParserException($"invalid symbol declaration name '{alias}'", aliasToken.Line, aliasToken.Column, aliasToken.Filename);

                    Eat(TokenType.From);
                    string path = Expect(TokenType.String, "expected string after 'from' in import statement").Value?.ToString() ?? string.Empty;
                    stmts.Add(new NamespaceImportSyntaxStmt(alias, path, line, col, file));
                }
                else if (_current.Type == TokenType.LBrace)
                {
                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;

                    List<ImportBindingSpec> imports = new();
                    HashSet<string> seenLocals = new(StringComparer.Ordinal);
                    Eat(TokenType.LBrace);
                    while (_current.Type != TokenType.RBrace)
                    {
                        string importName = Expect(TokenType.Ident, "expected identifier in named import list").Value?.ToString() ?? string.Empty;

                        string localName = importName;
                        if (_current.Type == TokenType.As)
                        {
                            Eat(TokenType.As);
                            localName = Expect(TokenType.Ident, "expected identifier after 'as' in named import").Value?.ToString() ?? string.Empty;
                        }

                        if (Lexer.Keywords.ContainsKey(localName))
                            throw new ParserException($"invalid symbol declaration name '{localName}'", _current.Line, _current.Column, _current.Filename);

                        if (!seenLocals.Add(localName))
                            throw new ParserException($"duplicate import target '{localName}'", _current.Line, _current.Column, _current.Filename);

                        imports.Add(new ImportBindingSpec(importName, localName));

                        if (_current.Type == TokenType.Comma)
                        {
                            Eat(TokenType.Comma);
                            if (_current.Type == TokenType.RBrace)
                                break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    Eat(TokenType.RBrace);
                    Eat(TokenType.From);
                    string path = Expect(TokenType.String, "expected string after 'from' in import statement").Value?.ToString() ?? string.Empty;
                    stmts.Add(new NamedImportSyntaxStmt(imports, path, line, col, file));
                }
                else if (_current.Type == TokenType.Ident)
                {
                    string importName = _current.Value?.ToString() ?? string.Empty;
                    Eat(TokenType.Ident);
                    Eat(TokenType.From);

                    if (_current.Type != TokenType.String)
                    {
                        throw new ParserException(
                            "expected string after 'from' in import statement",
                            _current.Line,
                            _current.Column,
                            _current.Filename);
                    }

                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;
                    string path = _current.Value?.ToString() ?? string.Empty;
                    Eat(TokenType.String);
                    stmts.Add(new DefaultImportSyntaxStmt(importName, path, line, col, file));
                }
                else
                {
                    throw new ParserException("invalid import statement", _current.Line, _current.Column, _current.Filename);
                }

                Eat(TokenType.Semi);
            }
        }
    }
}
