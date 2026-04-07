using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;

namespace CFGS_VM.VMCore.Codegen
{
    internal sealed class CompilationPipeline
    {
        public List<Instruction> Compile(Compiler compiler, List<Stmt> program)
        {
            compiler.ResetCompilationState();

            CompilationPlan plan = compiler.BuildSymbolIndex(program);
            compiler.ValidateTopLevelDeclarations();
            compiler.ResolveTypeGraph(plan);
            compiler.RunSemanticChecks(plan);
            compiler.EmitProgram(plan, program);

            return compiler.Context.Instructions;
        }
    }
}
