namespace CFGS_VM.VMCore.Extensions.Intrinsics.Handles
{
    /// <summary>
    /// Marker singleton for the built-in <c>eventlog</c> namespace, which exposes read/write access
    /// to the Windows Event Log. The actual intrinsics are registered in
    /// <see cref="CFGS_VM.StandardLibrary.CoreStandardLibrary"/>.
    /// </summary>
    public sealed class EventLogNamespace
    {
        public static readonly EventLogNamespace Instance = new();
        private EventLogNamespace() { }
    }
}
