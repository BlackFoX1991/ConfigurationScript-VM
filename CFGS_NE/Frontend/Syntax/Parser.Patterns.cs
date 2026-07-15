using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
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

                    // An unguarded `case _:` is the catch-all default, identical to `default:` and to an
                    // unguarded `_` arm in a match expression: it is order-independent and there may be at
                    // most one per match. A guarded `case _ if (...):` stays an ordinary positional arm.
                    if (pattern is WildcardMatchPattern && guard is null)
                    {
                        if (defaultCase is not null)
                            throw new ParserException("multiple default case not allowed", pattern.Line, pattern.Col, pattern.OriginFile);
                        defaultCase = body;
                    }
                    else
                    {
                        cases.Add(new CaseClause(pattern, guard, body, pattern.Line, pattern.Col, pattern.OriginFile));
                    }
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

        private MatchPattern ParseMatchPattern()
        {
            // Bound nested pattern recursion (`[[[ ... ]]]`, `{a: {b: ...}}`) with the same shared depth
            // budget as expressions and blocks, so a pathological pattern fails with a clean parse error
            // instead of an uncatchable StackOverflowException.
            if (EnterExpressionRecursion() > MaxRecursionDepth)
            {
                // Balance the increment before throwing (the guard fires before the try/finally below, so
                // its finally never runs for this frame). Otherwise the leaked count persists across
                // tolerant-parse error recovery and later flags valid code as over-nested.
                ExitExpressionRecursion();
                throw new ParserException($"Maximum nesting depth ({MaxRecursionDepth}) exceeded", _current.Line, _current.Column, _current.Filename);
            }
            try
            {
                return ParseMatchPatternCore();
            }
            finally
            {
                ExitExpressionRecursion();
            }
        }

        private MatchPattern ParseMatchPatternCore()
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
                        TokenType.String => RequirePlainStringTokenValue(_current, "dictionary match pattern key"),
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
                        TokenType.String => RequirePlainStringTokenValue(_current, "destructuring pattern key"),
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
