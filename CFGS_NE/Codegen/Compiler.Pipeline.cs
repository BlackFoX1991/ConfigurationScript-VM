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

        internal BoundProgram BuildSymbolIndex(List<Stmt> program)
        {
            return new SymbolIndex().Build(_context, Functions, program);
        }

        internal void ValidateTopLevelDeclarations()
        {
            new TopLevelValidator().Validate(_context);
        }

        internal void ResolveTypeGraph(BoundProgram program)
        {
            ClassCatalog classCatalog = new();
            InterfaceCatalog interfaceCatalog = new();

            classCatalog.NormalizeInheritanceDeclarations(this, _qualifiedClassDecls.Values.Distinct());
            interfaceCatalog.ValidateAllKnownInterfaces(this);

            List<InterfaceDeclStmt> orderedInterfaces = interfaceCatalog.OrderByInheritance(
                this,
                program.Interfaces.Select(iface => iface.Declaration).ToList());
            List<ClassDeclStmt> orderedClasses = classCatalog.OrderByInheritance(
                this,
                program.Classes.Select(cls => cls.Declaration).ToList());

            program.OrderedInterfaces = orderedInterfaces.Select(program.GetInterface).ToList();
            program.OrderedClasses = orderedClasses.Select(program.GetClass).ToList();
        }

        internal void RunSemanticChecks(BoundProgram program)
        {
            List<ClassDeclStmt> orderedClasses = program.OrderedClasses
                .Select(boundClass => boundClass.Declaration)
                .ToList();
            List<Stmt> syntaxProgram = program.TopLevelStatements
                .Select(boundStmt => boundStmt.Syntax)
                .ToList();

            new ClassSemanticValidator().ValidateAll(this, orderedClasses);
            ValidateNamespaceScopeSemantics(program);
            new StatementShapeValidator().Validate(syntaxProgram);
            new MemberUsageValidator(this).Validate(syntaxProgram);
        }

        private void ValidateNamespaceScopeSemantics(BoundProgram program)
        {
            ClassCatalog classCatalog = new();
            ClassSemanticValidator semanticValidator = new();

            foreach (BlockStmt namespaceScope in program.TopLevelStatements
                .Select(boundStmt => boundStmt.Syntax)
                .OfType<BlockStmt>())
            {
                if (!TryGetNamespaceScopePath(namespaceScope, out _))
                    continue;

                List<ClassDeclStmt> namespaceClasses = namespaceScope.Statements
                    .OfType<ClassDeclStmt>()
                    .ToList();

                if (namespaceClasses.Count == 0)
                    continue;

                List<ClassDeclStmt> sortedNamespaceClasses = classCatalog.OrderByInheritance(this, namespaceClasses);
                semanticValidator.ValidateInheritanceOverrides(this, sortedNamespaceClasses);
                semanticValidator.ValidateBaseConstructorCalls(this, sortedNamespaceClasses);
                semanticValidator.ValidateInterfaceImplementations(this, sortedNamespaceClasses);
            }
        }

        internal void EmitProgram(BoundProgram program)
            => new BytecodeEmitter().EmitProgram(this, program);

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

        internal List<(BoundFunction Function, int Start)> EmitFunctionBodies(IReadOnlyList<BoundFunction> functionDecls)
        {
            List<(BoundFunction Function, int Start)> orderedFunctions = [];

            foreach (BoundFunction function in functionDecls)
            {
                FuncDeclStmt functionDecl = function.Declaration;
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

                    orderedFunctions.Add((function, functionStart));
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

        internal void EmitFunctionClosures(IReadOnlyList<(BoundFunction Function, int Start)> orderedFunctions)
        {
            foreach ((BoundFunction function, int start) in orderedFunctions)
            {
                FuncDeclStmt declaration = function.Declaration;
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

        internal void EmitInterfaces(IReadOnlyList<BoundInterface> orderedInterfaces)
        {
            foreach (BoundInterface boundInterface in orderedInterfaces)
                CompileStmt(boundInterface.Declaration, insideFunction: false);
        }

        internal void EmitClasses(IReadOnlyList<BoundClass> orderedClasses)
        {
            foreach (BoundClass boundClass in orderedClasses)
            {
                ClassDeclStmt classDecl = boundClass.Declaration;
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

        internal void EmitRemainingTopLevelStatements(BoundProgram program)
        {
            foreach (BoundStmt boundStmt in program.TopLevelStatements)
            {
                if (boundStmt is BoundFunction or BoundInterface or BoundClass)
                    continue;

                Stmt stmt = boundStmt.Syntax;
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
