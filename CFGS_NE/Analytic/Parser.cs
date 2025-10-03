using CFGS_VM.Analytic.TTypes;
using CFGS_VM.VMCore.Command;
using System.Security.Cryptography;
using System.Text;

namespace CFGS_VM.Analytic
{
    /// <summary>
    /// Defines the <see cref="Parser" />
    /// </summary>
    public class Parser
    {
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
        /// Defines the _importedHashes
        /// </summary>
        private readonly HashSet<string> _importedHashes = new();

        /// <summary>
        /// Defines the _astByHash
        /// </summary>
        private readonly Dictionary<string, List<Stmt>> _astByHash = new();

        /// <summary>
        /// Defines the _importStack
        /// </summary>
        private readonly Stack<string> _importStack = new();

        /// <summary>
        /// Defines the _seenFunctions
        /// </summary>
        private readonly HashSet<string> _seenFunctions = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines the _seenClasses
        /// </summary>
        private readonly HashSet<string> _seenClasses = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines the _seenEnums
        /// </summary>
        private readonly HashSet<string> _seenEnums = new(StringComparer.Ordinal);

        /// <summary>
        /// The ComputeSha256
        /// </summary>
        /// <param name="path">The path<see cref="string"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string ComputeSha256(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream fs = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(fs);
            StringBuilder sb = new(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// The IndexTopLevelSymbols
        /// </summary>
        /// <param name="stmts">The stmts<see cref="IEnumerable{Stmt}"/></param>
        private void IndexTopLevelSymbols(IEnumerable<Stmt> stmts)
        {
            foreach (Stmt s in stmts)
            {
                switch (s)
                {
                    case FuncDeclStmt f: _seenFunctions.Add(f.Name); break;
                    case ClassDeclStmt c: _seenClasses.Add(c.Name); break;
                    case EnumDeclStmt e: _seenEnums.Add(e.Name); break;
                }
            }
        }

        /// <summary>
        /// The FilterDuplicateTopLevel
        /// </summary>
        /// <param name="stmts">The stmts<see cref="List{Stmt}"/></param>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        private List<Stmt> FilterDuplicateTopLevel(List<Stmt> stmts)
        {
            List<Stmt> filtered = new(stmts.Count);
            foreach (Stmt s in stmts)
            {
                switch (s)
                {
                    case FuncDeclStmt f when _seenFunctions.Contains(f.Name):
                        Console.Error.WriteLine($"Import Warning: Skipping duplicate function '{f.Name}' in line {f.Line}, position {f.Col}.");
                        break;
                    case ClassDeclStmt c when _seenClasses.Contains(c.Name):
                        Console.Error.WriteLine($"Import Warning: Skipping duplicate class '{c.Name}' in line {c.Line}, position {c.Col}.");
                        break;
                    case EnumDeclStmt e when _seenEnums.Contains(e.Name):
                        Console.Error.WriteLine($"Import Warning: Skipping duplicate enum '{e.Name}' in line {e.Line}, position {e.Col}.");
                        break;
                    default:
                        filtered.Add(s);
                        break;
                }
            }
            return filtered;
        }

        /// <summary>
        /// The Parse
        /// </summary>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        public List<Stmt> Parse()
        {
            List<Stmt> stmts = new();

            if (_current.Type == TokenType.Import)
            {
                while (_current.Type == TokenType.Import)
                {
                    Eat(TokenType.Import);
                    if (_current.Type == TokenType.String)
                    {
                        string path = _current.Value?.ToString() ?? "";
                        Eat(TokenType.String);
                        List<Stmt> imported = GetImports(path, _current.Line, _current.Column, _current.Filename);
                        stmts.AddRange(imported);
                        IndexTopLevelSymbols(imported);
                    }
                    else if (_current.Type == TokenType.Ident)
                    {
                        string clsName = _current.Value?.ToString() ?? "";
                        Eat(TokenType.Ident);
                        Eat(TokenType.From);
                        if (_current.Type != TokenType.String)
                            throw new ParserException("expected string after 'from' in import statement", _current.Line, _current.Column, _current.Filename);
                        string path = _current.Value?.ToString() ?? "";
                        List<Stmt> imported = GetImports(path, _current.Line, _current.Column, _current.Filename, clsName);
                        Eat(TokenType.String);

                        stmts.AddRange(imported);
                        IndexTopLevelSymbols(imported);
                    }
                    else
                    {
                        throw new ParserException("invalid import statement", _current.Line, _current.Column, _current.Filename);
                    }

                    Eat(TokenType.Semi);
                }
            }

            while (_current.Type != TokenType.EOF)
            {
                if (_current.Type == TokenType.Import)
                    throw new ParserException("Invalid import statement. Imports are only allowed in the header of the script", _current.Line, _current.Column, _current.Filename);
                Stmt st = Statement();
                stmts.Add(st);
            }

            IndexTopLevelSymbols(stmts);

            return stmts;
        }

        /// <summary>
        /// The GetImports
        /// </summary>
        /// <param name="path">The path<see cref="string"/></param>
        /// <param name="ln">The ln<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="fsname">The fsname<see cref="string"/></param>
        /// <param name="specClass">The specClass<see cref="string"/></param>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        private List<Stmt> GetImports(string path, int ln, int col, string fsname, string specClass = "")
        {
            List<Stmt> result = new();
            if (!File.Exists(path)) return result;

            string fullPath = Path.GetFullPath(path);

            if (_importStack.Contains(fullPath))
            {
                string chain = string.Join(" -> ", _importStack.Reverse().Append(fullPath));
                throw new ParserException($"Import cycle detected: {chain}", ln, col, fsname);
            }

            string h;
            try
            {
                h = ComputeSha256(fullPath);
            }
            catch (Exception ex)
            {
                throw new ParserException($"failed to hash '{fullPath}': {ex.Message}", ln, col, fsname);
            }

            if (_astByHash.TryGetValue(h, out List<Stmt>? cachedAst))
            {
                if (!string.IsNullOrWhiteSpace(specClass))
                {
                    Stmt? cls = cachedAst.FirstOrDefault(s => s is ClassDeclStmt c && c.Name == specClass);
                    if (cls is null)
                        throw new ParserException($"Could not find class '{specClass}' in import file '{path}'", ln, col, fsname);

                    result.Add(cls);
                }
                else
                {
                    result.AddRange(cachedAst);
                }

                result = FilterDuplicateTopLevel(result);
                IndexTopLevelSymbols(result);
                return result;
            }

            _importStack.Push(fullPath);
            try
            {
                using StreamReader sr = new(fullPath, detectEncodingFromByteOrderMarks: true);
                string nsrc = sr.ReadToEnd();

                Lexer lex = new(fullPath, nsrc);
                Parser prs = new(lex);

                List<Stmt> importedAst = prs.Parse();

                _astByHash[h] = importedAst;
                _importedHashes.Add(h);

                if (!string.IsNullOrWhiteSpace(specClass))
                {
                    Stmt? cls = importedAst.FirstOrDefault(s => s is ClassDeclStmt c && c.Name == specClass);
                    if (cls is null)
                        throw new ParserException($"Could not find class '{specClass}' in import file '{path}'", ln, col, fsname);
                    result.Add(cls);
                }
                else
                {
                    result.AddRange(importedAst);
                }

                result = FilterDuplicateTopLevel(result);
                IndexTopLevelSymbols(result);

                return result;
            }
            catch (ParserException)
            {
                throw;
            }
            catch (Exception pex)
            {
                throw new ParserException(pex.Message, _current.Line, _current.Column, _current.Filename);
            }
            finally
            {
                _importStack.Pop();
            }
        }

        /// <summary>
        /// The Statement
        /// </summary>
        /// <returns>The <see cref="Stmt"/></returns>
        private Stmt Statement()
        {
            return _current.Type switch
            {
                TokenType.Semi => ParseEmptyStmt,
                TokenType.Print => ParsePrint,
                TokenType.Var => ParseVarDecl,
                TokenType.Ident => ParseAssignOrIndexAssignOrPushOrExpr(),
                TokenType.LBrace => ParseBlock(),
                TokenType.If => ParseIf(),
                TokenType.While => ParseWhile(),
                TokenType.Break => ParseBreak,
                TokenType.Continue => ParseContinue,
                TokenType.For => ParseFor(),
                TokenType.Func => ParseFuncDecl(),
                TokenType.Return => ParseReturnStmt(),
                TokenType.Delete => ParseDelete,
                TokenType.Match => ParseMatch(),
                TokenType.Try => ParseTry,
                TokenType.Throw => ParseThrow,
                TokenType.Class => ParseClassDecl(),
                TokenType.Enum => ParseEnumDecl(),
                TokenType.Emit => ParseEmitStmt,

                _ => ParseExprStmt
            };
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
        /// Gets the ParseEmitStmt
        /// </summary>
        private EmitStmt ParseEmitStmt
        {
            get
            {
                object? argLine = string.Empty;
                Eat(TokenType.Emit);
                Eat(TokenType.LParen);
                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected opcode name", _current.Line, _current.Column, _current.Filename);
                int gcode = -1;
                if (Enum.TryParse<OpCode>(_current.Value.ToString() ?? "", ignoreCase: false, out OpCode cmd))
                    gcode = Convert.ToInt32(cmd);
                else
                    throw new ParserException("invalid opcode name", _current.Line, _current.Column, _current.Filename);

                Eat(TokenType.Ident);
                if (_current.Type == TokenType.Comma)
                {
                    Eat(TokenType.Comma);
                    if (_current.Type == TokenType.Number)
                        argLine = (int)_current.Value;
                    else if (_current.Type == TokenType.String)
                        argLine = _current.Value.ToString() ?? "";
                    else
                        throw new ParserException("expected number or string as opcode argument", _current.Line, _current.Column, _current.Filename);

                    Advance();
                }
                Eat(TokenType.RParen);
                Eat(TokenType.Semi);
                return new EmitStmt(gcode, argLine, _current.Line, _current.Column, _current.Filename);
            }
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

            List<EnumMemberNode> members = new();
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
            Eat(TokenType.Ident);

            List<string> ctorParams = new();
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

            string? baseName = null;
            List<Expr> baseArgs = new();
            if (_current.Type == TokenType.Colon)
            {
                Eat(TokenType.Colon);

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected base class name after ':'", line, col, _current.Filename);

                baseName = _current.Value.ToString() ?? "";
                Eat(TokenType.Ident);

                if (_current.Type == TokenType.LParen)
                {
                    Eat(TokenType.LParen);

                    if (_current.Type != TokenType.RParen)
                    {
                        do
                        {
                            Expr argExpr = Expr();
                            baseArgs.Add(argExpr);

                            if (_current.Type == TokenType.Comma)
                                Eat(TokenType.Comma);
                            else
                                break;
                        }
                        while (true);
                    }

                    Eat(TokenType.RParen);
                }
            }

            Eat(TokenType.LBrace);

            List<FuncDeclStmt> methods = new();
            Dictionary<string, Expr?> fields = new();

            List<FuncDeclStmt> staticMethods = new();
            Dictionary<string, Expr?> staticFields = new();
            List<EnumDeclStmt> staticEnums = new();

            while (_current.Type != TokenType.RBrace)
            {
                bool StaticSet = false;
                if (_current.Type == TokenType.Static)
                {
                    StaticSet = true;
                    Eat(TokenType.Static);
                }

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

                    if (StaticSet)
                        staticFields[fieldName] = init ?? new NumberExpr(0, line, col, _current.Filename);
                    else
                        fields[fieldName] = init ?? new NumberExpr(0, line, col, _current.Filename);
                }
                else if (_current.Type == TokenType.Enum)
                {
                    EnumDeclStmt enm = ParseEnumDecl();
                    staticEnums.Add(enm);
                }
                else if (_current.Type == TokenType.Func)
                {
                    FuncDeclStmt func = ParseFuncDecl();
                    if (StaticSet)
                        staticMethods.Add(func);
                    else
                        methods.Add(func);
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
                baseArgs
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

                Eat(TokenType.New);

                if (_current.Type != TokenType.Ident)
                    throw new ParserException("expected class name after 'new'", line, col, _current.Filename);

                string className = _current.Value.ToString() ?? "";
                Eat(TokenType.Ident);

                List<Expr> args = new();

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
        }

        /// <summary>
        /// The ParseExprList
        /// </summary>
        /// <returns>The <see cref="List{Expr}"/></returns>
        private List<Expr> ParseExprList()
        {
            List<Expr> list = new();

            list.Add(Expr());

            while (_current.Type == TokenType.Comma)
            {
                Eat(TokenType.Comma);
                list.Add(Expr());
            }

            return list;
        }

        /// <summary>
        /// Gets the ParseThrow
        /// </summary>
        private Stmt ParseThrow
        {
            get
            {
                int line = _current.Line;
                int col = _current.Column;
                Eat(TokenType.Throw);
                Expr ex = Expr();
                Eat(TokenType.Semi);
                return new ThrowStmt(ex, line, col, _current.Filename);
            }
        }

        /// <summary>
        /// Gets the ParseTry
        /// </summary>
        private Stmt ParseTry
        {
            get
            {
                int line = _current.Line;
                int col = _current.Column;

                Eat(TokenType.Try);
                BlockStmt tryBlock = ParseEmbeddedBlockOrSingleStatement();

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
                    catchBlock = ParseEmbeddedBlockOrSingleStatement();
                }

                if (_current.Type == TokenType.Finally)
                {
                    Eat(TokenType.Finally);
                    finallyBlock = ParseEmbeddedBlockOrSingleStatement();
                }

                return new TryCatchFinallyStmt(tryBlock, catchVar, catchBlock, finallyBlock, line, col, _current.Filename);
            }
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

            List<CaseClause> cases = new();
            BlockStmt? defaultCase = null;

            while (_current.Type == TokenType.Case || _current.Type == TokenType.Default)
            {
                if (_current.Type == TokenType.Case)
                {
                    Eat(TokenType.Case);
                    Expr pattern = Expr();
                    Eat(TokenType.Colon);
                    BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
                    cases.Add(new CaseClause(pattern, body, _current.Line, _current.Column, _current.Filename));
                }
                else if (_current.Type == TokenType.Default)
                {
                    Eat(TokenType.Default);
                    Eat(TokenType.Colon);
                    defaultCase = ParseEmbeddedBlockOrSingleStatement();
                }
            }

            Eat(TokenType.RBrace);
            return new MatchStmt(expr, cases, defaultCase, _current.Line, _current.Column, _current.Filename);
        }

        /// <summary>
        /// Gets the ParseBreak
        /// </summary>
        private Stmt ParseBreak
        {
            get
            {
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
                Eat(TokenType.Continue);
                Eat(TokenType.Semi);
                return new ContinueStmt(_current.Line, _current.Column, _current.Filename);
            }
        }

        /// <summary>
        /// Gets the ParsePrint
        /// </summary>
        private Stmt ParsePrint
        {
            get
            {
                Eat(TokenType.Print);
                Eat(TokenType.LParen);
                Expr e = Expr();
                Eat(TokenType.RParen);
                Eat(TokenType.Semi);
                return new PrintStmt(e, _current.Line, _current.Column, _current.Filename);
            }
        }

        /// <summary>
        /// Gets the ParseVarDecl
        /// </summary>
        private Stmt ParseVarDecl
        {
            get
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

                return new DeleteExprStmt(target, false, line, col, fsname);
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
            List<Stmt> stmts = new();

            Eat(TokenType.LBrace);
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
            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
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

            BlockStmt body = ParseEmbeddedBlockOrSingleStatement();
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
            while (_current.Type == TokenType.bShiftL || _current.Type == TokenType.bShiftR)
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
                node = ParseNew;
            }
            else if (_current.Type == TokenType.String)
            {
                string s = _current.Value.ToString() ?? "";
                Eat(TokenType.String);
                node = new StringExpr(s, _current.Line, _current.Column, _current.Filename);
            }
            else if (_current.Type == TokenType.Char)
            {
                char vlc;
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
                List<Expr> elems = new();
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
                node = new DictExpr(pairs, _current.Line, _current.Column, _current.Filename);
            }
            else if (_current.Type == TokenType.Func)
            {
                Eat(TokenType.Func);
                Eat(TokenType.LParen);

                List<string> parameters = new();
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
                    List<Expr> args = new();
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
            return node ?? new NullExpr(_current.Line, _current.Column, _current.Filename);
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
                TokenType op = _current.Type;
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

            List<string> parameters = new();
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
}
