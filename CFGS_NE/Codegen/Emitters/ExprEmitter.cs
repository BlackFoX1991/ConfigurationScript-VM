using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// The CompileExpr
        /// </summary>
        /// <param name="e">The e<see cref="Expr?"/></param>
        private void CompileExpr(Expr? e)
        {
            switch (e)
            {
                case NumberExpr n:
                    if (n.Value is int)
                        _insns.Add(new Instruction(OpCode.PUSH_INT, n.Value, e.Line, e.Col, e.OriginFile));
                    else if (n.Value is long)
                        _insns.Add(new Instruction(OpCode.PUSH_LNG, n.Value, e.Line, e.Col, e.OriginFile));
                    else if (n.Value is float)
                        _insns.Add(new Instruction(OpCode.PUSH_FLT, n.Value, e.Line, e.Col, e.OriginFile));
                    else if (n.Value is double)
                        _insns.Add(new Instruction(OpCode.PUSH_DBL, n.Value, e.Line, e.Col, e.OriginFile));
                    else if (n.Value is decimal)
                        _insns.Add(new Instruction(OpCode.PUSH_DEC, n.Value, e.Line, e.Col, e.OriginFile));
                    else if (n.Value is BigInteger)
                        _insns.Add(new Instruction(OpCode.PUSH_SPC, n.Value, e.Line, e.Col, e.OriginFile));
                    else
                        throw new CompilerException("invalid number value", n.Line, n.Col, e.OriginFile);
                    break;

                case StringExpr s:
                    _insns.Add(new Instruction(OpCode.PUSH_STR, s.Value, e.Line, e.Col, e.OriginFile));
                    break;

                case CharExpr che:
                    _insns.Add(new Instruction(OpCode.PUSH_CHR, che.Value, e.Line, e.Col, e.OriginFile));
                    break;
                case BoolExpr bxe:
                    _insns.Add(new Instruction(OpCode.PUSH_BOOL, bxe.Value, e.Line, e.Col, e.OriginFile));
                    break;

                case VarExpr v:
                    {
                        string name = v.Name;

                        if (IsReceiverIdentifier(name))
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, name, e.Line, e.Col, e.OriginFile));
                            break;
                        }

                        if (CurrentLocals.Contains(name))
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, name, e.Line, e.Col, e.OriginFile));
                            break;
                        }

                        if (!TryEmitImplicitMemberLoad(name, v))
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, name, e.Line, e.Col, e.OriginFile));
                        }
                        break;
                    }

                case ArrayExpr a:
                    foreach (Expr elem in a.Elements) CompileExpr(elem);
                    _insns.Add(new Instruction(OpCode.NEW_ARRAY, a.Elements.Count, e.Line, e.Col, e.OriginFile));
                    break;

                case IndexExpr idx:
                    if (idx.Index == null)
                        throw new CompilerException("empty index '[]' cannot be used as expression", idx.Line, idx.Col, e.OriginFile);

                    CompileExpr(idx.Target);
                    CompileExpr(idx.Index);
                    _insns.Add(new Instruction(OpCode.INDEX_GET, null, idx.Line, idx.Col, e.OriginFile));
                    break;

                case SliceExpr slice:
                    CompileExpr(slice.Target);

                    if (slice.Start is not null)
                        CompileExpr(slice.Start);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, slice.Line, slice.Col, e.OriginFile));

                    if (slice.End is not null)
                        CompileExpr(slice.End);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, slice.Line, slice.Col, e.OriginFile));

                    _insns.Add(new Instruction(OpCode.SLICE_GET, null, slice.Line, slice.Col, e.OriginFile));
                    break;
                case NullExpr nil:
                    _insns.Add(new Instruction(OpCode.PUSH_NULL, null, e.Line, e.Col, e.OriginFile));
                    break;

                case BinaryExpr b:
                    {
                        if (b.Op == TokenType.AndAnd)
                        {
                            CompileExpr(b.Left);

                            int jmpIfLeftFalse = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, b.Line, b.Col, b.OriginFile));

                            CompileExpr(b.Right);

                            int jmpIfRightFalse = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, b.Line, b.Col, b.OriginFile));

                            _insns.Add(new Instruction(OpCode.PUSH_BOOL, true, b.Line, b.Col, b.OriginFile));
                            int jmpEnd = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, b.Line, b.Col, b.OriginFile));

                            int lFalse = _insns.Count;
                            _insns[jmpIfLeftFalse] = new Instruction(OpCode.JMP_IF_FALSE, lFalse, b.Line, b.Col, b.OriginFile);
                            _insns[jmpIfRightFalse] = new Instruction(OpCode.JMP_IF_FALSE, lFalse, b.Line, b.Col, b.OriginFile);

                            _insns.Add(new Instruction(OpCode.PUSH_BOOL, false, b.Line, b.Col, b.OriginFile));

                            _insns[jmpEnd] = new Instruction(OpCode.JMP, _insns.Count, b.Line, b.Col, b.OriginFile);
                            break;
                        }
                        else if (b.Op == TokenType.OrOr)
                        {
                            CompileExpr(b.Left);

                            int jmpIfLeftTrue = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_TRUE, null, b.Line, b.Col, b.OriginFile));

                            CompileExpr(b.Right);

                            int jmpIfRightTrue = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_TRUE, null, b.Line, b.Col, b.OriginFile));

                            _insns.Add(new Instruction(OpCode.PUSH_BOOL, false, b.Line, b.Col, b.OriginFile));
                            int jmpEnd = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, b.Line, b.Col, b.OriginFile));

                            int lTrue = _insns.Count;
                            _insns[jmpIfLeftTrue] = new Instruction(OpCode.JMP_IF_TRUE, lTrue, b.Line, b.Col, b.OriginFile);
                            _insns[jmpIfRightTrue] = new Instruction(OpCode.JMP_IF_TRUE, lTrue, b.Line, b.Col, b.OriginFile);

                            _insns.Add(new Instruction(OpCode.PUSH_BOOL, true, b.Line, b.Col, b.OriginFile));

                            _insns[jmpEnd] = new Instruction(OpCode.JMP, _insns.Count, b.Line, b.Col, b.OriginFile);
                            break;
                        }
                        else if (b.Op == TokenType.QQNull)
                        {
                            CompileExpr(b.Left);

                            _insns.Add(new Instruction(OpCode.DUP, null, b.Line, b.Col, b.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, b.Line, b.Col, b.OriginFile));
                            _insns.Add(new Instruction(OpCode.EQ, null, b.Line, b.Col, b.OriginFile));

                            int jmpIfNull = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_TRUE, null, b.Line, b.Col, b.OriginFile));

                            int jmpEnd = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, b.Line, b.Col, b.OriginFile));

                            _insns[jmpIfNull] = new Instruction(OpCode.JMP_IF_TRUE, _insns.Count, b.Line, b.Col, b.OriginFile);

                            _insns.Add(new Instruction(OpCode.POP, null, b.Line, b.Col, b.OriginFile));
                            CompileExpr(b.Right);

                            _insns[jmpEnd] = new Instruction(OpCode.JMP, _insns.Count, b.Line, b.Col, b.OriginFile);
                            break;
                        }

                        OpCode op = OpFromToken(b.Op, b, FileName);
                        CompileExpr(b.Left);
                        CompileExpr(b.Right);
                        _insns.Add(new Instruction(op, null, b.Line, b.Col, b.OriginFile));
                        break;
                    }

                case UnaryExpr ue:
                    CompileExpr(ue.Right);
                    switch (ue.Op)
                    {
                        case TokenType.Minus: _insns.Add(new Instruction(OpCode.NEG, null, e.Line, e.Col, e.OriginFile)); break;
                        case TokenType.Plus: break;
                        case TokenType.Not: _insns.Add(new Instruction(OpCode.NOT, null, e.Line, e.Col, e.OriginFile)); break;
                        default: throw new CompilerException($"unknown unary operator {ue.Op}", ue.Line, ue.Col, e.OriginFile);
                    }
                    break;

                case DictExpr d:
                    foreach ((Expr k, Expr v) in d.Pairs)
                    {
                        CompileExpr(k);
                        CompileExpr(v);
                    }
                    _insns.Add(new Instruction(OpCode.NEW_DICT, d.Pairs.Count, e.Line, e.Col, e.OriginFile));
                    break;

                case PostfixExpr pf:
                    CompileLValue(pf.Target, load: true);
                    _insns.Add(new Instruction(OpCode.DUP, null, e.Line, e.Col, e.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_INT, 1, e.Line, e.Col, e.OriginFile));
                    _insns.Add(new Instruction(pf.Op == TokenType.PlusPlus ? OpCode.ADD : OpCode.SUB, null, e.Line, e.Col, e.OriginFile));
                    CompileLValueStore(pf.Target);
                    break;

                case PrefixExpr pre:
                    CompileLValue(pre.Target, load: true);
                    _insns.Add(new Instruction(OpCode.PUSH_INT, 1, e.Line, e.Col, e.OriginFile));
                    _insns.Add(new Instruction(pre.Op == TokenType.PlusPlus ? OpCode.ADD : OpCode.SUB, null, e.Line, e.Col, e.OriginFile));
                    CompileLValueStore(pre.Target);
                    CompileLValue(pre.Target, load: true);
                    break;

                case AwaitExpr aw:
                    {

                        CompileExpr(aw.Inner);
                        _insns.Add(new Instruction(OpCode.AWAIT, null, e.Line, e.Col, e.OriginFile));
                        break;
                    }
                case CallExpr call:
                    CompileCallExpression(call);
                    break;

                case NewExpr ne:
                    CompileNewExpression(ne);
                    break;

                case ObjectInitExpr oi:
                    CompileObjectInitializerExpression(oi);
                    break;

                case MethodCallExpr mce:
                    CompileMethodCallExpression(mce);
                    break;

                case FuncExpr fe:
                    {
                        int jmpOverFuncIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, e.Line, e.Col, e.OriginFile));

                        int funcStart = _insns.Count;

                        string anonName = $"__anon_{_anonCounter++}";
                        Functions[anonName] = new FunctionInfo(fe.Parameters, funcStart, fe.MinArgs, fe.RestParameter, fe.IsAsync);

                        ReceiverContextKind prevReceiverContext = _receiverContext;
                        _receiverContext = DetermineReceiverContext(fe);
                        EnterFunctionLocals(fe.Parameters);
                        if (fe.IsAsync) _asyncFunctionDepth++;
                        try
                        {
                            CompileStmt(fe.Body, insideFunction: true);
                        }
                        finally
                        {
                            if (fe.IsAsync) _asyncFunctionDepth--;
                            LeaveFunctionLocals();
                            _receiverContext = prevReceiverContext;
                        }

                        if (_insns.Count == 0 || _insns[^1].Code != OpCode.RET)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, e.Line, e.Col, e.OriginFile));
                            _insns.Add(new Instruction(OpCode.RET, null, e.Line, e.Col, e.OriginFile));
                        }

                        _insns[jmpOverFuncIdx] = new Instruction(OpCode.JMP, _insns.Count, e.Line, e.Col, e.OriginFile);

                        _insns.Add(new Instruction(OpCode.PUSH_CLOSURE, new object[] { funcStart, anonName }, e.Line, e.Col, e.OriginFile));
                        break;
                    }

                case NamedArgExpr na:
                    CompileNamedArgumentExpression(na);
                    break;

                case SpreadArgExpr sa:
                    CompileSpreadArgumentExpression(sa);
                    break;

                case OutExpr ox:
                    CompileOutExpression(ox);
                    break;

                case ConditionalExpr cnd:
                    CompileConditionalExpression(cnd);
                    break;

                case MatchExpr me:
                    CompileMatchExpression(me);
                    break;

                default:
                    throw new CompilerException($"unknown expr type {e?.GetType().Name}", e?.Line ?? -1, e?.Col ?? -1, e?.OriginFile ?? "");
            }
        }

    }
}
