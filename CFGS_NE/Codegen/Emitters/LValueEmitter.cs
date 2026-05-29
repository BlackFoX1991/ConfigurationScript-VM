using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// The CompileLValue
        /// </summary>
        /// <param name="target">The target<see cref="Expr?"/></param>
        /// <param name="load">The load<see cref="bool"/></param>
        private void CompileLValue(Expr? target, bool load)
        {
            if (target is VarExpr v)
            {
                if (load)
                {
                    if (!TryEmitImplicitMemberLoad(v.Name, v))
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, v.Name, v.Line, v.Col, v.OriginFile));
                }
            }
            else if (target is IndexExpr ie)
            {
                if (ie.Index == null)
                    throw new CompilerException($"empty index '[]' cannot be used for reading", ie.Line, ie.Col, FileName);

                CompileExpr(ie.Target);
                CompileExpr(ie.Index);
                if (load)
                    _insns.Add(new Instruction(OpCode.INDEX_GET, IndexAccessOperand(ie), ie.Line, ie.Col, ie.OriginFile));
            }
            else
            {
                throw new CompilerException("invalid lvalue expression", target?.Line ?? -1, target?.Col ?? -1, FileName);
            }
        }

        /// <summary>
        /// The CompileLValueStore
        /// </summary>
        /// <param name="target">The target<see cref="Expr?"/></param>
        private void CompileLValueStore(Expr? target)
        {
            if (target is VarExpr v)
            {
                if (!TryEmitImplicitMemberStore(v.Name, v))
                    _insns.Add(new Instruction(OpCode.STORE_VAR, v.Name, v.Line, v.Col, v.OriginFile));
            }
            else if (target is IndexExpr ie)
            {
                if (ie.Index == null)
                {
                    CompileExpr(ie.Target);
                    _insns.Add(new Instruction(OpCode.ARRAY_PUSH, null, ie.Line, ie.Col, ie.OriginFile));
                }
                else
                {
                    CompileExpr(ie.Target);
                    CompileExpr(ie.Index);
                    _insns.Add(new Instruction(OpCode.ROT, null, ie.Line, ie.Col, ie.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_SET, IndexAccessOperand(ie), ie.Line, ie.Col, ie.OriginFile));
                }
            }
            else
            {
                throw new CompilerException("Invalid lvalue for store.", target?.Line ?? -1, target?.Col ?? -1, FileName);
            }
        }
    }
}
