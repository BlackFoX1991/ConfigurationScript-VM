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
    }
}
