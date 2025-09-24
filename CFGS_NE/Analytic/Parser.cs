using CFGS_VM.Analytic;
/// <summary>
/// Defines the <see cref="Parser" />
/// </summary>
public class Parser
{
    /// <summary>
    /// Defines the _importedModules
    /// </summary>
    private readonly HashSet<string> _importedModules = new();

    /// <summary>
    /// Defines the _lexer
    /// </summary>
    private readonly Lexer _lexer;

    /// <summary>
    /// Defines the _current
    /// </summary>
    private Token _current;

    /// <summary>
    /// Defines the _next
    /// </summary>
    private Token _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="Parser"/> class.
    /// </summary>
    /// <param name="lexer">The lexer<see cref="Lexer"/></param>
    public Parser(Lexer lexer)
    {

        _lexer = lexer;
        _current = _lexer.GetNextToken();
        _next = _lexer.GetNextToken();
    }

    /// <summary>
    /// The Advance
    /// </summary>
    private void Advance()
    {
        _current = _next;
        _next = _lexer.GetNextToken();
    }

    /// <summary>
    /// The Eat
    /// </summary>
    /// <param name="type">The type<see cref="TokenType"/></param>
    private void Eat(TokenType type)
    {
        if (_current.Type != type)
            throw new ParserException($"Expected {type}, got {_current.Type} ", _current.Line, _current.Column, _current.Filename);
        Advance();
    }

    /// <summary>
    /// Gets the Current
    /// </summary>
    private Token Current => _current;

    /// <summary>
    /// Gets the LookAhead
    /// </summary>
    private Token LookAhead => _next;

    /// <summary>
    /// The Parse
    /// </summary>
    /// <returns>The <see cref="List{Stmt}"/></returns>
    public List<Stmt> Parse()
    {
        var stmts = new List<Stmt>();
        if (_current.Type == TokenType.Import)
        {
            while (_current.Type == TokenType.Import)
            {
                Eat(TokenType.Import);
                if (_current.Type != TokenType.String)
                    throw new ParserException($"expected string but got '{_current.Type}'", _current.Line, _current.Column, _current.Filename);

                string path = _current.Value.ToString() ?? "";
                Eat(TokenType.String);
                if (File.Exists(path))
                {
                    if (!_importedModules.Contains(path))
                    {
                        try
                        {

                            StreamReader lreader = new StreamReader(path, true);
                            string nsrc = lreader.ReadToEnd();
                            lreader.Close();
                            var lex = new Lexer(path, nsrc);
                            var prs = new Parser(lex);
                            stmts.AddRange(prs.Parse());

                            _importedModules.Add(path);
                        }
                        catch (Exception pex)
                        {

                            throw new ParserException(pex.Message, _current.Line, _current.Column, _current.Filename);
                        }
                    }
                    else
                        Console.WriteLine($"Warning : tried to import script more than once '{path}', line {_current.Line}, position {_current.Column}.");

                }
                Eat(TokenType.Semi);

            }
        }

        while (_current.Type != TokenType.EOF)
        {
            if (_current.Type == TokenType.Import)
                throw new ParserException("Invalid import statement. Imports are only allowed in the header of the script", _current.Line, _current.Column, _current.Filename);
            stmts.Add(Statement());
        }
        return stmts;
    }

    /// <summary>
    /// The Statement
    /// </summary>
    /// <returns>The <see cref="Stmt"/></returns>
    private Stmt Statement()
    {
        return _current.Type switch
        {
            TokenType.Print => ParsePrint(),
            TokenType.Var => ParseVarDecl(),
            TokenType.Ident => ParseAssignOrIndexAssignOrPushOrExpr(),
            TokenType.LBrace => ParseBlock(),
            TokenType.If => ParseIf(),
            TokenType.While => ParseWhile(),
            TokenType.Break => ParseBreak(),
            TokenType.Continue => ParseContinue(),
            TokenType.For => ParseFor(),
            TokenType.Func => ParseFuncDecl(),
            TokenType.Return => ParseReturnStmt(),
            TokenType.Delete => ParseDelete(),
            TokenType.Match => ParseMatch(),
            TokenType.Try => ParseTry(),
            TokenType.Throw => ParseThrow(),
            TokenType.Class => ParseClassDecl(),
            TokenType.Enum => ParseEnumDecl(),

            _ => ParseExprStmt()
        };
    }

