namespace CFGS_VM.Analytic.Core
{
    internal sealed class ParserContext
    {
        // Shared budget for every unbounded recursive-descent path (expression, block, and pattern
        // nesting). It must stay comfortably below the point at which the runtime call stack overflows,
        // because a StackOverflowException is uncatchable in .NET and hard-crashes the whole process
        // (including the language server), bypassing every try/catch. A Debug build overflows at roughly
        // 210 nested levels on the default 1 MB stack, so this leaves generous headroom while still
        // allowing far deeper nesting than any real source needs; beyond it the parser reports a clean
        // "maximum nesting depth exceeded" diagnostic instead of dying.
        public const int DefaultMaxRecursionDepth = 128;

        public int MaxRecursionDepth { get; }

        public int FunctionOrClassDepth { get; private set; }

        public int FunctionDepth { get; private set; }

        public int AsyncFunctionDepth { get; private set; }

        public int LoopDepth { get; private set; }

        public int RecursionDepth { get; private set; }

        public bool IsInMultipleVarDeclaration { get; private set; }

        public bool IsInFunctionOrClass => FunctionOrClassDepth > 0;

        public bool IsInFunction => FunctionDepth > 0;

        public bool IsInAsyncFunction => AsyncFunctionDepth > 0;

        public bool IsInLoop => LoopDepth > 0;

        public ParserContext(int maxRecursionDepth = DefaultMaxRecursionDepth)
        {
            MaxRecursionDepth = maxRecursionDepth;
        }

        public void EnterFunctionOrClass()
        {
            FunctionOrClassDepth++;
        }

        public void ExitFunctionOrClass()
        {
            FunctionOrClassDepth--;
        }

        public void EnterFunction(bool isAsync)
        {
            FunctionOrClassDepth++;
            FunctionDepth++;
            if (isAsync)
                AsyncFunctionDepth++;
        }

        public void ExitFunction(bool isAsync)
        {
            if (isAsync)
                AsyncFunctionDepth--;
            FunctionDepth--;
            FunctionOrClassDepth--;
        }

        public void EnterLoop()
        {
            LoopDepth++;
        }

        public void ExitLoop()
        {
            LoopDepth--;
        }

        /// <summary>
        /// Restores the "outside any nesting" invariant: at a top-level statement boundary every nesting
        /// depth must be zero. The tolerant (LSP) parser calls this before each top-level statement so that
        /// a depth counter leaked by a throw mid-statement (e.g. an unbalanced Enter/Exit on a malformed
        /// class body) cannot survive error recovery and skew a later statement's diagnostics. A no-op when
        /// nothing leaked. It is deliberately NOT called in the non-tolerant parse, which aborts on the
        /// first error and so cannot accumulate anything.
        /// </summary>
        public void ResetNestingDepths()
        {
            FunctionOrClassDepth = 0;
            FunctionDepth = 0;
            AsyncFunctionDepth = 0;
            LoopDepth = 0;
            RecursionDepth = 0;
        }

        public int EnterRecursion()
        {
            return ++RecursionDepth;
        }

        public void ExitRecursion()
        {
            RecursionDepth--;
        }

        public void ContinueMultipleVarDeclaration()
        {
            IsInMultipleVarDeclaration = true;
        }

        public void CompleteMultipleVarDeclaration()
        {
            IsInMultipleVarDeclaration = false;
        }

        public string AllocateDestructureParameterName()
        {
            return $"__arg_ds_{PostIncrement(ref _destructureParamCounter)}";
        }

        public string AllocateForeachPairTempName()
        {
            return $"__fe_pair_{PostIncrement(ref _foreachDestructureCounter)}";
        }

        public string AllocateForeachDestructureTempName()
        {
            return $"__fe_ds_{PostIncrement(ref _foreachDestructureCounter)}";
        }

        private int _destructureParamCounter;

        private int _foreachDestructureCounter;

        private static int PostIncrement(ref int value)
        {
            int current = value;
            value++;
            return current;
        }
    }
}
