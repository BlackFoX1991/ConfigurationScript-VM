using CFGS_VM.Analytic.Modules;

namespace CFGS.Lsp;

internal sealed class CfgsDocumentUriComparer : IEqualityComparer<string>
{
    public static readonly CfgsDocumentUriComparer Instance = new();

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public bool Equals(string? x, string? y)
        => PathComparer.Equals(Normalize(x), Normalize(y));

    public int GetHashCode(string uri)
        => PathComparer.GetHashCode(Normalize(uri));

    private static string Normalize(string? uri)
    {
        if (uri is null)
            return string.Empty;

        try
        {
            if (System.Uri.TryCreate(uri, UriKind.Absolute, out System.Uri? parsed) && parsed.IsFile)
                return FileUriPath.GetLocalPath(parsed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            // Invalid file URIs retain their original identity.
        }

        return uri;
    }
}
