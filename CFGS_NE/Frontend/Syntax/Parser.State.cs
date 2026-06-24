using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        private readonly TokenCursor _cursor;
        private readonly List<ParserException> _diagnostics = [];

        /// <summary>
        /// Gets the current token.
        /// </summary>
        private Token _current => _cursor.Current;

        /// <summary>
        /// Gets the lookahead token.
        /// </summary>
        private Token _next => _cursor.Next;

        internal IReadOnlyList<Token> Tokens => _cursor.Tokens;

        private readonly ParserContext _context = new();
        private bool IsTolerant => _tolerant;

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

        private int MarkNodeStart()
            => _current.Index >= 0 ? _current.Index : _cursor.CurrentIndex;

        private T StampNode<T>(T node, int startTokenIndex) where T : Node
        {
            node.SyntaxStartTokenIndex = Math.Max(0, startTokenIndex);
            node.SyntaxEndTokenIndex = GetNodeEndTokenIndex(startTokenIndex);
            return node;
        }

        private int GetNodeEndTokenIndex(int fallbackStartTokenIndex)
        {
            if (_cursor.LastConsumed is not null)
                return _cursor.LastConsumed.Index;

            int candidate = _current.Index - 1;
            return candidate >= fallbackStartTokenIndex ? candidate : fallbackStartTokenIndex;
        }

        private static T CopyNodeSpan<T>(T target, Node source) where T : Node
        {
            target.SyntaxStartTokenIndex = source.SyntaxStartTokenIndex;
            target.SyntaxEndTokenIndex = source.SyntaxEndTokenIndex;
            return target;
        }
    }
}
