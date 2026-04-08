using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;

namespace CFGS_VM.VMCore.Codegen
{
    internal sealed class CompilationPipeline
    {
        public List<Instruction> Compile(Compiler compiler, List<Stmt> program)
        {
            compiler.ResetCompilationState();

            BoundProgram boundProgram = compiler.BuildSymbolIndex(program);
            compiler.ValidateTopLevelDeclarations();
            compiler.ResolveTypeGraph(boundProgram);
            compiler.RunSemanticChecks(boundProgram);
            compiler.EmitProgram(boundProgram);

            return compiler.Context.Instructions;
        }
    }
}
