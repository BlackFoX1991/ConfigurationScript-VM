using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;

namespace CFGS_VM.VMCore.Codegen
{
    internal sealed class BytecodeEmitter
    {
        public void EmitProgram(Compiler compiler, BoundProgram program)
        {
            int jumpOverFunctionsIndex = compiler.ReserveFunctionJump();

            List<(BoundFunction Function, int Start)> orderedFunctions = compiler.EmitFunctionBodies(program.Functions);
            compiler.PatchFunctionJump(jumpOverFunctionsIndex);
            compiler.EmitFunctionClosures(orderedFunctions);

            compiler.EmitInterfaces(program.OrderedInterfaces);
            compiler.BuildClassInfos(program.OrderedClasses);
            compiler.EmitClasses(program.OrderedClasses);
            compiler.EmitRemainingTopLevelStatements(program);

            compiler.Context.Instructions.Add(new Instruction(OpCode.HALT, null, 0, 0));
        }
    }
}
