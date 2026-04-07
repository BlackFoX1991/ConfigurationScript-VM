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
            if (multipleVarDecl)
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
                return ParseExportDecl();

            if (TryParseCommonStatement(out Stmt stmt))
                return stmt;

            throw new ParserException(
                $"invalid top-level statement {_current.Type}",
                _current.Line, _current.Column, _current.Filename);
        }

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
        /// The ParseEnumDecl
        /// </summary>
        /// <returns>The <see cref="EnumDeclStmt"/></returns>
        private EnumDeclStmt ParseEnumDecl()
        {
            int declLine = _current.Line;
            int declCol = _current.Column;

            Eat(TokenType.Enum);

            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected enum name", declLine, declCol, _current.Filename);

            string name = _current.Value.ToString() ?? "";

            if (Lexer.Keywords.ContainsKey(name))
                throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.Ident);

            Eat(TokenType.LBrace);

            List<EnumMemberNode> members = new();
            HashSet<string> usedNames = new(StringComparer.Ordinal);
            HashSet<int> usedValues = new();

            int nextAutoValue = 0;

            while (_current.Type != TokenType.RBrace)
            {
                int memberLine = _current.Line;
                int memberCol = _current.Column;

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected identifier in enum body", memberLine, memberCol, _current.Filename);

                string memberName = _current.Value.ToString() ?? "";
                if (Lexer.Keywords.ContainsKey(memberName))
                    throw new ParserException($"invalid symbol declaration name '{memberName}'", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Ident);

                int value;
                if (_current.Type == TokenType.Assign)
                {
                    Eat(TokenType.Assign);
                    if (_current.Type != TokenType.Number || _current.Value is not int iv)
                        throw new ParserException("expected number after '='", _current.Line, _current.Column, _current.Filename);

                    value = Convert.ToInt32(_current.Value);
                    Eat(TokenType.Number);
                }
                else
                {
                    value = nextAutoValue;
                }

                if (!usedNames.Add(memberName))
                    throw new ParserException(
                        $"duplicate enum member name '{memberName}' in enum '{name}'",
                        memberLine, memberCol, _current.Filename
                    );

                if (!usedValues.Add(value))
                    throw new ParserException(
                        $"duplicate enum value '{value}' in enum '{name}'",
                        memberLine, memberCol, _current.Filename
                    );

                members.Add(new EnumMemberNode(memberName, value, memberLine, memberCol, _current.Filename));

                nextAutoValue = value + 1;

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

            return new EnumDeclStmt(name, members, declLine, declCol, _current.Filename);
        }

        /// <summary>
        /// The ParseInterfaceMethodDecl
        /// </summary>
        /// <returns>The <see cref="InterfaceMethodDecl"/></returns>
        private InterfaceMethodDecl ParseInterfaceMethodDecl()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            bool isAsync = false;
            if (_current.Type == TokenType.Async)
            {
                isAsync = true;
                Eat(TokenType.Async);
            }

            Eat(TokenType.Func);

            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected method name", _current.Line, _current.Column, _current.Filename);

            string name = _current.Value?.ToString() ?? "";
            if (Lexer.Keywords.ContainsKey(name))
                throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);
            Eat(TokenType.Ident);

            (List<string> parameters, int minArgs, string? restParameter, _) = ParseFunctionParamsWithDefaults();
            Eat(TokenType.Semi);

            return new InterfaceMethodDecl(name, parameters, minArgs, restParameter, line, col, file, isAsync);
        }

        /// <summary>
        /// The ParseInterfaceDecl
        /// </summary>
        /// <returns>The <see cref="InterfaceDeclStmt"/></returns>
        private InterfaceDeclStmt ParseInterfaceDecl()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Interface);

            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected interface name", line, col, file);

            string name = _current.Value?.ToString() ?? "";
            if (Lexer.Keywords.ContainsKey(name))
                throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);
            Eat(TokenType.Ident);

            List<string> baseInterfaces = new();
            if (_current.Type == TokenType.Colon)
            {
                Eat(TokenType.Colon);

                while (true)
                {
                    string baseName = ParseQualifiedTypeName();
                    if (string.Equals(baseName, name, StringComparison.Ordinal))
                        throw new ParserException("self inheritance not allowed", _current.Line, _current.Column, _current.Filename);

                    baseInterfaces.Add(baseName);

                    if (_current.Type != TokenType.Comma)
                        break;

                    Eat(TokenType.Comma);
                }
            }

            Eat(TokenType.LBrace);

            List<InterfaceMethodDecl> methods = new();
            while (_current.Type != TokenType.RBrace)
            {
                if (_current.Type != TokenType.Func && _current.Type != TokenType.Async)
                {
                    throw new ParserException(
                        "interfaces support only method signatures declared with 'func' or 'async func'",
                        _current.Line, _current.Column, _current.Filename);
                }

                methods.Add(ParseInterfaceMethodDecl());
            }

            Eat(TokenType.RBrace);
            return new InterfaceDeclStmt(name, methods, baseInterfaces, line, col, file);
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
        /// The ParseClassDecl
        /// </summary>
        /// <returns>The <see cref="ClassDeclStmt"/></returns>
        private ClassDeclStmt ParseClassDecl()
        {
            int line = _current.Line;
            int col = _current.Column;

            Eat(TokenType.Class);

            if (_current.Type != TokenType.Ident)
                throw new ParserException($"expected class name", line, col, _current.Filename);

            string name = _current.Value.ToString() ?? "";
            if (Lexer.Keywords.ContainsKey(name))
                throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);
            Eat(TokenType.Ident);

            List<string> ctorParams = ParseParams();

            string? baseName = null;
            List<Expr> baseArgs = new();
            List<string> implementedInterfaces = new();
            if (_current.Type == TokenType.Colon)
            {
                Eat(TokenType.Colon);

                baseName = ParseQualifiedTypeName();

                if (baseName == name)
                    throw new ParserException("self inheritance not allowed", _current.Line, _current.Column, _current.Filename);

                if (_current.Type == TokenType.LParen)
                {
                    Eat(TokenType.LParen);

                    if (_current.Type != TokenType.RParen)
                        baseArgs = ParseExprList(allowNamedArgs: true);

                    Eat(TokenType.RParen);
                }

                while (_current.Type == TokenType.Comma)
                {
                    Eat(TokenType.Comma);
                    implementedInterfaces.Add(ParseQualifiedTypeName());
                }
            }

            EnterFunctionOrClassContext();
            Eat(TokenType.LBrace);

            List<FuncDeclStmt> methods = new();
            Dictionary<string, Expr?> fields = new();

            List<FuncDeclStmt> staticMethods = new();
            Dictionary<string, Expr?> staticFields = new();
            List<EnumDeclStmt> staticEnums = new();
            List<ClassDeclStmt> nestedClasses = new();
            Dictionary<string, MemberVisibility> fieldVisibility = new(StringComparer.Ordinal);
            Dictionary<string, MemberVisibility> staticFieldVisibility = new(StringComparer.Ordinal);
            Dictionary<string, MemberVisibility> methodVisibility = new(StringComparer.Ordinal);
            Dictionary<string, MemberVisibility> staticMethodVisibility = new(StringComparer.Ordinal);
            Dictionary<string, MemberVisibility> enumVisibility = new(StringComparer.Ordinal);
            Dictionary<string, MemberVisibility> nestedClassVisibility = new(StringComparer.Ordinal);
            HashSet<string> constFields = new(StringComparer.Ordinal);
            HashSet<string> staticConstFields = new(StringComparer.Ordinal);

            bool CheckFieldNames(string nm) =>
                (from sm in staticMethods where sm.Name == nm select sm).Any() ||
                (from en in staticEnums where en.Name == nm select en).Any() ||
                (from sf in staticFields where sf.Key == nm select sf).Any() ||
                (from mt in methods where mt.Name == nm select mt).Any() ||
                (from fld in fields where fld.Key == nm select fld).Any() ||
                (from nsc in nestedClasses where nsc.Name == nm select nsc).Any();

            static MemberVisibility ParseVisibility(TokenType type)
            {
                return type switch
                {
                    TokenType.Public => MemberVisibility.Public,
                    TokenType.Private => MemberVisibility.Private,
                    TokenType.Protected => MemberVisibility.Protected,
                    _ => MemberVisibility.Public
                };
            }

            while (_current.Type != TokenType.RBrace)
            {
                bool StaticSet = false;
                bool seenStatic = false;
                bool seenVisibility = false;
                bool ConstSet = false;
                bool seenConst = false;
                MemberVisibility visibility = MemberVisibility.Public;

                while (true)
                {
                    if (_current.Type == TokenType.Static)
                    {
                        if (seenStatic)
                            throw new ParserException("duplicate 'static' modifier in class member declaration", _current.Line, _current.Column, _current.Filename);

                        seenStatic = true;
                        StaticSet = true;
                        Eat(TokenType.Static);
                        continue;
                    }

                    if (_current.Type == TokenType.Public || _current.Type == TokenType.Private || _current.Type == TokenType.Protected)
                    {
                        if (seenVisibility)
                            throw new ParserException("duplicate access modifier in class member declaration", _current.Line, _current.Column, _current.Filename);

                        visibility = ParseVisibility(_current.Type);
                        seenVisibility = true;
                        Advance();
                        continue;
                    }

                    if (_current.Type == TokenType.Const)
                    {
                        if (seenConst)
                            throw new ParserException("duplicate 'const' modifier in class member declaration", _current.Line, _current.Column, _current.Filename);

                        seenConst = true;
                        ConstSet = true;
                        Eat(TokenType.Const);
                        continue;
                    }

                    break;
                }

                if (_current.Type == TokenType.Var || (ConstSet && _current.Type == TokenType.Ident))
                {
                    if (_current.Type == TokenType.Var)
                        Eat(TokenType.Var);

                    string fieldName = _current.Value.ToString() ?? "";
                    if (CheckFieldNames(fieldName))
                        throw new ParserException($"Field '{fieldName}' already declared in class '{name}'", _current.Line, _current.Column, _current.Filename);
                    Eat(TokenType.Ident);

                    Expr? init = null;
                    if (_current.Type == TokenType.Assign)
                    {
                        Eat(TokenType.Assign);
                        init = Expr();
                    }
                    Eat(TokenType.Semi);

                    if (StaticSet)
                    {
                        staticFields[fieldName] = init ?? new NumberExpr(0, line, col, _current.Filename);
                        staticFieldVisibility[fieldName] = visibility;
                        if (ConstSet) staticConstFields.Add(fieldName);
                    }
                    else
                    {
                        fields[fieldName] = init ?? new NumberExpr(0, line, col, _current.Filename);
                        fieldVisibility[fieldName] = visibility;
                        if (ConstSet) constFields.Add(fieldName);
                    }
                }
                else if (_current.Type == TokenType.Class)
                {
                    if (StaticSet)
                        throw new ParserException("nested classes cannot be static", _current.Line, _current.Column, _current.Filename);
                    ClassDeclStmt inner = ParseClassDecl();

                    if (CheckFieldNames(inner.Name))
                        throw new ParserException($"Field '{inner.Name}' already declared in class '{name}'", _current.Line, _current.Column, _current.Filename);
                    inner = new ClassDeclStmt(
                        inner.Name, inner.Methods, inner.Enums,
                        inner.Fields, inner.StaticFields, inner.StaticMethods,
                        inner.Parameters, inner.Line, inner.Col, inner.OriginFile,
                        inner.BaseName, inner.BaseCtorArgs, inner.ImplementedInterfaces, inner.NestedClasses, isNested: true,
                        fieldVisibility: inner.FieldVisibility,
                        staticFieldVisibility: inner.StaticFieldVisibility,
                        methodVisibility: inner.MethodVisibility,
                        staticMethodVisibility: inner.StaticMethodVisibility,
                        enumVisibility: inner.EnumVisibility,
                        nestedClassVisibility: inner.NestedClassVisibility,
                        constFields: inner.ConstFields,
                        staticConstFields: inner.StaticConstFields
                    );
                    nestedClasses.Add(inner);
                    nestedClassVisibility[inner.Name] = visibility;
                }

                else if (_current.Type == TokenType.Enum)
                {
                    EnumDeclStmt enm = ParseEnumDecl();
                    if (CheckFieldNames(enm.Name))
                        throw new ParserException($"Field '{enm.Name}' already declared in class '{name}'", _current.Line, _current.Column, _current.Filename);
                    staticEnums.Add(enm);
                    enumVisibility[enm.Name] = visibility;
                }
                else if (_current.Type == TokenType.Func || _current.Type == TokenType.Async)
                {
                    FuncDeclStmt func = ParseFuncDecl();
                    if (CheckFieldNames(func.Name))
                        throw new ParserException($"Field '{func.Name}' already declared in class '{name}'", _current.Line, _current.Column, _current.Filename);
                    if (StaticSet)
                    {
                        staticMethods.Add(func);
                        staticMethodVisibility[func.Name] = visibility;
                    }
                    else
                    {
                        methods.Add(func);
                        methodVisibility[func.Name] = visibility;
                    }
                }
                else
                {
                    throw new ParserException(
                        $"unexpected token in class body: {_current.Type}",
                        _current.Line, _current.Column, _current.Filename
                    );
                }
            }

            Eat(TokenType.RBrace);
            ExitFunctionOrClassContext();
            return new ClassDeclStmt(
                name,
                methods,
                staticEnums,
                fields,
                staticFields,
                staticMethods,
                ctorParams,
                line,
                col,
                _current.Filename,
                baseName,
                baseArgs,
                implementedInterfaces,
                nestedClasses,
                false,
                fieldVisibility,
                staticFieldVisibility,
                methodVisibility,
                staticMethodVisibility,
                enumVisibility,
                nestedClassVisibility,
                constFields,
                staticConstFields
            );
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
                throw new ParserException("cannot bind '_' in match pattern; use '_' directly as wildcard", identTok.Line, identTok.Column, identTok.Filename);

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
                        _ => throw new ParserException("expected identifier or string key in dictionary match pattern", _current.Line, _current.Column, _current.Filename)
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
                        _ => throw new ParserException("expected identifier or string key in destructuring pattern", _current.Line, _current.Column, _current.Filename)
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
                            throw new ParserException("string key in destructuring pattern requires ':'", keyTok.Line, keyTok.Column, keyTok.Filename);

                        if (key == "_")
                            pat = new WildcardMatchPattern(keyTok.Line, keyTok.Column, keyTok.Filename);
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
        /// Gets the ParseVarDecl
        /// </summary>
        private Stmt ParseVarDecl
        {
            get
            {
                if (!multipleVarDecl) Eat(TokenType.Var);

                if (_current.Type == TokenType.LBracket || _current.Type == TokenType.LBrace)
                {
                    if (multipleVarDecl)
                        throw new ParserException("destructuring declaration cannot follow ',' in var declaration", _current.Line, _current.Column, _current.Filename);

                    MatchPattern pattern = ParseDestructurePattern();
                    ValidateUniqueDestructureBindings(pattern);

                    if (_current.Type != TokenType.Assign)
                        throw new ParserException("destructuring declarations require an initializer", _current.Line, _current.Column, _current.Filename);

                    Eat(TokenType.Assign);
                    Expr value = Expr();
                    Eat(TokenType.Semi);

                    return new DestructureDeclStmt(pattern, value, isConst: false, _current.Line, _current.Column, _current.Filename);
                }

                string name = _current.Value.ToString() ?? "";
                if (Lexer.Keywords.ContainsKey(name))
                    throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Ident);
                Expr? v = null;
                if (_current.Type == TokenType.Assign)
                {
                    Eat(TokenType.Assign);
                    v = Expr();
                }
                if (_current.Type == TokenType.Comma)
                {
                    multipleVarDecl = true;
                    Eat(TokenType.Comma);
                }
                else
                {
                    multipleVarDecl = false;
                    Eat(TokenType.Semi);
                }

                return new VarDecl(name, v, _current.Line, _current.Column, _current.Filename);
            }
        }

        /// <summary>
        /// Gets the ParseConstDecl
        /// </summary>
        private Stmt ParseConstDecl
        {
            get
            {
                Eat(TokenType.Const);

                if (_current.Type == TokenType.LBracket || _current.Type == TokenType.LBrace)
                {
                    MatchPattern pattern = ParseDestructurePattern();
                    ValidateUniqueDestructureBindings(pattern);

                    if (_current.Type != TokenType.Assign)
                        throw new ParserException("const destructuring declarations require an initializer", _current.Line, _current.Column, _current.Filename);

                    Eat(TokenType.Assign);
                    Expr destructValue = Expr();
                    Eat(TokenType.Semi);

                    return new DestructureDeclStmt(pattern, destructValue, isConst: true, _current.Line, _current.Column, _current.Filename);
                }

                string name = _current.Value?.ToString() ?? "";
                if (Lexer.Keywords.ContainsKey(name))
                    throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);
                Eat(TokenType.Ident);

                if (_current.Type != TokenType.Assign)
                    throw new ParserException("const declarations require an initializer", _current.Line, _current.Column, _current.Filename);

                Eat(TokenType.Assign);
                Expr value = Expr();
                Eat(TokenType.Semi);

                return new ConstDecl(name, value, _current.Line, _current.Column, _current.Filename);
            }
        }

        /// <summary>
        /// The ParseExportDecl
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseExportDecl()
        {
            int line = _current.Line;
            int col = _current.Column;
            string file = _current.Filename;

            Eat(TokenType.Export);

            Stmt inner = _current.Type switch
            {
                TokenType.Func => ParseFuncDecl(),
                TokenType.Async => ParseFuncDecl(),
                TokenType.Class => ParseClassDecl(),
                TokenType.Interface => ParseInterfaceDecl(),
                TokenType.Enum => ParseEnumDecl(),
                TokenType.Var => ParseSingleVarDecl(),
                TokenType.Const => ParseConstDecl,
                _ => throw new ParserException("export supports only var/const/func(async func)/class/interface/enum declarations", _current.Line, _current.Column, _current.Filename),
            };

            if (!TryGetNamedTopLevel(inner, out string? name) || string.IsNullOrWhiteSpace(name))
                throw new ParserException("exported declaration must have a symbol name", line, col, file);

            return new ExportStmt(name, inner, line, col, file);
        }

        /// <summary>
        /// The ParseSingleVarDecl
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseSingleVarDecl()
        {
            Eat(TokenType.Var);

            if (_current.Type == TokenType.LBracket || _current.Type == TokenType.LBrace)
                throw new ParserException("export var destructuring is not supported", _current.Line, _current.Column, _current.Filename);

            string name = _current.Value?.ToString() ?? "";
            if (Lexer.Keywords.ContainsKey(name))
                throw new ParserException($"invalid symbol declaration name '{name}'", _current.Line, _current.Column, _current.Filename);
            Eat(TokenType.Ident);

            Expr? value = null;
            if (_current.Type == TokenType.Assign)
            {
                Eat(TokenType.Assign);
                value = Expr();
            }

            if (_current.Type == TokenType.Comma)
                throw new ParserException("export var only supports a single declaration", _current.Line, _current.Column, _current.Filename);

            Eat(TokenType.Semi);
            return new VarDecl(name, value, _current.Line, _current.Column, _current.Filename);
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

        /// <summary>
        /// The ParseAssignOrIndexAssignOrPushOrExpr
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt ParseAssignOrIndexAssignOrPushOrExpr()
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

                    target = new IndexExpr(target, new StringExpr(fieldName, line, col, fsname), line, col, fsname);
                }

                else if (_current.Type == TokenType.LBracket)
                {
                    Eat(TokenType.LBracket);

                    if (_current.Type == TokenType.RBracket)
                    {
                        Eat(TokenType.RBracket);
                        Eat(TokenType.Assign);
                        Expr val = Expr();
                        Eat(TokenType.Semi);
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
                        Eat(TokenType.Semi);

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
                Eat(TokenType.Semi);

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
                Eat(TokenType.Semi);
                return new ExprStmt(new PostfixExpr(target, op, line, col, fsname), line, col, fsname);
            }

            Eat(TokenType.Semi);
            return new ExprStmt(target, line, col, fsname);
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
                            line, col, fsname
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
                name = $"__fe_pair_{_foreachDestructureCounter++}";
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
                name = $"__fe_ds_{_foreachDestructureCounter++}";
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
                Eat(TokenType.Semi);

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
                inc = Statement();
            }
            Eat(TokenType.RParen);
            EnterLoopContext();
            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
            ExitLoopContext();
            return new ForStmt(init, cond, inc, body, line, col, file);
        }
    }
}
