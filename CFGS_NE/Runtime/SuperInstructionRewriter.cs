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
    /// compiled and .cfb-loaded programs benefit. The pass is idempotent: a fused head is no longer a raw
    /// fusion head, and the loop skips the folded tail that head owns (see FoldedTailLength), so a second
    /// run cannot re-fuse part of an existing fold.
    /// </summary>
    internal static class SuperInstructionRewriter
    {
        public static void Rewrite(List<Instruction> program)
        {
            int n = program.Count;
            for (int i = 0; i < n; i++)
            {
                OpCode head = program[i].Code;

                // A head fused by an earlier run owns the instructions behind it, so skip them. Without
                // this the pass was NOT idempotent despite the claim above: the first run stepped over a
                // fold via "i += 3", a second run walked into that tail and re-fused part of it (the
                // ADD/STORE_SLOT pair behind an INC_SLOT became ADD_STORE_SLOT). The fused handler skips
                // the tail at runtime so nothing changed observably, but the documented guarantee that a
                // folded tail stays a valid fallback if control ever lands on it directly was gone. This
                // matters because a second run really happens: hot-starting an async call re-loaded the
                // same instruction list the parent VM was executing.
                int foldedTail = FoldedTailLength(head);
                if (foldedTail > 0)
                {
                    i += foldedTail;
                    continue;
                }

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

        /// <summary>
        /// Number of folded tail instructions an already-fused head owns. Mirrors the instruction-pointer
        /// advance of the matching handler in <c>VM.HandleInstruction</c>, so the two must stay in step:
        /// INC_SLOT/DEC_SLOT skip PUSH_INT, ADD/SUB and STORE_SLOT; SLOT_ADD_INT/SLOT_SUB_INT skip
        /// PUSH_INT and ADD/SUB; the remaining fused forms own a single trailing instruction.
        /// </summary>
        private static int FoldedTailLength(OpCode code)
            => code switch
            {
                OpCode.INC_SLOT or OpCode.DEC_SLOT => 3,
                OpCode.SLOT_ADD_INT or OpCode.SLOT_SUB_INT => 2,
                OpCode.LOAD_SLOT2 => 1,
                OpCode.ADD_STORE_SLOT or OpCode.SUB_STORE_SLOT => 1,
                OpCode.LT_JF or OpCode.LE_JF or OpCode.GT_JF or OpCode.GE_JF or OpCode.EQ_JF or OpCode.NEQ_JF => 1,
                _ => 0,
            };

        /// <summary>Returns a copy of <paramref name="original"/> with a new opcode, preserving operand and source location.</summary>
        private static Instruction Retag(Instruction original, OpCode code)
            => new Instruction(code, original.Operand, original.Line, original.Col, original.OriginFile);
    }
}
