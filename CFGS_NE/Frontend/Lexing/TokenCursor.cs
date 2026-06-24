using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;

namespace CFGS_VM.Analytic.Core
{
    /// <summary>
    /// Wraps sequential token consumption for the parser while preserving one-token lookahead.
    /// </summary>
    internal sealed class TokenCursor
    {
        private readonly IReadOnlyList<Token> _tokens;
        private int _index;

        /// <summary>
        /// Gets the current token under the parser cursor.
        /// </summary>
        public Token Current { get; private set; }

        /// <summary>
        /// Gets the next token used for one-token lookahead.
        /// </summary>
        public Token Next { get; private set; }

        /// <summary>
        /// Gets the last token that was consumed by the cursor.
        /// </summary>
        public Token? LastConsumed { get; private set; }

        /// <summary>
        /// Gets the full token stream.
        /// </summary>
        public IReadOnlyList<Token> Tokens => _tokens;

        /// <summary>
        /// Gets the current token index within the token stream.
        /// </summary>
        public int CurrentIndex => _index;

        /// <summary>
        /// Initializes a new instance of the <see cref="TokenCursor"/> class.
        /// </summary>
        /// <param name="lexer">The source lexer.</param>
        public TokenCursor(Lexer lexer)
        {
            ArgumentNullException.ThrowIfNull(lexer);

            List<Token> tokens = [];
            while (true)
            {
                Token token = lexer.GetNextToken();
                tokens.Add(token);
                if (token.Type == TokenType.EOF)
                    break;
            }

            _tokens = tokens;
            _index = 0;
            Current = _tokens[0];
            Next = _tokens.Count > 1 ? _tokens[1] : _tokens[0];
        }

        /// <summary>
        /// Advances the cursor by one token while preserving the lookahead token.
        /// </summary>
        public void Advance()
        {
            LastConsumed = Current;
            if (_index < _tokens.Count - 1)
                _index++;

            Current = _tokens[_index];
            Next = _index + 1 < _tokens.Count ? _tokens[_index + 1] : _tokens[^1];
        }

        /// <summary>
        /// Consumes the current token if it matches the supplied type.
        /// </summary>
        /// <param name="type">The token type to match.</param>
        /// <returns><see langword="true"/> when the token matched and was consumed; otherwise <see langword="false"/>.</returns>
        public bool Match(TokenType type)
        {
            if (Current.Type != type)
                return false;

            Advance();
            return true;
        }

        /// <summary>
        /// Consumes and returns the current token when it matches the expected type.
        /// </summary>
        /// <param name="type">The expected token type.</param>
        /// <param name="message">An optional parser error message.</param>
        /// <returns>The consumed token.</returns>
        public Token Expect(TokenType type, string? message = null)
        {
            Token token = Current;
            if (token.Type != type)
            {
                throw new ParserException(
                    message ?? $"Expected {type}, got {token.Type} -> '{token.Value}'",
                    token.Line,
                    token.Column,
                    token.Filename,
                    token.Length);
            }

            Advance();
            return token;
        }

        /// <summary>
        /// Consumes the current token when it matches the expected type.
        /// </summary>
        /// <param name="type">The expected token type.</param>
        public void Eat(TokenType type)
        {
            Expect(type);
        }
    }
}
