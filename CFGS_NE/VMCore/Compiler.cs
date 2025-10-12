using CFGS_VM.Analytic;
using CFGS_VM.Analytic.TTypes;
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
            try
            {
                _insns.Clear();
                _functions.Clear();

                List<FuncDeclStmt> funcDecls = new();
                foreach (Stmt s in program)
                {
                    if (s is FuncDeclStmt f)
                    {
                        if (_functions.ContainsKey(f.Name))
                            throw new CompilerException($"duplicate function '{f.Name}'", f.Line, f.Col, f.OriginFile);

                        _functions[f.Name] = new FunctionInfo(f.Parameters, -1);
                        funcDecls.Add(f);
                    }
                }

                int jmpOverAllFuncsIdx = _insns.Count;
                _insns.Add(new Instruction(OpCode.JMP, null, 0, 0));

                List<(FuncDeclStmt fd, int funcStart)> orderedFuncs = new();
                foreach (FuncDeclStmt fd in funcDecls)
                {
                    try
                    {
                        int funcStart = _insns.Count;
                        _functions[fd.Name] = new FunctionInfo(fd.Parameters, funcStart);

                        if (fd.Body is BlockStmt b)
                            b.IsFunctionBody = true;
                        else
                            throw new CompilerException($"function '{fd.Name}' must have a block body", fd.Line, fd.Col, fd.OriginFile);

                        CompileStmt(fd.Body, insideFunction: true);

                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, fd.Line, fd.Col, fd.OriginFile));
                        _insns.Add(new Instruction(OpCode.RET, null, fd.Line, fd.Col, fd.OriginFile));

                        orderedFuncs.Add((fd, funcStart));
                    }
                    catch (CompilerException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new CompilerException(
                            $"internal compiler error while compiling function '{fd.Name}': {ex.Message}",
                            fd.Line, fd.Col, fd.OriginFile);
                    }
                }

                _insns[jmpOverAllFuncsIdx] = new Instruction(OpCode.JMP, _insns.Count, 0, 0);

                foreach ((FuncDeclStmt fd, int funcStart) in orderedFuncs)
                {
                    _insns.Add(new Instruction(
                        OpCode.PUSH_CLOSURE, new object[] { funcStart, fd.Name }, fd.Line, fd.Col, fd.OriginFile));
                    _insns.Add(new Instruction(OpCode.VAR_DECL, fd.Name, fd.Line, fd.Col, fd.OriginFile));
                }

                foreach (Stmt s in program)
                {
                    if (s is FuncDeclStmt) continue;

                    try
                    {
                        CompileStmt(s, insideFunction: false);
                    }
                    catch (CompilerException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new CompilerException(
                            $"internal compiler error at top-level: {ex.Message}",
                            s.Line, s.Col, s.OriginFile);
                    }
                }

                _insns.Add(new Instruction(OpCode.HALT, null, 0, 0));
                return _insns;
            }
            catch (CompilerException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CompilerException(
                    $"internal compiler error: {ex.Message}",
                    0, 0, "<compiler>");
            }
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

                case EmptyStmt etst:
                    break;

                case EmitStmt emitStmt:
                    {
                        _insns.Add(new Instruction((OpCode)emitStmt.Command, emitStmt.Argument, s.Line, s.Col, s.OriginFile));
                        break;
                    }

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
                            throw new CompilerException("invalid use of 'push' []", ps.Line, ps.Col, s.OriginFile);
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

                case DeleteExprStmt des:
                    {
                        if (des.Target is SliceExpr se)
                        {
                            if (se.Target is VarExpr v)
                            {
                                if (se.Start != null) CompileExpr(se.Start); else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, des.Line, des.Col, s.OriginFile));
                                if (se.End != null) CompileExpr(se.End); else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, des.Line, des.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.ARRAY_DELETE_SLICE, v.Name, des.Line, des.Col, s.OriginFile));
                            }
                            else
                            {
                                CompileExpr(se.Target);
                                if (se.Start != null) CompileExpr(se.Start); else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, des.Line, des.Col, s.OriginFile));
                                if (se.End != null) CompileExpr(se.End); else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, des.Line, des.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.ARRAY_DELETE_SLICE, null, des.Line, des.Col, s.OriginFile));
                            }
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
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ALL, v2.Name, des.Line, des.Col, s.OriginFile));
                            break;
                        }

                        throw new CompilerException("unsupported delete target", des.Line, des.Col, s.OriginFile);
                    }

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
                    {
                        int jmpOverCtorIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, cds.Line, cds.Col, s.OriginFile));

                        FuncDeclStmt? initMethod = cds.Methods.FirstOrDefault(m => m.Name == "init");
                        List<string> ctorParams = initMethod != null
                            ? new List<string>(initMethod.Parameters)
                            : new List<string>(cds.Parameters);

                        if (cds.IsNested && (ctorParams.Count == 0 || ctorParams[0] != "__outer"))
                            ctorParams.Insert(0, "__outer");

                        int ctorStart = _insns.Count;
                        _functions[$"__ctor_{cds.Name}"] = new FunctionInfo(ctorParams, ctorStart);

                        const string SELF = "__obj";
                        _insns.Add(new Instruction(OpCode.NEW_OBJECT, cds.Name, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, SELF, cds.Line, cds.Col, s.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "__type", cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, cds.Name, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET, null, cds.Line, cds.Col, s.OriginFile));

                        if (!string.IsNullOrEmpty(cds.BaseName))
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, cds.BaseName, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "new", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, cds.Line, cds.Col, s.OriginFile));

                            for (int i = cds.BaseCtorArgs.Count - 1; i >= 0; i--)
                                CompileExpr(cds.BaseCtorArgs[i]);

                            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, cds.BaseCtorArgs.Count, cds.Line, cds.Col, s.OriginFile));

                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__base", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.ROT, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        if (cds.IsNested)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__outer", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, "__outer", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (KeyValuePair<string, Expr?> kv in cds.Fields)
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

                        foreach (string p in ctorParams)
                        {
                            if (p == "__outer") continue;
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, p, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, p, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.ROT, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (FuncDeclStmt func in cds.Methods)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, func.Line, func.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, func.Name, func.Line, func.Col, s.OriginFile));

                            List<string> methodParams = new(func.Parameters);
                            methodParams.Insert(0, "this");

                            FuncExpr methodFuncExpr = new(methodParams, func.Body, func.Line, func.Col, s.OriginFile);
                            CompileExpr(methodFuncExpr);

                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, func.Line, func.Col, s.OriginFile));
                        }

                        if (initMethod != null)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "init", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, cds.Line, cds.Col, s.OriginFile));

                            for (int i = ctorParams.Count - 1; i >= 0; i--)
                            {
                                string p = ctorParams[i];
                                if (p == "__outer") continue;
                                _insns.Add(new Instruction(OpCode.LOAD_VAR, p, cds.Line, cds.Col, s.OriginFile));
                            }

                            int argCountForInit = ctorParams.Count(p => p != "__outer");
                            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, argCountForInit, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.POP, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.RET, null, cds.Line, cds.Col, s.OriginFile));

                        _insns[jmpOverCtorIdx] = new Instruction(OpCode.JMP, _insns.Count, cds.Line, cds.Col, s.OriginFile);

                        _insns.Add(new Instruction(OpCode.NEW_STATIC, cds.Name, cds.Line, cds.Col, s.OriginFile));

                        _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "new", cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(
                            OpCode.PUSH_CLOSURE,
                            new object[] { ctorStart, $"__ctor_{cds.Name}" },
                            cds.Line, cds.Col, s.OriginFile
                        ));
                        _insns.Add(new Instruction(OpCode.INDEX_SET, null, cds.Line, cds.Col, s.OriginFile));

                        if (!string.IsNullOrEmpty(cds.BaseName))
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__base", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, cds.BaseName, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (KeyValuePair<string, Expr?> kv in cds.StaticFields)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, kv.Key, cds.Line, cds.Col, s.OriginFile));
                            if (kv.Value != null) CompileExpr(kv.Value);
                            else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (FuncDeclStmt func in cds.StaticMethods)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, func.Line, func.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, func.Name, func.Line, func.Col, s.OriginFile));

                            List<string> methodParams = new(func.Parameters);
                            methodParams.Insert(0, "type");

                            FuncExpr methodFuncExpr = new(methodParams, func.Body, func.Line, func.Col, s.OriginFile);
                            CompileExpr(methodFuncExpr);

                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, func.Line, func.Col, s.OriginFile));
                        }

                        foreach (EnumDeclStmt en in cds.Enums)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, en.Line, en.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, en.Name, en.Line, en.Col, s.OriginFile));

                            foreach (EnumMemberNode member in en.Members)
                            {
                                _insns.Add(new Instruction(OpCode.PUSH_STR, member.Name, en.Line, en.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.PUSH_INT, (int)member.Value, en.Line, en.Col, s.OriginFile));
                            }

                            _insns.Add(new Instruction(OpCode.PUSH_INT, en.Members.Count, en.Line, en.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.NEW_ENUM, en.Name, en.Line, en.Col, s.OriginFile));

                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, en.Line, en.Col, s.OriginFile));
                        }

                        foreach (ClassDeclStmt inner in cds.NestedClasses)
                        {
                            CompileStmt(inner, insideFunction: false);
                            _insns.Add(new Instruction(OpCode.DUP, null, inner.Line, inner.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, inner.Name, inner.Line, inner.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, inner.Name, inner.Line, inner.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, inner.Line, inner.Col, s.OriginFile));
                        }

                        _insns.Add(new Instruction(OpCode.VAR_DECL, cds.Name, cds.Line, cds.Col, s.OriginFile));
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
                    if (insideFunction && b.IsFunctionBody)
                    {
                        foreach (Stmt sub in b.Statements)
                            CompileStmt(sub, insideFunction: true);
                    }
                    else
                    {
                        _insns.Add(new Instruction(OpCode.PUSH_SCOPE, null, s.Line, s.Col, s.OriginFile));
                        foreach (Stmt sub in b.Statements)
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

                        foreach (int idx in _continueLists.Peek())
                            _insns[idx] = new Instruction(OpCode.JMP, loopStart, s.Line, s.Col, s.OriginFile);

                        _insns.Add(new Instruction(OpCode.JMP, loopStart, s.Line, s.Col, s.OriginFile));
                        _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, s.Line, s.Col, s.OriginFile);

                        foreach (int idx in _breakLists.Peek())
                            _insns[idx] = new Instruction(OpCode.JMP, _insns.Count, s.Line, s.Col, s.OriginFile);

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
                            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, s.Line, s.Col, s.OriginFile));

                            CompileStmt(fs.Body, insideFunction);

                            int incStart = _insns.Count;
                            foreach (int idx in _continueLists.Peek())
                                _insns[idx] = new Instruction(OpCode.JMP, incStart, s.Line, s.Col, s.OriginFile);

                            if (fs.Increment != null) CompileStmt(fs.Increment, insideFunction);
                            _insns.Add(new Instruction(OpCode.JMP, loopStart, s.Line, s.Col, s.OriginFile));
                            _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, s.Line, s.Col, s.OriginFile);

                            foreach (int idx in _breakLists.Peek())
                                _insns[idx] = new Instruction(OpCode.JMP, _insns.Count, s.Line, s.Col, s.OriginFile);
                        }
                        else
                        {
                            CompileStmt(fs.Body, insideFunction);

                            int incStart = _insns.Count;
                            foreach (int idx in _continueLists.Peek())
                                _insns[idx] = new Instruction(OpCode.JMP, incStart, s.Line, s.Col, s.OriginFile);

                            if (fs.Increment != null) CompileStmt(fs.Increment, insideFunction);
                            _insns.Add(new Instruction(OpCode.JMP, loopStart, s.Line, s.Col, s.OriginFile));

                            foreach (int idx in _breakLists.Peek())
                                _insns[idx] = new Instruction(OpCode.JMP, _insns.Count, s.Line, s.Col, s.OriginFile);
                        }

                        _breakLists.Pop();
                        _continueLists.Pop();
                        break;
                    }

                case ForeachStmt fe:
                    {
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

                        int loopStart = _insns.Count;
                        _breakLists.Push(new List<int>());
                        _continueLists.Push(new List<int>());

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

                        int jmpAfterSet = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, fe.Line, fe.Col, fe.OriginFile));

                        int seqPathAddr = _insns.Count;
                        _insns[jmpToSeqPath] = new Instruction(OpCode.JMP_IF_FALSE, seqPathAddr, fe.Line, fe.Col, fe.OriginFile);

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.STORE_VAR, fe.VarName, fe.Line, fe.Col, fe.OriginFile));

                        int afterSet = _insns.Count;
                        _insns[jmpAfterSet] = new Instruction(OpCode.JMP, afterSet, fe.Line, fe.Col, fe.OriginFile);

                        CompileStmt(fe.Body, insideFunction);

                        int incStart = _insns.Count;
                        foreach (int k in _continueLists.Peek())
                            _insns[k] = new Instruction(OpCode.JMP, incStart, fe.Line, fe.Col, fe.OriginFile);

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_INT, 1, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.ADD, null, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.STORE_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.JMP, loopStart, fe.Line, fe.Col, fe.OriginFile));
                        _insns[jmpIfFalse] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, fe.Line, fe.Col, fe.OriginFile);

                        foreach (int br in _breakLists.Peek())
                            _insns[br] = new Instruction(OpCode.JMP, _insns.Count, fe.Line, fe.Col, fe.OriginFile);

                        _breakLists.Pop();
                        _continueLists.Pop();
                        break;
                    }

                case MatchStmt ms:
                    {
                        CompileExpr(ms.Expression);

                        List<int> endJumps = new();

                        foreach (CaseClause c in ms.Cases)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, ms.Line, ms.Col, s.OriginFile));
                            CompileExpr(c.Pattern);
                            _insns.Add(new Instruction(OpCode.EQ, null, ms.Line, ms.Col, s.OriginFile));

                            int jmpNext = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, ms.Line, ms.Col, s.OriginFile));

                            _insns.Add(new Instruction(OpCode.POP, null, ms.Line, ms.Col, s.OriginFile));

                            CompileStmt(c.Body, insideFunction);

                            endJumps.Add(_insns.Count);
                            _insns.Add(new Instruction(OpCode.JMP, null, ms.Line, ms.Col, s.OriginFile));

                            _insns[jmpNext] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, ms.Line, ms.Col, s.OriginFile);
                        }

                        if (ms.DefaultCase != null)
                        {
                            _insns.Add(new Instruction(OpCode.POP, null, ms.Line, ms.Col, s.OriginFile));

                            CompileStmt(ms.DefaultCase, insideFunction);
                        }
                        else
                        {
                            _insns.Add(new Instruction(OpCode.POP, null, ms.Line, ms.Col, s.OriginFile));
                        }

                        int endTarget = _insns.Count;
                        foreach (int idx in endJumps)
                            _insns[idx] = new Instruction(OpCode.JMP, endTarget, ms.Line, ms.Col, s.OriginFile);

                        break;
                    }

                case TryStmt ts:
                    {
                        int tryStart = _insns.Count;

                        _insns.Add(new Instruction(OpCode.TRY_PUSH, null, ts.Line, ts.Col, ts.OriginFile));

                        CompileStmt(ts.TryBlock, insideFunction);

                        int catchStart = -1, finallyStart = -1;

                        if (ts.CatchBlock != null)
                        {
                            catchStart = _insns.Count;

                            if (ts.CatchIdent != null)
                                _insns.Add(new Instruction(OpCode.PUSH_SCOPE, null, ts.Line, ts.Col, ts.OriginFile));

                            if (ts.CatchIdent != null)
                                _insns.Add(new Instruction(OpCode.VAR_DECL, ts.CatchIdent, ts.Line, ts.Col, ts.OriginFile));

                            CompileStmt(ts.CatchBlock, insideFunction);

                            if (ts.CatchIdent != null)
                                _insns.Add(new Instruction(OpCode.POP_SCOPE, null, ts.Line, ts.Col, ts.OriginFile));
                        }

                        if (ts.FinallyBlock != null)
                        {
                            finallyStart = _insns.Count;
                            CompileStmt(ts.FinallyBlock, insideFunction);
                        }

                        _insns.Add(new Instruction(OpCode.TRY_POP, null, ts.Line, ts.Col, ts.OriginFile));

                        _insns[tryStart] = new Instruction(
                            OpCode.TRY_PUSH,
                            new object[] { catchStart, finallyStart },
                            ts.Line, ts.Col, ts.OriginFile
                        );
                        break;
                    }

                case ThrowStmt th:
                    {
                        CompileExpr(th.Value);
                        _insns.Add(new Instruction(OpCode.THROW, null, th.Line, th.Col, th.OriginFile));
                        break;
                    }

                case ContinueStmt:
                    {
                        _insns.Add(new Instruction(OpCode.POP_SCOPE, null, s.Line, s.Col, s.OriginFile));

                        int jmpIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, s.Line, s.Col, s.OriginFile));
                        _continueLists.Peek().Add(jmpIdx);
                        break;
                    }

                case BreakStmt:
                    {
                        _insns.Add(new Instruction(OpCode.POP_SCOPE, null, s.Line, s.Col, s.OriginFile));

                        int jmpIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, s.Line, s.Col, s.OriginFile));
                        _breakLists.Peek().Add(jmpIdx);
                        break;
                    }

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
                        _functions[internalName] = new FunctionInfo(fd.Parameters, funcStart);

                        if (fd.Body is BlockStmt fb) fb.IsFunctionBody = true;
                        CompileStmt(fd.Body, insideFunction: true);

                        if (_insns.Count == 0 || _insns[^1].Code != OpCode.RET)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, fd.Line, fd.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.RET, null, fd.Line, fd.Col, s.OriginFile));
                        }

                        _insns[jmpOverFuncIdx] = new Instruction(OpCode.JMP, _insns.Count, fd.Line, fd.Col, s.OriginFile);

                        _insns.Add(new Instruction(OpCode.PUSH_CLOSURE, new object[] { funcStart, fd.Name }, fd.Line, fd.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, fd.Name, fd.Line, fd.Col, s.OriginFile));
                        break;
                    }

                case ReturnStmt rs:
                    if (rs.Value != null) CompileExpr(rs.Value);
                    else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, s.Line, s.Col, s.OriginFile));
                    _insns.Add(new Instruction(OpCode.RET, null, s.Line, s.Col, s.OriginFile));
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
                        string[] parts = ne.ClassName.Split('.');

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, parts[0], ne.Line, ne.Col, e.OriginFile));
                        for (int i = 1; i < parts.Length; i++)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_STR, parts[i], ne.Line, ne.Col, e.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, ne.Line, ne.Col, e.OriginFile));
                        }

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

                case ConditionalExpr cnd:
                    {
                        CompileExpr(cnd.Condition);

                        int jmpIfFalseIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, e.Line, e.Col, e.OriginFile));

                        CompileExpr(cnd.ThenExpr);

                        int jmpEndIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, e.Line, e.Col, e.OriginFile));

                        _insns[jmpIfFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, e.Line, e.Col, e.OriginFile);

                        CompileExpr(cnd.ElseExpr);

                        _insns[jmpEndIdx] = new Instruction(OpCode.JMP, _insns.Count, e.Line, e.Col, e.OriginFile);
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
            TokenType.bShiftR => OpCode.SHR,
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
            TokenType.ModAssign => OpCode.MOD,

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
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, v.Name, v.Line, v.Col, v.OriginFile));
            }
            else if (target is IndexExpr ie)
            {
                if (ie.Index == null)
                    throw new CompilerException($"empty index '[]' cannot be used for reading", ie.Line, ie.Col, FileName);

                CompileExpr(ie.Target);
                CompileExpr(ie.Index);
                if (load) _insns.Add(new Instruction(OpCode.INDEX_GET, null, ie.Line, ie.Col, ie.OriginFile));
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
                    _insns.Add(new Instruction(OpCode.INDEX_SET, null, ie.Line, ie.Col, ie.OriginFile));
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
