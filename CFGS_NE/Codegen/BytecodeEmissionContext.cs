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

        public ClassInfo? CurrentClass { get; set; }

        public ClassDeclStmt? CurrentClassDecl { get; set; }

        public bool CurrentMethodIsStatic { get; set; }

        public ReceiverContextKind ReceiverContext { get; set; }

        public Stack<HashSet<string>> LocalVarsStack { get; } = new();

        public int ScopeDepth { get; set; }

        public int AsyncFunctionDepth { get; set; }

        public string? CurrentPropertyBackingSlotName { get; set; }

        public string? CurrentPropertyBackingReceiverName { get; set; }

        public void Reset()
        {
            AnonymousCounter = 0;
            BreakLists.Clear();
            ContinueLists.Clear();
            CurrentClass = null;
            CurrentClassDecl = null;
            CurrentMethodIsStatic = false;
            ReceiverContext = ReceiverContextKind.None;
            LocalVarsStack.Clear();
            ScopeDepth = 0;
            AsyncFunctionDepth = 0;
            CurrentPropertyBackingSlotName = null;
            CurrentPropertyBackingReceiverName = null;
        }
    }
}
