namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    public sealed class JsonNamespace
    {
        public static readonly JsonNamespace Instance = new();
        private JsonNamespace() { }
    }
}
