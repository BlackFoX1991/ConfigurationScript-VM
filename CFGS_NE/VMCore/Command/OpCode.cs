namespace CFGS_VM.VMCore.Command
{
    /// <summary>
    /// Defines the OpCode
    /// </summary>
    public enum OpCode
    {
        /// <summary>
        /// Defines the PUSH_INT
        /// </summary>
        PUSH_INT,

        /// <summary>
        /// Defines the PUSH_LNG
        /// </summary>
        PUSH_LNG,

        /// <summary>
        /// Defines the PUSH_FLT
        /// </summary>
        PUSH_FLT,

        /// <summary>
        /// Defines the PUSH_DBL
        /// </summary>
        PUSH_DBL,

        /// <summary>
        /// Defines the PUSH_DEC
        /// </summary>
        PUSH_DEC,

        /// <summary>
        /// Defines the PUSH_STR
        /// </summary>
        PUSH_STR,

        /// <summary>
        /// Defines the PUSH_CHR
        /// </summary>
        PUSH_CHR,

        /// <summary>
        /// Defines the PUSH_BOOL
        /// </summary>
        PUSH_BOOL,

        /// <summary>
        /// Defines the PUSH_SCOPE
        /// </summary>
        PUSH_SCOPE,

        /// <summary>
        /// Defines the PUSH_NULL
        /// </summary>
        PUSH_NULL,

        /// <summary>
        /// Defines the POP_SCOPE
        /// </summary>
        POP_SCOPE,

        /// <summary>
        /// Defines the NEW_ARRAY
        /// </summary>
        NEW_ARRAY,

        /// <summary>
        /// Defines the LOAD_VAR
        /// </summary>
        LOAD_VAR,

        /// <summary>
        /// Defines the STORE_VAR
        /// </summary>
        STORE_VAR,

        /// <summary>
        /// Defines the VAR_DECL
        /// </summary>
        VAR_DECL,

        /// <summary>
        /// Defines the LOAD_INDEX
        /// </summary>
        LOAD_INDEX,

        /// <summary>
        /// Defines the STORE_INDEX
        /// </summary>
        STORE_INDEX,

        /// <summary>
        /// Defines the INDEX_GET
        /// </summary>
        INDEX_GET,

        /// <summary>
        /// Defines the SLICE_GET
        /// </summary>
        SLICE_GET,

        /// <summary>
        /// Defines the SLICE_SET
        /// </summary>
        SLICE_SET,

        /// <summary>
        /// Defines the INDEX_SET
        /// </summary>
        INDEX_SET,

        /// <summary>
        /// Defines the NEW_DICT
        /// </summary>
        NEW_DICT,

        /// <summary>
        /// Defines the NEW_OBJECT
        /// </summary>
        NEW_OBJECT,

        /// <summary>
        /// Defines the ARRAY_PUSH
        /// </summary>
        ARRAY_PUSH,

        /// <summary>
        /// Defines the ARRAY_DELETE_ELEM
        /// </summary>
        ARRAY_DELETE_ELEM,

        /// <summary>
        /// Defines the ARRAY_DELETE_ALL
        /// </summary>
        ARRAY_DELETE_ALL,

        /// <summary>
        /// Defines the ARRAY_DELETE_ELEM_ALL
        /// </summary>
        ARRAY_DELETE_ELEM_ALL,

        ARRAY_DELETE_SLICE_ALL,
        ARRAY_DELETE_SLICE,

        /// <summary>
        /// Defines the ADD
        /// </summary>
        ADD,

        /// <summary>
        /// Defines the SUB
        /// </summary>
        SUB,

        /// <summary>
        /// Defines the MUL
        /// </summary>
        MUL,

        /// <summary>
        /// Defines the DIV
        /// </summary>
        DIV,

        /// <summary>
        /// Defines the EXPO
        /// </summary>
        EXPO,

        /// <summary>
        /// Defines the BIT_AND
        /// </summary>
        BIT_AND,

        /// <summary>
        /// Defines the BIT_OR
        /// </summary>
        BIT_OR,

        /// <summary>
        /// Defines the BIT_XOR
        /// </summary>
        BIT_XOR,

        /// <summary>
        /// Defines the SHL
        /// </summary>
        SHL,

        /// <summary>
        /// Defines the SHR
        /// </summary>
        SHR,

        /// <summary>
        /// Defines the EQ
        /// </summary>
        EQ,

        /// <summary>
        /// Defines the NEQ
        /// </summary>
        NEQ,

        /// <summary>
        /// Defines the LT
        /// </summary>
        LT,

        /// <summary>
        /// Defines the GT
        /// </summary>
        GT,

        /// <summary>
        /// Defines the LE
        /// </summary>
        LE,

        /// <summary>
        /// Defines the GE
        /// </summary>
        GE,

        /// <summary>
        /// Defines the AND
        /// </summary>
        AND,

        /// <summary>
        /// Defines the OR
        /// </summary>
        OR,

        /// <summary>
        /// Defines the NEG
        /// </summary>
        NEG,

        /// <summary>
        /// Defines the NOT
        /// </summary>
        NOT,

        /// <summary>
        /// Defines the DUP
        /// </summary>
        DUP,

        /// <summary>
        /// Defines the POP
        /// </summary>
        POP,

        /// <summary>
        /// Defines the PRINT
        /// </summary>
        PRINT,

        /// <summary>
        /// Defines the JMP
        /// </summary>
        JMP,

        /// <summary>
        /// Defines the JMP_IF_FALSE
        /// </summary>
        JMP_IF_FALSE,

        /// <summary>
        /// Defines the JMP_IF_TRUE
        /// </summary>
        JMP_IF_TRUE,

        /// <summary>
        /// Defines the HALT
        /// </summary>
        HALT,

        /// <summary>
        /// Defines the ROT
        /// </summary>
        ROT,

        /// <summary>
        /// Defines the CALL
        /// </summary>
        CALL,

        /// <summary>
        /// Defines the RET
        /// </summary>
        RET,

        /// <summary>
        /// Defines the MOD
        /// </summary>
        MOD,

        /// <summary>
        /// Defines the CALL_INDIRECT
        /// </summary>
        CALL_INDIRECT,


        /// <summary>
        /// Defines the PUSH_CLOSURE
        /// </summary>
        PUSH_CLOSURE,

        /// <summary>
        /// Defines the TRY_PUSH
        /// </summary>
        TRY_PUSH,

        /// <summary>
        /// Defines the TRY_POP
        /// </summary>
        TRY_POP,

        /// <summary>
        /// Defines the THROW
        /// </summary>
        THROW,

        /// <summary>
        /// Defines the END_FINALLY
        /// </summary>
        END_FINALLY,

    }
}
