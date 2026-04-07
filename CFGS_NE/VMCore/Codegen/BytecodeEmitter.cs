using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;

namespace CFGS_VM.VMCore.Codegen
{
    internal sealed class BytecodeEmitter
    {
        public void EmitProgram(Compiler compiler, CompilationPlan plan, List<Stmt> program)
        {
            int jumpOverFunctionsIndex = compiler.ReserveFunctionJump();

            List<(FuncDeclStmt Declaration, int Start)> orderedFunctions = compiler.EmitFunctionBodies(plan.FunctionDecls);
            compiler.PatchFunctionJump(jumpOverFunctionsIndex);
            compiler.EmitFunctionClosures(orderedFunctions);

            compiler.EmitInterfaces(plan.OrderedInterfaces);
            compiler.BuildClassInfos(plan.OrderedClasses);
            compiler.EmitClasses(plan.OrderedClasses);
            compiler.EmitRemainingTopLevelStatements(program);

            compiler.Context.Instructions.Add(new Instruction(OpCode.HALT, null, 0, 0));
        }
    }
}
