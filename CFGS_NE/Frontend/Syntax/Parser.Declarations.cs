using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
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
                    if (_current.Type != TokenType.Number || _current.Value is not int)
                        throw new ParserException("expected number after '='", _current.Line, _current.Column, _current.Filename);

                    value = Convert.ToInt32(_current.Value);
                    Eat(TokenType.Number);
                }
                else
                {
                    value = nextAutoValue;
                }

                if (!usedNames.Add(memberName))
                {
                    throw new ParserException(
                        $"duplicate enum member name '{memberName}' in enum '{name}'",
                        memberLine,
                        memberCol,
                        _current.Filename);
                }

                if (!usedValues.Add(value))
                {
                    throw new ParserException(
                        $"duplicate enum value '{value}' in enum '{name}'",
                        memberLine,
                        memberCol,
                        _current.Filename);
                }

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
                        _current.Line,
                        _current.Column,
                        _current.Filename);
                }

                methods.Add(ParseInterfaceMethodDecl());
            }

            Eat(TokenType.RBrace);
            return new InterfaceDeclStmt(name, methods, baseInterfaces, line, col, file);
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
                    _ => MemberVisibility.Public,
                };
            }

            while (_current.Type != TokenType.RBrace)
            {
                bool staticSet = false;
                bool seenStatic = false;
                bool seenVisibility = false;
                bool constSet = false;
                bool seenConst = false;
                MemberVisibility visibility = MemberVisibility.Public;

                while (true)
                {
                    if (_current.Type == TokenType.Static)
                    {
                        if (seenStatic)
                            throw new ParserException("duplicate 'static' modifier in class member declaration", _current.Line, _current.Column, _current.Filename);

                        seenStatic = true;
                        staticSet = true;
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
                        constSet = true;
                        Eat(TokenType.Const);
                        continue;
                    }

                    break;
                }

                if (_current.Type == TokenType.Var || (constSet && _current.Type == TokenType.Ident))
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

                    if (staticSet)
                    {
                        staticFields[fieldName] = init ?? new NumberExpr(0, line, col, _current.Filename);
                        staticFieldVisibility[fieldName] = visibility;
                        if (constSet)
                            staticConstFields.Add(fieldName);
                    }
                    else
                    {
                        fields[fieldName] = init ?? new NumberExpr(0, line, col, _current.Filename);
                        fieldVisibility[fieldName] = visibility;
                        if (constSet)
                            constFields.Add(fieldName);
                    }
                }
                else if (_current.Type == TokenType.Class)
                {
                    if (staticSet)
                        throw new ParserException("nested classes cannot be static", _current.Line, _current.Column, _current.Filename);
                    ClassDeclStmt inner = ParseClassDecl();

                    if (CheckFieldNames(inner.Name))
                        throw new ParserException($"Field '{inner.Name}' already declared in class '{name}'", _current.Line, _current.Column, _current.Filename);
                    inner = new ClassDeclStmt(
                        inner.Name,
                        inner.Methods,
                        inner.Enums,
                        inner.Fields,
                        inner.StaticFields,
                        inner.StaticMethods,
                        inner.Parameters,
                        inner.Line,
                        inner.Col,
                        inner.OriginFile,
                        inner.BaseName,
                        inner.BaseCtorArgs,
                        inner.ImplementedInterfaces,
                        inner.NestedClasses,
                        isNested: true,
                        fieldVisibility: inner.FieldVisibility,
                        staticFieldVisibility: inner.StaticFieldVisibility,
                        methodVisibility: inner.MethodVisibility,
                        staticMethodVisibility: inner.StaticMethodVisibility,
                        enumVisibility: inner.EnumVisibility,
                        nestedClassVisibility: inner.NestedClassVisibility,
                        constFields: inner.ConstFields,
                        staticConstFields: inner.StaticConstFields);
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
                    if (staticSet)
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
                        _current.Line,
                        _current.Column,
                        _current.Filename);
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
                staticConstFields);
        }

        /// <summary>
        /// Gets the ParseVarDecl
        /// </summary>
        private Stmt ParseVarDecl
        {
            get
            {
                if (!_context.IsInMultipleVarDeclaration)
                    Eat(TokenType.Var);

                if (_current.Type == TokenType.LBracket || _current.Type == TokenType.LBrace)
                {
                    if (_context.IsInMultipleVarDeclaration)
                    {
                        throw new ParserException(
                            "destructuring declaration cannot follow ',' in var declaration",
                            _current.Line,
                            _current.Column,
                            _current.Filename);
                    }

                    MatchPattern pattern = ParseDestructurePattern();
                    ValidateUniqueDestructureBindings(pattern);

                    if (_current.Type != TokenType.Assign)
                    {
                        throw new ParserException(
                            "destructuring declarations require an initializer",
                            _current.Line,
                            _current.Column,
                            _current.Filename);
                    }

                    Eat(TokenType.Assign);
                    Expr destructValue = Expr();
                    Eat(TokenType.Semi);

                    return new DestructureDeclStmt(pattern, destructValue, isConst: false, _current.Line, _current.Column, _current.Filename);
                }

                string name = _current.Value.ToString() ?? "";
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
                {
                    _context.ContinueMultipleVarDeclaration();
                    Eat(TokenType.Comma);
                }
                else
                {
                    _context.CompleteMultipleVarDeclaration();
                    Eat(TokenType.Semi);
                }

                return new VarDecl(name, value, _current.Line, _current.Column, _current.Filename);
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
                    {
                        throw new ParserException(
                            "const destructuring declarations require an initializer",
                            _current.Line,
                            _current.Column,
                            _current.Filename);
                    }

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
    }
}
