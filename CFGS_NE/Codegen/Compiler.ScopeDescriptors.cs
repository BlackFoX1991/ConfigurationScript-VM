using System.Collections.Generic;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Command;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// After all bytecode is emitted, attaches a <see cref="ScopeDescriptor"/> to every block-scope
        /// <c>PUSH_SCOPE</c> instruction: the names that scope declares directly, mapped to slot indices.
        /// The runtime uses it to pre-size the scope's slot storage. Access stays name-based in this stage,
        /// so a descriptor is a pure optimization hint — an inaccurate one only forgoes pre-sizing.
        ///
        /// Reads the emitted instructions as ground truth (so compiler-synthetic temps are covered) and
        /// uses the recorded function-body ranges to keep an inline-emitted function's own declarations out
        /// of the enclosing block's descriptor. Function-root descriptors are a later stage; here the
        /// global and function-root levels are walked only to reach the block scopes nested inside them.
        /// </summary>
        internal void FinalizeScopeDescriptors()
        {
            List<Instruction> insns = _insns;
            DescribeScope(0, insns.Count, _functionRanges, _functionParams, _context.RootScopeDescriptors, _scopePairings, insns, builder: null);

            // Second pass: rewrite name-based local reads/assignments to slot access where the reference
            // provably binds within the current function's own scope chain.
            _nameBasedIndices = new HashSet<int>();
            foreach ((int rangeStart, int rangeEnd) in _nameBasedRanges)
                for (int k = rangeStart; k < rangeEnd; k++)
                    _nameBasedIndices.Add(k);

            ResolveSlotAccess(0, insns.Count, new List<SlotFrame>());
        }

        // Instruction indices whose LOAD_VAR/STORE_VAR must stay name-based (exception-handler code).
        private HashSet<int> _nameBasedIndices = new();

        // A compile-time mirror of one runtime Env: the scope's slot descriptor, whether it is a function
        // root (the hop walk stops there — captures/globals stay name-based), and the names declared in it
        // so far at the current instruction (so a temporal-dead-zone reference resolves to an outer scope).
        private sealed class SlotFrame
        {
            public SlotFrame(ScopeDescriptor descriptor, bool isFunctionRoot, HashSet<string> declaredSoFar)
            {
                Descriptor = descriptor;
                IsFunctionRoot = isFunctionRoot;
                DeclaredSoFar = declaredSoFar;
            }

            public ScopeDescriptor Descriptor { get; }

            public bool IsFunctionRoot { get; }

            public HashSet<string> DeclaredSoFar { get; }
        }

        private void ResolveSlotAccess(int start, int end, List<SlotFrame> stack)
        {
            int j = start;
            while (j < end)
            {
                if (j != start && _functionRanges.TryGetValue(j, out int functionEnd))
                {
                    ScopeDescriptor rootDescriptor = _context.RootScopeDescriptors.TryGetValue(j, out ScopeDescriptor? rd)
                        ? rd
                        : ScopeDescriptor.Empty;

                    // Params are bound on entry (before the body runs), so they are "declared so far" from
                    // the start; a leading this/type receiver is not a slot (kept per-frame at runtime).
                    HashSet<string> declared = new(StringComparer.Ordinal);
                    if (_functionParams.TryGetValue(j, out IReadOnlyList<string>? pars))
                    {
                        int piStart = pars.Count > 0 && (pars[0] == "this" || pars[0] == "type") ? 1 : 0;
                        for (int p = piStart; p < pars.Count; p++)
                            declared.Add(pars[p]);
                    }

                    stack.Add(new SlotFrame(rootDescriptor, isFunctionRoot: true, declared));
                    ResolveSlotAccess(j, functionEnd, stack);
                    stack.RemoveAt(stack.Count - 1);
                    j = functionEnd;
                    continue;
                }

                Instruction ins = _insns[j];
                switch (ins.Code)
                {
                    case OpCode.PUSH_SCOPE:
                    {
                        int popIdx = ScopeEnd(j, end, _scopePairings, _insns);
                        ScopeDescriptor blockDescriptor = ins.Operand as ScopeDescriptor ?? ScopeDescriptor.Empty;
                        stack.Add(new SlotFrame(blockDescriptor, isFunctionRoot: false, new HashSet<string>(StringComparer.Ordinal)));
                        ResolveSlotAccess(j + 1, popIdx, stack);
                        stack.RemoveAt(stack.Count - 1);
                        j = popIdx + 1;
                        continue;
                    }

                    case OpCode.VAR_DECL:
                    case OpCode.CONST_DECL:
                    {
                        if (stack.Count > 0)
                        {
                            SlotFrame top = stack[^1];
                            string declName = (string)ins.Operand!;

                            // Rewrite to a slot declaration when this scope has a descriptor slot for the
                            // name and it is not in a name-based handler region. The declaration runs in
                            // _scopes[^1] at runtime, which is exactly this innermost frame's scope.
                            if (!_nameBasedIndices.Contains(j)
                                && top.Descriptor.NameIndex.TryGetValue(declName, out int declIdx)
                                && declIdx <= 0xFFFF)
                            {
                                OpCode slotDecl = ins.Code == OpCode.VAR_DECL ? OpCode.VAR_DECL_SLOT : OpCode.CONST_DECL_SLOT;
                                _insns[j] = new Instruction(slotDecl, declIdx, ins.Line, ins.Col, ins.OriginFile);
                            }

                            // The declaration becomes visible to slot resolution only from here on (TDZ).
                            top.DeclaredSoFar.Add(declName);
                        }
                        break;
                    }

                    case OpCode.LOAD_VAR:
                    case OpCode.STORE_VAR:
                    {
                        string name = (string)ins.Operand!;
                        if (!_nameBasedIndices.Contains(j)
                            && !IsReceiverKeyword(name)
                            && TryResolveSlot(stack, name, out int hops, out int idx)
                            && hops <= 0xFFFF && idx <= 0xFFFF)
                        {
                            OpCode slotOp = ins.Code == OpCode.LOAD_VAR ? OpCode.LOAD_SLOT : OpCode.STORE_SLOT;
                            _insns[j] = new Instruction(slotOp, (hops << 16) | idx, ins.Line, ins.Col, ins.OriginFile);
                        }
                        break;
                    }
                }

                j++;
            }
        }

        private static bool IsReceiverKeyword(string name)
            => name is "this" or "type" or "super" or "outer";

        // Walks the compile-time scope stack from innermost outward, counting one hop per scope (== one
        // runtime Env parent link). Resolves to the first scope that has already declared the name, stopping
        // at the enclosing function boundary so cross-function captures stay name-based.
        private static bool TryResolveSlot(List<SlotFrame> stack, string name, out int hops, out int idx)
        {
            hops = 0;
            for (int f = stack.Count - 1; f >= 0; f--)
            {
                SlotFrame frame = stack[f];
                if (frame.DeclaredSoFar.Contains(name) && frame.Descriptor.NameIndex.TryGetValue(name, out idx))
                    return true;

                hops++;
                if (frame.IsFunctionRoot)
                    break;
            }

            idx = 0;
            return false;
        }

        // Walks [start, end) at ONE scope level. Collects the VAR_DECL/CONST_DECL names declared directly in
        // this scope into <paramref name="builder"/> (null for scopes whose descriptor this stage discards),
        // and recurses into nested block scopes (assigning their descriptors) and inline function bodies
        // (assigning their nested block descriptors), skipping both so their declarations do not leak here.
        private static void DescribeScope(
            int start,
            int end,
            Dictionary<int, int> functionRanges,
            Dictionary<int, IReadOnlyList<string>> functionParams,
            Dictionary<int, ScopeDescriptor> rootDescriptors,
            Dictionary<int, int> scopePairings,
            List<Instruction> insns,
            ScopeDescriptorBuilder? builder)
        {
            int j = start;
            while (j < end)
            {
                // A function body begins at j (but not when j is this scope's own start — that entry is the
                // function whose body we are currently describing, detected by the enclosing scope).
                if (j != start && functionRanges.TryGetValue(j, out int functionEnd))
                {
                    // Build the function-root descriptor: param slots (minus a leading this/type receiver,
                    // which the runtime keeps per-frame, not in a slot) followed by the body's top-level
                    // declarations, in the same order the call scope defines them.
                    ScopeDescriptorBuilder root = new();
                    if (functionParams.TryGetValue(j, out IReadOnlyList<string>? pars))
                    {
                        int piStart = pars.Count > 0 && (pars[0] == "this" || pars[0] == "type") ? 1 : 0;
                        for (int p = piStart; p < pars.Count; p++)
                            root.Declare(pars[p]);
                    }

                    DescribeScope(j, functionEnd, functionRanges, functionParams, rootDescriptors, scopePairings, insns, root);
                    rootDescriptors[j] = root.Build();
                    j = functionEnd;
                    continue;
                }

                Instruction ins = insns[j];
                if (ins.Code == OpCode.PUSH_SCOPE)
                {
                    int popIdx = ScopeEnd(j, end, scopePairings, insns);
                    ScopeDescriptorBuilder child = new();
                    DescribeScope(j + 1, popIdx, functionRanges, functionParams, rootDescriptors, scopePairings, insns, child);
                    insns[j] = new Instruction(OpCode.PUSH_SCOPE, child.Build(), ins.Line, ins.Col, ins.OriginFile);
                    j = popIdx + 1;
                    continue;
                }

                if (ins.Code == OpCode.VAR_DECL || ins.Code == OpCode.CONST_DECL)
                    builder?.Declare((string)ins.Operand!);

                j++;
            }
        }

        // The end (paired POP_SCOPE index) of the scope opened at <paramref name="pushIdx"/>. Prefers the
        // pairing recorded during emission (correct even when a construct emits extra raw POP_SCOPEs on
        // control-flow fail paths), falling back to a depth scan for any unpaired scope.
        private static int ScopeEnd(int pushIdx, int end, Dictionary<int, int> scopePairings, List<Instruction> insns)
            => scopePairings.TryGetValue(pushIdx, out int popIdx) ? popIdx : FindMatchingPop(pushIdx, end, insns);

        // Returns the index of the POP_SCOPE that closes the PUSH_SCOPE at <paramref name="pushIdx"/> by
        // depth scan. Fallback for scopes without a recorded pairing.
        private static int FindMatchingPop(int pushIdx, int end, List<Instruction> insns)
        {
            int depth = 0;
            for (int j = pushIdx; j < end; j++)
            {
                OpCode code = insns[j].Code;
                if (code == OpCode.PUSH_SCOPE)
                {
                    depth++;
                }
                else if (code == OpCode.POP_SCOPE)
                {
                    depth--;
                    if (depth == 0)
                        return j;
                }
            }

            // Lexically unbalanced (should not happen): treat the rest of the range as the scope body.
            return end;
        }
    }
}
