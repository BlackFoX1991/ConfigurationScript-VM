namespace CFGS_VM.VMCore.Command
{
    public enum OpCode
    {
        PUSH_INT,

        PUSH_LNG,

        PUSH_FLT,

        PUSH_DBL,

        PUSH_DEC,

        PUSH_SPC,

        PUSH_STR,

        PUSH_CHR,

        PUSH_BOOL,

        PUSH_SCOPE,

        PUSH_NULL,

        PUSH_CONST,

        POP_SCOPE,

        LEAVE,

        NEW_ARRAY,

        LOAD_VAR,

        STORE_VAR,

        VAR_DECL,

        CONST_DECL,

        LOAD_INDEX,

        STORE_INDEX,

        INDEX_GET,

        SLICE_GET,

        SLICE_SET,

        INDEX_SET,

        INDEX_SET_INTERNAL,

        INDEX_INIT,

        NEW_DICT,

        NEW_OBJECT,

        NEW_STATIC,

        NEW_ENUM,

        ARRAY_PUSH,

        ARRAY_DELETE_ELEM,

        ARRAY_DELETE_ALL,

        ARRAY_DELETE_ELEM_ALL,

        ARRAY_DELETE_SLICE_ALL,

        ARRAY_DELETE_SLICE,

        ARRAY_CLEAR,

        ADD,

        SUB,

        MUL,

        DIV,

        EXPO,

        BIT_AND,

        BIT_OR,

        BIT_XOR,

        SHL,

        SHR,

        EQ,

        NEQ,

        LT,

        GT,

        LE,

        GE,

        AND,

        OR,

        NEG,

        NOT,

        DUP,

        POP,

        DESTROY,

        PRINT,

        JMP,

        JMP_IF_FALSE,

        JMP_IF_TRUE,

        TRY_PUSH,

        TRY_POP,

        THROW,

        LABEL,

        HALT,

        ROT,

        SWAP,

        IS_DICT,

        IS_ARRAY,

        DICT_KEYS,

        LEN,

        HAS_KEY,

        CALL,

        MAKE_NAMED_ARG,

        MAKE_SPREAD_ARG,

        RET,

        MOD,

        CALL_INDIRECT,

        PUSH_CLOSURE,

        AWAIT,

        IS_TYPE,

        YIELD,

        NEW_DICT_INTERNAL,

        MERGE_LEFT,

        MERGE_RIGHT,

        NEW_NAMESPACE,

        // Slot-based local variable access (Plan A, stage 1b). Operand packs (hops, slotIndex) as
        // (hops << 16) | idx: walk `hops` parent scopes then index the slot array directly, bypassing the
        // name hash + scope walk of LOAD_VAR/STORE_VAR. Emitted only for reads/assignments the compiler
        // resolves within the current function's own scope chain; captures/globals/builtins/receiver
        // keywords stay name-based, and declarations keep VAR_DECL/CONST_DECL (which already write the
        // right descriptor slot by name and retain the redeclaration/const checks). Appended at the end so
        // existing opcode values (and .cfb bytecode) keep their numbering.
        LOAD_SLOT,

        STORE_SLOT,

        // Slot-based declaration in the current scope (operand = slot index; hops is always 0). Writes the
        // popped value into the pre-sized descriptor slot directly, keeping the runtime redeclaration check
        // (the slot is the Undefined sentinel until first declared). Emitted only for a declaration whose
        // scope has a descriptor slot for the name and that is not in a name-based handler region.
        VAR_DECL_SLOT,

        CONST_DECL_SLOT,

        // Superinstructions (peephole fusion, applied at load time by SuperInstructionRewriter). Each fused
        // opcode OVERLAYS the first instruction of a recognized run: its handler reads the folded operands
        // from the following (unmodified) slots and advances _ip past them. Instruction indices are never
        // shifted, so every absolute jump target / handler range / address-keyed map stays valid, and the
        // folded tail instructions remain in place as a correct fallback if ever entered directly.
        // Appended at the end so existing opcode numbers (and .cfb bytecode) keep their values.

        // <cmp>, JMP_IF_FALSE  ->  compare and branch-on-false in one dispatch (loop/if backbone).
        LT_JF,

        LE_JF,

        GT_JF,

        GE_JF,

        EQ_JF,

        NEQ_JF,

        // LOAD_SLOT, PUSH_INT, (ADD|SUB)  ->  local +/- integer constant (i++, n-1, index math).
        SLOT_ADD_INT,

        SLOT_SUB_INT,

        // --- Superinstructions phase 2 ---

        // LOAD_SLOT x, PUSH_INT k, (ADD|SUB), STORE_SLOT x  (same slot x)  ->  x +/-= k in place
        // (the canonical loop counter update `i = i + 1` / countdown `i = i - 1`).
        INC_SLOT,

        DEC_SLOT,

        // LOAD_SLOT, LOAD_SLOT  ->  push two locals in one dispatch (`a + b`, `a < b`).
        LOAD_SLOT2,

        // (ADD|SUB), STORE_SLOT  ->  combine + store to a local (accumulator `s = s + i`).
        ADD_STORE_SLOT,

        SUB_STORE_SLOT,

        // Like INDEX_GET, but a missing namespace member yields null instead of raising an "undefined
        // member" error. Emitted only for the get-or-create probe the namespace lowerer wraps in `?? {}`.
        INDEX_GET_OPT,

        // Default-parameter presence test. Operand = parameter name. Pushes true when that parameter's
        // slot still holds the Unset omitted-argument sentinel (i.e. the caller supplied no value), false
        // otherwise. Emitted only for the `if (<param> is unset) <param> = <default>` prologue the
        // ParamLowerer generates, so an explicitly passed null no longer triggers the default. Appended at
        // the end so existing opcode numbers (and .cfb bytecode) keep their values.
        IS_ARG_UNSET,
    }
}
