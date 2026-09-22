using System.Collections.Generic;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        private void EmitCallArgumentsReversed(IReadOnlyList<Expr> args)
        {
            for (int i = args.Count - 1; i >= 0; i--)
                CompileExpr(args[i]);
        }

        private void CompileCallExpression(CallExpr call)
        {
            if (call.Target is IndexExpr ie)
            {
                CompileExpr(ie.Target);
                if (ie.Index is StringExpr s)
                    _insns.Add(new Instruction(OpCode.PUSH_STR, s.Value, call.Line, call.Col, call.OriginFile));
                else
                    CompileExpr(ie.Index);

                _insns.Add(new Instruction(OpCode.INDEX_GET, IndexAccessOperand(ie), call.Line, call.Col, call.OriginFile));
                EmitCallArgumentsReversed(call.Args);
                _insns.Add(new Instruction(OpCode.CALL_INDIRECT, call.Args.Count, call.Line, call.Col, call.OriginFile));
                return;
            }

            CompileExpr(call.Target);
            EmitCallArgumentsReversed(call.Args);
            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, call.Args.Count, call.Line, call.Col, call.OriginFile));
        }

        private void CompileNamedArgumentExpression(NamedArgExpr na)
        {
            CompileExpr(na.Value);
            _insns.Add(new Instruction(OpCode.MAKE_NAMED_ARG, na.Name, na.Line, na.Col, na.OriginFile));
        }

        private void CompileSpreadArgumentExpression(SpreadArgExpr sa)
        {
            CompileExpr(sa.Value);
            _insns.Add(new Instruction(OpCode.MAKE_SPREAD_ARG, null, sa.Line, sa.Col, sa.OriginFile));
        }
    }
}
