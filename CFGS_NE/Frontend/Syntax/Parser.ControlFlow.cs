using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        /// <summary>
        /// The ParseUsing
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseUsing()
        {
            int line = _current.Line, col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Using);

            if (_current.Type == TokenType.Var || _current.Type == TokenType.Const)
                throw new ParserException("short using declarations are only allowed inside '{...}' blocks", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.LParen);

            string? bindingName = null;
            bool bindingIsConst = false;

            if (_current.Type == TokenType.Var || _current.Type == TokenType.Const)
            {
                bindingIsConst = _current.Type == TokenType.Const;
                Advance();

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected identifier in using binding", _current.Line, _current.Column, _current.Filename);

                bindingName = _current.Value?.ToString();
                if (bindingName is not null && Lexer.Keywords.ContainsKey(bindingName))
                    throw new ParserException($"invalid symbol declaration name '{bindingName}'", _current.Line, _current.Column, _current.Filename);

                Advance();
                Eat(TokenType.Assign);
            }

            Expr resource = Expr();
            Eat(TokenType.RParen);

            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
            return new UsingStmt(bindingName, bindingIsConst, resource, body, line, col, file);
        }

        /// <summary>
        /// The ParseDefer
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseDefer()
            => throw new ParserException("defer is only allowed inside '{...}' blocks", _current.Line, _current.Column, _current.Filename);

        /// <summary>
        /// Gets a value indicating whether the current token starts a short using declaration inside a block.
        /// </summary>
        private bool IsUsingScopeDeclarationStart
            => _current.Type == TokenType.Using && (_next.Type == TokenType.Var || _next.Type == TokenType.Const);

        /// <summary>
        /// Gets a value indicating whether the current token starts a block scoped defer declaration.
        /// </summary>
        private bool IsDeferScopeDeclarationStart
            => _current.Type == TokenType.Defer;

        /// <summary>
        /// The ParseUsingScopeDeclarationInBlock
        /// </summary>
        /// <returns>The <see cref="UsingStmt"/></returns>
        private UsingStmt ParseUsingScopeDeclarationInBlock()
        {
            int line = _current.Line, col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Using);

            bool bindingIsConst = _current.Type == TokenType.Const;
            if (_current.Type != TokenType.Var && _current.Type != TokenType.Const)
                throw new ParserException("expected 'var' or 'const' after 'using'", _current.Line, _current.Column, _current.Filename);

            Advance();

            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected identifier in using binding", _current.Line, _current.Column, _current.Filename);

            string bindingName = _current.Value?.ToString() ?? "";
            if (Lexer.Keywords.ContainsKey(bindingName))
                throw new ParserException($"invalid symbol declaration name '{bindingName}'", _current.Line, _current.Column, _current.Filename);

            Advance();
            Eat(TokenType.Assign);

            Expr resource = Expr();
            Eat(TokenType.Semi);

            int bodyLine = _current.Line;
            int bodyCol = _current.Column;
            string bodyFile = _current.Filename;

            List<Stmt> remaining = ParseBlockStatementsUntilRBrace();
            BlockStmt body = new(remaining, bodyLine, bodyCol, bodyFile);

            return new UsingStmt(bindingName, bindingIsConst, resource, body, line, col, file);
        }

        /// <summary>
        /// The ParseDeferScopeDeclarationInBlock
        /// </summary>
        /// <returns>The <see cref="TryStmt"/></returns>
        private TryStmt ParseDeferScopeDeclarationInBlock()
        {
            int line = _current.Line, col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Defer);

            BlockStmt finallyBlock = ParseEmbeddedBlockOrSingleStatement();

            int bodyLine = _current.Line;
            int bodyCol = _current.Column;
            string bodyFile = _current.Filename;

            List<Stmt> remaining = ParseBlockStatementsUntilRBrace();
            BlockStmt body = new(remaining, bodyLine, bodyCol, bodyFile);

            return new TryStmt(body, null, null, finallyBlock, line, col, file);
        }

        /// <summary>
        /// The ParseBlockStatementsUntilRBrace
        /// </summary>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        private List<Stmt> ParseBlockStatementsUntilRBrace()
        {
            List<Stmt> stmts = new();

            while (_current.Type != TokenType.RBrace)
            {
                if (_current.Type == TokenType.EOF)
                    throw new ParserException("expected '}' before end of file", _current.Line, _current.Column, _current.Filename);

                if (IsUsingScopeDeclarationStart)
                {
                    stmts.Add(ParseUsingScopeDeclarationInBlock());
                    break;
                }

                if (IsDeferScopeDeclarationStart)
                {
                    stmts.Add(ParseDeferScopeDeclarationInBlock());
                    break;
                }

                stmts.Add(Statement());
            }

            return stmts;
        }

        /// <summary>
        /// The ParseThrow
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseThrow()
        {
            int line = _current.Line, col = _current.Column;
            Eat(TokenType.Throw);
            Expr ex = Expr();
            Eat(TokenType.Semi);
            return new ThrowStmt(ex, line, col, _current.Filename);
        }

        /// <summary>
        /// The ParseTry
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseTry()
        {
            int line = _current.Line, col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Try);
            BlockStmt tryBlock = ParseEmbeddedBlockOrSingleStatement();

            string? catchIdent = null;
            BlockStmt? catchBlock = null;
            if (_current.Type == TokenType.Catch)
            {
                Eat(TokenType.Catch);
                Eat(TokenType.LParen);

                if (_current.Type == TokenType.Ident)
                {
                    catchIdent = _current.Value!.ToString();

                    if (catchIdent is not null && Lexer.Keywords.ContainsKey(catchIdent))
                        throw new ParserException($"invalid symbol declaration name '{catchIdent}'", _current.Line, _current.Column, _current.Filename);

                    Advance();
                }
                else if (_current.Type != TokenType.RParen)
                {
                    throw new ParserException("expected identifier or ')'", _current.Line, _current.Column, _current.Filename);
                }

                Eat(TokenType.RParen);
                catchBlock = ParseEmbeddedBlockOrSingleStatement();
            }

            BlockStmt? finallyBlock = null;
            if (_current.Type == TokenType.Finally)
            {
                Eat(TokenType.Finally);
                finallyBlock = ParseEmbeddedBlockOrSingleStatement();
            }

            if (catchBlock == null && finallyBlock == null)
                throw new ParserException("try must have at least catch or finally", line, col, file);

            return new TryStmt(tryBlock, catchIdent, catchBlock, finallyBlock, line, col, file);
        }

        /// <summary>
        /// Gets the ParseBreak
        /// </summary>
        private Stmt ParseBreak
        {
            get
            {
                if (!IsInLoop)
                    throw new ParserException("break can only be used in loops", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Break);
                Eat(TokenType.Semi);
                return new BreakStmt(_current.Line, _current.Column, _current.Filename);
            }
        }

        /// <summary>
        /// Gets the ParseContinue
        /// </summary>
        private Stmt ParseContinue
        {
            get
            {
                if (!IsInLoop)
                    throw new ParserException("continue can only be used in loops", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Continue);
                Eat(TokenType.Semi);
                return new ContinueStmt(_current.Line, _current.Column, _current.Filename);
            }
        }

        /// <summary>
        /// The ParseEmbeddedBlockOrSingleStatement
        /// </summary>
        /// <returns>The <see cref="BlockStmt"/></returns>
        private BlockStmt ParseEmbeddedBlockOrSingleStatement()
        {
            if (_current.Type == TokenType.LBrace)
            {
                return ParseBlock();
            }

            Stmt one = Statement();
            return new BlockStmt(new List<Stmt> { one }, one.Line, one.Col, one.OriginFile);
        }

        /// <summary>
        /// The ParseBlock
        /// </summary>
        /// <returns>The <see cref="BlockStmt"/></returns>
        private BlockStmt ParseBlock()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.LBrace);
            List<Stmt> stmts = ParseBlockStatementsUntilRBrace();

            Eat(TokenType.RBrace);

            return new BlockStmt(stmts, line, col, file);
        }

        /// <summary>
        /// The ParseIf
        /// </summary>
        /// <returns>The <see cref="IfStmt"/></returns>
        private IfStmt ParseIf()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.If);
            Eat(TokenType.LParen);
            Expr cond = Expr();
            Eat(TokenType.RParen);
            BlockStmt thenBlk = ParseEmbeddedBlockOrSingleStatement();

            Stmt? elseBlk = null;
            if (_current.Type == TokenType.Else)
            {
                Eat(TokenType.Else);

                if (_current.Type == TokenType.If)
                {
                    elseBlk = ParseIf();
                }
                else
                {
                    elseBlk = ParseEmbeddedBlockOrSingleStatement();
                }
            }

            return new IfStmt(cond, thenBlk, elseBlk, line, col, file);
        }

        /// <summary>
        /// The ParseWhile
        /// </summary>
        /// <returns>The <see cref="WhileStmt"/></returns>
        private WhileStmt ParseWhile()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.While);
            Eat(TokenType.LParen);
            Expr cond = Expr();
            Eat(TokenType.RParen);
            EnterLoopContext();
            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
            ExitLoopContext();
            return new WhileStmt(cond, body, line, col, file);
        }

        /// <summary>
        /// The ParseDoWhile
        /// </summary>
        /// <returns>The <see cref="DoWhileStmt"/></returns>
        private DoWhileStmt ParseDoWhile()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.Do);
            EnterLoopContext();
            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
            ExitLoopContext();
            Eat(TokenType.While);
            Eat(TokenType.LParen);
            Expr cond = Expr();
            Eat(TokenType.RParen);
            if (_current.Type == TokenType.Semi)
                Eat(TokenType.Semi);
            return new DoWhileStmt(body, cond, line, col, file);
        }

        /// <summary>
        /// The ParseForeach
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseForeach()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.ForEach);
            Eat(TokenType.LParen);

            bool declare = false;
            string name;
            MatchPattern? targetPattern = null;
            bool useIndexValuePair = false;

            if (_current.Type == TokenType.Var)
            {
                declare = true;
                Eat(TokenType.Var);
            }

            if (_current.Type == TokenType.Ident && _next.Type == TokenType.Comma)
            {
                Token firstTok = _current;
                string firstName = _current.Value?.ToString() ?? "";
                Eat(TokenType.Ident);
                Eat(TokenType.Comma);

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected identifier after ',' in foreach", _current.Line, _current.Column, _current.Filename);

                Token secondTok = _current;
                string secondName = _current.Value?.ToString() ?? "";
                Eat(TokenType.Ident);

                targetPattern = new ArrayMatchPattern(
                    new List<MatchPattern>
                    {
                        new BindingMatchPattern(firstName, firstTok.Line, firstTok.Column, firstTok.Filename),
                        new BindingMatchPattern(secondName, secondTok.Line, secondTok.Column, secondTok.Filename)
                    },
                    firstTok.Line,
                    firstTok.Column,
                    firstTok.Filename);
                ValidateUniqueDestructureBindings(targetPattern);

                useIndexValuePair = true;
                name = _context.AllocateForeachPairTempName();
            }
            else if (_current.Type == TokenType.Ident)
            {
                name = _current.Value?.ToString() ?? "";
                Eat(TokenType.Ident);
            }
            else if (_current.Type == TokenType.LBracket || _current.Type == TokenType.LBrace)
            {
                targetPattern = ParseDestructurePattern();
                ValidateUniqueDestructureBindings(targetPattern);
                name = _context.AllocateForeachDestructureTempName();
            }
            else
            {
                throw new ParserException("expected identifier or destructuring pattern after 'foreach('", line, col, _current.Filename);
            }

            if (_current.Type != TokenType.In)
                throw new ParserException("expected 'in' in foreach", _current.Line, _current.Column, _current.Filename);
            Eat(TokenType.In);

            Expr iterable = Expr();

            Eat(TokenType.RParen);
            EnterLoopContext();
            Stmt body = ParseEmbeddedBlockOrSingleStatement();
            ExitLoopContext();
            return new ForeachStmt(name, targetPattern, declare, iterable, useIndexValuePair, body, line, col, file);
        }

        /// <summary>
        /// The ParseFor
        /// </summary>
        /// <returns>The <see cref="ForStmt"/></returns>
        private ForStmt ParseFor()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;
            Eat(TokenType.For);
            Eat(TokenType.LParen);

            Stmt? init = null;
            if (_current.Type != TokenType.Semi)
            {
                if (_current.Type == TokenType.Var)
                    init = ParseVarDecl;
                else if (_current.Type == TokenType.Const)
                    init = ParseConstDecl;
                else
                    init = ParseAssignOrIndexAssignOrPushOrExpr();
            }
            else
            {
                Eat(TokenType.Semi);
            }

            Expr? cond = null;
            if (_current.Type != TokenType.Semi)
                cond = Expr();
            Eat(TokenType.Semi);

            Stmt? inc = null;
            if (_current.Type != TokenType.RParen)
            {
                switch (_current.Type)
                {
                    case TokenType.Var:
                    case TokenType.Const:
                    case TokenType.Delete:
                    case TokenType.Func:
                    case TokenType.Async:
                    case TokenType.Class:
                    case TokenType.Enum:
                    case TokenType.Return:
                    case TokenType.Break:
                    case TokenType.Continue:
                    case TokenType.Try:
                    case TokenType.Throw:
                    case TokenType.If:
                    case TokenType.While:
                    case TokenType.For:
                    case TokenType.ForEach:
                    case TokenType.Match:
                    case TokenType.Yield:
                        throw new ParserException("invalid expression in for statement", _current.Line, _current.Column, _current.Filename);
                }

                inc = ParseForIncrement();
            }

            Eat(TokenType.RParen);
            EnterLoopContext();
            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
            ExitLoopContext();
            return new ForStmt(init, cond, inc, body, line, col, file);
        }

        private Stmt ParseForIncrement()
        {
            switch (_current.Type)
            {
                case TokenType.Ident:
                    return ParseAssignOrIndexAssignOrPushOrExpr(requireSemicolon: false);

                case TokenType.PlusPlus:
                case TokenType.MinusMinus:
                    {
                        int line = _current.Line;
                        int col = _current.Column;
                        string file = _current.Filename;
                        Expr expr = Expr();
                        if (_current.Type == TokenType.Semi)
                            Eat(TokenType.Semi);
                        return new ExprStmt(expr, line, col, file);
                    }

                default:
                    throw new ParserException("invalid expression in for statement", _current.Line, _current.Column, _current.Filename);
            }
        }
    }
}
