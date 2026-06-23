namespace CFGS_VM.Hosting
{
    /// <summary>
    /// Capability surface for embedded scripts. Defaults are <b>fail-closed</b>: every capability is
    /// denied until the host explicitly grants it, so a host that forgets to configure permissions
    /// gets the safe (restricted) behavior rather than full access.
    /// <para>
    /// Note: the CFGS standard library is delivered as a plugin DLL and pulled in via
    /// <c>import "CFGS.StandardLibrary.dll"</c>. Hosts that run scripts which use the standard
    /// library must therefore set <see cref="AllowPluginLoading"/> to <c>true</c>.
    /// </para>
    /// </summary>
    public sealed class CfgsScriptPermissions
    {
        /// <summary>Allows standard-library file-system operations. Disabled by default.</summary>
        public bool AllowFileIO { get; set; } = false;

        /// <summary>Allows <c>import native</c> resolution and native .NET member invocation. Disabled by default.</summary>
        public bool AllowNativeInterop { get; set; } = false;

        /// <summary>Allows loading plugin DLLs (including the standard library). Disabled by default.</summary>
        public bool AllowPluginLoading { get; set; } = false;

        /// <summary>
        /// When enabled, standard-library file operations are jailed to the VM working directory:
        /// any path that resolves outside it (via an absolute path or <c>..</c> traversal) is
        /// rejected. Opt-in and disabled by default so granting <see cref="AllowFileIO"/> keeps
        /// unrestricted path access unless the host also requests containment.
        /// </summary>
        public bool RestrictFileIoToWorkspace { get; set; } = false;

        public TimeSpan? MaxExecutionTime { get; set; }

        public CfgsScriptPermissions Clone()
            => new()
            {
                AllowFileIO = AllowFileIO,
                AllowNativeInterop = AllowNativeInterop,
                AllowPluginLoading = AllowPluginLoading,
                RestrictFileIoToWorkspace = RestrictFileIoToWorkspace,
                MaxExecutionTime = MaxExecutionTime
            };
    }
}