    /// <summary>
    /// The ParseEnumDecl
    /// </summary>
    /// <returns>The <see cref="EnumDeclStmt"/></returns>
    private EnumDeclStmt ParseEnumDecl()
    {
        int line = _current.Line;
        int col = _current.Column;

        Eat(TokenType.Enum);

        if (_current.Type != TokenType.Ident)
            throw new ParserException("expected enum name", line, col, _current.Filename);

        string name = _current.Value.ToString() ?? "";
        Eat(TokenType.Ident);

        Eat(TokenType.LBrace);

        var members = new List<EnumMemberNode>();
        int currentValue = 0;

        while (_current.Type != TokenType.RBrace)
        {
            string memberName = _current.Value.ToString() ?? "";
            Eat(TokenType.Ident);

            if (_current.Type == TokenType.Assign)
            {
                Eat(TokenType.Assign);
                if (_current.Type != TokenType.Number)
                    throw new ParserException("expected number after '='", line, col, _current.Filename);

                currentValue = Convert.ToInt32(_current.Value);
                Eat(TokenType.Number);
            }

            if (members.Any(m => m.Value == currentValue))
                throw new ParserException(
                    $"duplicate enum value '{currentValue}' in enum '{name}'",
                    line, col, _current.Filename
                );

            members.Add(new EnumMemberNode(memberName, currentValue, line, col, _current.Filename));

            currentValue++;

            if (_current.Type == TokenType.Comma)
                Eat(TokenType.Comma);
            else
                break;
        }

        Eat(TokenType.RBrace);

        return new EnumDeclStmt(name, members, line, col, _current.Filename);
    }

    /// <summary>
    /// The ParseExprStmt
    /// </summary>
    /// <returns>The <see cref="Stmt"/></returns>
    private Stmt ParseExprStmt()
    {
        int line = _current.Line;
        int col = _current.Column;

        Expr e = Expr();
        Eat(TokenType.Semi);
        return new ExprStmt(e, line, col, _current.Filename);
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
        Eat(TokenType.Ident);

        var ctorParams = new List<string>();
        if (_current.Type == TokenType.LParen)
        {
            Eat(TokenType.LParen);

            if (_current.Type != TokenType.RParen)
            {
                do
                {
                    string paramName = _current.Value.ToString() ?? "";
                    Eat(TokenType.Ident);
                    ctorParams.Add(paramName);

                    if (_current.Type == TokenType.Comma)
                        Eat(TokenType.Comma);
                    else
                        break;
                }
                while (true);
            }

            Eat(TokenType.RParen);
        }

        Eat(TokenType.LBrace);

        var methods = new List<FuncDeclStmt>();
        var fields = new Dictionary<string, Expr?>();

        while (_current.Type != TokenType.RBrace)
        {
            if (_current.Type == TokenType.Var)
            {
                Eat(TokenType.Var);
                string fieldName = _current.Value.ToString() ?? "";
                Eat(TokenType.Ident);

                Expr? init = null;
                if (_current.Type == TokenType.Assign)
                {
                    Eat(TokenType.Assign);
                    init = Expr();
                }
                Eat(TokenType.Semi);

                fields[fieldName] = init ?? new NumberExpr(0, line, col, _current.Filename);
            }
            else if (_current.Type == TokenType.Func)
            {
                var func = ParseFuncDecl();
                methods.Add(func);
            }
            else
            {
                throw new ParserException($"unexpected token in class body: {_current.Type}", _current.Line, _current.Column, _current.Filename);
            }
        }

        Eat(TokenType.RBrace);

        return new ClassDeclStmt(name, methods, fields, ctorParams, line, col, _current.Filename);
    }

    /// <summary>
    /// The ParseNew
    /// </summary>
    /// <returns>The <see cref="Expr"/></returns>
    private Expr ParseNew()
    {
        int line = _current.Line;
        int col = _current.Column;

        Eat(TokenType.New);

        if (_current.Type != TokenType.Ident)
            throw new ParserException("expected class name after 'new'", line, col, _current.Filename);

        string className = _current.Value.ToString() ?? "";
        Eat(TokenType.Ident);

        List<Expr> args = new List<Expr>();

        if (_current.Type == TokenType.LParen)
        {
            Eat(TokenType.LParen);

            if (_current.Type != TokenType.RParen)
            {
                args = ParseExprList();
            }

            Eat(TokenType.RParen);
        }

        return new NewExpr(className, args, line, col, _current.Filename);
    }

    /// <summary>
    /// The ParseExprList
    /// </summary>
    /// <returns>The <see cref="List{Expr}"/></returns>
    private List<Expr> ParseExprList()
    {
        var list = new List<Expr>();

        list.Add(Expr());

        while (_current.Type == TokenType.Comma)
        {
            Eat(TokenType.Comma);
            list.Add(Expr());
        }

        return list;
    }

