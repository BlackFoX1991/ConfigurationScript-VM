using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        private void CompileLValue(Expr? target, bool load)
        {
            if (target is VarExpr v)
            {
                if (load)
                {
                    if (IsResolvedGlobalReference(v))
                        _insns.Add(new Instruction(OpCode.LOAD_GLOBAL, v.Name, v.Line, v.Col, v.OriginFile));
                    else if (!TryEmitImplicitMemberLoad(v.Name, v))
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

        private static bool IsSimpleLValuePart(Expr? e)
            => e is VarExpr or NumberExpr or StringExpr or CharExpr or BoolExpr or NullExpr;

        /// <summary>
        /// Runs a read-modify-write <paramref name="body"/> against a target whose sub-expressions are
        /// evaluated at most once. Compound/increment assignment reads via <see cref="CompileLValue"/>
        /// and writes via <see cref="CompileLValueStore"/>, each of which independently re-emits the
        /// target and index expressions. That is harmless for side-effect-free parts (a plain variable
        /// or literal), but for an index/receiver with side effects it would run them twice (prefix:
        /// three times) and — worse — read and write different locations. For such cases we hoist the
        /// target and index into fresh temp locals once, then let <paramref name="body"/> operate on a
        /// synthetic index expression that only reads those temps (idempotent LOAD_VARs).
        /// </summary>
        private void WithStableLValue(Expr? target, Action<Expr?> body)
        {
            if (target is IndexExpr ie
                && ie.Index != null
                && (!IsSimpleLValuePart(ie.Target) || !IsSimpleLValuePart(ie.Index)))
            {
                EmitPushScope(ie);

                string tTmp = $"__lv_t_{_anonCounter++}";
                CompileExpr(ie.Target);
                _insns.Add(new Instruction(OpCode.VAR_DECL, tTmp, ie.Line, ie.Col, ie.OriginFile));

                string iTmp = $"__lv_i_{_anonCounter++}";
                CompileExpr(ie.Index);
                _insns.Add(new Instruction(OpCode.VAR_DECL, iTmp, ie.Line, ie.Col, ie.OriginFile));

                IndexExpr stable = new(
                    new VarExpr(tTmp, ie.Line, ie.Col, ie.OriginFile),
                    new VarExpr(iTmp, ie.Line, ie.Col, ie.OriginFile),
                    ie.Line, ie.Col, ie.OriginFile, ie.IsDotAccess);

                body(stable);
                EmitPopScope(ie);
            }
            else
            {
                body(target);
            }
        }

        private void CompileLValueStore(Expr? target)
        {
            if (target is VarExpr v)
            {
                if (IsResolvedGlobalReference(v))
                    _insns.Add(new Instruction(OpCode.STORE_GLOBAL, v.Name, v.Line, v.Col, v.OriginFile));
                else if (!TryEmitImplicitMemberStore(v.Name, v))
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
