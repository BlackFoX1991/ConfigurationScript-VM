namespace CFGS_VM.Hosting
{
    public sealed class CfgsScriptPermissions
    {
        public bool AllowFileIO { get; set; } = true;

        public bool AllowNativeInterop { get; set; } = true;

        public bool AllowPluginLoading { get; set; } = true;

        public TimeSpan? MaxExecutionTime { get; set; }

        public CfgsScriptPermissions Clone()
            => new()
            {
                AllowFileIO = AllowFileIO,
                AllowNativeInterop = AllowNativeInterop,
                AllowPluginLoading = AllowPluginLoading,
                MaxExecutionTime = MaxExecutionTime
            };
    }
}
