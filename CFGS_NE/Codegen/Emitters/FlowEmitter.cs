using System.Collections.Generic;
using System.Linq;
using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        private void EmitPushScope(Node node)
        {
            _scopePairingStack.Push(_insns.Count);
            _insns.Add(new Instruction(OpCode.PUSH_SCOPE, null, node.Line, node.Col, node.OriginFile));
            _scopeDepth++;
        }

        private void EmitPopScope(Node node)
        {
            if (_scopeDepth <= 0)
                throw new CompilerException("internal compiler error: scope underflow while emitting POP_SCOPE", node.Line, node.Col, node.OriginFile);

            // Record the real push/pop pairing before adding the POP_SCOPE. Raw fail-path POP_SCOPEs
            // (emitted without EmitPopScope) are intentionally not paired.
            if (_scopePairingStack.Count > 0)
                _scopePairings[_scopePairingStack.Pop()] = _insns.Count;

            _insns.Add(new Instruction(OpCode.POP_SCOPE, null, node.Line, node.Col, node.OriginFile));
            _scopeDepth--;
        }

        private static int ScopePopsTo(int fromDepth, int targetDepth, Node node)
        {
            int pops = fromDepth - targetDepth;
            if (pops < 0)
                throw new CompilerException("internal compiler error: negative scope-pop count for loop leave", node.Line, node.Col, node.OriginFile);
            return pops;
        }

        // A block that binds no name at its own level needs no runtime scope: skipping its PUSH_SCOPE /
        // POP_SCOPE removes a per-iteration Env allocation in hot loops (the while/if bodies of tight loops
        // declare nothing) and lets inner slot accesses resolve one hop shallower. Because the scope is never
        // emitted, _scopeDepth stays consistent (so break/continue LEAVE pop-counts remain correct) and the
        // descriptor/slot passes never see it (so hop counts are correct) — no post-hoc fixup is needed.
        private bool BlockNeedsOwnScope(BlockStmt b)
        {
            // The declarators of `for (var i = 0, j = 1; ...)` bind in the loop scope itself.
            if (b.IsDeclarationGroup)
                return false;

            // Namespace scope blocks publish types through a dedicated scope; always keep it.
            if (TryGetNamespaceScopePath(b, out _))
                return true;

            foreach (Stmt sub in b.Statements)
                if (StatementBindsNameInEnclosingScope(sub))
                    return true;

            return false;
        }

        // Conservative whitelist: only statements known NOT to introduce a name into the enclosing block
        // scope return false. Control-flow constructs bind their loop / catch / using variables in their own
        // scopes, not here. Anything else (var/const/destructure/func/class/interface/enum/namespace/import
        // declarations, and unknown node types) keeps the scope.
        private static bool StatementBindsNameInEnclosingScope(Stmt s)
        {
            switch (s)
            {
                case ExprStmt:
                case AssignStmt:
                case AssignExprStmt:
                case CompoundAssignStmt:
                case DestructureAssignStmt:
                case SliceSetStmt:
                case PushStmt:
                case DeleteExprStmt:
                case ReturnStmt:
                case BreakStmt:
                case ContinueStmt:
                case ThrowStmt:
                case YieldStmt:
                case EmptyStmt:
                case ErrorStmt:
                case IfStmt:
                case WhileStmt:
                case DoWhileStmt:
                case ForStmt:
                case ForeachStmt:
                case MatchStmt:
                case TryStmt:
                case UsingStmt:
                    return false;

                // A nested block binds in its own scope, except a declaration group, which binds here.
                case BlockStmt block:
                    return block.IsDeclarationGroup;

                // export <decl> binds iff its wrapped declaration does.
                case ExportStmt ex:
                    return StatementBindsNameInEnclosingScope(ex.Inner);

                default:
                    return true;
            }
        }

        private void CompileBlockStatement(BlockStmt b, bool insideFunction)
        {
            if (insideFunction && b.IsFunctionBody)
            {
                foreach (Stmt sub in b.Statements)
                    CompileStmt(sub, insideFunction: true);
                return;
            }

            bool ownScope = BlockNeedsOwnScope(b);
            if (ownScope)
                EmitPushScope(b);
            try
            {
                if (TryGetNamespaceScopePath(b, out _))
                {
                    InterfaceCatalog interfaceCatalog = new();
                    ClassCatalog classCatalog = new();
                    HashSet<Stmt> emittedNamespacePrelude = [];
                    Dictionary<string, List<Stmt>> namespaceTypePublications = b.Statements
                        .Where(static stmt => TryGetPublishedNamespaceMemberName(stmt, out _))
                        .GroupBy(static stmt => GetPublishedNamespaceMemberName(stmt), StringComparer.Ordinal)
                        .ToDictionary(
                            static group => group.Key,
                            static group => group.ToList(),
                            StringComparer.Ordinal);
                    HashSet<Stmt> emittedNamespacePublications = [];

                    foreach (Stmt sub in b.Statements)
                    {
                        if (!IsNamespacePreludeStmt(sub))
                            continue;

                        emittedNamespacePrelude.Add(sub);
                        CompileStmt(sub, insideFunction);
                    }

                    List<InterfaceDeclStmt> namespaceInterfaces = b.Statements
                        .OfType<InterfaceDeclStmt>()
                        .ToList();

                    List<InterfaceDeclStmt> sortedNamespaceInterfaces = interfaceCatalog.OrderByInheritance(this, namespaceInterfaces);
                    foreach (InterfaceDeclStmt nsInterface in sortedNamespaceInterfaces)
                    {
                        CompileStmt(nsInterface, insideFunction: false);
                        EmitNamespaceTypePublications(nsInterface.Name, namespaceTypePublications, emittedNamespacePublications, insideFunction);
                    }

                    List<ClassDeclStmt> namespaceClasses = b.Statements
                        .OfType<ClassDeclStmt>()
                        .ToList();

                    List<ClassDeclStmt> sortedNamespaceClasses = classCatalog.OrderByInheritance(this, namespaceClasses);

                    foreach (ClassDeclStmt nsClass in sortedNamespaceClasses)
                    {
                        CompileStmt(nsClass, insideFunction: false);
                        EmitNamespaceTypePublications(nsClass.Name, namespaceTypePublications, emittedNamespacePublications, insideFunction);
                    }

                    foreach (Stmt sub in b.Statements)
                    {
                        if (sub is ClassDeclStmt ||
                            sub is InterfaceDeclStmt ||
                            emittedNamespacePrelude.Contains(sub) ||
                            emittedNamespacePublications.Contains(sub))
                        {
                            continue;
                        }

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
                if (ownScope)
                    EmitPopScope(b);
            }
        }

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

        private void EmitNamespaceTypePublications(
            string memberName,
            IReadOnlyDictionary<string, List<Stmt>> namespaceTypePublications,
            ISet<Stmt> emittedNamespacePublications,
            bool insideFunction)
        {
            if (!namespaceTypePublications.TryGetValue(memberName, out List<Stmt>? publications))
                return;

            foreach (Stmt publication in publications)
            {
                if (!emittedNamespacePublications.Add(publication))
                    continue;

                CompileStmt(publication, insideFunction);
            }
        }

        private static bool TryGetPublishedNamespaceMemberName(Stmt stmt, out string memberName)
        {
            switch (stmt)
            {
                case AssignExprStmt
                {
                    IsSyntheticNamespacePath: true,
                    Target: IndexExpr
                    {
                        Index: StringExpr key
                    },
                    Value: VarExpr valueVar
                } when string.Equals(key.Value, valueVar.Name, StringComparison.Ordinal):
                    memberName = key.Value;
                    return true;

                case AssignStmt
                {
                    Name: string hiddenBindingName,
                    Value: VarExpr valueVar
                } when hiddenBindingName.StartsWith(NamespaceRuntimeBindingNames.HiddenMemberPrefix, StringComparison.Ordinal):
                    memberName = valueVar.Name;
                    return true;

                default:
                    memberName = string.Empty;
                    return false;
            }
        }

        private static bool IsNamespacePreludeStmt(Stmt stmt)
        {
            return stmt switch
            {
                VarDecl { Name: string name } when name.StartsWith("__ns_scope_", StringComparison.Ordinal) => true,
                VarDecl { Name: string name } when name.StartsWith(NamespaceRuntimeBindingNames.HiddenMemberPrefix, StringComparison.Ordinal) => true,
                _ => false
            };
        }

        private static string GetPublishedNamespaceMemberName(Stmt stmt)
        {
            if (!TryGetPublishedNamespaceMemberName(stmt, out string memberName))
                throw new InvalidOperationException("statement is not a namespace publication");

            return memberName;
        }

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

        // Names the for-initializer binds. These are the ones that get a fresh per-iteration
        // binding; an initializer that only assigns or evaluates (`for (; i < n; i++)`) binds
        // nothing, so such a loop keeps the plain single-scope shape.
        private static List<string> CollectForInitBindingNames(Stmt? init)
        {
            switch (init)
            {
                case VarDecl v:
                    return [v.Name];
                case ConstDecl c:
                    return [c.Name];
                case DestructureDeclStmt d:
                    return CollectDestructureBindingNames(d.Pattern);
                case BlockStmt { IsDeclarationGroup: true } group:
                    return group.Statements.SelectMany(CollectForInitBindingNames).ToList();
                default:
                    return [];
            }
        }

        // Only a closure can outlive the iteration that created it, so only a body that can build one
        // needs the per-iteration scope. A tight numeric loop keeps the single-scope shape and its
        // original speed; the extra Env allocation and write-back cost roughly 40% on such a loop.
        // The scan is exhaustive by construction (see NodeWalker), so a body shape not considered
        // here cannot silently fall back to shared capture.
        private static bool BodyCanOutliveIteration(Stmt body)
            => NodeWalker.AnyDescendantOrSelf(body, static n => n is FuncExpr or FuncDeclStmt or ClassDeclStmt);

        private void CompileForStatement(ForStmt fs, bool insideFunction)
        {
            // The outer scope carries the initializer bindings across iterations; the loop body runs
            // in a per-iteration scope that re-declares them, so a closure created in the body
            // captures that iteration's value instead of one binding shared by the whole loop. This
            // matches foreach. The carried value is read at the top of each iteration and written
            // back at the bottom, so the increment and the condition still see a single logical
            // variable.
            EmitPushScope(fs);
            int loopScopeDepth = _scopeDepth;

            if (fs.Init != null)
                CompileStmt(fs.Init, insideFunction);

            List<string> carried = CollectForInitBindingNames(fs.Init);
            bool perIteration = carried.Count > 0 && BodyCanOutliveIteration(fs.Body);
            bool carryBack = perIteration && fs.Init is not ConstDecl;

            int loopStart = _insns.Count;
            _breakLists.Push(new List<LoopLeavePatch>());
            _continueLists.Push(new List<LoopLeavePatch>());

            // The condition is evaluated before the per-iteration scope exists, reading the carried
            // binding. That keeps the false path free of a scope to unwind.
            int jmpFalseIdx = -1;
            if (fs.Condition != null)
            {
                CompileExpr(fs.Condition);
                jmpFalseIdx = _insns.Count;
                _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, fs.Line, fs.Col, fs.OriginFile));
            }

            int bodyScopeDepth = loopScopeDepth;
            if (perIteration)
            {
                EmitPushScope(fs);
                bodyScopeDepth = _scopeDepth;

                foreach (string name in carried)
                {
                    // Reads the carried binding, because the inner slot is not declared yet.
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, name, fs.Line, fs.Col, fs.OriginFile));
                    _insns.Add(new Instruction(
                        fs.Init is ConstDecl ? OpCode.CONST_DECL : OpCode.VAR_DECL,
                        name, fs.Line, fs.Col, fs.OriginFile));
                    if (_localVarsStack.Count > 0)
                        CurrentLocals.Add(name);
                }
            }

            CompileStmt(fs.Body, insideFunction);

            // `continue` skips to the increment while still inside the per-iteration scope, so the
            // increment and the write-back below apply to this iteration's binding.
            int incStart = _insns.Count;
            foreach (LoopLeavePatch patch in _continueLists.Peek())
                _insns[patch.Index] = new Instruction(
                    OpCode.LEAVE,
                    new object[] { incStart, ScopePopsTo(patch.ScopeDepth, bodyScopeDepth, fs) },
                    fs.Line, fs.Col, fs.OriginFile);

            if (perIteration)
            {
                // Stack whatever the body left in the iteration bindings, drop the scope, then write
                // them back. The order matters: Env.SetValue resolves innermost first, so the stores
                // have to happen after the pop to reach the carried binding.
                if (carryBack)
                    foreach (string name in carried)
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, name, fs.Line, fs.Col, fs.OriginFile));

                EmitPopScope(fs);

                if (carryBack)
                    for (int i = carried.Count - 1; i >= 0; i--)
                        _insns.Add(new Instruction(OpCode.STORE_VAR, carried[i], fs.Line, fs.Col, fs.OriginFile));
            }

            // The increment runs on the carried binding, after the per-iteration scope is gone. If it
            // ran inside that scope it would advance the very binding the body's closures captured,
            // and every closure would again observe the next iteration's value.
            if (fs.Increment != null)
                CompileStmt(fs.Increment, insideFunction);

            _insns.Add(new Instruction(OpCode.JMP, loopStart, fs.Line, fs.Col, fs.OriginFile));

            if (jmpFalseIdx >= 0)
                _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, fs.Line, fs.Col, fs.OriginFile);

            foreach (LoopLeavePatch patch in _breakLists.Peek())
                _insns[patch.Index] = new Instruction(
                    OpCode.LEAVE,
                    new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, fs) },
                    fs.Line, fs.Col, fs.OriginFile);

            _breakLists.Pop();
            _continueLists.Pop();

            EmitPopScope(fs);
        }

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
            _insns.Add(new Instruction(OpCode.DICT_KEYS, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.VAR_DECL, keysNm, fe.Line, fe.Col, fe.OriginFile));

            _insns.Add(new Instruction(OpCode.LOAD_VAR, keysNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.LEN, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.VAR_DECL, lenNm, fe.Line, fe.Col, fe.OriginFile));

            int jmpAfterDictInit = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP, null, fe.Line, fe.Col, fe.OriginFile));

            int notDictAddr = _insns.Count;
            _insns[jmpIfNotDict] = new Instruction(OpCode.JMP_IF_FALSE, notDictAddr, fe.Line, fe.Col, fe.OriginFile);

            _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.LEN, null, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.VAR_DECL, lenNm, fe.Line, fe.Col, fe.OriginFile));

            int afterInit = _insns.Count;
            _insns[jmpAfterDictInit] = new Instruction(OpCode.JMP, afterInit, fe.Line, fe.Col, fe.OriginFile);

            _insns.Add(new Instruction(OpCode.PUSH_INT, 0, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.VAR_DECL, idxNm, fe.Line, fe.Col, fe.OriginFile));

            int loopStart = _insns.Count;
            _breakLists.Push(new List<LoopLeavePatch>());
            _continueLists.Push(new List<LoopLeavePatch>());

            _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.LOAD_VAR, lenNm, fe.Line, fe.Col, fe.OriginFile));
            _insns.Add(new Instruction(OpCode.LT, null, fe.Line, fe.Col, fe.OriginFile));
            int jmpIfFalse = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, fe.Line, fe.Col, fe.OriginFile));

            // Per-iteration scope: the loop variable (and any destructuring bindings) get a FRESH
            // binding each iteration, so closures created in the body capture that iteration's value
            // (C# foreach semantics). The sequence/index/length temps stay in the outer foreach scope
            // because they must persist across iterations.
            EmitPushScope(fe);
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

            // Close the per-iteration scope on the normal path before advancing the index. A
            // continue's LEAVE pops this scope at runtime and jumps to incStart (past this POP_SCOPE);
            // a break's LEAVE likewise pops it on the way to the loop end.
            EmitPopScope(fe);

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

            // The catch/finally handlers run via TRY routing at a scope depth that need not match their
            // lexical nesting; keep their variable access name-based.
            int firstHandler = catchStart >= 0 ? catchStart : finallyStart;
            if (firstHandler >= 0)
                _nameBasedRanges.Add((firstHandler, endTryPopIdx));

            _insns[tryPushIdx] = new Instruction(
                OpCode.TRY_PUSH,
                new object[] { catchStart, finallyStart },
                ts.Line, ts.Col, ts.OriginFile);

            _insns[jmpAfterTryToEndIdx] = new Instruction(
                OpCode.JMP,
                endTryPopIdx,
                ts.Line, ts.Col, ts.OriginFile);
        }

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

                // The dispose runs via TRY routing (also on the return/throw path, where the body's
                // POP_SCOPEs are skipped), so its resource access must stay name-based.
                _nameBasedRanges.Add((finallyStart, endTryPopIdx));

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

        private void CompileThrowStatement(ThrowStmt th)
        {
            CompileExpr(th.Value);
            _insns.Add(new Instruction(OpCode.THROW, null, th.Line, th.Col, th.OriginFile));
        }

        private void CompileYieldStatement(YieldStmt ys, bool insideFunction)
        {
            if (!insideFunction)
                throw new CompilerException("yield can only be used in function statements", ys.Line, ys.Col, ys.OriginFile);

            if (_asyncFunctionDepth <= 0)
                throw new CompilerException("yield can only be used in async function statements", ys.Line, ys.Col, ys.OriginFile);

            _insns.Add(new Instruction(OpCode.YIELD, null, ys.Line, ys.Col, ys.OriginFile));
            _insns.Add(new Instruction(OpCode.POP, null, ys.Line, ys.Col, ys.OriginFile));
        }

        private void CompileContinueStatement(Node node)
        {
            // CompilerException, not VMException: this fires during codegen, and the rest of the compiler
            // reports through CompilerException. Using a runtime exception type here made the CLI and the
            // language server categorize these two diagnostics differently from every other compile error.
            if (!IsInsideLoopOfCurrentFunction)
                throw new CompilerException("'continue' outside of loop", node.Line, node.Col, node.OriginFile);

            int leaveIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.LEAVE, null, node.Line, node.Col, node.OriginFile));
            _continueLists.Peek().Add(new LoopLeavePatch(leaveIdx, _scopeDepth));
        }

        private void CompileBreakStatement(Node node)
        {
            if (!IsInsideLoopOfCurrentFunction)
                throw new CompilerException("'break' outside of loop", node.Line, node.Col, node.OriginFile);

            int leaveIdx = _insns.Count;
            _insns.Add(new Instruction(OpCode.LEAVE, null, node.Line, node.Col, node.OriginFile));
            _breakLists.Peek().Add(new LoopLeavePatch(leaveIdx, _scopeDepth));
        }

        private void CompileReturnStatement(ReturnStmt rs)
        {
            if (rs.Value != null)
                CompileExpr(rs.Value);
            else
                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, rs.Line, rs.Col, rs.OriginFile));

            _insns.Add(new Instruction(OpCode.RET, null, rs.Line, rs.Col, rs.OriginFile));
        }

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
