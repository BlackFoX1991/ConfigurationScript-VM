using CFGS_VM.Analytic;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extension;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Defines the <see cref="Compiler" />
    /// </summary>
    public class Compiler(string fname)
    {
        /// <summary>
        /// Defines the _anonCounter
        /// </summary>
        private int _anonCounter = 0;

        /// <summary>
        /// Gets or sets the FileName
        /// </summary>
        public string FileName { get; set; } = fname;

        /// <summary>
        /// The IsBuiltinFunction
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsBuiltinFunction(string name) => VM.bInFunc.ContainsKey(name);

        /// <summary>
        /// Defines the _insns
        /// </summary>
        private readonly List<Instruction> _insns = [];

        /// <summary>
        /// Defines the _breakLists
        /// </summary>
        private readonly Stack<List<int>> _breakLists = new();

        /// <summary>
        /// Defines the _continueLists
        /// </summary>
        private readonly Stack<List<int>> _continueLists = new();

        /// <summary>
        /// Defines the _functions
        /// </summary>
        public Dictionary<string, FunctionInfo> _functions = [];

        /// <summary>
        /// The Compile
        /// </summary>
        /// <param name="program">The program<see cref="List{Stmt}"/></param>
        /// <returns>The <see cref="List{Instruction}"/></returns>
        public List<Instruction> Compile(List<Stmt> program)
        {
            Stmt? LastStmt = null;
            foreach (var s in program)
            {
                CompileStmt(s, insideFunction: false);
                LastStmt = s;
            }
            if (LastStmt is not null)
                _insns.Add(new Instruction(OpCode.HALT, null, LastStmt.Line + 1, 1, LastStmt.OriginFile));
            else
                _insns.Add(new Instruction(OpCode.HALT));

            return _insns;
        }

        /// <summary>
        /// The CompileStmt
        /// </summary>
        /// <param name="s">The s<see cref="Stmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileStmt(Stmt s, bool insideFunction)
        {
            switch (s)
            {

                case VarDecl v:
                    if (v.Value != null)
                        CompileExpr(v.Value);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, s.Line, s.Col, s.OriginFile));

                    _insns.Add(new Instruction(OpCode.VAR_DECL, v.Name, s.Line, s.Col, s.OriginFile));
                    break;

                case AssignStmt a:
                    CompileExpr(a.Value);
                    _insns.Add(new Instruction(OpCode.STORE_VAR, a.Name, s.Line, s.Col, s.OriginFile));
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
                        _insns.Add(new Instruction(OpCode.PUSH_INT, 0, sliceSet.Line, sliceSet.Col, s.OriginFile));

                    if (sliceSet.Slice.End is not null)
                        CompileExpr(sliceSet.Slice.End);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_INT, null, sliceSet.Line, sliceSet.Col, s.OriginFile));

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

                        if (ps.Target is VarExpr v)
                        {
                            _insns.Add(new Instruction(OpCode.ARRAY_PUSH, v.Name, s.Line, s.Col, s.OriginFile));
                        }
                        else if (ps.Target is IndexExpr eie)
                        {
                            CompileExpr(eie);
                            _insns.Add(new Instruction(OpCode.ARRAY_PUSH, null, s.Line, s.Col, s.OriginFile));
                        }

                        else
                        {
                            throw new CompilerException("push [] nur für Variablen oder IndexExpr unterstützt.", ps.Line, ps.Col, s.OriginFile);
                        }
                        break;
                    }

                case DeleteIndexStmt di:
                    CompileExpr(di.Index);
                    _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ELEM, di.Name, s.Line, s.Col, s.OriginFile));
                    break;

                case DeleteVarStmt dv:
                    _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ALL, dv.Name, s.Line, s.Col, s.OriginFile));
                    break;

                case DeleteAllStmt das:
                    {
                        if (das.Target is VarExpr var)
                        {
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ALL, var.Name, das.Line, das.Col, s.OriginFile));
                        }
                        else if (das.Target is IndexExpr xie)
                        {
                            CompileExpr(xie.Target);
                            CompileExpr(xie.Index);
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ELEM_ALL, null, das.Line, das.Col, s.OriginFile));
                        }

                        else
                        {
                            throw new CompilerException("delete [] only supported for variables or indexed containers.", das.Line, das.Col, s.OriginFile);
                        }
                        break;
                    }

                case DeleteExprStmt des:
                    if (des.Target is IndexExpr ie)
                    {
                        CompileExpr(ie.Target);
                        CompileExpr(ie.Index);

                        if (des.DeleteAll)
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ELEM_ALL, null, des.Line, des.Col, s.OriginFile));
                        else
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ELEM, null, des.Line, des.Col, s.OriginFile));
                    }
                    else if (des.Target is VarExpr v)
                    {
                        if (des.DeleteAll)
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ALL, v.Name, des.Line, des.Col, s.OriginFile));
                        else
                            throw new CompilerException("delete without [] not supported for plain variables", des.Line, des.Col, s.OriginFile);
                    }
                    else
                    {
                        throw new CompilerException("delete [] only supported for variables or indexed containers.", des.Line, des.Col, s.OriginFile);
                    }
                    break;

                case ClassDeclStmt cds:
                    {
                        int jmpOverCtorIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, cds.Line, cds.Col, s.OriginFile));

                        var initMethod = cds.Methods.FirstOrDefault(m => m.Name == "init");
                        var ctorParams = initMethod != null ? new List<string>(initMethod.Parameters)
                                                            : new List<string>(cds.Parameters);

                        int ctorStart = _insns.Count;
                        _functions[$"__ctor_{cds.Name}"] = new FunctionInfo(ctorParams, ctorStart);

                        const string SELF = "__obj";
                        _insns.Add(new Instruction(OpCode.NEW_OBJECT, cds.Name, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, SELF, cds.Line, cds.Col, s.OriginFile));

                        foreach (var kv in cds.Fields)
                        {
                            string fieldName = kv.Key;
                            Expr? initExpr = kv.Value;

                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, fieldName, cds.Line, cds.Col, s.OriginFile));

                            if (initExpr != null)
                                CompileExpr(initExpr);
                            else
                                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, cds.Line, cds.Col, s.OriginFile));

                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (var p in ctorParams)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, p, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, p, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.ROT, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (var en in cds.Enums)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, en.Name, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.NEW_DICT, 0, en.Line, en.Col, s.OriginFile));

                            var reverseDict = new Dictionary<int, string>();
                            foreach (var member in en.Members)
                            {
                                _insns.Add(new Instruction(OpCode.DUP, null, en.Line, en.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.PUSH_STR, member.Name, en.Line, en.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.PUSH_INT, (int)member.Value, en.Line, en.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.INDEX_SET, null, en.Line, en.Col, s.OriginFile));
                                reverseDict[(int)member.Value] = member.Name;
                            }

                            _insns.Add(new Instruction(OpCode.DUP, null, en.Line, en.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__name", en.Line, en.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.NEW_DICT, 0, en.Line, en.Col, s.OriginFile));
                            foreach (var kv2 in reverseDict)
                            {
                                _insns.Add(new Instruction(OpCode.DUP, null, en.Line, en.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.PUSH_INT, kv2.Key, en.Line, en.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.PUSH_STR, kv2.Value, en.Line, en.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.INDEX_SET, null, en.Line, en.Col, s.OriginFile));
                            }
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, en.Line, en.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, en.Line, en.Col, s.OriginFile));
                        }

                        foreach (var func in cds.Methods)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, func.Name, cds.Line, cds.Col, s.OriginFile));

                            var methodParams = new List<string>(func.Parameters);
                            methodParams.Insert(0, "this");

                            var methodFuncExpr = new FuncExpr(methodParams, func.Body, func.Line, func.Col, s.OriginFile);
                            CompileExpr(methodFuncExpr);

                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        if (initMethod != null)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "init", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, cds.Line, cds.Col, s.OriginFile));

                            foreach (var p in ctorParams)
                                _insns.Add(new Instruction(OpCode.LOAD_VAR, p, cds.Line, cds.Col, s.OriginFile));

                            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, ctorParams.Count, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.POP, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.RET, null, cds.Line, cds.Col, s.OriginFile));

                        _insns[jmpOverCtorIdx] = new Instruction(OpCode.JMP, _insns.Count, cds.Line, cds.Col, s.OriginFile);

                        _insns.Add(new Instruction(
                            OpCode.PUSH_CLOSURE,
                            new object[] { ctorStart, $"__ctor_{cds.Name}" },
                            cds.Line, cds.Col, s.OriginFile
                        ));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, cds.Name, cds.Line, cds.Col, s.OriginFile));
                        break;
                    }

                case EnumDeclStmt eds:
                    {
                        _insns.Add(new Instruction(OpCode.NEW_DICT, 0, eds.Line, eds.Col));

                        var reverseDict = new Dictionary<int, string>();

                        foreach (var member in eds.Members)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, eds.Line, eds.Col));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, member.Name, eds.Line, eds.Col));
                            _insns.Add(new Instruction(OpCode.PUSH_INT, (int)member.Value, eds.Line, eds.Col));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, eds.Line, eds.Col));

                            reverseDict[(int)member.Value] = member.Name;
                        }

                        _insns.Add(new Instruction(OpCode.DUP, null, eds.Line, eds.Col));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "__name", eds.Line, eds.Col));
                        _insns.Add(new Instruction(OpCode.NEW_DICT, 0, eds.Line, eds.Col));

                        foreach (var kv in reverseDict)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, eds.Line, eds.Col));
                            _insns.Add(new Instruction(OpCode.PUSH_INT, kv.Key, eds.Line, eds.Col));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, kv.Value, eds.Line, eds.Col));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, eds.Line, eds.Col));
                        }

                        _insns.Add(new Instruction(OpCode.INDEX_SET, null, eds.Line, eds.Col));

                        _insns.Add(new Instruction(OpCode.VAR_DECL, eds.Name, eds.Line, eds.Col));

                        break;
                    }

                case BlockStmt b:
                    if (insideFunction && b.IsFunctionBody)
                    {
                        foreach (var sub in b.Statements)
                            CompileStmt(sub, insideFunction: true);
                    }
                    else
                    {
                        _insns.Add(new Instruction(OpCode.PUSH_SCOPE, null, s.Line, s.Col, s.OriginFile));
                        foreach (var sub in b.Statements)
                            CompileStmt(sub, insideFunction);
                        _insns.Add(new Instruction(OpCode.POP_SCOPE, null, s.Line, s.Col, s.OriginFile));
                    }
                    break;

                case IfStmt ifs:
                    {
                        CompileExpr(ifs.Condition);
                        int jmpFalseIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, s.Line, s.Col, s.OriginFile));

                        CompileStmt(ifs.ThenBlock, insideFunction);

                        if (ifs.ElseBranch != null)
                        {
                            int jmpEndIdx = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, s.Line, s.Col, s.OriginFile));
                            _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, s.Line, s.Col, s.OriginFile);
                            CompileStmt(ifs.ElseBranch, insideFunction);
                            _insns[jmpEndIdx] = new Instruction(OpCode.JMP, _insns.Count, s.Line, s.Col, s.OriginFile);
                        }
                        else
                        {
                            _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, s.Line, s.Col, s.OriginFile);
                        }
                        break;
                    }

                case WhileStmt ws:
                    {
                        int loopStart = _insns.Count;
                        _breakLists.Push([]);
                        _continueLists.Push([]);

                        CompileExpr(ws.Condition);
                        int jmpFalseIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, s.Line, s.Col, s.OriginFile));

                        CompileStmt(ws.Body, insideFunction);

                        foreach (var idx in _continueLists.Peek())
                            _insns[idx] = new Instruction(OpCode.JMP, loopStart, s.Line, s.Col, s.OriginFile);

                        _insns.Add(new Instruction(OpCode.JMP, loopStart, s.Line, s.Col));
                        _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, s.Line, s.Col, s.OriginFile);

                        foreach (var idx in _breakLists.Peek())
                            _insns[idx] = new Instruction(OpCode.JMP, _insns.Count, s.Line, s.Col);

                        _breakLists.Pop();
                        _continueLists.Pop();
                        break;
                    }

                case ForStmt fs:
                    {
                        if (fs.Init != null) CompileStmt(fs.Init, insideFunction);
                        int loopStart = _insns.Count;
                        _breakLists.Push([]);
                        _continueLists.Push([]);

                        if (fs.Condition != null)
                        {
                            CompileExpr(fs.Condition);
                            int jmpFalseIdx = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, s.Line, s.Col));

                            CompileStmt(fs.Body, insideFunction);

                            int incStart = _insns.Count;
                            foreach (var idx in _continueLists.Peek())
                                _insns[idx] = new Instruction(OpCode.JMP, incStart, s.Line, s.Col);

                            if (fs.Increment != null) CompileStmt(fs.Increment, insideFunction);
                            _insns.Add(new Instruction(OpCode.JMP, loopStart, s.Line, s.Col));
                            _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, s.Line, s.Col);

                            foreach (var idx in _breakLists.Peek())
                                _insns[idx] = new Instruction(OpCode.JMP, _insns.Count, s.Line, s.Col);
                        }
                        else
                        {
                            CompileStmt(fs.Body, insideFunction);

                            int incStart = _insns.Count;
                            foreach (var idx in _continueLists.Peek())
                                _insns[idx] = new Instruction(OpCode.JMP, incStart, s.Line, s.Col);

                            if (fs.Increment != null) CompileStmt(fs.Increment, insideFunction);
                            _insns.Add(new Instruction(OpCode.JMP, loopStart, s.Line, s.Col));

                            foreach (var idx in _breakLists.Peek())
                                _insns[idx] = new Instruction(OpCode.JMP, _insns.Count, s.Line, s.Col);
                        }

                        _breakLists.Pop();
                        _continueLists.Pop();
                        break;
                    }

                case MatchStmt ms:
                    {
                        CompileExpr(ms.Expression);
                        var endJumps = new List<int>();

                        foreach (var c in ms.Cases)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, ms.Line, ms.Col));
                            CompileExpr(c.Pattern);
                            _insns.Add(new Instruction(OpCode.EQ, null, ms.Line, ms.Col));

                            int jmpNext = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, ms.Line, ms.Col));

                            CompileStmt(c.Body, insideFunction);

                            endJumps.Add(_insns.Count);
                            _insns.Add(new Instruction(OpCode.JMP, null, ms.Line, ms.Col));

                            _insns[jmpNext] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, ms.Line, ms.Col);
                        }

                        if (ms.DefaultCase != null)
                            CompileStmt(ms.DefaultCase, insideFunction);

                        int endTarget = _insns.Count;
                        foreach (int idx in endJumps)
                            _insns[idx] = new Instruction(OpCode.JMP, endTarget, ms.Line, ms.Col);

                        _insns.Add(new Instruction(OpCode.POP, null, ms.Line, ms.Col));
                        break;
                    }

                case ThrowStmt ts:
                    CompileExpr(ts.Value);
                    _insns.Add(new Instruction(OpCode.THROW, null, s.Line, s.Col));
                    break;

                case TryCatchFinallyStmt tcf:
                    {
                        int tryPushIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.TRY_PUSH, new int[] { -1, -1 }, s.Line, s.Col));

                        CompileStmt(tcf.TryBlock, insideFunction);

                        if (tcf.FinallyBlock != null)
                        {
                            int jmpToFinallyIdx = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, s.Line, s.Col));

                            int catchStart = -1;
                            int jmpAfterCatchIdx = -1;
                            if (tcf.CatchBlock != null)
                            {
                                catchStart = _insns.Count;
                                _insns.Add(new Instruction(OpCode.PUSH_SCOPE, null, s.Line, s.Col));
                                if (!string.IsNullOrEmpty(tcf.CatchVar))
                                    _insns.Add(new Instruction(OpCode.VAR_DECL, tcf.CatchVar, s.Line, s.Col));
                                foreach (var st in tcf.CatchBlock.Statements) CompileStmt(st, insideFunction);
                                _insns.Add(new Instruction(OpCode.POP_SCOPE, null, s.Line, s.Col));
                                jmpAfterCatchIdx = _insns.Count;
                                _insns.Add(new Instruction(OpCode.JMP, null, s.Line, s.Col));
                            }

                            int finallyStart = _insns.Count;

                            _insns[tryPushIdx] = new Instruction(OpCode.TRY_PUSH, new int[] { catchStart, finallyStart }, s.Line, s.Col);

                            CompileStmt(tcf.FinallyBlock, insideFunction);

                            _insns.Add(new Instruction(OpCode.END_FINALLY, null, s.Line, s.Col));

                            _insns[jmpToFinallyIdx] = new Instruction(OpCode.JMP, finallyStart, s.Line, s.Col);
                            if (jmpAfterCatchIdx != -1)
                                _insns[jmpAfterCatchIdx] = new Instruction(OpCode.JMP, finallyStart, s.Line, s.Col);
                        }
                        else
                        {
                            _insns.Add(new Instruction(OpCode.TRY_POP, null, s.Line, s.Col));
                            int jmpOverCatchIdx = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, s.Line, s.Col));

                            int catchStart = -1;
                            if (tcf.CatchBlock != null)
                            {
                                catchStart = _insns.Count;
                                _insns.Add(new Instruction(OpCode.PUSH_SCOPE, null, s.Line, s.Col));
                                if (!string.IsNullOrEmpty(tcf.CatchVar))
                                    _insns.Add(new Instruction(OpCode.VAR_DECL, tcf.CatchVar, s.Line, s.Col));
                                foreach (var st in tcf.CatchBlock.Statements) CompileStmt(st, insideFunction);
                                _insns.Add(new Instruction(OpCode.POP_SCOPE, null, s.Line, s.Col));
                                _insns.Add(new Instruction(OpCode.TRY_POP, null, s.Line, s.Col));
                            }

                            int endIdx = _insns.Count;
                            _insns[tryPushIdx] = new Instruction(OpCode.TRY_PUSH, new int[] { catchStart, -1 }, s.Line, s.Col);
                            _insns[jmpOverCatchIdx] = new Instruction(OpCode.JMP, endIdx, s.Line, s.Col);
                        }

                        break;
                    }

                case BreakStmt:
                    _insns.Add(new Instruction(OpCode.JMP, null, s.Line, s.Col));
                    _breakLists.Peek().Add(_insns.Count - 1);
                    break;

                case ContinueStmt:
                    _insns.Add(new Instruction(OpCode.JMP, null, s.Line, s.Col));
                    _continueLists.Peek().Add(_insns.Count - 1);
                    break;

                case ExprStmt es:
                    CompileExpr(es.Expression);
                    _insns.Add(new Instruction(OpCode.POP, null, s.Line, s.Col));
                    break;

                case CompoundAssignStmt ca:
                    CompileLValue(ca.Target, load: true);
                    CompileExpr(ca.Value);

                    _insns.Add(new Instruction(OpFromToken(ca.Op, ca, FileName), null, s.Line, s.Col));
                    CompileLValueStore(ca.Target);
                    break;

                case FuncDeclStmt fd:
                    {
                        int jmpOverFuncIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, s.Line, s.Col));

                        int funcStart = _insns.Count;
                        _functions[fd.Name] = new FunctionInfo(fd.Parameters, funcStart);

                        if (fd.Body is BlockStmt b) b.IsFunctionBody = true;
                        CompileStmt(fd.Body, insideFunction: true);

                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, s.Line, s.Col));
                        _insns.Add(new Instruction(OpCode.RET, null, s.Line, s.Col));

                        _insns[jmpOverFuncIdx] = new Instruction(OpCode.JMP, _insns.Count, s.Line, s.Col);

                        _insns.Add(new Instruction(OpCode.PUSH_CLOSURE, new object[] { funcStart, fd.Name }, s.Line, s.Col));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, fd.Name, s.Line, s.Col));
                        break;
                    }

                case ReturnStmt rs:
                    if (rs.Value != null) CompileExpr(rs.Value);
                    else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, s.Line, s.Col));
                    _insns.Add(new Instruction(OpCode.RET, null, s.Line, s.Col));
                    break;

                default:
                    throw new CompilerException($"unknown statement type {s.GetType().Name}", s.Line, s.Col, s.OriginFile);
            }
        }

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
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, v.Name, e.Line, e.Col, e.OriginFile));
                    break;

                case ArrayExpr a:
                    foreach (var elem in a.Elements) CompileExpr(elem);
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
                        _insns.Add(new Instruction(OpCode.PUSH_INT, 0, slice.Line, slice.Col, e.OriginFile));

                    if (slice.End is not null)
                        CompileExpr(slice.End);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_INT, null, slice.Line, slice.Col, e.OriginFile));

                    _insns.Add(new Instruction(OpCode.SLICE_GET, null, slice.Line, slice.Col, e.OriginFile));
                    break;

                case BinaryExpr b:
                    {
                        var op = OpFromToken(b.Op, b, FileName);

                        CompileExpr(b.Left);
                        CompileExpr(b.Right);

                        _insns.Add(new Instruction(op, null, e.Line, e.Col, e.OriginFile));
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
                    foreach (var (k, v) in d.Pairs)
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

                case CallExpr call:
                    {
                        if (call.Target is IndexExpr ie)
                        {
                            CompileExpr(ie.Target);
                            if (ie.Index is StringExpr s)
                                _insns.Add(new Instruction(OpCode.PUSH_STR, s.Value, e.Line, e.Col, e.OriginFile));
                            else
                                CompileExpr(ie.Index);
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, e.Line, e.Col, e.OriginFile));

                            for (int i = call.Args.Count - 1; i >= 0; i--)
                                CompileExpr(call.Args[i]);

                            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, call.Args.Count, e.Line, e.Col, e.OriginFile));
                        }
                        else if (call.Target is VarExpr ve && IsBuiltinFunction(ve.Name))
                        {
                            for (int i = call.Args.Count - 1; i >= 0; i--)
                                CompileExpr(call.Args[i]);

                            _insns.Add(new Instruction(OpCode.CALL, ve.Name, e.Line, e.Col, e.OriginFile));
                        }
                        else
                        {
                            CompileExpr(call.Target);

                            for (int i = call.Args.Count - 1; i >= 0; i--)
                                CompileExpr(call.Args[i]);

                            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, call.Args.Count, e.Line, e.Col, e.OriginFile));
                        }
                        break;
                    }

                case NewExpr ne:
                    {
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, ne.ClassName, ne.Line, ne.Col, e.OriginFile));
                        for (int i = ne.Args.Count - 1; i >= 0; i--)
                            CompileExpr(ne.Args[i]);
                        _insns.Add(new Instruction(OpCode.CALL_INDIRECT, ne.Args.Count, ne.Line, ne.Col, e.OriginFile));
                        break;
                    }

                case MethodCallExpr mce:
                    {
                        CompileExpr(mce.Target);
                        _insns.Add(new Instruction(OpCode.PUSH_STR, mce.Method, mce.Line, mce.Col, e.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, mce.Line, mce.Col, e.OriginFile));

                        for (int i = mce.Args.Count - 1; i >= 0; i--)
                            CompileExpr(mce.Args[i]);

                        _insns.Add(new Instruction(OpCode.CALL_INDIRECT, mce.Args.Count, mce.Line, mce.Col, e.OriginFile));
                        break;
                    }

                case FuncExpr fe:
                    {
                        int jmpOverFuncIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, e.Line, e.Col, e.OriginFile));

                        int funcStart = _insns.Count;

                        string anonName = $"__anon_{_anonCounter++}";
                        _functions[anonName] = new FunctionInfo(fe.Parameters, funcStart);

                        CompileStmt(fe.Body, insideFunction: true);

                        if (_insns.Count == 0 || _insns[^1].Code != OpCode.RET)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, e.Line, e.Col, e.OriginFile));
                            _insns.Add(new Instruction(OpCode.RET, null, e.Line, e.Col, e.OriginFile));
                        }

                        _insns[jmpOverFuncIdx] = new Instruction(OpCode.JMP, _insns.Count, e.Line, e.Col, e.OriginFile);

                        _insns.Add(new Instruction(OpCode.PUSH_CLOSURE, new object[] { funcStart, anonName }, e.Line, e.Col, e.OriginFile));
                        break;
                    }

                default:
                    throw new CompilerException($"unknown expr type {e?.GetType().Name}", e?.Line ?? -1, e?.Col ?? -1, e?.OriginFile ?? "");
            }
        }

        /// <summary>
        /// The OpFromToken
        /// </summary>
        /// <param name="t">The t<see cref="TokenType"/></param>
        /// <param name="tp">The tp<see cref="Node"/></param>
        /// <param name="outOfFile">The outOfFile<see cref="string"/></param>
        /// <returns>The <see cref="OpCode"/></returns>
        private static OpCode OpFromToken(TokenType t, Node tp, string outOfFile) => t switch
        {
            TokenType.Plus => OpCode.ADD,
            TokenType.Minus => OpCode.SUB,
            TokenType.Star => OpCode.MUL,
            TokenType.Slash => OpCode.DIV,
            TokenType.Modulo => OpCode.MOD,
            TokenType.BShiftR => OpCode.SHR,
            TokenType.bShiftL => OpCode.SHL,
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
            TokenType.AndAnd => OpCode.AND,
            TokenType.OrOr => OpCode.OR,
            TokenType.PlusAssign => OpCode.ADD,
            TokenType.MinusAssign => OpCode.SUB,
            TokenType.StarAssign => OpCode.MUL,
            TokenType.SlashAssign => OpCode.DIV,
            TokenType.ModuloAssign => OpCode.MOD,

            _ => throw new CompilerException($"unsupported operator token for bytecode: {t}", tp.Line, tp.Col, outOfFile)
        };

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
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, v.Name, v.Line, v.Col));
            }
            else if (target is IndexExpr ie)
            {
                if (ie.Index == null)
                    throw new CompilerException($"empty index '[]' cannot be used for reading", ie.Line, ie.Col, FileName);

                CompileExpr(ie.Target);
                CompileExpr(ie.Index);
                if (load) _insns.Add(new Instruction(OpCode.INDEX_GET, null, ie.Line, ie.Col));
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
                _insns.Add(new Instruction(OpCode.STORE_VAR, v.Name, v.Line, v.Col));
            }
            else if (target is IndexExpr ie)
            {
                if (ie.Index == null)
                {
                    CompileExpr(ie.Target);
                    _insns.Add(new Instruction(OpCode.ARRAY_PUSH, null, ie.Line, ie.Col));
                }
                else
                {
                    CompileExpr(ie.Target);
                    CompileExpr(ie.Index);
                    _insns.Add(new Instruction(OpCode.ROT, null, ie.Line, ie.Col));
                    _insns.Add(new Instruction(OpCode.INDEX_SET, null, ie.Line, ie.Col));
                }
            }
            else
            {
                throw new CompilerException("Invalid lvalue for store.", target?.Line ?? -1, target?.Col ?? -1, FileName);
            }
        }
    }

    /// <summary>
    /// Defines the <see cref="CompilerException" />
    /// </summary>
    public sealed class CompilerException(string message, int line, int column, string fileSource) : Exception($"{message}. ( Line : {line}, Column : {column} ) : [Source : '{fileSource}']");
}