    /// <summary>
    /// The ParseThrow
    /// </summary>
    /// <returns>The <see cref="Stmt"/></returns>
    private Stmt ParseThrow()
    {
        int line = _current.Line;
        int col = _current.Column;
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
        int line = _current.Line;
        int col = _current.Column;

        Eat(TokenType.Try);
        BlockStmt tryBlock = ParseBlock();

        string? catchVar = null;
        BlockStmt? catchBlock = null;
        BlockStmt? finallyBlock = null;

        if (_current.Type == TokenType.Catch)
        {
            Eat(TokenType.Catch);
            Eat(TokenType.LParen);
            if (_current.Type != TokenType.Ident)
                throw new ParserException("expected identifier in catch(...)", _current.Line, _current.Column, _current.Filename);
            catchVar = _current.Value.ToString();
            Eat(TokenType.Ident);
            Eat(TokenType.RParen);
            catchBlock = ParseBlock();
        }

        if (_current.Type == TokenType.Finally)
        {
            Eat(TokenType.Finally);
            finallyBlock = ParseBlock();
        }

        return new TryCatchFinallyStmt(tryBlock, catchVar, catchBlock, finallyBlock, line, col, _current.Filename);
    }

    /// <summary>
    /// The ParseMatch
    /// </summary>
    /// <returns>The <see cref="MatchStmt"/></returns>
    private MatchStmt ParseMatch()
    {
        Eat(TokenType.Match);
        Eat(TokenType.LParen);
        Expr expr = Expr();
        Eat(TokenType.RParen);
        Eat(TokenType.LBrace);

        var cases = new List<CaseClause>();
        BlockStmt? defaultCase = null;

        while (_current.Type == TokenType.Case || _current.Type == TokenType.Default)
        {
            if (_current.Type == TokenType.Case)
            {
                Eat(TokenType.Case);
                Expr pattern = Expr();
                Eat(TokenType.Colon);
                BlockStmt body = ParseBlock();
                cases.Add(new CaseClause(pattern, body, _current.Line, _current.Column, _current.Filename));
            }
            else if (_current.Type == TokenType.Default)
            {
                Eat(TokenType.Default);
                Eat(TokenType.Colon);
                defaultCase = ParseBlock();
            }
        }

        Eat(TokenType.RBrace);
        return new MatchStmt(expr, cases, defaultCase, _current.Line, _current.Column, _current.Filename);
    }

    /// <summary>
    /// The ParseBreak
    /// </summary>
    /// <returns>The <see cref="Stmt"/></returns>
    private Stmt ParseBreak()
    {
        Eat(TokenType.Break);
        Eat(TokenType.Semi);
        return new BreakStmt(_current.Line, _current.Column, _current.Filename);
    }

    /// <summary>
    /// The ParseContinue
    /// </summary>
    /// <returns>The <see cref="Stmt"/></returns>
    private Stmt ParseContinue()
    {
        Eat(TokenType.Continue);
        Eat(TokenType.Semi);
        return new ContinueStmt(_current.Line, _current.Column, _current.Filename);
    }

    /// <summary>
    /// The ParsePrint
    /// </summary>
    /// <returns>The <see cref="Stmt"/></returns>
    private Stmt ParsePrint()
    {
        Eat(TokenType.Print);
        Eat(TokenType.LParen);
        Expr e = Expr();
        Eat(TokenType.RParen);
        Eat(TokenType.Semi);
        return new PrintStmt(e, _current.Line, _current.Column, _current.Filename);
    }

    /// <summary>
    /// The ParseVarDecl
    /// </summary>
    /// <returns>The <see cref="Stmt"/></returns>
    private Stmt ParseVarDecl()
    {
        Eat(TokenType.Var);
        string name = _current.Value.ToString() ?? "";
        Eat(TokenType.Ident);
        Expr? v = null;
        if (_current.Type == TokenType.Assign)
        {
            Eat(TokenType.Assign);
            v = Expr();
        }
        Eat(TokenType.Semi);
        return new VarDecl(name, v, _current.Line, _current.Column, _current.Filename);
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
                var args = new List<Expr>();
                if (_current.Type != TokenType.RParen)
                {
                    args.Add(Expr());
                    while (_current.Type == TokenType.Comma)
                    {
                        Eat(TokenType.Comma);
                        args.Add(Expr());
                    }
                }
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
            var op = _current.Type;
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
            var op = _current.Type;
            Eat(op);
            Eat(TokenType.Semi);
            return new ExprStmt(new PostfixExpr(target, op, line, col, fsname), line, col, fsname);
        }

