namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    public sealed class ConsoleNamespace
    {
        public static readonly ConsoleNamespace Instance = new();
        private ConsoleNamespace() { }
    }
}
