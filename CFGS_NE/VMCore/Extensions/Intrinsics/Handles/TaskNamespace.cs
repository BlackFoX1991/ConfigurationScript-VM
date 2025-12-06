namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    public sealed class TaskNamespace
    {
        public static readonly TaskNamespace Instance = new();
        private TaskNamespace() { }
    }
}