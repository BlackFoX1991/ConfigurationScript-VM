using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// Emits instructions that load a runtime value by qualified symbol path.
        /// </summary>
        private void EmitLoadQualifiedRuntimeValue(string qualifiedPath, Node node)
        {
            if (string.IsNullOrWhiteSpace(qualifiedPath))
                throw new CompilerException("internal compiler error: empty qualified runtime path", node.Line, node.Col, node.OriginFile);

            string[] parts = qualifiedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new CompilerException("internal compiler error: invalid qualified runtime path", node.Line, node.Col, node.OriginFile);

            _insns.Add(new Instruction(OpCode.LOAD_VAR, parts[0], node.Line, node.Col, node.OriginFile));
            for (int i = 1; i < parts.Length; i++)
            {
                _insns.Add(new Instruction(OpCode.PUSH_STR, parts[i], node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_GET, null, node.Line, node.Col, node.OriginFile));
            }
        }

        /// <summary>
        /// The OpFromToken
        /// </summary>
        /// <param name="t">The t<see cref="TokenType"/></param>
        /// <param name="tp">The tp<see cref="Node"/></param>
        /// <param name="outOfFile">The outOfFile<see cref="string"/></param>
        /// <returns>The <see cref="OpCode"/></returns>
        private static OpCode OpFromToken(TokenType t, Node tp, string outOfFile) => t switch
        {
            TokenType.Plus => OpCode.ADD,
            TokenType.Minus => OpCode.SUB,
            TokenType.Star => OpCode.MUL,
            TokenType.Slash => OpCode.DIV,
            TokenType.Modulo => OpCode.MOD,
            TokenType.bShiftR => OpCode.SHR,
            TokenType.bShiftL => OpCode.SHL,
            TokenType.bOr => OpCode.BIT_OR,
            TokenType.bXor => OpCode.BIT_XOR,
            TokenType.bAnd => OpCode.BIT_AND,
            TokenType.Expo => OpCode.EXPO,
            TokenType.Eq => OpCode.EQ,
            TokenType.Neq => OpCode.NEQ,
            TokenType.Lt => OpCode.LT,
            TokenType.Gt => OpCode.GT,
            TokenType.Le => OpCode.LE,
            TokenType.Ge => OpCode.GE,
            TokenType.Is => OpCode.IS_TYPE,
            TokenType.AndAnd => OpCode.AND,
            TokenType.OrOr => OpCode.OR,
            TokenType.PlusAssign => OpCode.ADD,
            TokenType.MinusAssign => OpCode.SUB,
            TokenType.StarAssign => OpCode.MUL,
            TokenType.SlashAssign => OpCode.DIV,
            TokenType.ModAssign => OpCode.MOD,

            _ => throw new CompilerException($"unsupported operator token for bytecode: {t}", tp.Line, tp.Col, outOfFile)
        };

        /// <summary>
        /// The EnterFunctionLocals
        /// </summary>
        /// <param name="parameters">The parameters<see cref="IEnumerable{string}"/></param>
        private void EnterFunctionLocals(IEnumerable<string> parameters)
        {
            HashSet<string> inherited;

            if (_localVarsStack.Count > 0)
                inherited = new HashSet<string>(_localVarsStack.Peek(), StringComparer.Ordinal);
            else
                inherited = new HashSet<string>(StringComparer.Ordinal);

            foreach (string p in parameters)
                inherited.Add(p);

            _localVarsStack.Push(inherited);
        }

        /// <summary>
        /// The LeaveFunctionLocals
        /// </summary>
        private void LeaveFunctionLocals()
        {
            if (_localVarsStack.Count > 0)
                _localVarsStack.Pop();
        }
    }
}