        Eat(TokenType.Semi);
        return new ExprStmt(target, line, col, fsname);
    }

    /// <summary>
    /// The ParseDelete
    /// </summary>
    /// <returns>The <see cref="Stmt"/></returns>
    private Stmt ParseDelete()
    {
        int line = _current.Line;
        int col = _current.Column;

        Eat(TokenType.Delete);

        if (_current.Type != TokenType.Ident)
            throw new ParserException("expected identifier after delete", line, col, _current.Filename);

        Expr target = new VarExpr(_current.Value.ToString() ?? "", line, col, _current.Filename);
        Eat(TokenType.Ident);

        while (_current.Type == TokenType.LBracket)
        {
            Eat(TokenType.LBracket);

            if (_current.Type == TokenType.RBracket)
            {
                Eat(TokenType.RBracket);
                Eat(TokenType.Semi);
                return new DeleteExprStmt(target, true, line, col, _current.Filename);
            }
            else
            {
                Expr idx = Expr();
                Eat(TokenType.RBracket);
                target = new IndexExpr(target, idx, line, col, _current.Filename);
            }
        }

        Eat(TokenType.Semi);
        return new DeleteExprStmt(target, false, line, col, _current.Filename);
    }

    /// <summary>
    /// The ParseBlock
    /// </summary>
    /// <returns>The <see cref="BlockStmt"/></returns>
    private BlockStmt ParseBlock()
    {
        Eat(TokenType.LBrace);
        var stmts = new List<Stmt>();
        while (_current.Type != TokenType.RBrace)
            stmts.Add(Statement());
        Eat(TokenType.RBrace);
        return new BlockStmt(stmts, _current.Line, _current.Column, _current.Filename);
    }

    /// <summary>
    /// The ParseIf
    /// </summary>
    /// <returns>The <see cref="IfStmt"/></returns>
    private IfStmt ParseIf()
    {
        Eat(TokenType.If);
        Eat(TokenType.LParen);
        Expr cond = Expr();
        Eat(TokenType.RParen);
        BlockStmt thenBlk = ParseBlock();

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
                elseBlk = ParseBlock();
            }
        }

        return new IfStmt(cond, thenBlk, elseBlk, _current.Line, _current.Column, _current.Filename);
    }

    /// <summary>
    /// The ParseWhile
    /// </summary>
    /// <returns>The <see cref="WhileStmt"/></returns>
    private WhileStmt ParseWhile()
    {
        Eat(TokenType.While);
        Eat(TokenType.LParen);
        Expr cond = Expr();
        Eat(TokenType.RParen);
        BlockStmt body = ParseBlock();
        return new WhileStmt(cond, body, _current.Line, _current.Column, _current.Filename);
    }

    /// <summary>
    /// The ParseFor
    /// </summary>
    /// <returns>The <see cref="ForStmt"/></returns>
    private ForStmt ParseFor()
    {
        Eat(TokenType.For);
        Eat(TokenType.LParen);

        Stmt? init = null;
        if (_current.Type != TokenType.Semi)
            init = Statement();
        else
            Eat(TokenType.Semi);

        Expr? cond = null;
        if (_current.Type != TokenType.Semi)
            cond = Expr();
        Eat(TokenType.Semi);

        Stmt? inc = null;
        if (_current.Type != TokenType.RParen)
            inc = Statement();
        Eat(TokenType.RParen);

        BlockStmt body = ParseBlock();
        return new ForStmt(init, cond, inc, body, _current.Line, _current.Column, _current.Filename);
    }

    /// <summary>
    /// The Expr
    /// </summary>
    /// <returns>The <see cref="Expr"/></returns>
    private Expr Expr() => Or();

    /// <summary>
    /// The Or
    /// </summary>
    /// <returns>The <see cref="Expr"/></returns>
    private Expr Or()
    {
        Expr node = And();
        while (_current.Type == TokenType.OrOr)
        {
            Eat(TokenType.OrOr);
            node = new BinaryExpr(node, TokenType.OrOr, And(), _current.Line, _current.Column, _current.Filename);
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
            Eat(TokenType.AndAnd);
            node = new BinaryExpr(node, TokenType.AndAnd, Comparison(), _current.Line, _current.Column, _current.Filename);
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
               _current.Type == TokenType.Le || _current.Type == TokenType.Ge)
        {
            TokenType op = _current.Type;
            Eat(op);
            node = new BinaryExpr(node, op, BitwiseOr(), _current.Line, _current.Column, _current.Filename);
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
            Eat(op);
            node = new BinaryExpr(node, op, BitwiseXor(), _current.Line, _current.Column, _current.Filename);
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
            Eat(op);
            node = new BinaryExpr(node, op, BitwiseAnd(), _current.Line, _current.Column, _current.Filename);
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
            Eat(op);
            node = new BinaryExpr(node, op, Shift(), _current.Line, _current.Column, _current.Filename);
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
        while (_current.Type == TokenType.bShiftL || _current.Type == TokenType.BShiftR)
        {
            TokenType op = _current.Type;
            Eat(op);
            node = new BinaryExpr(node, op, Additive(), _current.Line, _current.Column, _current.Filename);
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
            Eat(op);
            node = new BinaryExpr(node, op, Term(), _current.Line, _current.Column, _current.Filename);
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
            Eat(op);
            node = new BinaryExpr(node, op, Power(), _current.Line, _current.Column, _current.Filename);
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
            Eat(op);
            node = new BinaryExpr(node, op, Power(), _current.Line, _current.Column, _current.Filename);
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
            node = null;
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
            node = ParseNew();
        }
        else if (_current.Type == TokenType.String)
        {
            string s = _current.Value.ToString() ?? "";
            Eat(TokenType.String);
            node = new StringExpr(s, _current.Line, _current.Column, _current.Filename);
        }
        else if (_current.Type == TokenType.Char)
        {
            char vlc = (char)0;
            if (_current.Value.ToString() == "\\n")
                vlc = (char)13;
            else if (_current.Value.ToString() == "\\r")
                vlc = (char)10;
            else if (_current.Value.ToString() == "\\t")
                vlc = (char)9;
            else if (_current.Value.ToString() == "\\\"")
                vlc = (char)34;
            else if (_current.Value.ToString() == "\\\\")
                vlc = (char)47;
            else if (_current.Value.ToString() == "\\\'")
                vlc = (char)39;
            else if (!char.TryParse(_current.Value.ToString(), out vlc))
                throw new ParserException($"invalid char value '{_current.Value.ToString()}'", _current.Line, _current.Column, _current.Filename);
            Eat(TokenType.Char);
            node = new CharExpr(vlc, _current.Line, _current.Column, _current.Filename);
        }
        else if (_current.Type == TokenType.Ident)
        {
            string name = _current.Value.ToString() ?? "";
            Eat(TokenType.Ident);
            node = new VarExpr(name, _current.Line, _current.Column, _current.Filename);
        }
        else if (_current.Type == TokenType.LParen)
        {
            Eat(TokenType.LParen);
            node = Expr();
            Eat(TokenType.RParen);
        }
        else if (_current.Type == TokenType.LBracket)
        {
            Eat(TokenType.LBracket);
            var elems = new List<Expr>();
            if (_current.Type != TokenType.RBracket)
            {
                elems.Add(Expr());
                while (_current.Type == TokenType.Comma)
                {
                    Eat(TokenType.Comma);
                    elems.Add(Expr());
                }
            }
            Eat(TokenType.RBracket);
            node = new ArrayExpr(elems, _current.Line, _current.Column, _current.Filename);
        }
        else if (_current.Type == TokenType.LBrace)
        {
            Eat(TokenType.LBrace);
            var pairs = new List<(Expr, Expr)>();

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
            node = new DictExpr(pairs, _current.Line, _current.Column, _current.Filename);
        }
        else if (_current.Type == TokenType.Func)
        {
            Eat(TokenType.Func);
            Eat(TokenType.LParen);

            var parameters = new List<string>();
            if (_current.Type != TokenType.RParen)
            {
                do
                {
                    string paramName = _current.Value.ToString() ?? "";
                    Eat(TokenType.Ident);
                    parameters.Add(paramName);
                    if (_current.Type == TokenType.Comma)
                        Eat(TokenType.Comma);
                    else
                        break;
                } while (true);
            }

            Eat(TokenType.RParen);
            BlockStmt body = ParseBlock();

            node = new FuncExpr(parameters, body, _current.Line, _current.Column, _current.Filename);
        }
        else
        {
            throw new ParserException($"invalid factor, token {_current.Type}", _current.Line, _current.Column, _current.Filename);
        }

        while (true)
        {
            if (_current.Type == TokenType.LParen)
            {
                Eat(TokenType.LParen);
                var args = new List<Expr>();
                if (_current.Type != TokenType.RParen)
                {
                    do
                    {
                        args.Add(Expr());
                        if (_current.Type == TokenType.Comma)
                            Eat(TokenType.Comma);
                        else
                            break;
                    } while (true);
                }
                Eat(TokenType.RParen);
                node = new CallExpr(node, args, _current.Line, _current.Column, _current.Filename);
            }
            else if (_current.Type == TokenType.LBracket)
            {
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
                        node = new SliceExpr(node, start, end, _current.Line, _current.Column, _current.Filename);
                        continue;
                    }
                    else
                    {
                        Eat(TokenType.RBracket);
                        node = new IndexExpr(node, start, _current.Line, _current.Column, _current.Filename);
                        continue;
                    }
                }

                Eat(TokenType.RBracket);
                node = new SliceExpr(node, null, null, _current.Line, _current.Column, _current.Filename);
            }

            else if (_current.Type == TokenType.Dot)
            {
                Eat(TokenType.Dot);
                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected identifier after '.'", _current.Line, _current.Column, _current.Filename);
                string member = _current.Value.ToString() ?? "";
                node = new IndexExpr(node, new StringExpr(member, _current.Line, _current.Column, _current.Filename), node?.Line ?? -1, node?.Col ?? -1, node?.OriginFile ?? "");
                Eat(TokenType.Ident);
            }
            else if (_current.Type == TokenType.PlusPlus || _current.Type == TokenType.MinusMinus)
            {
                TokenType op = _current.Type;
                Eat(op);
                node = new PostfixExpr(node, op, _current.Line, _current.Column, _current.Filename);
            }
            else
            {
                break;
            }
        }
        return node is null ? throw new ParserException("Null Reference", _current.Line, _current.Column, _current.Filename) : node;
    }

    /// <summary>
    /// The Unary
    /// </summary>
    /// <returns>The <see cref="Expr"/></returns>
    private Expr Unary()
    {
        if (_current.Type == TokenType.Minus)
        {
            Eat(TokenType.Minus);
            return new UnaryExpr(TokenType.Minus, Unary(), _current.Line, _current.Column, _current.Filename);
        }
        if (_current.Type == TokenType.Plus)
        {
            Eat(TokenType.Plus);
            return new UnaryExpr(TokenType.Plus, Unary(), _current.Line, _current.Column, _current.Filename);
        }
        if (_current.Type == TokenType.Not)
        {
            Eat(TokenType.Not);
            return new UnaryExpr(TokenType.Not, Unary(), _current.Line, _current.Column, _current.Filename);
        }

        if (_current.Type == TokenType.PlusPlus || _current.Type == TokenType.MinusMinus)
        {
            var op = _current.Type;
            Eat(op);

            Expr? target = Unary();
            return new PrefixExpr(target, op, _current.Line, _current.Column, _current.Filename);
        }

        return Factor();
    }

    /// <summary>
    /// The ParseFuncDecl
    /// </summary>
    /// <returns>The <see cref="FuncDeclStmt"/></returns>
    private FuncDeclStmt ParseFuncDecl()
    {
        Eat(TokenType.Func);
        string name = _current.Value.ToString() ?? "";
        Eat(TokenType.Ident);
        Eat(TokenType.LParen);

        var parameters = new List<string>();
        if (_current.Type != TokenType.RParen)
        {
            do
            {
                string paramName = _current.Value.ToString() ?? "";
                Eat(TokenType.Ident);
                parameters.Add(paramName);
                if (_current.Type == TokenType.Comma)
                    Eat(TokenType.Comma);
                else
                    break;
            } while (true);
        }

        Eat(TokenType.RParen);
        BlockStmt body = ParseBlock();

        return new FuncDeclStmt(name, parameters, body, _current.Line, _current.Column, _current.Filename);
    }

    /// <summary>
    /// The ParseReturnStmt
    /// </summary>
    /// <returns>The <see cref="ReturnStmt"/></returns>
    private ReturnStmt ParseReturnStmt()
    {
        Eat(TokenType.Return);
        Expr value = Expr();
        Eat(TokenType.Semi);
        return new ReturnStmt(value, _current.Line, _current.Column, _current.Filename);
    }
}

/// <summary>
/// Defines the <see cref="ParserException" />
/// </summary>
public sealed class ParserException(string message, int line, int column, string filename) : Exception($"{message}. ( Line : {line}, Column : {column} ) : [Source : '{filename}']");
