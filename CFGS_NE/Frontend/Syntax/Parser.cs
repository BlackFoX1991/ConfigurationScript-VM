using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    public sealed record ParserParseResult(List<Stmt> Statements, IReadOnlyList<ParserException> Diagnostics);

    public partial class Parser
    {
        public enum TopLevelMode
        {
            Script,
            Module
        }

        private readonly Lexer _lexer;
        private readonly TopLevelMode _topLevelMode;
        private readonly bool _tolerant;

        /// <summary>
        /// Initializes a new instance of the <see cref="Parser"/> class.
        /// </summary>
        /// <param name="lexer">The lexer<see cref="Lexer"/></param>
        public Parser(Lexer lexer, TopLevelMode topLevelMode = TopLevelMode.Script, bool tolerant = false)
        {
            _lexer = lexer;
            _topLevelMode = topLevelMode;
            _tolerant = tolerant;
            _cursor = new TokenCursor(_lexer);
        }

        private static bool IsMemberNameToken(TokenType type)
        {
            return type switch
            {
                TokenType.Ident or
                TokenType.Print or
                TokenType.Var or
                TokenType.Const or
                TokenType.Static or
                TokenType.Property or
                TokenType.Public or
                TokenType.Private or
                TokenType.Protected or
                TokenType.If or
                TokenType.Else or
                TokenType.While or
                TokenType.Do or
                TokenType.For or
                TokenType.ForEach or
                TokenType.Break or
                TokenType.Continue or
                TokenType.Delete or
                TokenType.Match or
                TokenType.Case or
                TokenType.Default or
                TokenType.In or
                TokenType.Is or
                TokenType.Null or
                TokenType.True or
                TokenType.False or
                TokenType.Try or
                TokenType.Catch or
                TokenType.Finally or
                TokenType.Using or
                TokenType.Defer or
                TokenType.Throw or
                TokenType.Class or
                TokenType.Interface or
                TokenType.Enum or
                TokenType.Import or
                TokenType.Use or
                TokenType.Export or
                TokenType.Namespace or
                TokenType.From or
                TokenType.As or
                TokenType.New or
                TokenType.Func or
                TokenType.Return or
                TokenType.Async or
                TokenType.Yield or
                TokenType.Await => true,
                _ => false
            };
        }

        private static string MemberNameFromToken(Token token)
        {
            return token.Value?.ToString() ?? "";
        }

        public List<Stmt> Parse()
        {
            List<Stmt> stmts = new();

            ParseImportHeader(stmts);
            ParseUseHeader(stmts);
            ResetKnownTopLevelStateFromSeenSymbols();

            if (_current.Type == TokenType.Namespace)
            {
                NamespaceDeclStmt firstNamespace = ParseNamespaceDeclStatement(allowFileScoped: true);
                stmts.Add(firstNamespace);
                TrackKnownTopLevelSymbols(new[] { firstNamespace });

                if (firstNamespace.IsFileScoped)
                {
                    ValidateTopLevelSymbolUniqueness(stmts);
                    IndexTopLevelSymbols(stmts);
                    return stmts;
                }
            }

            while (_current.Type != TokenType.EOF)
            {
                if (_current.Type == TokenType.Import)
                {
                    throw new ParserException(
                        "Invalid import statement. Imports are only allowed in the header of the script",
                        _current.Line,
                        _current.Column,
                        _current.Filename);
                }

                if (_current.Type == TokenType.Use)
                {
                    throw new ParserException(
                        "Invalid use statement. Use directives are only allowed in the header of the script",
                        _current.Line,
                        _current.Column,
                        _current.Filename);
                }

                if (_current.Type == TokenType.Namespace)
                {
                    NamespaceDeclStmt nsStmt = ParseNamespaceDeclStatement(allowFileScoped: false);
                    stmts.Add(nsStmt);
                    TrackKnownTopLevelSymbols(new[] { nsStmt });
                    continue;
                }

                Stmt stmt = Statement();
                stmts.Add(stmt);
                TrackKnownTopLevelSymbols(new[] { stmt });
            }

            ValidateTopLevelSymbolUniqueness(stmts);
            IndexTopLevelSymbols(stmts);

            return stmts;
        }

        public ParserParseResult ParseTolerant()
        {
            List<Stmt> stmts = new();

            ParseImportHeaderTolerant(stmts);
            ParseUseHeaderTolerant(stmts);
            ResetKnownTopLevelStateFromSeenSymbols();

            if (_current.Type == TokenType.Namespace)
            {
                TryParseTopLevelStatement(stmts, allowFileScopedNamespace: true);

                if (stmts.LastOrDefault() is NamespaceDeclStmt { IsFileScoped: true })
                {
                    FinalizeTopLevelStateTolerant(stmts);
                    return new ParserParseResult(stmts, _diagnostics);
                }
            }

            while (_current.Type != TokenType.EOF)
                TryParseTopLevelStatement(stmts, allowFileScopedNamespace: false);

            FinalizeTopLevelStateTolerant(stmts);
            return new ParserParseResult(stmts, _diagnostics);
        }
    }
}
