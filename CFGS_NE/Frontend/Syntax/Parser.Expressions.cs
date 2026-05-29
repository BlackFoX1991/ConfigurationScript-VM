using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using System.Text;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        /// <summary>
        /// The Expr
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Expr()
        {
            if (EnterExpressionRecursion() > MaxRecursionDepth)
                throw new ParserException($"Maximum nesting depth ({MaxRecursionDepth}) exceeded", _current.Line, _current.Column, _lexer.FileName);
            try { return Coalesce(); }
            finally { ExitExpressionRecursion(); }
        }

        /// <summary>
        /// The Coalesce
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Coalesce()
        {
            Expr node = Conditional();
            while (_current.Type == TokenType.QQNull)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.QQNull);
                Expr rhs = Conditional();
                node = new BinaryExpr(node, TokenType.QQNull, rhs, line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The Conditional
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Conditional()
        {
            Expr condition = Or();

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

        /// <summary>
        /// The Or
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
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

        /// <summary>
        /// The And
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
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

        /// <summary>
        /// The Comparison
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr Comparison()
        {
            Expr node = BitwiseOr();
            while (_current.Type == TokenType.Eq || _current.Type == TokenType.Neq ||
                   _current.Type == TokenType.Lt || _current.Type == TokenType.Gt ||
                   _current.Type == TokenType.Le || _current.Type == TokenType.Ge ||
                   _current.Type == TokenType.Is)
            {
                TokenType op = _current.Type;
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(op);
                node = new BinaryExpr(node, op, BitwiseOr(), line, col, file);
            }
            return node;
        }

        /// <summary>
        /// The BitwiseOr
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
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

        /// <summary>
        /// The BitwiseXor
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
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

        /// <summary>
        /// The BitwiseAnd
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr BitwiseAnd()
        {
            Expr node = Shift();
            while (_current.Type == TokenType.bAnd)
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

        /// <summary>
        /// The Shift
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
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

        /// <summary>
        /// The Additive
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
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

        /// <summary>
        /// The Power
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
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

        /// <summary>
        /// The Term
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
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

        /// <summary>
        /// The Factor
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
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
                string s = _current.Value.ToString() ?? "";
                Eat(TokenType.String);
                node = new StringExpr(s, line, col, file);
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

                        if (_current.Type == TokenType.Comma)
                            Eat(TokenType.Comma);
                        else
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
            else if (_current.Type == TokenType.Out)
            {
                int line = _current.Line;
                int col = _current.Column;
                string file = _current.Filename;
                Eat(TokenType.Out);
                EnterOutBlockContext();
                BlockStmt body = ParseBlock();
                ExitOutBlockContext();
                node = new OutExpr(body, line, col, file);
            }
            else
            {
                throw new ParserException($"invalid factor, token {_current.Type}", _current.Line, _current.Column, _current.Filename);
            }

            while (true)
            {
                if (_current.Type == TokenType.LParen)
                {
                    if (node is NullExpr)
                        throw new ParserException("invalid access on null reference", _current.Line, _current.Column, _current.Filename);

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
                        throw new ParserException("invalid access on null reference", _current.Line, _current.Column, _current.Filename);
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
                        throw new ParserException("invalid access on null reference", _current.Line, _current.Column, _current.Filename);
                    Eat(TokenType.Dot);
                    if (_current.Type != TokenType.Ident)
                        throw new ParserException("expected identifier after '.'", _current.Line, _current.Column, _current.Filename);
                    int line = _current.Line;
                    int col = _current.Column;
                    string file = _current.Filename;
                    string member = _current.Value?.ToString() ?? "";
                    node = new IndexExpr(
                        node,
                        new StringExpr(member, line, col, file),
                        line, col, file,
                        isDotAccess: true
                    );
                    Eat(TokenType.Ident);
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
                        throw new ParserException("invalid access on null reference", _current.Line, _current.Column, _current.Filename);
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

        /// <summary>
        /// The Unary
        /// </summary>
        /// <returns>The <see cref="Expr"/></returns>
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
        /// The ParseFuncDecl
        /// </summary>
        /// <returns>The <see cref="FuncDeclStmt"/></returns>
        private FuncDeclStmt ParseFuncDecl()
        {
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
            Eat(TokenType.Ident);

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

            return new FuncDeclStmt(name, parameters, body, minArgs, restParameter, line, col, file, isAsync, parameterSpecs);
        }

        /// <summary>
        /// The ParseReturnStmt
        /// </summary>
        /// <returns>The <see cref="ReturnStmt"/></returns>
        private ReturnStmt ParseReturnStmt()
        {
            if (!IsInFunction)
                throw new ParserException("return can only be used in function statements", _current.Line, _current.Column, _current.Filename);

            if (IsInOutBlock)
                throw new ParserException("return can not be used in out-Block", _current.Line, _current.Column, _current.Filename);

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

        /// <summary>
        /// The ParseYieldStmt
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
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

        /// <summary>
        /// The ParseExprList
        /// </summary>
        /// <param name="allowNamedArgs">The allowNamedArgs<see cref="bool"/></param>
        /// <returns>The <see cref="List{Expr}"/></returns>
        private List<Expr> ParseExprList(bool allowNamedArgs = false)
        {
            List<Expr> list = new();
            list.Add(ParseCallArgument(allowNamedArgs));
            while (_current.Type == TokenType.Comma)
            {
                Eat(TokenType.Comma);
                list.Add(ParseCallArgument(allowNamedArgs));
            }
            return list;
        }

        /// <summary>
        /// The ParseCallArgument
        /// </summary>
        /// <param name="allowNamedArgs">The allowNamedArgs<see cref="bool"/></param>
        /// <returns>The <see cref="Expr"/></returns>
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
