namespace CFGS_VM.VMCore.Debugging
{
    public enum VmDebuggerControl
    {
        Continue,
        Stop
    }

    public readonly record struct VmDebugContext(
        int Ip,
        Instruction Instruction,
        int StackCount,
        int ScopeCount,
        int CallStackDepth);

    public readonly record struct VmDebugVariable(string Name, object? Value, bool IsConst);

    public readonly record struct VmDebugFrame(int Index, int ReturnIp, int BaseScopeDepth, string OriginFile, int Line, int Col);

    public interface IVmDebugger
    {
        VmDebuggerControl BeforeInstruction(VM vm, VmDebugContext context);

        void AfterInstruction(VM vm, VmDebugContext context)
        {
        }

        void OnException(VM vm, VmDebugContext context, Exception exception)
        {
        }
    }
}
