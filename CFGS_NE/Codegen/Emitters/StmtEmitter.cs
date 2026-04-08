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
        /// The CompileStmt
        /// </summary>
        /// <param name="s">The s<see cref="Stmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileStmt(Stmt s, bool insideFunction)
        {
            switch (s)
            {
                case ExportStmt ex:
                    CompileStmt(ex.Inner, insideFunction);
                    break;

                case VarDecl v:
                    if (v.Value != null)
                        CompileExpr(v.Value);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, s.Line, s.Col, s.OriginFile));

                    _insns.Add(new Instruction(OpCode.VAR_DECL, v.Name, s.Line, s.Col, s.OriginFile));

                    if (_localVarsStack.Count > 0)
                        CurrentLocals.Add(v.Name);

                    break;

                case ConstDecl c:
                    CompileExpr(c.Value);
                    _insns.Add(new Instruction(OpCode.CONST_DECL, c.Name, s.Line, s.Col, s.OriginFile));

                    if (_localVarsStack.Count > 0)
                        CurrentLocals.Add(c.Name);

                    break;

                case DestructureDeclStmt dd:
                    EmitDestructure(dd.Pattern, dd.Value, dd.IsConst ? DestructureBindMode.ConstDecl : DestructureBindMode.VarDecl, dd);
                    break;

                case AssignStmt a:
                    {
                        CompileExpr(a.Value);

                        if (!TryEmitImplicitMemberStore(a.Name, a))
                        {
                            _insns.Add(new Instruction(OpCode.STORE_VAR, a.Name, s.Line, s.Col, s.OriginFile));
                        }
                        break;
                    }

                case DestructureAssignStmt da:
                    EmitDestructure(da.Pattern, da.Value, DestructureBindMode.Assign, da);
                    break;

                case EmptyStmt etst:
                    break;

                case AssignIndexExprStmt aies:
                    {
                        CompileExpr(aies.Value);
                        CompileExpr(aies.Target);
                        _insns.Add(new Instruction(OpCode.ROT, null, aies.Line, aies.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET, null, aies.Line, aies.Col, s.OriginFile));
                        break;
                    }

                case SliceSetStmt sliceSet:
                    CompileExpr(sliceSet.Slice.Target);

                    if (sliceSet.Slice.Start is not null)
                        CompileExpr(sliceSet.Slice.Start);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, sliceSet.Line, sliceSet.Col, s.OriginFile));

                    if (sliceSet.Slice.End is not null)
                        CompileExpr(sliceSet.Slice.End);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, sliceSet.Line, sliceSet.Col, s.OriginFile));

                    CompileExpr(sliceSet.Value);

                    _insns.Add(new Instruction(OpCode.SLICE_SET, null, sliceSet.Line, sliceSet.Col, s.OriginFile));
                    break;

                case AssignExprStmt aes:
                    {
                        CompileExpr(aes.Value);
                        CompileLValueStore(aes.Target);
                        break;
                    }

                case PushStmt ps:
                    {
                        CompileExpr(ps.Value);
                        if (ps.Target is VarExpr or IndexExpr)
                        {
                            CompileExpr(ps.Target);
                            _insns.Add(new Instruction(OpCode.ARRAY_PUSH, null, s.Line, s.Col, s.OriginFile));
                        }
                        else
                        {
                            throw new CompilerException("invalid use of 'push' []", ps.Line, ps.Col, s.OriginFile);
                        }
                        break;
                    }

                case DeleteIndexStmt di:
                    {
                        VarExpr targetExpr = new(di.Name, di.Line, di.Col, s.OriginFile);
                        CompileExpr(targetExpr);
                        CompileExpr(di.Index);
                        _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ELEM, null, s.Line, s.Col, s.OriginFile));
                        break;
                    }

                case DeleteVarStmt dv:
                    {
                        VarExpr targetExpr = new(dv.Name, dv.Line, dv.Col, s.OriginFile);
                        CompileExpr(targetExpr);
                        _insns.Add(new Instruction(OpCode.ARRAY_CLEAR, null, dv.Line, dv.Col, s.OriginFile));
                        break;
                    }

                case DeleteExprStmt des:
                    {
                        if (des.Target is SliceExpr se)
                        {
                            CompileExpr(se.Target);

                            if (se.Start != null)
                                CompileExpr(se.Start);
                            else
                                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, des.Line, des.Col, s.OriginFile));

                            if (se.End != null)
                                CompileExpr(se.End);
                            else
                                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, des.Line, des.Col, s.OriginFile));

                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_SLICE, null, des.Line, des.Col, s.OriginFile));
                            break;
                        }

                        if (des.Target is IndexExpr ie)
                        {
                            CompileExpr(ie.Target);
                            CompileExpr(ie.Index);
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ELEM, null, des.Line, des.Col, s.OriginFile));
                            break;
                        }

                        if (des.Target is VarExpr v2 && des.DeleteAll)
                        {
                            CompileExpr(v2);
                            _insns.Add(new Instruction(OpCode.ARRAY_CLEAR, null, des.Line, des.Col, s.OriginFile));
                            break;
                        }

                        throw new CompilerException("unsupported delete target", des.Line, des.Col, s.OriginFile);
                    }

                case DeleteAllStmt das:
                    {
                        if (das.Target is VarExpr var)
                        {
                            CompileExpr(var);

                            _insns.Add(new Instruction(OpCode.ARRAY_CLEAR, null, das.Line, das.Col, s.OriginFile));
                        }
                        else if (das.Target is IndexExpr xie)
                        {
                            CompileExpr(xie.Target);
                            CompileExpr(xie.Index);
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ELEM_ALL, null, das.Line, das.Col, s.OriginFile));
                        }
                        else if (das.Target is SliceExpr xse)
                        {
                            CompileExpr(xse.Target);
                            if (xse.Start != null) CompileExpr(xse.Start); else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, das.Line, das.Col, s.OriginFile));
                            if (xse.End != null) CompileExpr(xse.End); else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, das.Line, das.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_SLICE, null, das.Line, das.Col, s.OriginFile));
                        }
                        else
                        {
                            throw new CompilerException("invalid use of 'delete'", das.Line, das.Col, s.OriginFile);
                        }
                        break;
                    }

                case ClassDeclStmt cds:
                    CompileClassDeclaration(cds);
                    break;

                case InterfaceDeclStmt ids:
                    {
                        _insns.Add(new Instruction(OpCode.NEW_STATIC, ids.Name, ids.Line, ids.Col, ids.OriginFile));

                        _insns.Add(new Instruction(OpCode.DUP, null, ids.Line, ids.Col, ids.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "__is_interface", ids.Line, ids.Col, ids.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_INT, 1, ids.Line, ids.Col, ids.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, ids.Line, ids.Col, ids.OriginFile));

                        if (ids.BaseInterfaces.Count > 0)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, ids.Line, ids.Col, ids.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__interfaces", ids.Line, ids.Col, ids.OriginFile));
                            foreach (string baseName in ids.BaseInterfaces)
                                EmitLoadQualifiedRuntimeValue(baseName, ids);
                            _insns.Add(new Instruction(OpCode.NEW_ARRAY, ids.BaseInterfaces.Count, ids.Line, ids.Col, ids.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, ids.Line, ids.Col, ids.OriginFile));
                        }

                        _insns.Add(new Instruction(OpCode.VAR_DECL, ids.Name, ids.Line, ids.Col, ids.OriginFile));
                        break;
                    }

                case EnumDeclStmt eds:
                    {

                        foreach (EnumMemberNode member in eds.Members)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_STR, member.Name, eds.Line, eds.Col, eds.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_INT, (int)member.Value, eds.Line, eds.Col, eds.OriginFile));
                        }

                        _insns.Add(new Instruction(OpCode.PUSH_INT, eds.Members.Count, eds.Line, eds.Col, eds.OriginFile));
                        _insns.Add(new Instruction(OpCode.NEW_ENUM, eds.Name, eds.Line, eds.Col, eds.OriginFile));

                        _insns.Add(new Instruction(OpCode.VAR_DECL, eds.Name, eds.Line, eds.Col, eds.OriginFile));
                        break;
                    }

                case BlockStmt b:
                    CompileBlockStatement(b, insideFunction);
                    break;

                case IfStmt ifs:
                    CompileIfStatement(ifs, insideFunction);
                    break;

                case MatchStmt ms:
                    CompileMatchStatement(ms, insideFunction);
                    break;

                case DoWhileStmt dws:
                    CompileDoWhileStatement(dws, insideFunction);
                    break;

                case WhileStmt ws:
                    CompileWhileStatement(ws, insideFunction);
                    break;

                case ForStmt fs:
                    CompileForStatement(fs, insideFunction);
                    break;

                case ForeachStmt fe:
                    CompileForeachStatement(fe, insideFunction);
                    break;

                case TryStmt ts:
                    CompileTryStatement(ts, insideFunction);
                    break;

                case UsingStmt us:
                    CompileUsingStatement(us, insideFunction);
                    break;

                case ThrowStmt th:
                    CompileThrowStatement(th);
                    break;

                case YieldStmt ys:
                    CompileYieldStatement(ys, insideFunction);
                    break;

                case ContinueStmt:
                    CompileContinueStatement(s);
                    break;

                case BreakStmt:
                    CompileBreakStatement(s);
                    break;

                case ExprStmt es:
                    CompileExpr(es.Expression);
                    _insns.Add(new Instruction(OpCode.POP, null, s.Line, s.Col, s.OriginFile));
                    break;

                case CompoundAssignStmt ca:
                    CompileLValue(ca.Target, load: true);
                    CompileExpr(ca.Value);

                    _insns.Add(new Instruction(OpFromToken(ca.Op, ca, FileName), null, s.Line, s.Col, s.OriginFile));
                    CompileLValueStore(ca.Target);
                    break;

                case FuncDeclStmt fd:
                    {
                        int jmpOverFuncIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, fd.Line, fd.Col, s.OriginFile));

                        int funcStart = _insns.Count;
                        string internalName = $"__local_{fd.Name}_{_anonCounter++}";
                        Functions[internalName] = new FunctionInfo(fd.Parameters, funcStart, fd.MinArgs, fd.RestParameter, fd.IsAsync);

                        if (fd.Body is BlockStmt fb) fb.IsFunctionBody = true;

                        ReceiverContextKind prevReceiverContext = _receiverContext;
                        _receiverContext = ReceiverContextKind.None;
                        EnterFunctionLocals(fd.Parameters);
                        if (fd.IsAsync) _asyncFunctionDepth++;
                        try
                        {
                            CompileStmt(fd.Body, insideFunction: true);
                        }
                        finally
                        {
                            if (fd.IsAsync) _asyncFunctionDepth--;
                            LeaveFunctionLocals();
                            _receiverContext = prevReceiverContext;
                        }

                        if (_insns.Count == 0 || _insns[^1].Code != OpCode.RET)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, fd.Line, fd.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.RET, null, fd.Line, fd.Col, s.OriginFile));
                        }

                        _insns[jmpOverFuncIdx] = new Instruction(OpCode.JMP, _insns.Count, fd.Line, fd.Col, s.OriginFile);

                        _insns.Add(new Instruction(OpCode.PUSH_CLOSURE, new object[] { funcStart, fd.Name }, fd.Line, fd.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, fd.Name, fd.Line, fd.Col, s.OriginFile));

                        if (_localVarsStack.Count > 0)
                            CurrentLocals.Add(fd.Name);

                        break;
                    }

                case ReturnStmt rs:
                    CompileReturnStatement(rs);
                    break;

                default:
                    throw new CompilerException($"unknown statement type {s.GetType().Name}", s.Line, s.Col, s.OriginFile);
            }
        }

    }
}
