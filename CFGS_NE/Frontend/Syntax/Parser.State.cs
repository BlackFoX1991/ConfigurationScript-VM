using CFGS_VM.Analytic.Tokens;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        /// <summary>
        /// Defines the token cursor.
        /// </summary>
        private readonly TokenCursor _cursor;

        /// <summary>
        /// Gets the current token.
        /// </summary>
        private Token _current => _cursor.Current;

        /// <summary>
        /// Gets the lookahead token.
        /// </summary>
        private Token _next => _cursor.Next;

        /// <summary>
        /// Defines the parser context.
        /// </summary>
        private readonly ParserContext _context = new();

        /// <summary>
        /// Gets a value indicating whether IsInFunctionOrClass
        /// </summary>
        private bool IsInFunctionOrClass => _context.IsInFunctionOrClass;

        /// <summary>
        /// Gets a value indicating whether IsInFunction
        /// </summary>
        private bool IsInFunction => _context.IsInFunction;

        /// <summary>
        /// Gets a value indicating whether IsInAsyncFunction
        /// </summary>
        private bool IsInAsyncFunction => _context.IsInAsyncFunction;

        /// <summary>
        /// Gets a value indicating whether IsInLoop
        /// </summary>
        private bool IsInLoop => _context.IsInLoop;

        /// <summary>
        /// Gets a value indicating whether IsInOutBlock
        /// </summary>
        private bool IsInOutBlock => _context.IsInOutBlock;

        private int MaxRecursionDepth => _context.MaxRecursionDepth;

        /// <summary>
        /// The Advance
        /// </summary>
        private void Advance()
        {
            _cursor.Advance();
        }

        /// <summary>
        /// The EnterFunctionContext
        /// </summary>
        /// <param name="isAsync">The isAsync<see cref="bool"/></param>
        private void EnterFunctionContext(bool isAsync)
        {
            _context.EnterFunction(isAsync);
        }

        /// <summary>
        /// The ExitFunctionContext
        /// </summary>
        /// <param name="isAsync">The isAsync<see cref="bool"/></param>
        private void ExitFunctionContext(bool isAsync)
        {
            _context.ExitFunction(isAsync);
        }

        /// <summary>
        /// The EnterFunctionOrClassContext
        /// </summary>
        private void EnterFunctionOrClassContext()
        {
            _context.EnterFunctionOrClass();
        }

        /// <summary>
        /// The ExitFunctionOrClassContext
        /// </summary>
        private void ExitFunctionOrClassContext()
        {
            _context.ExitFunctionOrClass();
        }

        /// <summary>
        /// The EnterLoopContext
        /// </summary>
        private void EnterLoopContext()
        {
            _context.EnterLoop();
        }

        /// <summary>
        /// The ExitLoopContext
        /// </summary>
        private void ExitLoopContext()
        {
            _context.ExitLoop();
        }

        /// <summary>
        /// The EnterOutBlockContext
        /// </summary>
        private void EnterOutBlockContext()
        {
            _context.EnterOutBlock();
        }

        /// <summary>
        /// The ExitOutBlockContext
        /// </summary>
        private void ExitOutBlockContext()
        {
            _context.ExitOutBlock();
        }

        /// <summary>
        /// The EnterExpressionRecursion
        /// </summary>
        /// <returns>The <see cref="int"/></returns>
        private int EnterExpressionRecursion()
        {
            return _context.EnterRecursion();
        }

        /// <summary>
        /// The ExitExpressionRecursion
        /// </summary>
        private void ExitExpressionRecursion()
        {
            _context.ExitRecursion();
        }

        /// <summary>
        /// The Match
        /// </summary>
        /// <param name="type">The type<see cref="TokenType"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool Match(TokenType type)
        {
            return _cursor.Match(type);
        }

        /// <summary>
        /// The Expect
        /// </summary>
        /// <param name="type">The type<see cref="TokenType"/></param>
        /// <param name="message">The message<see cref="string?"/></param>
        /// <returns>The <see cref="Token"/></returns>
        private Token Expect(TokenType type, string? message = null)
        {
            return _cursor.Expect(type, message);
        }

        /// <summary>
        /// The Eat
        /// </summary>
        /// <param name="type">The type<see cref="TokenType"/></param>
        private void Eat(TokenType type)
        {
            _cursor.Eat(type);
        }
    }
}
