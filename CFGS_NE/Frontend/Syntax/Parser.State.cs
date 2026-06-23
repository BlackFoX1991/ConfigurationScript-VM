using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        private readonly TokenCursor _cursor;

        /// <summary>
        /// Gets the current token.
        /// </summary>
        private Token _current => _cursor.Current;

        /// <summary>
        /// Gets the lookahead token.
        /// </summary>
        private Token _next => _cursor.Next;

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

        private void Advance()
        {
            _cursor.Advance();
        }

        private void EnterFunctionContext(bool isAsync)
        {
            _context.EnterFunction(isAsync);
        }

        private void ExitFunctionContext(bool isAsync)
        {
            _context.ExitFunction(isAsync);
        }

        private void EnterFunctionOrClassContext()
        {
            _context.EnterFunctionOrClass();
        }

        private void ExitFunctionOrClassContext()
        {
            _context.ExitFunctionOrClass();
        }

        private void EnterLoopContext()
        {
            _context.EnterLoop();
        }

        private void ExitLoopContext()
        {
            _context.ExitLoop();
        }

        private void EnterOutBlockContext()
        {
            _context.EnterOutBlock();
        }

        private void ExitOutBlockContext()
        {
            _context.ExitOutBlock();
        }

        private int EnterExpressionRecursion()
        {
            return _context.EnterRecursion();
        }

        private void ExitExpressionRecursion()
        {
            _context.ExitRecursion();
        }

        private bool Match(TokenType type)
        {
            return _cursor.Match(type);
        }

        private Token Expect(TokenType type, string? message = null)
        {
            return _cursor.Expect(type, message);
        }

        private ParserException ErrorAtCurrent(string message)
            => ErrorAt(message, _current);

        private static ParserException ErrorAt(string message, Token token)
            => new(message, token.Line, token.Column, token.Filename, token.Length);

        private void Eat(TokenType type)
        {
            _cursor.Eat(type);
        }
    }
}
