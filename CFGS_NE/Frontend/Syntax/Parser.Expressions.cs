using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using System.Linq;
using System.Text;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        private static string RequirePlainStringTokenValue(Token token, string context)
        {
            if (token.Value is string s)
                return s;

            throw new ParserException(
                $"{context} does not allow interpolated string literals",
                token.Line,
                token.Column,
                token.Filename,
                token.Length);
        }

        private static Expr ParseInterpolatedExpressionFragment(InterpolatedStringExpressionTokenPart part, string file)
        {
            Parser parser = new(new Lexer(file, part.ExpressionText, part.Line, part.Column));
            Expr expr = parser.Expr();
            if (parser._current.Type != TokenType.EOF)
            {
                throw new ParserException(
                    "unexpected token in interpolated string expression",
                    parser._current.Line,
                    parser._current.Column,
                    parser._current.Filename,
                    parser._current.Length);
            }

            return expr;
        }

        private static Expr BuildInterpolatedStringExpr(InterpolatedStringTokenValue tokenValue, int line, int col, string file)
        {
            bool hasExpression = tokenValue.Parts.Any(static part => part is InterpolatedStringExpressionTokenPart);
            if (!hasExpression)
            {
                StringBuilder text = new();
                foreach (InterpolatedStringTokenPart part in tokenValue.Parts)
                {
                    if (part is InterpolatedStringTextTokenPart textPart)
                        text.Append(textPart.Text);
                }

                return new StringExpr(text.ToString(), line, col, file);
            }

            List<InterpolatedStringPart> parts = new();
            foreach (InterpolatedStringTokenPart part in tokenValue.Parts)
            {
                switch (part)
                {
                    case InterpolatedStringTextTokenPart textPart:
                        parts.Add(new InterpolatedStringTextPart(textPart.Text));
                        break;
                    case InterpolatedStringExpressionTokenPart exprPart:
                        parts.Add(new InterpolatedStringExprPart(
                            ParseInterpolatedExpressionFragment(exprPart, file),
                            exprPart.Alignment,
                            exprPart.Format));
                        break;
                }
            }

            return new InterpolatedStringExpr(parts, line, col, file);
        }

        private Expr Expr()
        {
            int startTokenIndex = MarkNodeStart();
            if (EnterExpressionRecursion() > MaxRecursionDepth)
            {
                // Balance the increment before throwing: the guard fires BEFORE the try/finally below, so
                // its finally never runs for this frame. Leaving the increment leaks one recursion count
                // that persists across statement-level error recovery in tolerant (LSP) parsing and would
                // eventually flag valid, shallow code as "maximum nesting depth exceeded".
                ExitExpressionRecursion();
                throw new ParserException($"Maximum nesting depth ({MaxRecursionDepth}) exceeded", _current.Line, _current.Column, _lexer.FileName);
            }
            try { return StampNode(Conditional(), startTokenIndex); }
            finally { ExitExpressionRecursion(); }
        }

        private Expr Coalesce()
        {
            Expr node = Or();
            while (_current.Type == TokenType.QQNull)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.QQNull);
                Expr rhs = Or();
                node = new BinaryExpr(node, TokenType.QQNull, rhs, line, col, file);
            }
            return node;
        }

        private Expr Conditional()
        {
            Expr condition = Coalesce();

            if (_current.Type == TokenType.Question)
            {
                int line = _current.Line;
                int col = _current.Column;
                string fs = _current.Filename;

                Eat(TokenType.Question);

                Expr thenExpr = Conditional();

                Eat(TokenType.Colon);

                Expr elseExpr = Conditional();

                return new ConditionalExpr(condition, thenExpr, elseExpr, line, col, fs);
            }

            return condition;
        }

        private Expr Or()
        {
            Expr node = And();
            while (_current.Type == TokenType.OrOr)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.OrOr);
                node = new BinaryExpr(node, TokenType.OrOr, And(), line, col, file);
            }
            return node;
        }

        private Expr And()
        {
            Expr node = Comparison();
            while (_current.Type == TokenType.AndAnd)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.AndAnd);
                node = new BinaryExpr(node, TokenType.AndAnd, Comparison(), line, col, file);
            }
            return node;
        }

        private Expr Comparison()
        {
            Expr node = BitwiseOr();
            if (IsComparisonOperator(_current.Type))
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, BitwiseOr(), line, col, file);

                if (IsComparisonOperator(_current.Type))
                    throw new ParserException(
                        "comparison operators cannot be chained; add parentheses to disambiguate (e.g. '(a < b) < c')",
                        _current.Line, _current.Column, _current.Filename);
            }
            return node;
        }

        /// <summary>
        /// Gets a value indicating whether the token is a (non-chainable) comparison operator.
        /// </summary>
        private static bool IsComparisonOperator(TokenType type)
            => type is TokenType.Eq or TokenType.Neq or TokenType.Lt or TokenType.Gt
                    or TokenType.Le or TokenType.Ge or TokenType.Is;

        private Expr BitwiseOr()
        {
            Expr node = BitwiseXor();
            while (_current.Type == TokenType.bOr)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, BitwiseXor(), line, col, file);
            }
            return node;
        }

        private Expr BitwiseXor()
        {
            Expr node = BitwiseAnd();
            while (_current.Type == TokenType.bXor)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, BitwiseAnd(), line, col, file);
            }
            return node;
        }

        private Expr BitwiseAnd()
        {
            Expr node = Merge();
            while (_current.Type == TokenType.bAnd)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Merge(), line, col, file);
            }
            return node;
        }

        // Dictionary merge operators: '<<<' (left wins on key conflicts) and '>>>' (right wins).
        // The arrows point at the winning side. Left-associative, like the surrounding binary levels.
        private Expr Merge()
        {
            Expr node = Shift();
            while (_current.Type == TokenType.MergeLeft || _current.Type == TokenType.MergeRight)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Shift(), line, col, file);
            }
            return node;
        }

        private Expr Shift()
        {
            Expr node = Additive();
            while (_current.Type == TokenType.bShiftL || _current.Type == TokenType.bShiftR)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Additive(), line, col, file);
            }
            return node;
        }

        private Expr Additive()
        {
            Expr node = Term();
            while (_current.Type == TokenType.Plus || _current.Type == TokenType.Minus)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Term(), line, col, file);
            }
            return node;
        }

        private Expr Power()
        {
            Expr? node = Unary();

            if (_current.Type == TokenType.Expo)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Power(), line, col, file);
            }

            return node;
        }

        private Expr Term()
        {
            Expr node = Power();
            while (_current.Type == TokenType.Star || _current.Type == TokenType.Slash || _current.Type == TokenType.Modulo)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, Power(), line, col, file);
            }
            return node;
        }

        private Expr Factor()
        {
            Expr? node;

            if (_current.Type == TokenType.Number)
            {
                object v = _current.Value;
                node = new NumberExpr(v, _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Number);
            }
            else if (_current.Type == TokenType.Null)
            {
                node = new NullExpr(_current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Null);
            }
            else if (_current.Type == TokenType.True)
            {
                node = new BoolExpr(true, _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.True);
            }
            else if (_current.Type == TokenType.False)
            {
                node = new BoolExpr(false, _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.False);
            }
            else if (_current.Type == TokenType.New)
            {
                node = ParseNew;
            }
            else if (_current.Type == TokenType.String)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                object tokenValue = _current.Value;
                Eat(TokenType.String);
                node = tokenValue switch
                {
                    string s => new StringExpr(s, line, col, file),
                    InterpolatedStringTokenValue interpolated => BuildInterpolatedStringExpr(interpolated, line, col, file),
                    _ => throw new ParserException("invalid string token payload", line, col, file)
                };
            }
            else if (_current.Type == TokenType.Char)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                if (!char.TryParse(_current.Value.ToString(), out char vlc))
                    throw new ParserException($"invalid char value '{_current.Value.ToString()}'", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Char);
                node = new CharExpr(vlc, line, col, file);
            }
            else if (_current.Type == TokenType.Ident)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                string name = _current.Value.ToString() ?? "";
                Eat(TokenType.Ident);
                node = new VarExpr(name, line, col, file);
            }
            else if (_current.Type == TokenType.LParen)
            {
                Eat(TokenType.LParen);
                node = Expr();
                Eat(TokenType.RParen);
            }
            else if (_current.Type == TokenType.LBracket)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.LBracket);
                List<Expr> elems = new();
                if (_current.Type != TokenType.RBracket)
                    elems.AddRange(ParseExprList());

                Eat(TokenType.RBracket);
                node = new ArrayExpr(elems, line, col, file);
            }
            else if (_current.Type == TokenType.LBrace)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.LBrace);
                List<(Expr, Expr)> pairs = new();

                if (_current.Type != TokenType.RBrace)
                {
                    do
                    {
                        Expr key = Expr();
                        Eat(TokenType.Colon);
                        Expr value = Expr();
                        pairs.Add((key, value));

                        if (_current.Type != TokenType.Comma)
                            break;

                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBrace)
                            break;
                    } while (true);
                }

                Eat(TokenType.RBrace);
                node = new DictExpr(pairs, line, col, file);
            }
            else if (_current.Type == TokenType.Func || _current.Type == TokenType.Async)
            {
                bool isAsync = false;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                if (_current.Type == TokenType.Async)
                {
                    int asyncLine = _current.Line;
                    int asyncCol = _current.Column;
                    string asyncFile = _current.Filename;
                    Eat(TokenType.Async);
                    if (_current.Type != TokenType.Func)
                        throw new ParserException("expected 'func' after 'async'", asyncLine, asyncCol, asyncFile);
                    isAsync = true;
                }

                Eat(TokenType.Func);

                EnterFunctionContext(isAsync);
                List<string> parameters;
                int minArgs;
                string? restParameter;
                List<FunctionParameterSpec> parameterSpecs;
                BlockStmt body;
                try
                {
                    (parameters, minArgs, restParameter, parameterSpecs) = ParseFunctionParamsWithDefaults();
                    body = ParseBlock();
                }
                finally
                {
                    ExitFunctionContext(isAsync);
                }

                node = new FuncExpr(parameters, body, minArgs, restParameter, line, col, file, isAsync, parameterSpecs);
            }
            else
            {
                throw ErrorAtCurrent($"invalid factor, token {_current.Type}");
            }

            while (true)
            {
                if (_current.Type == TokenType.LParen)
                {
                    if (node is NullExpr)
                        throw ErrorAtCurrent("invalid access on null reference");

                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;
                    Eat(TokenType.LParen);
                    List<Expr> args = new();
                    if (_current.Type != TokenType.RParen)
                        args.AddRange(ParseExprList(allowNamedArgs: true));
                    Eat(TokenType.RParen);

                    node = new CallExpr(node, args, line, col, file);
                    continue;
                }
                else if (_current.Type == TokenType.LBracket)
                {
                    if (node is NullExpr)
                        throw ErrorAtCurrent("invalid access on null reference");
                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;
                    Eat(TokenType.LBracket);

                    Expr? start = null;
                    Expr? end = null;

                    if (_current.Type != TokenType.RBracket)
                    {
                        if (_current.Type != TokenType.Range) start = Expr();

                        if (_current.Type == TokenType.Range)
                        {
                            Eat(TokenType.Range);

                            if (_current.Type != TokenType.RBracket)
                                end = Expr();

                            Eat(TokenType.RBracket);
                            node = new SliceExpr(node, start, end, line, col, file);
                            continue;
                        }
                        else
                        {
                            Eat(TokenType.RBracket);
                            node = new IndexExpr(node, start, line, col, file);
                            continue;
                        }
                    }

                    Eat(TokenType.RBracket);
                    node = new SliceExpr(node, null, null, line, col, file);
                    continue;
                }
                else if (_current.Type == TokenType.Dot)
                {
                    if (node is NullExpr)
                        throw ErrorAtCurrent("invalid access on null reference");
                    Eat(TokenType.Dot);
                    if (!IsMemberNameToken(_current.Type))
                        throw ErrorAtCurrent("expected identifier after '.'");
                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;
                    TokenType memberToken = _current.Type;
                    string member = MemberNameFromToken(_current);
                    node = new IndexExpr(
                        node,
                        new StringExpr(member, line, col, file),
                        line, col, file,
                        isDotAccess: true
                    );
                    Eat(memberToken);
                    continue;
                }
                else if (_current.Type == TokenType.Match)
                {
                    int line = _current.Line, col = _current.Column;
                    string fs = _current.Filename;

                    Eat(TokenType.Match);
                    Eat(TokenType.LBrace);

                    List<CaseExprArm> arms = new();
                    Expr? defaultArm = null;

                    if (_current.Type != TokenType.RBrace)
                    {
                        while (true)
                        {
                            MatchPattern pat = ParseMatchPattern();
                            ValidateUniquePatternBindings(pat);
                            Expr? guard = ParseOptionalMatchGuard();
                            Eat(TokenType.Colon);
                            Expr body = Expr();

                            if (pat is WildcardMatchPattern && guard is null)
                            {
                                if (defaultArm != null)
                                    throw new ParserException("duplicate '_' default arm in match expression", _current.Line, _current.Column, _current.Filename);
                                defaultArm = body;
                            }
                            else
                            {
                                arms.Add(new CaseExprArm(pat, guard, body, pat.Line, pat.Col, pat.OriginFile));
                            }

                            if (_current.Type == TokenType.Comma)
                            {
                                Eat(TokenType.Comma);
                                if (_current.Type == TokenType.RBrace) break;
                                continue;
                            }
                            else break;
                        }
                    }

                    Eat(TokenType.RBrace);

                    node = new MatchExpr(node!, arms, defaultArm, line, col, fs);
                    continue;
                }
                else if (_current.Type == TokenType.PlusPlus || _current.Type == TokenType.MinusMinus)
                {
                    if (node is NullExpr)
                        throw ErrorAtCurrent("invalid access on null reference");
                    TokenType op = _current.Type;
                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;
                    Eat(op);
                    node = new PostfixExpr(node, op, line, col, file);
                    continue;
                }
                else
                {
                    break;
                }
            }

            node = MaybeParseObjectInitializer(node);

            return node ?? new NullExpr(_current.Line, _current.Column, _current.Filename);
        }

        private Expr Unary()
        {
            if (_current.Type == TokenType.Await)
            {
                int awaitLine = _current.Line;
                int awaitCol = _current.Column;
                string awaitFile = _current.Filename;

                if ((IsInFunction && !IsInAsyncFunction) ||
                    (!IsInFunction && _topLevelMode == TopLevelMode.Module))
                    throw new ParserException("await can only be used in async function statements", awaitLine, awaitCol, awaitFile);

                Eat(TokenType.Await);
                Expr awaited = Unary();
                return new AwaitExpr(awaited, awaitLine, awaitCol, awaitFile);
            }

            if (_current.Type == TokenType.Minus)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.Minus);
                return new UnaryExpr(TokenType.Minus, Power(), line, col, file);
            }
            if (_current.Type == TokenType.Plus)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.Plus);
                return new UnaryExpr(TokenType.Plus, Unary(), line, col, file);
            }
            if (_current.Type == TokenType.Not)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.Not);
                return new UnaryExpr(TokenType.Not, Unary(), line, col, file);
            }

            if (_current.Type == TokenType.PlusPlus || _current.Type == TokenType.MinusMinus)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);

                Expr? target = Unary();
                return new PrefixExpr(target, op, line, col, file);
            }

            return Factor();
        }

        /// <summary>
        /// Rejects declaration names that the lexer reserves, the same check var, const, class and enum
        /// declarations already apply. Only the receiver names (this, type, super, outer) can reach here
        /// as identifier tokens; a function declared with one of them used to pass -check and then fail at
        /// runtime or at the first call site.
        /// </summary>
        private static void ThrowIfReservedDeclarationName(string name, Token token)
        {
            if (Lexer.Keywords.ContainsKey(name))
                throw new ParserException($"invalid symbol declaration name '{name}'", token.Line, token.Column, token.Filename, token.Length);
        }

        private FuncDeclStmt ParseFuncDecl()
        {
            int startTokenIndex = MarkNodeStart();
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            bool isAsync = false;

            if (_current.Type == TokenType.Async)
            {
                Eat(TokenType.Async);
                if (_current.Type != TokenType.Func)
                    throw new ParserException("expected 'func' after 'async'", _current.Line, _current.Column, _current.Filename);
                isAsync = true;
            }

            Eat(TokenType.Func);

            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected function name", line, col, _current.Filename);

            string name = _current.Value.ToString() ?? "";
            ThrowIfReservedDeclarationName(name, _current);
            Eat(TokenType.Ident);

            string? explicitInterfaceName = null;
            if (_current.Type == TokenType.Dot)
            {
                explicitInterfaceName = name;
                Eat(TokenType.Dot);
                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected method name after explicit interface qualifier", _current.Line, _current.Column, _current.Filename);

                name = _current.Value.ToString() ?? "";
                ThrowIfReservedDeclarationName(name, _current);
                Eat(TokenType.Ident);
            }

            EnterFunctionContext(isAsync);
            List<string> parameters;
            int minArgs;
            string? restParameter;
            List<FunctionParameterSpec> parameterSpecs;
            BlockStmt body;
            try
            {
                (parameters, minArgs, restParameter, parameterSpecs) = ParseFunctionParamsWithDefaults();
                body = ParseEmbeddedBlockOrSingleStatement();
            }
            finally
            {
                ExitFunctionContext(isAsync);
            }

            return StampNode(new FuncDeclStmt(name, parameters, body, minArgs, restParameter, line, col, file, isAsync, parameterSpecs, explicitInterfaceName), startTokenIndex);
        }

        private ReturnStmt ParseReturnStmt()
        {
            if (!IsInFunction)
                throw new ParserException("return can only be used in function statements", _current.Line, _current.Column, _current.Filename);

            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.Return);
            Expr? value = null;
            if (_current.Type != TokenType.Semi)
                value = Expr();

            Eat(TokenType.Semi);
            return new ReturnStmt(value, line, col, file);
        }

        private Stmt ParseYieldStmt()
        {
            if (!IsInFunction)
                throw new ParserException("yield can only be used in function statements", _current.Line, _current.Column, _current.Filename);

            if (!IsInAsyncFunction)
                throw new ParserException("yield can only be used in async function statements", _current.Line, _current.Column, _current.Filename);

            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Yield);
            if (_current.Type != TokenType.Semi)
                throw new ParserException("yield does not accept a value", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.Semi);
            return new YieldStmt(line, col, file);
        }

        private List<Expr> ParseExprList(bool allowNamedArgs = false)
        {
            List<Expr> list = new();
            list.Add(ParseCallArgument(allowNamedArgs));
            while (_current.Type == TokenType.Comma)
            {
                Eat(TokenType.Comma);

                // Every caller closes the list with ')' or ']', so a comma directly before the closing
                // bracket is a trailing comma, as in match arms, patterns, enums, and imports.
                if (_current.Type is TokenType.RParen or TokenType.RBracket)
                    break;

                list.Add(ParseCallArgument(allowNamedArgs));
            }
            return list;
        }

        private Expr ParseCallArgument(bool allowNamedArgs)
        {
            if (allowNamedArgs && _current.Type == TokenType.Star)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.Star);
                Expr value = Expr();
                return new SpreadArgExpr(value, line, col, file);
            }

            if (allowNamedArgs && _current.Type == TokenType.Ident && _next.Type == TokenType.Colon)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                string name = _current.Value?.ToString() ?? "";
                Eat(TokenType.Ident);
                Eat(TokenType.Colon);
                Expr value = Expr();
                return new NamedArgExpr(name, value, line, col, file);
            }

            return Expr();
        }
    }
}
