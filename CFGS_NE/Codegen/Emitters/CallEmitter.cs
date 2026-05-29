using System.Collections.Generic;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// The EmitCallArgumentsReversed
        /// </summary>
        /// <param name="args">The args<see cref="IReadOnlyList{Expr}"/></param>
        private void EmitCallArgumentsReversed(IReadOnlyList<Expr> args)
        {
            for (int i = args.Count - 1; i >= 0; i--)
                CompileExpr(args[i]);
        }

        /// <summary>
        /// The CompileCallExpression
        /// </summary>
        /// <param name="call">The call<see cref="CallExpr"/></param>
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

        /// <summary>
        /// The CompileMethodCallExpression
        /// </summary>
        /// <param name="mce">The mce<see cref="MethodCallExpr"/></param>
        private void CompileMethodCallExpression(MethodCallExpr mce)
        {
            CompileExpr(mce.Target);
            _insns.Add(new Instruction(OpCode.PUSH_STR, mce.Method, mce.Line, mce.Col, mce.OriginFile));
            _insns.Add(new Instruction(OpCode.INDEX_GET, true, mce.Line, mce.Col, mce.OriginFile));
            EmitCallArgumentsReversed(mce.Args);
            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, mce.Args.Count, mce.Line, mce.Col, mce.OriginFile));
        }

        /// <summary>
        /// The CompileNamedArgumentExpression
        /// </summary>
        /// <param name="na">The na<see cref="NamedArgExpr"/></param>
        private void CompileNamedArgumentExpression(NamedArgExpr na)
        {
            CompileExpr(na.Value);
            _insns.Add(new Instruction(OpCode.MAKE_NAMED_ARG, na.Name, na.Line, na.Col, na.OriginFile));
        }

        /// <summary>
        /// The CompileSpreadArgumentExpression
        /// </summary>
        /// <param name="sa">The sa<see cref="SpreadArgExpr"/></param>
        private void CompileSpreadArgumentExpression(SpreadArgExpr sa)
        {
            CompileExpr(sa.Value);
            _insns.Add(new Instruction(OpCode.MAKE_SPREAD_ARG, null, sa.Line, sa.Col, sa.OriginFile));
        }
    }
}
