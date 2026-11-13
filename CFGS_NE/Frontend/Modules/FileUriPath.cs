namespace CFGS_VM.Analytic.Modules;

internal static class FileUriPath
{
    public static string GetLocalPath(Uri uri)
    {
        if (!uri.IsFile)
            throw new ArgumentException("Expected a file URI.", nameof(uri));

        string localPath = uri.LocalPath;
        // VS Code encodes the drive colon as %3A. System.Uri then leaves a leading slash in
        // LocalPath ("/c:/...") on Windows, which Path.GetFullPath treats as a path on the
        // current drive instead of the intended absolute path.
        if (OperatingSystem.IsWindows() &&
            localPath.Length >= 3 &&
            localPath[0] == '/' &&
            char.IsLetter(localPath[1]) &&
            localPath[2] == ':' &&
            (localPath.Length == 3 || localPath[3] is '/' or '\\'))
        {
            localPath = localPath[1..];
        }

        return Path.GetFullPath(localPath);
    }
}
