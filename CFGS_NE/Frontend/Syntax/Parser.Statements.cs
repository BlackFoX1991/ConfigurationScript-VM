using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using System.Text;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        /// <summary>
        /// The Statement
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt Statement()
        {
            if (_context.IsInMultipleVarDeclaration)
                return ParseVarDecl;

            return IsInFunctionOrClass
                ? ParseStatementInMemberScope()
                : ParseStatementTopLevel();
        }

        /// <summary>
        /// The TryParseCommonStatement
        /// </summary>
        /// <param name="stmt">The stmt<see cref="Stmt"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryParseCommonStatement(out Stmt stmt)
        {
            switch (_current.Type)
            {
                case TokenType.Semi:
                    stmt = ParseEmptyStmt;
                    return true;
                case TokenType.Var:
                    stmt = ParseVarDecl;
                    return true;
                case TokenType.Const:
                    stmt = ParseConstDecl;
                    return true;
                case TokenType.Ident:
                    stmt = ParseAssignOrIndexAssignOrPushOrExpr();
                    return true;
                case TokenType.LParen:
                    if (_next.Type == TokenType.LBracket || _next.Type == TokenType.LBrace)
                    {
                        stmt = ParseParenDestructureAssignStmt();
                        return true;
                    }
                    stmt = default!;
                    return false;
                case TokenType.LBracket:
                    stmt = ParseDestructureAssignStmt();
                    return true;
                case TokenType.Func:
                case TokenType.Async:
                    stmt = ParseFuncDecl();
                    return true;
                case TokenType.LBrace:
                    stmt = ParseBlock();
                    return true;
                case TokenType.Class:
                    stmt = ParseClassDecl();
                    return true;
                case TokenType.Interface:
                    stmt = ParseInterfaceDecl();
                    return true;
                case TokenType.Enum:
                    stmt = ParseEnumDecl();
                    return true;
                default:
                    stmt = default!;
                    return false;
            }
        }

        /// <summary>
        /// Tries to parse a declaration-only statement that is valid in module scope.
        /// </summary>
        private bool TryParseModuleScopeStatement(out Stmt stmt)
        {
            switch (_current.Type)
            {
                case TokenType.Semi:
                    stmt = ParseEmptyStmt;
                    break;
                case TokenType.Var:
                    stmt = ParseVarDecl;
                    break;
                case TokenType.Const:
                    stmt = ParseConstDecl;
                    break;
                case TokenType.Func:
                case TokenType.Async:
                    stmt = ParseFuncDecl();
                    break;
                case TokenType.Class:
                    stmt = ParseClassDecl();
                    break;
                case TokenType.Interface:
                    stmt = ParseInterfaceDecl();
                    break;
                case TokenType.Enum:
                    stmt = ParseEnumDecl();
                    break;
                default:
                    stmt = default!;
                    return false;
            }

            ValidateModuleScopeInitializer(stmt);
            return true;
        }

        private static void ValidateModuleScopeInitializer(Stmt stmt)
        {
            switch (stmt)
            {
                case VarDecl { Value: not null } varDecl:
                    ValidatePureModuleInitializer(varDecl.Value, varDecl.Line, varDecl.Col, varDecl.OriginFile);
                    return;

                case ConstDecl constDecl:
                    ValidatePureModuleInitializer(constDecl.Value, constDecl.Line, constDecl.Col, constDecl.OriginFile);
                    return;

                case DestructureDeclStmt destructureDecl:
                    ValidatePureModuleInitializer(destructureDecl.Value, destructureDecl.Line, destructureDecl.Col, destructureDecl.OriginFile);
                    return;

                case ExportStmt exportStmt:
                    ValidateModuleScopeInitializer(exportStmt.Inner);
                    return;
            }
        }

        private static void ValidatePureModuleInitializer(Expr expr, int line, int col, string file)
        {
            if (IsPureModuleInitializer(expr))
                return;

            throw new ParserException(
                "module-scope declaration initializers cannot execute code; use literals, variable references, and pure operators only",
                line,
                col,
                file);
        }

        private static bool IsPureModuleInitializer(Expr expr)
            => expr switch
            {
                NullExpr or NumberExpr or StringExpr or CharExpr or BoolExpr or VarExpr => true,
                UnaryExpr unaryExpr => IsPureModuleInitializer(unaryExpr.Right),
                BinaryExpr binaryExpr => IsPureModuleInitializer(binaryExpr.Left) && IsPureModuleInitializer(binaryExpr.Right),
                ConditionalExpr conditionalExpr => IsPureModuleInitializer(conditionalExpr.Condition) &&
                                                   IsPureModuleInitializer(conditionalExpr.ThenExpr) &&
                                                   IsPureModuleInitializer(conditionalExpr.ElseExpr),
                ArrayExpr arrayExpr => arrayExpr.Elements.All(IsPureModuleInitializer),
                DictExpr dictExpr => dictExpr.Pairs.All(pair => IsPureModuleInitializer(pair.Item1) && IsPureModuleInitializer(pair.Item2)),
                IndexExpr indexExpr => indexExpr.Target is not null &&
                                       indexExpr.Index is not null &&
                                       IsPureModuleInitializer(indexExpr.Target) &&
                                       IsPureModuleInitializer(indexExpr.Index),
                SliceExpr sliceExpr => sliceExpr.Target is not null &&
                                       IsPureModuleInitializer(sliceExpr.Target) &&
                                       (sliceExpr.Start is null || IsPureModuleInitializer(sliceExpr.Start)) &&
                                       (sliceExpr.End is null || IsPureModuleInitializer(sliceExpr.End)),
                GetFieldExpr getFieldExpr => IsPureModuleInitializer(getFieldExpr.Target),
                MatchExpr matchExpr => IsPureModuleInitializer(matchExpr.Scrutinee) &&
                                       matchExpr.Arms.All(arm =>
                                           (arm.Guard is null || IsPureModuleInitializer(arm.Guard)) &&
                                           IsPureModuleInitializer(arm.Body)) &&
                                       (matchExpr.DefaultArm is null || IsPureModuleInitializer(matchExpr.DefaultArm)),
                TryUnwrapExpr tryUnwrapExpr => tryUnwrapExpr.Inner is null || IsPureModuleInitializer(tryUnwrapExpr.Inner),
                _ => false
            };

        /// <summary>
        /// The ParseStatementInMemberScope
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseStatementInMemberScope()
        {
            switch (_current.Type)
            {
                case TokenType.ForEach: return ParseForeach();
                case TokenType.Try: return ParseTry();
                case TokenType.Using: return ParseUsing();
                case TokenType.Defer: return ParseDefer();
                case TokenType.Throw: return ParseThrow();
                case TokenType.Return: return ParseReturnStmt();
                case TokenType.Yield: return ParseYieldStmt();
                case TokenType.Delete: return ParseDelete;
                case TokenType.Match: return ParseMatch();
                case TokenType.If: return ParseIf();
                case TokenType.While: return ParseWhile();
                case TokenType.Do: return ParseDoWhile();
                case TokenType.Break: return ParseBreak;
                case TokenType.Continue: return ParseContinue;
                case TokenType.For: return ParseFor();
            }

            if (TryParseCommonStatement(out Stmt stmt))
                return stmt;

            return ParseExprStmt;
        }

        /// <summary>
        /// The ParseStatementTopLevel
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseStatementTopLevel()
        {
            if (_current.Type == TokenType.Export)
            {
                Stmt exportStmt = ParseExportDecl();
                if (_topLevelMode == TopLevelMode.Module)
                    ValidateModuleScopeInitializer(exportStmt);
                return exportStmt;
            }

            if (_topLevelMode == TopLevelMode.Module)
            {
                if (TryParseModuleScopeStatement(out Stmt moduleStmt))
                    return moduleStmt;

                throw new ParserException(
                    $"invalid top-level statement {_current.Type}",
                    _current.Line, _current.Column, _current.Filename);
            }

            if (TryParseCommonStatement(out Stmt stmt))
                return stmt;

            throw new ParserException(
                $"invalid top-level statement {_current.Type}",
                _current.Line, _current.Column, _current.Filename);
        }

        /// <summary>
        /// Gets the ParseEmptyStmt
        /// </summary>
        private Stmt ParseEmptyStmt
        {
            get
            {
                int line = _current.Line;
                int col = _current.Column;
                string fs = _current.Filename;

                Eat(TokenType.Semi);
                return new EmptyStmt(TokenType.Semi, line, col, fs);
            }
        }

        /// <summary>
        /// Gets the ParseExprStmt
        /// </summary>
        private Stmt ParseExprStmt
        {
            get
            {
                int line = _current.Line;
                int col = _current.Column;

                Expr e = Expr();
                Eat(TokenType.Semi);
                return new ExprStmt(e, line, col, _current.Filename);
            }
        }

        /// <summary>
        /// Gets the ParseNew
        /// </summary>
        private Expr ParseNew
        {
            get
            {
                int line = _current.Line;
                int col = _current.Column;
                string origin = _current.Filename;

                Eat(TokenType.New);

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected class name after 'new'", line, col, origin);

                StringBuilder qn = new();
                qn.Append(_current.Value.ToString());
                Eat(TokenType.Ident);

                while (_current.Type == TokenType.Dot)
                {
                    Eat(TokenType.Dot);
                    if (_current.Type != TokenType.Ident)
                        throw new ParserException("expected identifier after '.' in qualified class name",
                                                  _current.Line, _current.Column, origin);
                    qn.Append('.');
                    qn.Append(_current.Value.ToString());
                    Eat(TokenType.Ident);
                }

                List<Expr> args = new();

                if (_current.Type == TokenType.LParen)
                {
                    Eat(TokenType.LParen);
                    if (_current.Type != TokenType.RParen)
                        args = ParseExprList(allowNamedArgs: true);
                    Eat(TokenType.RParen);
                }

                List<(string, Expr)> inits = new();
                if (_current.Type == TokenType.LBrace)
                    inits = ParseInitializerItems();

                return new NewExpr(qn.ToString(), args, inits, line, col, origin);
            }
        }

        /// <summary>
        /// The ParseInitializerItems
        /// </summary>
        /// <returns>The <see cref="List{(string, Expr)}"/></returns>
        private List<(string, Expr)> ParseInitializerItems()
        {
            List<(string, Expr)> items = new();

            Eat(TokenType.LBrace);

            if (_current.Type != TokenType.RBrace)
            {
                while (true)
                {
                    if (_current.Type != TokenType.Ident)
                        throw new ParserException("expected identifier as initializer key",
                                                  _current.Line, _current.Column, _current.Filename);

                    string name = _current.Value.ToString()!;
                    Eat(TokenType.Ident);

                    if (_current.Type == TokenType.Colon || _current.Type == TokenType.Assign)
                        Advance();
                    else
                        throw new ParserException("expected ':' or '=' in object initializer",
                                                  _current.Line, _current.Column, _current.Filename);

                    Expr val = Expr();
                    items.Add((name!, val!));

                    if (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        if (_current.Type == TokenType.RBrace) break;
                        continue;
                    }
                    break;
                }
            }

            Eat(TokenType.RBrace);
            return items;
        }

        /// <summary>
        /// The MaybeParseObjectInitializer
        /// </summary>
        /// <param name="target">The target<see cref="Expr"/></param>
        /// <returns>The <see cref="Expr"/></returns>
        private Expr MaybeParseObjectInitializer(Expr target)
        {
            if (target is NewExpr) return target;

            if (_current.Type == TokenType.LBrace)
            {
                List<(string, Expr)> inits = ParseInitializerItems();
                return new ObjectInitExpr(target, inits, target.Line, target.Col, target.OriginFile);
            }
            return target;
        }

        /// <summary>
        /// The ParseAssignOrIndexAssignOrPushOrExpr
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseAssignOrIndexAssignOrPushOrExpr(bool requireSemicolon = true)
        {
            int line = _current.Line;
            int col = _current.Column;
            string fsname = _current.Filename;

            string name = _current.Value.ToString() ?? "";
            Eat(TokenType.Ident);

            Expr target = new VarExpr(name, line, col, fsname);

            while (true)
            {
                if (_current.Type == TokenType.Dot)
                {
                    Eat(TokenType.Dot);

                    if (_current.Type != TokenType.Ident)
                        throw new ParserException("expected identifier after '.'", line, col, _current.Filename);

                    string fieldName = _current.Value.ToString() ?? "";
                    Eat(TokenType.Ident);

                    target = new IndexExpr(target, new StringExpr(fieldName, line, col, fsname), line, col, fsname, isDotAccess: true);
                }

                else if (_current.Type == TokenType.LBracket)
                {
                    Eat(TokenType.LBracket);

                    if (_current.Type == TokenType.RBracket)
                    {
                        Eat(TokenType.RBracket);
                        Eat(TokenType.Assign);
                        Expr val = Expr();
                        EatStatementTerminator(requireSemicolon);
                        return new PushStmt(target, val, line, col, fsname);
                    }

                    Expr? idxStart = Expr();
                    Expr? idxEnd = null;

                    if (_current.Type == TokenType.Range)
                    {
                        Eat(TokenType.Range);

                        if (_current.Type != TokenType.RBracket)
                            idxEnd = Expr();

                        target = new SliceExpr(target, idxStart, idxEnd, line, col, fsname);
                    }
                    else
                    {
                        target = new IndexExpr(target, idxStart, line, col, fsname);
                    }

                    Eat(TokenType.RBracket);

                    if (_current.Type == TokenType.Assign)
                    {
                        Eat(TokenType.Assign);
                        Expr val = Expr();
                        EatStatementTerminator(requireSemicolon);

                        if (target is SliceExpr slice)
                            return new SliceSetStmt(slice, val, line, col, fsname);
                        else
                            return new AssignExprStmt(target, val, line, col, fsname);
                    }
                }

                else if (_current.Type == TokenType.LParen)
                {
                    Eat(TokenType.LParen);
                    List<Expr> args = new();
                    if (_current.Type != TokenType.RParen)
                        args.AddRange(ParseExprList(allowNamedArgs: true));
                    Eat(TokenType.RParen);

                    target = new CallExpr(target, args, line, col, fsname);
                }
                else
                {
                    break;
                }
            }

            if (_current.Type == TokenType.Assign ||
                _current.Type == TokenType.PlusAssign ||
                _current.Type == TokenType.MinusAssign ||
                _current.Type == TokenType.StarAssign ||
                _current.Type == TokenType.SlashAssign ||
                _current.Type == TokenType.ModAssign)
            {
                TokenType op = _current.Type;
                Eat(op);
                Expr val = Expr();
                EatStatementTerminator(requireSemicolon);

                if (op == TokenType.Assign)
                {
                    if (target is SliceExpr slice)
                        return new SliceSetStmt(slice, val, line, col, fsname);
                    else
                        return new AssignExprStmt(target, val, line, col, fsname);
                }
                else
                {
                    return new CompoundAssignStmt(target, op, val, line, col, fsname);
                }
            }

            if (_current.Type == TokenType.PlusPlus || _current.Type == TokenType.MinusMinus)
            {
                TokenType op = _current.Type;
                Eat(op);
                EatStatementTerminator(requireSemicolon);
                return new ExprStmt(new PostfixExpr(target, op, line, col, fsname), line, col, fsname);
            }

            EatStatementTerminator(requireSemicolon);
            return new ExprStmt(target, line, col, fsname);
        }

        private void EatStatementTerminator(bool requireSemicolon)
        {
            if (requireSemicolon || _current.Type == TokenType.Semi)
                Eat(TokenType.Semi);
        }

        /// <summary>
        /// Gets the ParseDelete
        /// </summary>
        private Stmt ParseDelete
        {
            get
            {
                int line = _current.Line;
                int col = _current.Column;
                string fsname = _current.Filename;

                Eat(TokenType.Delete);

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected identifier after delete", line, col, _current.Filename);

                Expr target = new VarExpr(_current.Value?.ToString() ?? "", line, col, fsname);
                Eat(TokenType.Ident);

                while (true)
                {
                    if (_current.Type == TokenType.Dot)
                    {
                        Eat(TokenType.Dot);
                        if (_current.Type != TokenType.Ident)
                            throw new ParserException("expected identifier after '.'", line, col, _current.Filename);

                        string fieldName = _current.Value?.ToString() ?? "";
                        Eat(TokenType.Ident);

                        target = new IndexExpr(
                            target,
                            new StringExpr(fieldName, line, col, fsname),
                            line, col, fsname,
                            isDotAccess: true
                        );
                        continue;
                    }

                    if (_current.Type == TokenType.LBracket)
                    {
                        Eat(TokenType.LBracket);

                        if (_current.Type == TokenType.RBracket)
                        {
                            Eat(TokenType.RBracket);
                            Eat(TokenType.Semi);
                            return new DeleteExprStmt(target, true, line, col, fsname);
                        }

                        Expr? start = null;
                        Expr? end = null;

                        if (_current.Type != TokenType.Range)
                            start = Expr();

                        if (_current.Type == TokenType.Range)
                        {
                            Eat(TokenType.Range);
                            if (_current.Type != TokenType.RBracket)
                                end = Expr();

                            Eat(TokenType.RBracket);
                            target = new SliceExpr(target, start, end, line, col, fsname);
                        }
                        else
                        {
                            if (start is null)
                                throw new ParserException("expected index expression before ']'", line, col, fsname);

                            Eat(TokenType.RBracket);
                            target = new IndexExpr(target, start, line, col, fsname);
                        }

                        continue;
                    }

                    break;
                }

                Eat(TokenType.Semi);

                bool deleteAll = target is VarExpr;

                return new DeleteExprStmt(target, deleteAll, line, col, fsname);
            }
        }

    }
}
