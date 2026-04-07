using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;

namespace CFGS_VM.Analytic.Core
{
    /// <summary>
    /// Wraps sequential token consumption for the parser while preserving one-token lookahead.
    /// </summary>
    internal sealed class TokenCursor
    {
        private readonly Lexer _lexer;

        /// <summary>
        /// Gets the current token under the parser cursor.
        /// </summary>
        public Token Current { get; private set; }

        /// <summary>
        /// Gets the next token used for one-token lookahead.
        /// </summary>
        public Token Next { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TokenCursor"/> class.
        /// </summary>
        /// <param name="lexer">The source lexer.</param>
        public TokenCursor(Lexer lexer)
        {
            _lexer = lexer ?? throw new ArgumentNullException(nameof(lexer));
            Current = _lexer.GetNextToken();
            Next = _lexer.GetNextToken();
        }

        /// <summary>
        /// Advances the cursor by one token while preserving the lookahead token.
        /// </summary>
        public void Advance()
        {
            Current = Next;
            Next = _lexer.GetNextToken();
        }

        /// <summary>
        /// Consumes the current token when it matches the expected type.
        /// </summary>
        /// <param name="type">The expected token type.</param>
        public void Eat(TokenType type)
        {
            if (Current.Type != type)
            {
                throw new ParserException(
                    $"Expected {type}, got {Current.Type} -> '{Current.Value}'",
                    Current.Line,
                    Current.Column,
                    Current.Filename);
            }

            Advance();
        }
    }
}
