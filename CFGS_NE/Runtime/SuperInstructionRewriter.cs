using CFGS_VM.VMCore.Command;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Peephole pass that fuses common adjacent bytecode runs into single superinstructions, cutting the
    /// per-opcode dispatch overhead on the hottest loops (measured on the loop/fibonacci micro-benchmarks).
    ///
    /// The pass OVERLAYS the first instruction of a run with a fused opcode and leaves the following
    /// instructions untouched: at runtime the fused handler reads the folded operands from those trailing
    /// slots and advances the instruction pointer past them. Because the instruction list keeps the same
    /// length and every instruction keeps its index, all absolute jump targets, try-handler ranges,
    /// scope pairings and address-keyed maps remain valid without any fixup — and the folded tail
    /// instructions stay in place as a correct fallback should control ever land on them directly.
    ///
    /// Runs at load time (not at serialization time) so the .cfb format is unaffected and both freshly
    /// compiled and .cfb-loaded programs benefit. The pass is idempotent (a fused head is no longer a raw
    /// fusion head, so a second run is a no-op).
    /// </summary>
    internal static class SuperInstructionRewriter
    {
        public static void Rewrite(List<Instruction> program)
        {
            int n = program.Count;
            for (int i = 0; i < n; i++)
            {
                OpCode head = program[i].Code;

                if (head == OpCode.LOAD_SLOT)
                {
                    // Longest first: LOAD_SLOT x, PUSH_INT k, (ADD|SUB), STORE_SLOT x (same x) -> x +/-= k.
                    if (i + 3 < n
                        && program[i + 1].Code == OpCode.PUSH_INT
                        && (program[i + 2].Code == OpCode.ADD || program[i + 2].Code == OpCode.SUB)
                        && program[i + 3].Code == OpCode.STORE_SLOT
                        && Equals(program[i + 3].Operand, program[i].Operand))
                    {
                        program[i] = Retag(program[i], program[i + 2].Code == OpCode.ADD ? OpCode.INC_SLOT : OpCode.DEC_SLOT);
                        i += 3;
                        continue;
                    }

                    // LOAD_SLOT, PUSH_INT, (ADD|SUB) -> local +/- integer constant.
                    if (i + 2 < n && program[i + 1].Code == OpCode.PUSH_INT)
                    {
                        OpCode third = program[i + 2].Code;
                        if (third == OpCode.ADD)
                        {
                            program[i] = Retag(program[i], OpCode.SLOT_ADD_INT);
                            i += 2;
                            continue;
                        }

                        if (third == OpCode.SUB)
                        {
                            program[i] = Retag(program[i], OpCode.SLOT_SUB_INT);
                            i += 2;
                            continue;
                        }
                    }

                    // LOAD_SLOT, LOAD_SLOT -> push two locals in one dispatch.
                    if (i + 1 < n && program[i + 1].Code == OpCode.LOAD_SLOT)
                    {
                        program[i] = Retag(program[i], OpCode.LOAD_SLOT2);
                        i += 1;
                        continue;
                    }
                }

                // (ADD|SUB), STORE_SLOT -> combine + store to a local (accumulator).
                if ((head == OpCode.ADD || head == OpCode.SUB)
                    && i + 1 < n
                    && program[i + 1].Code == OpCode.STORE_SLOT)
                {
                    program[i] = Retag(program[i], head == OpCode.ADD ? OpCode.ADD_STORE_SLOT : OpCode.SUB_STORE_SLOT);
                    i += 1;
                    continue;
                }

                // <cmp>, JMP_IF_FALSE -> compare and branch-on-false in one dispatch.
                if (i + 1 < n && program[i + 1].Code == OpCode.JMP_IF_FALSE)
                {
                    OpCode fused = head switch
                    {
                        OpCode.LT => OpCode.LT_JF,
                        OpCode.LE => OpCode.LE_JF,
                        OpCode.GT => OpCode.GT_JF,
                        OpCode.GE => OpCode.GE_JF,
                        OpCode.EQ => OpCode.EQ_JF,
                        OpCode.NEQ => OpCode.NEQ_JF,
                        _ => head,
                    };

                    if (fused != head)
                    {
                        program[i] = Retag(program[i], fused);
                        i += 1;
                        continue;
                    }
                }
            }
        }

        /// <summary>Returns a copy of <paramref name="original"/> with a new opcode, preserving operand and source location.</summary>
        private static Instruction Retag(Instruction original, OpCode code)
            => new Instruction(code, original.Operand, original.Line, original.Col, original.OriginFile);
    }
}
