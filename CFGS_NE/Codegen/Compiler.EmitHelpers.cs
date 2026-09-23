using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.Analytic.Semantics;
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

            if (TryEmitNamespaceMemberLoad(qualifiedPath, node))
                return;

            string[] parts = qualifiedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new CompilerException("internal compiler error: invalid qualified runtime path", node.Line, node.Col, node.OriginFile);

            _insns.Add(new Instruction(OpCode.LOAD_VAR, parts[0], node.Line, node.Col, node.OriginFile));
            for (int i = 1; i < parts.Length; i++)
            {
                // A qualified type path traverses namespace values, which require dot access (member
                // access), so emit INDEX_GET as dot access rather than bracket. This keeps the compiler's
                // own namespace traversal on the dot-only path that user code must also use.
                _insns.Add(new Instruction(OpCode.PUSH_STR, parts[i], node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_GET, true, node.Line, node.Col, node.OriginFile));
            }
        }

        /// <summary>
        /// Loads a qualified path that starts inside a script namespace (for example
        /// <c>App.Core.Box.Inner</c>): the member right after the longest namespace prefix is read from its
        /// global binding, and the remaining segments are ordinary member accesses.
        /// </summary>
        private bool TryEmitNamespaceMemberLoad(string qualifiedPath, Node node)
        {
            string[] parts = qualifiedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (int memberIndex = parts.Length - 1; memberIndex >= 1; memberIndex--)
            {
                string namespacePath = string.Join(".", parts, 0, memberIndex);
                if (!_context.NamespacePaths.Contains(namespacePath))
                    continue;

                _insns.Add(new Instruction(
                    OpCode.LOAD_GLOBAL,
                    NamespaceRuntimeBindingNames.GetHiddenMemberBindingName(namespacePath, parts[memberIndex]),
                    node.Line,
                    node.Col,
                    node.OriginFile));

                for (int i = memberIndex + 1; i < parts.Length; i++)
                {
                    _insns.Add(new Instruction(OpCode.PUSH_STR, parts[i], node.Line, node.Col, node.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_GET, true, node.Line, node.Col, node.OriginFile));
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Loads a type named in a declaration (a base class or an implemented or inherited interface). A
        /// type declared directly in a namespace is read from its member binding, which also covers a name
        /// the declaration wrote relative to its own namespace. Any other name keeps its written form, so a
        /// nested type still resolves through the declaring class's scope while that class is being built.
        /// </summary>
        private void EmitLoadDeclaredTypeReference(string typeName, Stmt? resolvedDecl, Node node)
        {
            if (resolvedDecl is not null && _context.DeclarationBindingNames.TryGetValue(resolvedDecl, out string? bindingName))
            {
                _insns.Add(new Instruction(OpCode.LOAD_GLOBAL, bindingName, node.Line, node.Col, node.OriginFile));
                return;
            }

            EmitLoadQualifiedRuntimeValue(typeName, node);
        }

        private static OpCode OpFromToken(TokenType t, Node tp, string outOfFile) => t switch
        {
            TokenType.Plus => OpCode.ADD,
            TokenType.Minus => OpCode.SUB,
            TokenType.Star => OpCode.MUL,
            TokenType.Slash => OpCode.DIV,
            TokenType.Modulo => OpCode.MOD,
            TokenType.bShiftR => OpCode.SHR,
            TokenType.bShiftL => OpCode.SHL,
            TokenType.MergeLeft => OpCode.MERGE_LEFT,
            TokenType.MergeRight => OpCode.MERGE_RIGHT,
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
            // note: `is` against a primitive type name is emitted as IS_PRIMITIVE_TYPE in ExprEmitter
            // before this mapping is consulted.
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

        private static object? IndexAccessOperand(IndexExpr indexExpr)
            => indexExpr.IsDotAccess ? true : null;

        private void EnterFunctionLocals(IEnumerable<string> parameters)
        {
            List<string> paramList = parameters as List<string> ?? parameters.ToList();

            HashSet<string> inherited;

            if (_localVarsStack.Count > 0)
                inherited = new HashSet<string>(_localVarsStack.Peek(), StringComparer.Ordinal);
            else
                inherited = new HashSet<string>(StringComparer.Ordinal);

            foreach (string p in paramList)
                inherited.Add(p);

            _localVarsStack.Push(inherited);

            // Mark the start of this function body's instruction range. The body is emitted inline from
            // here; recording [start, end) lets the descriptor pass exclude it from the enclosing scope,
            // and the params seed the function-root descriptor.
            int start = _insns.Count;
            _functionStartStack.Push(start);
            _functionParams[start] = paramList;

            // Loops of the enclosing code are not visible to break/continue inside this body.
            _functionLoopBase.Push(_breakLists.Count);
        }

        private void LeaveFunctionLocals()
        {
            if (_functionLoopBase.Count > 0)
                _functionLoopBase.Pop();

            if (_localVarsStack.Count > 0)
                _localVarsStack.Pop();

            if (_functionStartStack.Count > 0)
            {
                int start = _functionStartStack.Pop();
                _functionRanges[start] = _insns.Count;
            }
        }
    }
}
