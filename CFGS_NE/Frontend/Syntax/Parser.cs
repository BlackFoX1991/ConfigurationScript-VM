using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    /// <summary>
    /// Defines the <see cref="Parser" />
    /// </summary>
    public partial class Parser
    {
        /// <summary>
        /// Defines the _lexer
        /// </summary>
        private readonly Lexer _lexer;

        /// <summary>
        /// Initializes a new instance of the <see cref="Parser"/> class.
        /// </summary>
        /// <param name="lexer">The lexer<see cref="Lexer"/></param>
        public Parser(Lexer lexer)
        {
            _lexer = lexer;
            _cursor = new TokenCursor(_lexer);
        }

        /// <summary>
        /// The Parse
        /// </summary>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        public List<Stmt> Parse()
        {
            List<Stmt> stmts = new();

            ParseImportHeader(stmts);
            ResetKnownTopLevelStateFromSeenSymbols();

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

                if (_current.Type == TokenType.Namespace)
                {
                    NamespaceDeclStmt nsStmt = ParseNamespaceDeclStatement();
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
    }
}
