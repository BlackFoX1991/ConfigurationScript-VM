namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    public sealed class FileSystemNamespace
    {
        public static readonly FileSystemNamespace Instance = new();
        private FileSystemNamespace() { }
    }
}
