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
    }
}
