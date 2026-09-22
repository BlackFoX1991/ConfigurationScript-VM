using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore.Codegen
{
    internal enum ReceiverContextKind
    {
        None,
        InstanceMethod,
        StaticMethod
    }

    internal readonly record struct LoopLeavePatch(int Index, int ScopeDepth);

    internal sealed class BytecodeEmissionContext
    {
        public int AnonymousCounter { get; set; }

        public Stack<List<LoopLeavePatch>> BreakLists { get; } = new();

        public Stack<List<LoopLeavePatch>> ContinueLists { get; } = new();

        // BreakLists.Count at the start of each function body. Function bodies are emitted inline inside
        // the enclosing code, so without this boundary a `break` in a closure would patch into the
        // enclosing loop and jump across the call frame.
        public Stack<int> FunctionLoopBase { get; } = new();

        public ClassInfo? CurrentClass { get; set; }

        public ClassDeclStmt? CurrentClassDecl { get; set; }

        public bool CurrentMethodIsStatic { get; set; }

        public ReceiverContextKind ReceiverContext { get; set; }

        public Stack<HashSet<string>> LocalVarsStack { get; } = new();

        // Function-body instruction ranges [start, end), recorded at EnterFunctionLocals/LeaveFunctionLocals.
        // The scope-descriptor finalization pass uses them to keep an (inline-emitted) function body's own
        // declarations out of the enclosing block's descriptor.
        public Stack<int> FunctionStartStack { get; } = new();

        public Dictionary<int, int> FunctionRanges { get; } = new();

        // Function-body start index → its parameter names (including any leading this/type receiver), so the
        // descriptor pass can seed each function-root descriptor with param slots the runtime places.
        public Dictionary<int, IReadOnlyList<string>> FunctionParams { get; } = new();

        // PUSH_SCOPE index → its paired POP_SCOPE index, recorded from the balanced EmitPushScope/
        // EmitPopScope calls. The descriptor pass uses this instead of depth-counting POP_SCOPE, because
        // some constructs (match arms) emit extra raw POP_SCOPEs on control-flow fail paths that would
        // otherwise mis-pair a scope by prematurely balancing the depth.
        public Stack<int> ScopePairingStack { get; } = new();

        public Dictionary<int, int> ScopePairings { get; } = new();

        // Instruction ranges [Start, End) for exception-handler code (catch / finally / using-dispose).
        // Such code is entered via the TRY_PUSH routing machinery, so at runtime the scope stack is not at
        // the handler's lexical depth (normal POP_SCOPEs are skipped on the return/throw path). Slot access
        // there would index the wrong env, so references inside these ranges stay name-based.
        public List<(int Start, int End)> NameBasedRanges { get; } = new();

        public int ScopeDepth { get; set; }

        public int AsyncFunctionDepth { get; set; }

        public string? CurrentPropertyBackingSlotName { get; set; }

        public string? CurrentPropertyBackingReceiverName { get; set; }

        public void Reset()
        {
            AnonymousCounter = 0;
            BreakLists.Clear();
            ContinueLists.Clear();
            FunctionLoopBase.Clear();
            CurrentClass = null;
            CurrentClassDecl = null;
            CurrentMethodIsStatic = false;
            ReceiverContext = ReceiverContextKind.None;
            LocalVarsStack.Clear();
            FunctionStartStack.Clear();
            FunctionRanges.Clear();
            FunctionParams.Clear();
            ScopePairingStack.Clear();
            ScopePairings.Clear();
            NameBasedRanges.Clear();
            ScopeDepth = 0;
            AsyncFunctionDepth = 0;
            CurrentPropertyBackingSlotName = null;
            CurrentPropertyBackingReceiverName = null;
        }
    }
}
