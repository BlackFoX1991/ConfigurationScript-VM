namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    internal sealed class TaskNamespace
    {
        public static readonly TaskNamespace Instance = new();
        private TaskNamespace() { }
    }
}