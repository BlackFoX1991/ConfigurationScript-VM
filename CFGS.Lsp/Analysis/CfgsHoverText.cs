using System.Text;

namespace CFGS.Lsp;

internal static class CfgsHoverText
{
    // Builds the markdown shown by editors on hover. The first block stays close to
    // the CFGS declaration, the following lines explain where the symbol lives.
    public static string Create(string header, string uri, string qualifiedName, int kind, string detail)
    {
        StringBuilder sb = new();
        sb.AppendLine("```cfgs");
        sb.AppendLine(header);
        sb.AppendLine("```");
        sb.AppendLine();
        AppendSymbolLocation(sb, qualifiedName, kind, detail);
        sb.Append("Defined in `");
        sb.Append(uri);
        sb.Append('`');
        return sb.ToString();
    }

    private static void AppendSymbolLocation(StringBuilder sb, string qualifiedName, int kind, string detail)
    {
        string cleanQualifiedName = StripLocalSuffix(qualifiedName);
        if (string.IsNullOrWhiteSpace(cleanQualifiedName))
            return;

        sb.Append("Symbol: `");
        sb.Append(cleanQualifiedName);
        sb.AppendLine("`");

        string? container = TryGetContainerPath(cleanQualifiedName);
        if (!string.IsNullOrWhiteSpace(container))
        {
            if (IsTypeMember(kind, detail))
            {
                string? namespacePath = TryGetContainerPath(container!);
                if (!string.IsNullOrWhiteSpace(namespacePath))
                {
                    sb.Append("Namespace: `");
                    sb.Append(namespacePath);
                    sb.AppendLine("`");
                }

                sb.Append("Type/Class: `");
                sb.Append(GetLastPathPart(container!));
                sb.AppendLine("`");
            }
            else if (IsNamespaceScopedSymbol(kind))
            {
                sb.Append("Namespace: `");
                sb.Append(container);
                sb.AppendLine("`");
            }

            sb.Append("Container: `");
            sb.Append(container);
            sb.AppendLine("`");
        }

        sb.AppendLine();
    }

    private static string StripLocalSuffix(string qualifiedName)
    {
        int marker = qualifiedName.IndexOf('#', StringComparison.Ordinal);
        return marker < 0 ? qualifiedName : qualifiedName[..marker];
    }

    private static string? TryGetContainerPath(string qualifiedName)
    {
        int dot = qualifiedName.LastIndexOf('.');
        return dot <= 0 ? null : qualifiedName[..dot];
    }

    private static string GetLastPathPart(string path)
    {
        int dot = path.LastIndexOf('.');
        return dot < 0 ? path : path[(dot + 1)..];
    }

    private static bool IsTypeMember(int kind, string detail)
        => kind is 6 or 7 or 22 ||
           detail.StartsWith("static var ", StringComparison.Ordinal) ||
           detail.StartsWith("static const ", StringComparison.Ordinal) ||
           detail.StartsWith("property ", StringComparison.Ordinal) ||
           detail.StartsWith("static property ", StringComparison.Ordinal);

    private static bool IsNamespaceScopedSymbol(int kind)
        => kind is 5 or 10 or 11 or 12;
}
