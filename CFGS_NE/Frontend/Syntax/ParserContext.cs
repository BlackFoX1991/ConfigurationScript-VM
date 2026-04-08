namespace CFGS_VM.Analytic.Core
{
    internal sealed class ParserContext
    {
        public const int DefaultMaxRecursionDepth = 256;

        public int MaxRecursionDepth { get; }

        public int FunctionOrClassDepth { get; private set; }

        public int FunctionDepth { get; private set; }

        public int AsyncFunctionDepth { get; private set; }

        public int LoopDepth { get; private set; }

        public int OutBlockDepth { get; private set; }

        public int RecursionDepth { get; private set; }

        public bool IsInMultipleVarDeclaration { get; private set; }

        public bool IsInFunctionOrClass => FunctionOrClassDepth > 0;

        public bool IsInFunction => FunctionDepth > 0;

        public bool IsInAsyncFunction => AsyncFunctionDepth > 0;

        public bool IsInLoop => LoopDepth > 0;

        public bool IsInOutBlock => OutBlockDepth > 0;

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

        public void EnterOutBlock()
        {
            OutBlockDepth++;
        }

        public void ExitOutBlock()
        {
            OutBlockDepth--;
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
