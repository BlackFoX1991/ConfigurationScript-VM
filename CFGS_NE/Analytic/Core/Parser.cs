using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Modules;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using System.Text;

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
        /// Defines the multipleVarDecl
        /// </summary>
        private bool multipleVarDecl = false;

        /// <summary>
        /// Defines the _destructureParamCounter
        /// </summary>
        private int _destructureParamCounter = 0;

        /// <summary>
        /// Defines the _foreachDestructureCounter
        /// </summary>
        private int _foreachDestructureCounter = 0;

        /// <summary>
        /// Gets a value indicating whether IsInOutBlock
        /// </summary>
        private bool IsInOutBlock => _context.IsInOutBlock;

        private int MaxRecursionDepth => _context.MaxRecursionDepth;

        /// <summary>
        /// Defines the import resolver.
        /// </summary>
        private readonly ImportResolver _importResolver;

        /// <summary>
        /// Initializes a new instance of the <see cref="Parser"/> class.
        /// </summary>
        /// <param name="lexer">The lexer<see cref="Lexer"/></param>
        /// <param name="loadPluginDll">The loadPluginDll<see cref="Action{string}?"/></param>
        /// <param name="sharedAstByHash">Optional import cache (legacy name) shared across nested parser instances.</param>
        /// <param name="sharedImportStack">Optional import stack shared across nested parser instances.</param>
        public Parser(
            Lexer lexer,
            Action<string>? loadPluginDll = null,
            Dictionary<string, List<Stmt>>? sharedAstByHash = null,
            Stack<string>? sharedImportStack = null,
            Func<string, string?>? loadImportSource = null)
            : this(
                lexer,
                new ImportResolver(
                    loadPluginDll,
                    loadImportSource,
                    sharedAstByHash,
                    sharedImportStack))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Parser"/> class with a shared import resolver.
        /// </summary>
        /// <param name="lexer">The lexer<see cref="Lexer"/></param>
        /// <param name="importResolver">The importResolver<see cref="ImportResolver"/></param>
        internal Parser(Lexer lexer, ImportResolver importResolver)
        {
            _lexer = lexer;
            _cursor = new TokenCursor(_lexer);
            _importResolver = importResolver;
        }

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
        /// The Eat
        /// </summary>
        /// <param name="type">The type<see cref="TokenType"/></param>
        private void Eat(TokenType type)
        {
            _cursor.Eat(type);
        }

        /// <summary>
        /// The IsReservedBindingName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReservedBindingName(string name)
        {
            if (name.StartsWith("__", StringComparison.Ordinal))
                return true;

            return name == "this" || name == "type" || name == "super" || name == "outer";
        }

        /// <summary>
        /// The ThrowIfInvalidParameterName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        private static void ThrowIfInvalidParameterName(string name, int line, int col, string file)
        {
            if (Lexer.Keywords.ContainsKey(name) || IsReservedBindingName(name))
                throw new ParserException($"invalid parameter name '{name}'", line, col, file);
        }

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
        /// Defines the _seenTopLevelSymbols
        /// </summary>
        private readonly Dictionary<string, (string Kind, string Origin)> _seenTopLevelSymbols = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines known top-level symbol names while parsing the current script.
        /// </summary>
        private readonly HashSet<string> _knownTopLevelNames = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines known top-level kinds while parsing the current script.
        /// </summary>
        private readonly Dictionary<string, string> _knownTopLevelKinds = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines namespace roots introduced by namespace declarations in the current script.
        /// </summary>
        private readonly HashSet<string> _knownNamespaceRoots = new(StringComparer.Ordinal);
    }
}
