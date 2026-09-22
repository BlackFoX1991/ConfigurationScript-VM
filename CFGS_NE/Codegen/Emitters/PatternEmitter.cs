using System;
using System.Collections.Generic;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        private bool EnterMatchArmLocals()
        {
            if (_localVarsStack.Count == 0)
                return false;

            _localVarsStack.Push(new HashSet<string>(_localVarsStack.Peek(), StringComparer.Ordinal));
            return true;
        }

        private void LeaveMatchArmLocals(bool entered)
        {
            if (entered && _localVarsStack.Count > 0)
                _localVarsStack.Pop();
        }

        private void EmitVarDeclTracked(string name, Node node, bool trackInLocals)
        {
            _insns.Add(new Instruction(OpCode.VAR_DECL, name, node.Line, node.Col, node.OriginFile));
            if (trackInLocals && _localVarsStack.Count > 0)
                CurrentLocals.Add(name);
        }

        private void EmitPatternFailJump(List<int> failJumps, Node node)
        {
            int idx = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, node.Line, node.Col, node.OriginFile));
            failJumps.Add(idx);
        }

        private void EmitPatternMatch(MatchPattern pattern, string sourceVar, List<int> failJumps)
        {
            switch (pattern)
            {
                case WildcardMatchPattern:
                    return;

                case BindingMatchPattern bind:
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, bind.Line, bind.Col, bind.OriginFile));
                    EmitVarDeclTracked(bind.Name, bind, trackInLocals: true);
                    return;

                case ValueMatchPattern val:
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, val.Line, val.Col, val.OriginFile));
                    CompileExpr(val.Value);
                    _insns.Add(new Instruction(OpCode.EQ, null, val.Line, val.Col, val.OriginFile));
                    EmitPatternFailJump(failJumps, val);
                    return;

                case ArrayMatchPattern arr:
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, arr.Line, arr.Col, arr.OriginFile));
                    _insns.Add(new Instruction(OpCode.IS_ARRAY, null, arr.Line, arr.Col, arr.OriginFile));
                    EmitPatternFailJump(failJumps, arr);

                    _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, arr.Line, arr.Col, arr.OriginFile));
                    _insns.Add(new Instruction(OpCode.LEN, null, arr.Line, arr.Col, arr.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_INT, arr.Elements.Count, arr.Line, arr.Col, arr.OriginFile));
                    _insns.Add(new Instruction(OpCode.EQ, null, arr.Line, arr.Col, arr.OriginFile));
                    EmitPatternFailJump(failJumps, arr);

                    for (int i = 0; i < arr.Elements.Count; i++)
                    {
                        string elemVar = $"__match_elem_{_anonCounter++}";
                        MatchPattern sub = arr.Elements[i];
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_INT, i, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, sub.Line, sub.Col, sub.OriginFile));
                        EmitVarDeclTracked(elemVar, sub, trackInLocals: false);
                        EmitPatternMatch(sub, elemVar, failJumps);
                    }
                    return;

                case DictMatchPattern dict:
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, dict.Line, dict.Col, dict.OriginFile));
                    _insns.Add(new Instruction(OpCode.IS_DICT, null, dict.Line, dict.Col, dict.OriginFile));
                    EmitPatternFailJump(failJumps, dict);

                    foreach ((string key, MatchPattern sub) in dict.Entries)
                    {
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, key, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.HAS_KEY, null, sub.Line, sub.Col, sub.OriginFile));
                        EmitPatternFailJump(failJumps, sub);

                        string valueVar = $"__match_key_{_anonCounter++}";
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, key, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, sub.Line, sub.Col, sub.OriginFile));
                        EmitVarDeclTracked(valueVar, sub, trackInLocals: false);
                        EmitPatternMatch(sub, valueVar, failJumps);
                    }
                    return;

                case OrMatchPattern or:
                    {
                        // Alternatives are tried left to right. Every pattern check leaves the stack as it
                        // found it, so a failed alternative jumps straight to the next one; only a failure of
                        // the last alternative fails the whole pattern. Alternatives bind no variables, so
                        // the arm scope is the same whichever alternative matched.
                        List<int> successJumps = new();
                        for (int i = 0; i < or.Alternatives.Count - 1; i++)
                        {
                            List<int> alternativeFails = new();
                            EmitPatternMatch(or.Alternatives[i], sourceVar, alternativeFails);
                            successJumps.Add(_insns.Count);
                            _insns.Add(new Instruction(OpCode.JMP, null, or.Line, or.Col, or.OriginFile));

                            int nextAlternative = _insns.Count;
                            foreach (int failIdx in alternativeFails)
                                _insns[failIdx] = new Instruction(OpCode.JMP_IF_FALSE, nextAlternative, or.Line, or.Col, or.OriginFile);
                        }

                        EmitPatternMatch(or.Alternatives[^1], sourceVar, failJumps);

                        int matched = _insns.Count;
                        foreach (int successIdx in successJumps)
                            _insns[successIdx] = new Instruction(OpCode.JMP, matched, or.Line, or.Col, or.OriginFile);
                        return;
                    }

                default:
                    throw new CompilerException($"unsupported match pattern '{pattern.GetType().Name}'", pattern.Line, pattern.Col, pattern.OriginFile);
            }
        }

        private enum DestructureBindMode
        {
            VarDecl,

            ConstDecl,

            Assign
        }

        private void EmitDestructure(MatchPattern pattern, Expr value, DestructureBindMode mode, Node node)
        {
            CompileExpr(value);
            EmitDestructureBindingFromValue(pattern, mode, node);
        }

        private void EmitDestructureBinding(MatchPattern pattern, string sourceVar, DestructureBindMode mode, Node node)
        {
            _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, pattern.Line, pattern.Col, pattern.OriginFile));
            EmitDestructureBindingFromValue(pattern, mode, node);
        }

        private void EmitDestructureBindingFromValue(MatchPattern pattern, DestructureBindMode mode, Node node)
        {
            switch (pattern)
            {
                case WildcardMatchPattern:
                    _insns.Add(new Instruction(OpCode.POP, null, pattern.Line, pattern.Col, pattern.OriginFile));
                    return;

                case BindingMatchPattern bind:
                    switch (mode)
                    {
                        case DestructureBindMode.VarDecl:
                            _insns.Add(new Instruction(OpCode.VAR_DECL, bind.Name, bind.Line, bind.Col, bind.OriginFile));
                            if (_localVarsStack.Count > 0)
                                CurrentLocals.Add(bind.Name);
                            break;
                        case DestructureBindMode.ConstDecl:
                            _insns.Add(new Instruction(OpCode.CONST_DECL, bind.Name, bind.Line, bind.Col, bind.OriginFile));
                            if (_localVarsStack.Count > 0)
                                CurrentLocals.Add(bind.Name);
                            break;
                        case DestructureBindMode.Assign:
                            if (!TryEmitImplicitMemberStore(bind.Name, node))
                                _insns.Add(new Instruction(OpCode.STORE_VAR, bind.Name, bind.Line, bind.Col, bind.OriginFile));
                            break;
                    }
                    return;

                case ArrayMatchPattern arr:
                    for (int i = 0; i < arr.Elements.Count; i++)
                    {
                        MatchPattern sub = arr.Elements[i];
                        _insns.Add(new Instruction(OpCode.DUP, null, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_INT, i, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, sub.Line, sub.Col, sub.OriginFile));
                        EmitDestructureBindingFromValue(sub, mode, node);
                    }

                    _insns.Add(new Instruction(OpCode.POP, null, pattern.Line, pattern.Col, pattern.OriginFile));
                    return;

                case DictMatchPattern dict:
                    foreach ((string key, MatchPattern sub) in dict.Entries)
                    {
                        _insns.Add(new Instruction(OpCode.DUP, null, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, key, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, sub.Line, sub.Col, sub.OriginFile));
                        EmitDestructureBindingFromValue(sub, mode, node);
                    }

                    _insns.Add(new Instruction(OpCode.POP, null, pattern.Line, pattern.Col, pattern.OriginFile));
                    return;

                default:
                    throw new CompilerException($"unsupported destructuring pattern '{pattern.GetType().Name}'", pattern.Line, pattern.Col, pattern.OriginFile);
            }
        }

        private static List<string> CollectDestructureBindingNames(MatchPattern pattern)
        {
            List<string> names = new();

            static void Walk(MatchPattern p, List<string> acc)
            {
                switch (p)
                {
                    case WildcardMatchPattern:
                        return;

                    case BindingMatchPattern b:
                        acc.Add(b.Name);
                        return;

                    case ArrayMatchPattern a:
                        foreach (MatchPattern elem in a.Elements)
                            Walk(elem, acc);
                        return;

                    case DictMatchPattern d:
                        foreach ((string _, MatchPattern sub) in d.Entries)
                            Walk(sub, acc);
                        return;

                    default:
                        throw new CompilerException($"unsupported destructuring pattern '{p.GetType().Name}'", p.Line, p.Col, p.OriginFile);
                }
            }

            Walk(pattern, names);
            return names;
        }

        private void CompileMatchStatement(MatchStmt ms, bool insideFunction)
        {
            EmitPushScope(ms);

            string scrutineeVar = $"__match_scrut_{_anonCounter++}";
            CompileExpr(ms.Expression);
            EmitVarDeclTracked(scrutineeVar, ms, trackInLocals: false);

            List<int> endJumps = new();

            foreach (CaseClause c in ms.Cases)
            {
                bool enteredArmLocals = EnterMatchArmLocals();
                EmitPushScope(c);

                List<int> failJumps = new();
                EmitPatternMatch(c.Pattern, scrutineeVar, failJumps);

                if (c.Guard != null)
                {
                    CompileExpr(c.Guard);
                    EmitPatternFailJump(failJumps, c);
                }

                CompileStmt(c.Body, insideFunction);
                EmitPopScope(c);

                int jmpEnd = _insns.Count;
                _insns.Add(new Instruction(OpCode.JMP, null, c.Line, c.Col, c.OriginFile));
                endJumps.Add(jmpEnd);

                int failTarget = _insns.Count;
                foreach (int failIdx in failJumps)
                    _insns[failIdx] = new Instruction(OpCode.JMP_IF_FALSE, failTarget, c.Line, c.Col, c.OriginFile);

                _insns.Add(new Instruction(OpCode.POP_SCOPE, null, c.Line, c.Col, c.OriginFile));
                LeaveMatchArmLocals(enteredArmLocals);
            }

            if (ms.DefaultCase != null)
                CompileStmt(ms.DefaultCase, insideFunction);

            int endTarget = _insns.Count;
            EmitPopScope(ms);
            foreach (int idx in endJumps)
                _insns[idx] = new Instruction(OpCode.JMP, endTarget, ms.Line, ms.Col, ms.OriginFile);
        }

        private void CompileMatchExpression(MatchExpr me)
        {
            EmitPushScope(me);

            string scrutineeVar = $"__match_scrut_{_anonCounter++}";
            CompileExpr(me.Scrutinee);
            EmitVarDeclTracked(scrutineeVar, me, trackInLocals: false);

            List<int> endJumps = new();

            foreach (CaseExprArm arm in me.Arms)
            {
                bool enteredArmLocals = EnterMatchArmLocals();
                EmitPushScope(arm);

                List<int> failJumps = new();
                EmitPatternMatch(arm.Pattern, scrutineeVar, failJumps);

                if (arm.Guard != null)
                {
                    CompileExpr(arm.Guard);
                    EmitPatternFailJump(failJumps, arm);
                }

                CompileExpr(arm.Body);
                EmitPopScope(arm);

                int jmpEnd = _insns.Count;
                _insns.Add(new Instruction(OpCode.JMP, null, arm.Line, arm.Col, arm.OriginFile));
                endJumps.Add(jmpEnd);

                int failTarget = _insns.Count;
                foreach (int failIdx in failJumps)
                    _insns[failIdx] = new Instruction(OpCode.JMP_IF_FALSE, failTarget, arm.Line, arm.Col, arm.OriginFile);

                _insns.Add(new Instruction(OpCode.POP_SCOPE, null, arm.Line, arm.Col, arm.OriginFile));
                LeaveMatchArmLocals(enteredArmLocals);
            }

            if (me.DefaultArm != null)
                CompileExpr(me.DefaultArm);
            else
                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, me.Line, me.Col, me.OriginFile));

            int endTarget = _insns.Count;
            EmitPopScope(me);
            foreach (int idx in endJumps)
                _insns[idx] = new Instruction(OpCode.JMP, endTarget, me.Line, me.Col, me.OriginFile);
        }
    }
}
