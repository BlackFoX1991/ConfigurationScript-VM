using System.Collections.Generic;
using System.Linq;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// The EmitPushScope
        /// </summary>
        /// <param name="node">The node<see cref="Node"/></param>
        private void EmitPushScope(Node node)
        {
            _insns.Add(new Instruction(OpCode.PUSH_SCOPE, null, node.Line, node.Col, node.OriginFile));
            _scopeDepth++;
        }

        /// <summary>
        /// The EmitPopScope
        /// </summary>
        /// <param name="node">The node<see cref="Node"/></param>
        private void EmitPopScope(Node node)
        {
            if (_scopeDepth <= 0)
                throw new CompilerException("internal compiler error: scope underflow while emitting POP_SCOPE", node.Line, node.Col, node.OriginFile);

            _insns.Add(new Instruction(OpCode.POP_SCOPE, null, node.Line, node.Col, node.OriginFile));
            _scopeDepth--;
        }

        /// <summary>
        /// The ScopePopsTo
        /// </summary>
        /// <param name="fromDepth">The fromDepth<see cref="int"/></param>
        /// <param name="targetDepth">The targetDepth<see cref="int"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static int ScopePopsTo(int fromDepth, int targetDepth, Node node)
        {
            int pops = fromDepth - targetDepth;
            if (pops < 0)
                throw new CompilerException("internal compiler error: negative scope-pop count for loop leave", node.Line, node.Col, node.OriginFile);
            return pops;
        }

        /// <summary>
        /// The CompileBlockStatement
        /// </summary>
        /// <param name="b">The b<see cref="BlockStmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileBlockStatement(BlockStmt b, bool insideFunction)
        {
            if (insideFunction && b.IsFunctionBody)
            {
                foreach (Stmt sub in b.Statements)
                    CompileStmt(sub, insideFunction: true);
                return;
            }

            EmitPushScope(b);
            try
            {
                if (TryGetNamespaceScopePath(b, out _))
                {
                    List<InterfaceDeclStmt> namespaceInterfaces = b.Statements
                        .OfType<InterfaceDeclStmt>()
                        .ToList();

                    List<InterfaceDeclStmt> sortedNamespaceInterfaces = OrderInterfacesByInheritance(namespaceInterfaces);
                    foreach (InterfaceDeclStmt nsInterface in sortedNamespaceInterfaces)
                        CompileStmt(nsInterface, insideFunction: false);

                    List<ClassDeclStmt> namespaceClasses = b.Statements
                        .OfType<ClassDeclStmt>()
                        .ToList();

                    List<ClassDeclStmt> sortedNamespaceClasses = OrderClassesByInheritance(namespaceClasses);
                    ValidateInheritanceOverrides(sortedNamespaceClasses);
                    ValidateBaseConstructorCalls(sortedNamespaceClasses);
                    ValidateInterfaceImplementations(sortedNamespaceClasses);

                    foreach (ClassDeclStmt nsClass in sortedNamespaceClasses)
                        CompileStmt(nsClass, insideFunction: false);

                    foreach (Stmt sub in b.Statements)
                    {
                        if (sub is ClassDeclStmt || sub is InterfaceDeclStmt)
                            continue;

                        CompileStmt(sub, insideFunction);
                    }
                }
                else
                {
                    foreach (Stmt sub in b.Statements)
                        CompileStmt(sub, insideFunction);
                }
            }
            finally
            {
                EmitPopScope(b);
            }
        }

        /// <summary>
        /// The CompileIfStatement
        /// </summary>
        /// <param name="ifs">The ifs<see cref="IfStmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileIfStatement(IfStmt ifs, bool insideFunction)
        {
            CompileExpr(ifs.Condition);
            int jmpFalseIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, ifs.Line, ifs.Col, ifs.OriginFile));

            CompileStmt(ifs.ThenBlock, insideFunction);

            if (ifs.ElseBranch != null)
            {
                int jmpEndIdx = _insns.Count;
                _insns.Add(new Instruction(OpCode.JMP, null, ifs.Line, ifs.Col, ifs.OriginFile));
                _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, ifs.Line, ifs.Col, ifs.OriginFile);
                CompileStmt(ifs.ElseBranch, insideFunction);
                _insns[jmpEndIdx] = new Instruction(OpCode.JMP, _insns.Count, ifs.Line, ifs.Col, ifs.OriginFile);
            }
            else
            {
                _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, ifs.Line, ifs.Col, ifs.OriginFile);
            }
        }

        /// <summary>
        /// The CompileDoWhileStatement
        /// </summary>
        /// <param name="dws">The dws<see cref="DoWhileStmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileDoWhileStatement(DoWhileStmt dws, bool insideFunction)
        {
            int loopStart = _insns.Count;
            int loopScopeDepth = _scopeDepth;
            _breakLists.Push(new List<LoopLeavePatch>());
            _continueLists.Push(new List<LoopLeavePatch>());

            CompileStmt(dws.Body, insideFunction);

            int condStart = _insns.Count;

            foreach (LoopLeavePatch patch in _continueLists.Peek())
                _insns[patch.Index] = new Instruction(
                    OpCode.LEAVE,
                    new object[] { condStart, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, dws) },
                    dws.Line, dws.Col, dws.OriginFile);

            CompileExpr(dws.Condition);
            int jmpFalseIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, dws.Line, dws.Col, dws.OriginFile));

            _insns.Add(new Instruction(OpCode.JMP, loopStart, dws.Line, dws.Col, dws.OriginFile));
            _insns[jmpFalseIdx] = new Instruction(
                OpCode.JMP_IF_FALSE,
                _insns.Count,
                dws.Line, dws.Col, dws.OriginFile);

            foreach (LoopLeavePatch patch in _breakLists.Peek())
                _insns[patch.Index] = new Instruction(
                    OpCode.LEAVE,
                    new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, dws) },
                    dws.Line, dws.Col, dws.OriginFile);

            _breakLists.Pop();
            _continueLists.Pop();
        }

        /// <summary>
        /// The CompileWhileStatement
        /// </summary>
        /// <param name="ws">The ws<see cref="WhileStmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileWhileStatement(WhileStmt ws, bool insideFunction)
        {
            int loopStart = _insns.Count;
            int loopScopeDepth = _scopeDepth;
            _breakLists.Push(new List<LoopLeavePatch>());
            _continueLists.Push(new List<LoopLeavePatch>());

            CompileExpr(ws.Condition);
            int jmpFalseIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, ws.Line, ws.Col, ws.OriginFile));

            CompileStmt(ws.Body, insideFunction);

            foreach (LoopLeavePatch patch in _continueLists.Peek())
                _insns[patch.Index] = new Instruction(
                    OpCode.LEAVE,
                    new object[] { loopStart, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, ws) },
                    ws.Line, ws.Col, ws.OriginFile);

            _insns.Add(new Instruction(OpCode.JMP, loopStart, ws.Line, ws.Col, ws.OriginFile));
            _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, ws.Line, ws.Col, ws.OriginFile);

            foreach (LoopLeavePatch patch in _breakLists.Peek())
                _insns[patch.Index] = new Instruction(
                    OpCode.LEAVE,
                    new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, ws) },
                    ws.Line, ws.Col, ws.OriginFile);

            _breakLists.Pop();
            _continueLists.Pop();
        }

        /// <summary>
        /// The CompileForStatement
        /// </summary>
        /// <param name="fs">The fs<see cref="ForStmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileForStatement(ForStmt fs, bool insideFunction)
        {
            EmitPushScope(fs);
            int loopScopeDepth = _scopeDepth;

            if (fs.Init != null)
                CompileStmt(fs.Init, insideFunction);

            int loopStart = _insns.Count;
            _breakLists.Push(new List<LoopLeavePatch>());
            _continueLists.Push(new List<LoopLeavePatch>());

            if (fs.Condition != null)
            {
                CompileExpr(fs.Condition);
                int jmpFalseIdx = _insns.Count;
                _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, fs.Line, fs.Col, fs.OriginFile));

                CompileStmt(fs.Body, insideFunction);

                int incStart = _insns.Count;
                foreach (LoopLeavePatch patch in _continueLists.Peek())
                    _insns[patch.Index] = new Instruction(
                        OpCode.LEAVE,
                        new object[] { incStart, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, fs) },
                        fs.Line, fs.Col, fs.OriginFile);

                if (fs.Increment != null)
                    CompileStmt(fs.Increment, insideFunction);

                _insns.Add(new Instruction(OpCode.JMP, loopStart, fs.Line, fs.Col, fs.OriginFile));
                _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, fs.Line, fs.Col, fs.OriginFile);

                foreach (LoopLeavePatch patch in _breakLists.Peek())
                    _insns[patch.Index] = new Instruction(
                        OpCode.LEAVE,
                        new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, fs) },
                        fs.Line, fs.Col, fs.OriginFile);
            }
            else
            {
                CompileStmt(fs.Body, insideFunction);

                int incStart = _insns.Count;
                foreach (LoopLeavePatch patch in _continueLists.Peek())
                    _insns[patch.Index] = new Instruction(
                        OpCode.LEAVE,
                        new object[] { incStart, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, fs) },
                        fs.Line, fs.Col, fs.OriginFile);

                if (fs.Increment != null)
                    CompileStmt(fs.Increment, insideFunction);

                _insns.Add(new Instruction(OpCode.JMP, loopStart, fs.Line, fs.Col, fs.OriginFile));

                foreach (LoopLeavePatch patch in _breakLists.Peek())
                    _insns[patch.Index] = new Instruction(
                        OpCode.LEAVE,
                        new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, fs) },
                        fs.Line, fs.Col, fs.OriginFile);
            }

            _breakLists.Pop();
            _continueLists.Pop();

            EmitPopScope(fs);
        }

        /// <summary>
        /// The CompileForeachStatement
        /// </summary>
        /// <param name="fe">The fe<see cref="ForeachStmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileForeachStatement(ForeachStmt fe, bool insideFunction)
        {
            EmitPushScope(fe);
            int loopScopeDepth = _scopeDepth;

            string seqNm = $"__fe_seq_{_anonCounter++}";
            string keysNm = $"__fe_keys_{_anonCounter++}";
            string lenNm = $"__fe_len_{_anonCounter++}";
            string idxNm = $"__fe_i_{_anonCounter++}";
            string isDictNm = $"__fe_isdict_{_anonCounter++}";

            CompileExpr(fe.Iterable);
            _insns.Add(new Instruction(OpCode.VAR_DECL, seqNm, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.IS_DICT, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.VAR_DECL, isDictNm, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.LOAD_VAR, isDictNm, fe.Line, fe.Col, fe.OriginFile));
            int jmpIfNotDict = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.PUSH_STR, "keys", fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, 0, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.VAR_DECL, keysNm, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.LOAD_VAR, keysNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.PUSH_STR, "len", fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, 0, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.VAR_DECL, lenNm, fe.Line, fe.Col, fe.OriginFile));

            int jmpAfterDictInit = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP, null, fe.Line, fe.Col, fe.OriginFile));

            int notDictAddr = _insns.Count;
            _insns[jmpIfNotDict] = new Instruction(OpCode.JMP_IF_FALSE, notDictAddr, fe.Line, fe.Col, fe.OriginFile);

            _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.PUSH_STR, "len", fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, 0, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.VAR_DECL, lenNm, fe.Line, fe.Col, fe.OriginFile));

            int afterInit = _insns.Count;
            _insns[jmpAfterDictInit] = new Instruction(OpCode.JMP, afterInit, fe.Line, fe.Col, fe.OriginFile);

            _insns.Add(new Instruction(OpCode.PUSH_INT, 0, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.VAR_DECL, idxNm, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.VAR_DECL, fe.VarName, fe.Line, fe.Col, fe.OriginFile));
            if (_localVarsStack.Count > 0)
                CurrentLocals.Add(fe.VarName);

            if (fe.DeclareLocal && fe.TargetPattern is not null)
            {
                foreach (string binding in CollectDestructureBindingNames(fe.TargetPattern))
                {
                    _insns.Add(new Instruction(OpCode.PUSH_NULL, null, fe.Line, fe.Col, fe.OriginFile));
                    _insns.Add(new Instruction(OpCode.VAR_DECL, binding, fe.Line, fe.Col, fe.OriginFile));
                    if (_localVarsStack.Count > 0)
                        CurrentLocals.Add(binding);
                }
            }

            int loopStart = _insns.Count;
            _breakLists.Push(new List<LoopLeavePatch>());
            _continueLists.Push(new List<LoopLeavePatch>());

            _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.LOAD_VAR, lenNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.LT, null, fe.Line, fe.Col, fe.OriginFile));
            int jmpIfFalse = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.LOAD_VAR, isDictNm, fe.Line, fe.Col, fe.OriginFile));
            int jmpToSeqPath = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.LOAD_VAR, keysNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.SWAP, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.LOAD_VAR, keysNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.SWAP, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.NEW_ARRAY, 2, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.STORE_VAR, fe.VarName, fe.Line, fe.Col, fe.OriginFile));
            if (fe.TargetPattern is not null)
                EmitDestructureBinding(fe.TargetPattern, fe.VarName, DestructureBindMode.Assign, fe);

            int jmpAfterSet = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP, null, fe.Line, fe.Col, fe.OriginFile));

            int seqPathAddr = _insns.Count;
            _insns[jmpToSeqPath] = new Instruction(OpCode.JMP_IF_FALSE, seqPathAddr, fe.Line, fe.Col, fe.OriginFile);

            if (fe.UseIndexValuePair)
            {
                _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
                _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
                _insns.Add(new Instruction(OpCode.NEW_ARRAY, 2, fe.Line, fe.Col, fe.OriginFile));
                _insns.Add(new Instruction(OpCode.STORE_VAR, fe.VarName, fe.Line, fe.Col, fe.OriginFile));
            }
            else
            {
                _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
                _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
                _insns.Add(new Instruction(OpCode.STORE_VAR, fe.VarName, fe.Line, fe.Col, fe.OriginFile));
            }
            if (fe.TargetPattern is not null)
                EmitDestructureBinding(fe.TargetPattern, fe.VarName, DestructureBindMode.Assign, fe);

            int afterSet = _insns.Count;
            _insns[jmpAfterSet] = new Instruction(OpCode.JMP, afterSet, fe.Line, fe.Col, fe.OriginFile);

            CompileStmt(fe.Body, insideFunction);

            int incStart = _insns.Count;
            foreach (LoopLeavePatch patch in _continueLists.Peek())
                _insns[patch.Index] = new Instruction(
                    OpCode.LEAVE,
                    new object[] { incStart, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, fe) },
                    fe.Line, fe.Col, fe.OriginFile);

            _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.PUSH_INT, 1, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.ADD, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.STORE_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.JMP, loopStart, fe.Line, fe.Col, fe.OriginFile));
            _insns[jmpIfFalse] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, fe.Line, fe.Col, fe.OriginFile);

            foreach (LoopLeavePatch patch in _breakLists.Peek())
                _insns[patch.Index] = new Instruction(
                    OpCode.LEAVE,
                    new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, fe) },
                    fe.Line, fe.Col, fe.OriginFile);

            _breakLists.Pop();
            _continueLists.Pop();

            EmitPopScope(fe);
        }

        /// <summary>
        /// The CompileTryStatement
        /// </summary>
        /// <param name="ts">The ts<see cref="TryStmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileTryStatement(TryStmt ts, bool insideFunction)
        {
            int tryPushIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.TRY_PUSH, null, ts.Line, ts.Col, ts.OriginFile));

            CompileStmt(ts.TryBlock, insideFunction);
            _insns.Add(new Instruction(OpCode.TRY_POP, null, ts.Line, ts.Col, ts.OriginFile));

            int jmpAfterTryToEndIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP, null, ts.Line, ts.Col, ts.OriginFile));

            int catchStart = -1;
            if (ts.CatchBlock != null)
            {
                catchStart = _insns.Count;

                if (ts.CatchIdent != null)
                {
                    EmitPushScope(ts.CatchBlock);
                    _insns.Add(new Instruction(OpCode.VAR_DECL, ts.CatchIdent, ts.CatchBlock.Line, ts.CatchBlock.Col, ts.CatchBlock.OriginFile));
                }
                else
                {
                    _insns.Add(new Instruction(OpCode.POP, null, ts.CatchBlock.Line, ts.CatchBlock.Col, ts.CatchBlock.OriginFile));
                }

                CompileStmt(ts.CatchBlock, insideFunction);

                if (ts.CatchIdent != null)
                    EmitPopScope(ts.CatchBlock);

                _insns.Add(new Instruction(OpCode.TRY_POP, null, ts.Line, ts.Col, ts.OriginFile));
            }

            int finallyStart = -1;
            if (ts.FinallyBlock != null)
            {
                finallyStart = _insns.Count;
                EmitPushScope(ts.FinallyBlock);
                CompileStmt(ts.FinallyBlock, insideFunction);
                EmitPopScope(ts.FinallyBlock);
            }

            int endTryPopIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.TRY_POP, null, ts.Line, ts.Col, ts.OriginFile));

            _insns[tryPushIdx] = new Instruction(
                OpCode.TRY_PUSH,
                new object[] { catchStart, finallyStart },
                ts.Line, ts.Col, ts.OriginFile);

            _insns[jmpAfterTryToEndIdx] = new Instruction(
                OpCode.JMP,
                endTryPopIdx,
                ts.Line, ts.Col, ts.OriginFile);
        }

        /// <summary>
        /// The CompileUsingStatement
        /// </summary>
        /// <param name="us">The us<see cref="UsingStmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileUsingStatement(UsingStmt us, bool insideFunction)
        {
            EmitPushScope(us);
            try
            {
                string resourceTemp = $"__using_resource_{_anonCounter++}";

                CompileExpr(us.Resource);
                _insns.Add(new Instruction(OpCode.VAR_DECL, resourceTemp, us.Line, us.Col, us.OriginFile));
                if (_localVarsStack.Count > 0)
                    CurrentLocals.Add(resourceTemp);

                if (us.BindingName != null)
                {
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, resourceTemp, us.Line, us.Col, us.OriginFile));
                    _insns.Add(new Instruction(
                        us.BindingIsConst ? OpCode.CONST_DECL : OpCode.VAR_DECL,
                        us.BindingName,
                        us.Line,
                        us.Col,
                        us.OriginFile));

                    if (_localVarsStack.Count > 0)
                        CurrentLocals.Add(us.BindingName);
                }

                int tryPushIdx = _insns.Count;
                _insns.Add(new Instruction(OpCode.TRY_PUSH, null, us.Line, us.Col, us.OriginFile));

                CompileStmt(us.Body, insideFunction);
                _insns.Add(new Instruction(OpCode.TRY_POP, null, us.Line, us.Col, us.OriginFile));

                int jmpAfterBodyToEndIdx = _insns.Count;
                _insns.Add(new Instruction(OpCode.JMP, null, us.Line, us.Col, us.OriginFile));

                int finallyStart = _insns.Count;
                _insns.Add(new Instruction(OpCode.LOAD_VAR, resourceTemp, us.Line, us.Col, us.OriginFile));
                _insns.Add(new Instruction(OpCode.DESTROY, null, us.Line, us.Col, us.OriginFile));

                int endTryPopIdx = _insns.Count;
                _insns.Add(new Instruction(OpCode.TRY_POP, null, us.Line, us.Col, us.OriginFile));

                _insns[tryPushIdx] = new Instruction(
                    OpCode.TRY_PUSH,
                    new object[] { -1, finallyStart },
                    us.Line, us.Col, us.OriginFile);

                _insns[jmpAfterBodyToEndIdx] = new Instruction(
                    OpCode.JMP,
                    endTryPopIdx,
                    us.Line, us.Col, us.OriginFile);
            }
            finally
            {
                EmitPopScope(us);
            }
        }

        /// <summary>
        /// The CompileThrowStatement
        /// </summary>
        /// <param name="th">The th<see cref="ThrowStmt"/></param>
        private void CompileThrowStatement(ThrowStmt th)
        {
            CompileExpr(th.Value);
            _insns.Add(new Instruction(OpCode.THROW, null, th.Line, th.Col, th.OriginFile));
        }

        /// <summary>
        /// The CompileYieldStatement
        /// </summary>
        /// <param name="ys">The ys<see cref="YieldStmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileYieldStatement(YieldStmt ys, bool insideFunction)
        {
            if (!insideFunction)
                throw new CompilerException("yield can only be used in function statements", ys.Line, ys.Col, ys.OriginFile);

            if (_asyncFunctionDepth <= 0)
                throw new CompilerException("yield can only be used in async function statements", ys.Line, ys.Col, ys.OriginFile);

            _insns.Add(new Instruction(OpCode.YIELD, null, ys.Line, ys.Col, ys.OriginFile));
            _insns.Add(new Instruction(OpCode.POP, null, ys.Line, ys.Col, ys.OriginFile));
        }

        /// <summary>
        /// The CompileContinueStatement
        /// </summary>
        /// <param name="node">The node<see cref="Node"/></param>
        private void CompileContinueStatement(Node node)
        {
            if (_continueLists.Count == 0)
                throw new VMException("Compile error: 'continue' outside of loop.", node.Line, node.Col, node.OriginFile, false, null!);

            int leaveIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.LEAVE, null, node.Line, node.Col, node.OriginFile));
            _continueLists.Peek().Add(new LoopLeavePatch(leaveIdx, _scopeDepth));
        }

        /// <summary>
        /// The CompileBreakStatement
        /// </summary>
        /// <param name="node">The node<see cref="Node"/></param>
        private void CompileBreakStatement(Node node)
        {
            if (_breakLists.Count == 0)
                throw new VMException("Compile error: 'break' outside of loop.", node.Line, node.Col, node.OriginFile, false, null!);

            int leaveIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.LEAVE, null, node.Line, node.Col, node.OriginFile));
            _breakLists.Peek().Add(new LoopLeavePatch(leaveIdx, _scopeDepth));
        }

        /// <summary>
        /// The CompileReturnStatement
        /// </summary>
        /// <param name="rs">The rs<see cref="ReturnStmt"/></param>
        private void CompileReturnStatement(ReturnStmt rs)
        {
            if (rs.Value != null)
                CompileExpr(rs.Value);
            else
                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, rs.Line, rs.Col, rs.OriginFile));

            _insns.Add(new Instruction(OpCode.RET, null, rs.Line, rs.Col, rs.OriginFile));
        }

        /// <summary>
        /// The CompileOutExpression
        /// </summary>
        /// <param name="ox">The ox<see cref="OutExpr"/></param>
        private void CompileOutExpression(OutExpr ox)
        {
            List<Stmt> stmts = ox.Body.Statements;

            int lastExprIdx = -1;
            for (int i = stmts.Count - 1; i >= 0; i--)
            {
                if (stmts[i] is ExprStmt)
                {
                    lastExprIdx = i;
                    break;
                }
            }

            EmitPushScope(ox);

            for (int i = 0; i < stmts.Count; i++)
            {
                if (stmts[i] is ExprStmt es)
                {
                    CompileExpr(es.Expression);
                    if (i != lastExprIdx)
                        _insns.Add(new Instruction(OpCode.POP, null, es.Line, es.Col, es.OriginFile));
                }
                else
                {
                    CompileStmt(stmts[i], insideFunction: false);
                }
            }

            if (lastExprIdx == -1)
                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, ox.Line, ox.Col, ox.OriginFile));

            EmitPopScope(ox);
        }

        /// <summary>
        /// The CompileConditionalExpression
        /// </summary>
        /// <param name="cnd">The cnd<see cref="ConditionalExpr"/></param>
        private void CompileConditionalExpression(ConditionalExpr cnd)
        {
            CompileExpr(cnd.Condition);

            int jmpIfFalseIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, cnd.Line, cnd.Col, cnd.OriginFile));

            CompileExpr(cnd.ThenExpr);

            int jmpEndIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP, null, cnd.Line, cnd.Col, cnd.OriginFile));

            _insns[jmpIfFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, cnd.Line, cnd.Col, cnd.OriginFile);

            CompileExpr(cnd.ElseExpr);

            _insns[jmpEndIdx] = new Instruction(OpCode.JMP, _insns.Count, cnd.Line, cnd.Col, cnd.OriginFile);
        }
    }
}
