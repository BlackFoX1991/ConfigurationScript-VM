using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        /// <summary>
        /// The ParseMatch
        /// </summary>
        /// <returns>The <see cref="MatchStmt"/></returns>
        private MatchStmt ParseMatch()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.Match);
            Eat(TokenType.LParen);
            Expr expr = Expr();
            Eat(TokenType.RParen);
            Eat(TokenType.LBrace);

            List<CaseClause> cases = new();
            BlockStmt? defaultCase = null;

            while (_current.Type == TokenType.Case || _current.Type == TokenType.Default)
            {
                if (_current.Type == TokenType.Case)
                {
                    Eat(TokenType.Case);
                    MatchPattern pattern = ParseMatchPattern();
                    ValidateUniquePatternBindings(pattern);
                    Expr? guard = ParseOptionalMatchGuard();
                    Eat(TokenType.Colon);
                    BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
                    cases.Add(new CaseClause(pattern, guard, body, pattern.Line, pattern.Col, pattern.OriginFile));
                }
                else if (_current.Type == TokenType.Default)
                {
                    if (defaultCase is not null)
                        throw new ParserException("multiple default case not allowed", _current.Line, _current.Column, _current.Filename);
                    Eat(TokenType.Default);
                    Eat(TokenType.Colon);
                    defaultCase = ParseEmbeddedBlockOrSingleStatement();
                }
            }

            Eat(TokenType.RBrace);
            return new MatchStmt(expr, cases, defaultCase, line, col, file);
        }

        /// <summary>
        /// The ParseMatchPattern
        /// </summary>
        /// <returns>The <see cref="MatchPattern"/></returns>
        private MatchPattern ParseMatchPattern()
        {
            if (_current.Type == TokenType.Ident && (_current.Value?.ToString() ?? "") == "_")
            {
                Token tok = _current;
                Eat(TokenType.Ident);
                return new WildcardMatchPattern(tok.Line, tok.Column, tok.Filename);
            }

            if (_current.Type == TokenType.Var)
                return ParseMatchBindingPattern();

            if (_current.Type == TokenType.LBracket)
                return ParseArrayMatchPattern();

            if (_current.Type == TokenType.LBrace)
                return ParseDictMatchPattern();

            Expr value = Expr();
            return new ValueMatchPattern(value, value.Line, value.Col, value.OriginFile);
        }

        /// <summary>
        /// The ParseMatchBindingPattern
        /// </summary>
        /// <returns>The <see cref="BindingMatchPattern"/></returns>
        private BindingMatchPattern ParseMatchBindingPattern()
        {
            Eat(TokenType.Var);
            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected identifier after 'var' in match pattern", _current.Line, _current.Column, _current.Filename);

            Token identTok = _current;
            string name = identTok.Value?.ToString() ?? "";
            if (name == "_")
            {
                throw new ParserException(
                    "cannot bind '_' in match pattern; use '_' directly as wildcard",
                    identTok.Line,
                    identTok.Column,
                    identTok.Filename);
            }

            if (name == "this" || name == "type" || name == "super" || name == "outer")
                throw new ParserException($"reserved name '{name}' cannot be used as match binding", identTok.Line, identTok.Column, identTok.Filename);

            Eat(TokenType.Ident);
            return new BindingMatchPattern(name, identTok.Line, identTok.Column, identTok.Filename);
        }

        /// <summary>
        /// The ParseArrayMatchPattern
        /// </summary>
        /// <returns>The <see cref="ArrayMatchPattern"/></returns>
        private ArrayMatchPattern ParseArrayMatchPattern()
        {
            Token open = _current;
            Eat(TokenType.LBracket);

            List<MatchPattern> elements = new();
            if (_current.Type != TokenType.RBracket)
            {
                while (true)
                {
                    elements.Add(ParseMatchPattern());
                    if (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBracket)
                            break;
                        continue;
                    }

                    break;
                }
            }

            Eat(TokenType.RBracket);
            return new ArrayMatchPattern(elements, open.Line, open.Column, open.Filename);
        }

        /// <summary>
        /// The ParseDictMatchPattern
        /// </summary>
        /// <returns>The <see cref="DictMatchPattern"/></returns>
        private DictMatchPattern ParseDictMatchPattern()
        {
            Token open = _current;
            Eat(TokenType.LBrace);

            List<(string Key, MatchPattern Pattern)> entries = new();
            HashSet<string> seenKeys = new(StringComparer.Ordinal);

            if (_current.Type != TokenType.RBrace)
            {
                while (true)
                {
                    Token keyTok = _current;
                    string key = _current.Type switch
                    {
                        TokenType.Ident => _current.Value?.ToString() ?? "",
                        TokenType.String => _current.Value?.ToString() ?? "",
                        _ => throw new ParserException("expected identifier or string key in dictionary match pattern", _current.Line, _current.Column, _current.Filename),
                    };

                    if (_current.Type == TokenType.Ident)
                        Eat(TokenType.Ident);
                    else
                        Eat(TokenType.String);

                    if (!seenKeys.Add(key))
                        throw new ParserException($"duplicate key '{key}' in dictionary match pattern", keyTok.Line, keyTok.Column, keyTok.Filename);

                    Eat(TokenType.Colon);
                    MatchPattern pat = ParseMatchPattern();
                    entries.Add((key, pat));

                    if (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBrace)
                            break;
                        continue;
                    }

                    break;
                }
            }

            Eat(TokenType.RBrace);
            return new DictMatchPattern(entries, open.Line, open.Column, open.Filename);
        }

        /// <summary>
        /// The ValidateUniquePatternBindings
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        private static void ValidateUniquePatternBindings(MatchPattern pattern)
        {
            HashSet<string> names = new(StringComparer.Ordinal);

            static void Walk(MatchPattern p, HashSet<string> seen)
            {
                switch (p)
                {
                    case WildcardMatchPattern:
                    case ValueMatchPattern:
                        return;

                    case BindingMatchPattern b:
                        if (!seen.Add(b.Name))
                            throw new ParserException($"duplicate match binding '{b.Name}'", b.Line, b.Col, b.OriginFile);
                        return;

                    case ArrayMatchPattern a:
                        foreach (MatchPattern elem in a.Elements)
                            Walk(elem, seen);
                        return;

                    case DictMatchPattern d:
                        foreach ((string _, MatchPattern sub) in d.Entries)
                            Walk(sub, seen);
                        return;

                    default:
                        throw new ParserException($"unsupported match pattern node '{p.GetType().Name}'", p.Line, p.Col, p.OriginFile);
                }
            }

            Walk(pattern, names);
        }

        /// <summary>
        /// The ParseOptionalMatchGuard
        /// </summary>
        /// <returns>The <see cref="Expr?"/></returns>
        private Expr? ParseOptionalMatchGuard()
        {
            if (_current.Type != TokenType.If)
                return null;

            Eat(TokenType.If);
            Eat(TokenType.LParen);
            Expr guard = Expr();
            Eat(TokenType.RParen);
            return guard;
        }

        /// <summary>
        /// The ParseDestructurePattern
        /// </summary>
        /// <returns>The <see cref="MatchPattern"/></returns>
        private MatchPattern ParseDestructurePattern()
        {
            if (_current.Type == TokenType.Ident)
            {
                Token tok = _current;
                string name = tok.Value?.ToString()
                    ?? throw new ParserException("invalid destructuring target", tok.Line, tok.Column, tok.Filename);
                Eat(TokenType.Ident);

                if (name == "_")
                    return new WildcardMatchPattern(tok.Line, tok.Column, tok.Filename);

                if (Lexer.Keywords.ContainsKey(name))
                    throw new ParserException($"invalid destructuring binding name '{name}'", tok.Line, tok.Column, tok.Filename);

                return new BindingMatchPattern(name, tok.Line, tok.Column, tok.Filename);
            }

            if (_current.Type == TokenType.LBracket)
                return ParseArrayDestructurePattern();

            if (_current.Type == TokenType.LBrace)
                return ParseDictDestructurePattern();

            throw new ParserException("invalid destructuring target", _current.Line, _current.Column, _current.Filename);
        }

        /// <summary>
        /// The ParseArrayDestructurePattern
        /// </summary>
        /// <returns>The <see cref="ArrayMatchPattern"/></returns>
        private ArrayMatchPattern ParseArrayDestructurePattern()
        {
            Token open = _current;
            Eat(TokenType.LBracket);

            List<MatchPattern> elements = new();
            if (_current.Type != TokenType.RBracket)
            {
                while (true)
                {
                    elements.Add(ParseDestructurePattern());

                    if (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBracket)
                            break;
                        continue;
                    }

                    break;
                }
            }

            Eat(TokenType.RBracket);
            return new ArrayMatchPattern(elements, open.Line, open.Column, open.Filename);
        }

        /// <summary>
        /// The ParseDictDestructurePattern
        /// </summary>
        /// <returns>The <see cref="DictMatchPattern"/></returns>
        private DictMatchPattern ParseDictDestructurePattern()
        {
            Token open = _current;
            Eat(TokenType.LBrace);

            List<(string Key, MatchPattern Pattern)> entries = new();
            HashSet<string> seenKeys = new(StringComparer.Ordinal);

            if (_current.Type != TokenType.RBrace)
            {
                while (true)
                {
                    Token keyTok = _current;
                    string key = _current.Type switch
                    {
                        TokenType.Ident => _current.Value?.ToString()
                            ?? throw new ParserException("invalid key in destructuring pattern", _current.Line, _current.Column, _current.Filename),
                        TokenType.String => _current.Value?.ToString()
                            ?? throw new ParserException("invalid key in destructuring pattern", _current.Line, _current.Column, _current.Filename),
                        _ => throw new ParserException("expected identifier or string key in destructuring pattern", _current.Line, _current.Column, _current.Filename),
                    };
                    Advance();

                    if (!seenKeys.Add(key))
                        throw new ParserException($"duplicate key '{key}' in destructuring pattern", keyTok.Line, keyTok.Column, keyTok.Filename);

                    MatchPattern pat;
                    if (_current.Type == TokenType.Colon)
                    {
                        Eat(TokenType.Colon);
                        pat = ParseDestructurePattern();
                    }
                    else
                    {
                        if (keyTok.Type != TokenType.Ident)
                        {
                            throw new ParserException(
                                "string key in destructuring pattern requires ':'",
                                keyTok.Line,
                                keyTok.Column,
                                keyTok.Filename);
                        }

                        if (key == "_")
                        {
                            pat = new WildcardMatchPattern(keyTok.Line, keyTok.Column, keyTok.Filename);
                        }
                        else
                        {
                            if (Lexer.Keywords.ContainsKey(key))
                                throw new ParserException($"invalid destructuring binding name '{key}'", keyTok.Line, keyTok.Column, keyTok.Filename);
                            pat = new BindingMatchPattern(key, keyTok.Line, keyTok.Column, keyTok.Filename);
                        }
                    }

                    entries.Add((key, pat));

                    if (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBrace)
                            break;
                        continue;
                    }

                    break;
                }
            }

            Eat(TokenType.RBrace);
            return new DictMatchPattern(entries, open.Line, open.Column, open.Filename);
        }

        /// <summary>
        /// The ValidateUniqueDestructureBindings
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        private static void ValidateUniqueDestructureBindings(MatchPattern pattern)
        {
            HashSet<string> names = new(StringComparer.Ordinal);

            static void Walk(MatchPattern p, HashSet<string> seen)
            {
                switch (p)
                {
                    case WildcardMatchPattern:
                        return;

                    case BindingMatchPattern b:
                        if (!seen.Add(b.Name))
                            throw new ParserException($"duplicate destructuring binding '{b.Name}'", b.Line, b.Col, b.OriginFile);
                        return;

                    case ArrayMatchPattern a:
                        foreach (MatchPattern elem in a.Elements)
                            Walk(elem, seen);
                        return;

                    case DictMatchPattern d:
                        foreach ((string _, MatchPattern sub) in d.Entries)
                            Walk(sub, seen);
                        return;

                    default:
                        throw new ParserException($"unsupported destructuring pattern node '{p.GetType().Name}'", p.Line, p.Col, p.OriginFile);
                }
            }

            Walk(pattern, names);
        }

        /// <summary>
        /// The CollectDestructureBindingNames
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        /// <returns>The <see cref="List{string}"/></returns>
        private static List<string> CollectDestructureBindingNames(MatchPattern pattern)
        {
            List<string> names = new();

            static void Walk(MatchPattern p, List<string> acc)
            {
                switch (p)
                {
                    case WildcardMatchPattern:
                        return;

                    case BindingMatchPattern b:
                        acc.Add(b.Name);
                        return;

                    case ArrayMatchPattern a:
                        foreach (MatchPattern elem in a.Elements)
                            Walk(elem, acc);
                        return;

                    case DictMatchPattern d:
                        foreach ((string _, MatchPattern sub) in d.Entries)
                            Walk(sub, acc);
                        return;

                    default:
                        throw new ParserException($"unsupported destructuring pattern node '{p.GetType().Name}'", p.Line, p.Col, p.OriginFile);
                }
            }

            Walk(pattern, names);
            return names;
        }

        /// <summary>
        /// The ParseDestructureAssignStmt
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseDestructureAssignStmt()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            MatchPattern pattern = ParseDestructurePattern();
            ValidateUniqueDestructureBindings(pattern);

            if (_current.Type != TokenType.Assign)
                throw new ParserException("destructuring assignment requires '='", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.Assign);
            Expr value = Expr();
            Eat(TokenType.Semi);
            return new DestructureAssignStmt(pattern, value, line, col, file);
        }

        /// <summary>
        /// The ParseParenDestructureAssignStmt
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseParenDestructureAssignStmt()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.LParen);
            MatchPattern pattern = ParseDestructurePattern();
            ValidateUniqueDestructureBindings(pattern);

            if (_current.Type != TokenType.Assign)
                throw new ParserException("destructuring assignment requires '='", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.Assign);
            Expr value = Expr();
            Eat(TokenType.RParen);
            Eat(TokenType.Semi);
            return new DestructureAssignStmt(pattern, value, line, col, file);
        }
    }
}
