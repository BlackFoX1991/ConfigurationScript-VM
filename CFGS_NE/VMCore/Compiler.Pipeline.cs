using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        internal CompilationContext Context => _context;

        internal void ResetCompilationState()
        {
            _context.Reset();
            _emission.Reset();
        }

        internal CompilationPlan BuildSymbolIndex(List<Stmt> program)
        {
            return new SymbolIndex().Build(_context, Functions, program);
        }

        internal void ValidateTopLevelDeclarations()
        {
            new TopLevelValidator().Validate(_context);
        }

        internal void ResolveTypeGraph(CompilationPlan plan)
        {
            ClassCatalog classCatalog = new();
            InterfaceCatalog interfaceCatalog = new();

            classCatalog.NormalizeInheritanceDeclarations(this, _qualifiedClassDecls.Values.Distinct());
            interfaceCatalog.ValidateAllKnownInterfaces(this);

            plan.OrderedInterfaces = interfaceCatalog.OrderByInheritance(this, plan.InterfaceDecls);
            plan.OrderedClasses = classCatalog.OrderByInheritance(this, plan.ClassDecls);
        }

        internal void RunSemanticChecks(CompilationPlan plan)
        {
            new ClassSemanticValidator().ValidateAll(this, plan.OrderedClasses);
        }

        internal void EmitProgram(CompilationPlan plan, List<Stmt> program)
            => new BytecodeEmitter().EmitProgram(this, plan, program);

        internal int ReserveFunctionJump()
        {
            int jumpIndex = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP, null, 0, 0));
            return jumpIndex;
        }

        internal void PatchFunctionJump(int jumpIndex)
        {
            _insns[jumpIndex] = new Instruction(OpCode.JMP, _insns.Count, 0, 0);
        }

        internal List<(FuncDeclStmt Declaration, int Start)> EmitFunctionBodies(List<FuncDeclStmt> functionDecls)
        {
            List<(FuncDeclStmt Declaration, int Start)> orderedFunctions = [];

            foreach (FuncDeclStmt functionDecl in functionDecls)
            {
                try
                {
                    int functionStart = _insns.Count;
                    Functions[functionDecl.Name] = new FunctionInfo(
                        functionDecl.Parameters,
                        functionStart,
                        functionDecl.MinArgs,
                        functionDecl.RestParameter,
                        functionDecl.IsAsync);

                    if (functionDecl.Body is BlockStmt block)
                    {
                        block.IsFunctionBody = true;
                    }
                    else
                    {
                        throw new CompilerException(
                            $"function '{functionDecl.Name}' must have a block body",
                            functionDecl.Line,
                            functionDecl.Col,
                            functionDecl.OriginFile);
                    }

                    ReceiverContextKind previousReceiverContext = _receiverContext;
                    _receiverContext = ReceiverContextKind.None;
                    EnterFunctionLocals(functionDecl.Parameters);
                    if (functionDecl.IsAsync)
                        _asyncFunctionDepth++;

                    try
                    {
                        CompileStmt(functionDecl.Body, insideFunction: true);
                    }
                    finally
                    {
                        if (functionDecl.IsAsync)
                            _asyncFunctionDepth--;

                        LeaveFunctionLocals();
                        _receiverContext = previousReceiverContext;
                    }

                    _insns.Add(new Instruction(OpCode.PUSH_NULL, null, functionDecl.Line, functionDecl.Col, functionDecl.OriginFile));
                    _insns.Add(new Instruction(OpCode.RET, null, functionDecl.Line, functionDecl.Col, functionDecl.OriginFile));

                    orderedFunctions.Add((functionDecl, functionStart));
                }
                catch (CompilerException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new CompilerException(
                        $"internal compiler error while compiling function '{functionDecl.Name}': {ex.Message}",
                        functionDecl.Line,
                        functionDecl.Col,
                        functionDecl.OriginFile);
                }
            }

            return orderedFunctions;
        }

        internal void EmitFunctionClosures(List<(FuncDeclStmt Declaration, int Start)> orderedFunctions)
        {
            foreach ((FuncDeclStmt declaration, int start) in orderedFunctions)
            {
                _insns.Add(new Instruction(
                    OpCode.PUSH_CLOSURE,
                    new object[] { start, declaration.Name },
                    declaration.Line,
                    declaration.Col,
                    declaration.OriginFile));

                _insns.Add(new Instruction(
                    OpCode.VAR_DECL,
                    declaration.Name,
                    declaration.Line,
                    declaration.Col,
                    declaration.OriginFile));
            }
        }

        internal void EmitInterfaces(List<InterfaceDeclStmt> orderedInterfaces)
        {
            foreach (InterfaceDeclStmt interfaceDecl in orderedInterfaces)
                CompileStmt(interfaceDecl, insideFunction: false);
        }

        internal void EmitClasses(List<ClassDeclStmt> orderedClasses)
        {
            foreach (ClassDeclStmt classDecl in orderedClasses)
            {
                try
                {
                    CompileStmt(classDecl, insideFunction: false);
                }
                catch (CompilerException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new CompilerException(
                        $"internal compiler error while compiling class '{classDecl.Name}': {ex.Message}",
                        classDecl.Line,
                        classDecl.Col,
                        classDecl.OriginFile);
                }
            }
        }

        internal void EmitRemainingTopLevelStatements(List<Stmt> program)
        {
            foreach (Stmt stmt in program)
            {
                Stmt unwrapped = stmt is ExportStmt exportStmt ? exportStmt.Inner : stmt;
                if (unwrapped is FuncDeclStmt || unwrapped is InterfaceDeclStmt || unwrapped is ClassDeclStmt)
                    continue;

                try
                {
                    CompileStmt(stmt, insideFunction: false);
                }
                catch (CompilerException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new CompilerException(
                        $"internal compiler error at top-level: {ex.Message}",
                        stmt.Line,
                        stmt.Col,
                        stmt.OriginFile);
                }
            }
        }
    }
}
