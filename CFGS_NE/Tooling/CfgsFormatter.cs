using System.Text;
using CFGS_VM.Analytic.Tokens;

namespace CFGS_VM.Tooling;

public sealed record CfgsFormatOptions(int IndentSize = 4, bool UseTabs = false, int MaxLineWidth = int.MaxValue);

public sealed record CfgsFormatResult(string Text, bool Changed);

public static class CfgsFormatter
{
    public static CfgsFormatResult Format(string sourceText, CfgsFormatOptions options)
    {
        string normalized = sourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string? astFormatted = CfgsAstFormatter.TryFormat(normalized, options);
        if (astFormatted is not null && IsStableAstFormat(astFormatted, options))
            return new CfgsFormatResult(astFormatted, !string.Equals(sourceText, astFormatted, StringComparison.Ordinal));

        string formatted = FormatLegacy(normalized, options);
        return new CfgsFormatResult(formatted, !string.Equals(sourceText, formatted, StringComparison.Ordinal));
    }

    /// <summary>
    /// Safety net for the AST-based formatter, which re-emits source from the parsed tree. The
    /// output is only trusted when re-formatting it is a no-op (a stable fixed point). A faulty
    /// re-emit — for example one that drops semantically-required parentheses — would parse to a
    /// different tree and therefore not be a fixed point, so we reject it and fall back to the
    /// conservative line-based formatter, which never rewrites expressions. If the formatted text
    /// fails to re-parse at all, <see cref="CfgsAstFormatter.TryFormat"/> returns null and we also
    /// fall back. This keeps a formatter regression from silently changing a script's meaning.
    /// </summary>
    private static bool IsStableAstFormat(string astFormatted, CfgsFormatOptions options)
    {
        string? reFormatted = CfgsAstFormatter.TryFormat(astFormatted, options);
        return reFormatted is not null && string.Equals(reFormatted, astFormatted, StringComparison.Ordinal);
    }

    private static string FormatLegacy(string normalized, CfgsFormatOptions options)
    {
        string[] lines = normalized.Split('\n');
        string indentUnit = options.UseTabs ? "\t" : new string(' ', Math.Max(1, options.IndentSize));
        StringBuilder output = new(normalized.Length + lines.Length * 2);
        int depth = 0;
        bool inBlockComment = false;

        for (int i = 0; i < lines.Length; i++)
        {
            bool isLastArtificialLine = i == lines.Length - 1 && lines[i].Length == 0 && normalized.EndsWith('\n');
            if (isLastArtificialLine)
                continue;

            string raw = lines[i];
            string rawTrimmed = raw.Trim();
            string trimmed = ShouldPreserveCommentLine(rawTrimmed, inBlockComment)
                ? rawTrimmed
                : NormalizeInlineSpacing(rawTrimmed);

            if (trimmed.Length == 0)
            {
                output.Append('\n');
                continue;
            }

            int lineDepth = depth;
            if (!inBlockComment)
                lineDepth = Math.Max(0, depth - CountLeadingCloseBraces(trimmed));

            output.Append(Repeat(indentUnit, lineDepth));
            output.Append(trimmed);
            output.Append('\n');

            if (!inBlockComment)
            {
                CountLineBraces(trimmed, ref inBlockComment, out int opens, out int closes);
                depth = Math.Max(0, depth + opens - closes);
            }
            else
            {
                CountLineBraces(trimmed, ref inBlockComment, out _, out _);
            }
        }

        return output.ToString();
    }

    private static string Repeat(string text, int count)
    {
        if (count <= 0)
            return string.Empty;

        StringBuilder sb = new(text.Length * count);
        for (int i = 0; i < count; i++)
            sb.Append(text);
        return sb.ToString();
    }

    private static int CountLeadingCloseBraces(string trimmed)
    {
        int count = 0;
        while (count < trimmed.Length && trimmed[count] == '}')
            count++;
        return count;
    }

    private static string NormalizeInlineSpacing(string line)
    {
        if (line.Length == 0 || line.StartsWith('#'))
            return line;

        StringBuilder sb = new(line.Length + 8);
        bool inString = false;
        bool inChar = false;
        bool escape = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            char prev = i > 0 ? line[i - 1] : '\0';
            char next = i + 1 < line.Length ? line[i + 1] : '\0';

            if (inString || inChar)
            {
                sb.Append(ch);
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                    escape = true;
                else if (inString && ch == '"')
                    inString = false;
                else if (inChar && ch == '\'')
                    inChar = false;

                continue;
            }

            if (ch == '#')
            {
                TrimTrailingSpaces(sb);
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(line.AsSpan(i));
                break;
            }

            if (ch == '"')
            {
                inString = true;
                sb.Append(ch);
                continue;
            }

            if (ch == '\'')
            {
                inChar = true;
                sb.Append(ch);
                continue;
            }

            if (ch == ',')
            {
                TrimTrailingSpaces(sb);
                sb.Append(',');
                if (next != '\0' && !char.IsWhiteSpace(next) && next != ')' && next != ']' && next != '}')
                    sb.Append(' ');
                continue;
            }

            if (ch == ':' && next != ':' && prev != ':')
            {
                TrimTrailingSpaces(sb);
                sb.Append(": ");
                while (i + 1 < line.Length && char.IsWhiteSpace(line[i + 1]))
                    i++;
                continue;
            }

            if (IsOperatorStart(ch))
            {
                string op = ReadOperator(line, i);
                if (op.Length > 0)
                {
                    if (IsUnaryOperator(op) && IsUnaryContext(sb))
                    {
                        sb.Append(op);
                        while (i + op.Length < line.Length && char.IsWhiteSpace(line[i + op.Length]))
                            i++;
                    }
                    else if (ShouldSpaceOperator(op, prev, next))
                    {
                        TrimTrailingSpaces(sb);
                        if (sb.Length > 0)
                            sb.Append(' ');
                        sb.Append(op);
                        if (i + op.Length < line.Length && !char.IsWhiteSpace(line[i + op.Length]))
                            sb.Append(' ');
                    }
                    else
                    {
                        sb.Append(op);
                    }

                    i += op.Length - 1;
                    continue;
                }
            }

            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1]))
                    sb.Append(' ');
                continue;
            }

            sb.Append(ch);
        }

        TrimTrailingSpaces(sb);
        return sb.ToString();
    }

    private static bool IsOperatorStart(char ch)
        => ch is '=' or '!' or '<' or '>' or '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^' or '?';

    private static bool ShouldPreserveCommentLine(string trimmed, bool inBlockComment)
        => inBlockComment ||
           trimmed.StartsWith('#') ||
           trimmed.StartsWith("+#", StringComparison.Ordinal);

    private static string ReadOperator(string line, int index)
    {
        // Longest-match first: the three-character merge operators must precede the two-character
        // shifts, otherwise "<<<" is read as "<<" plus a stray "<" and the file is corrupted.
        foreach (string op in new[] { "<<<", ">>>", "==", "!=", "<=", ">=", "&&", "||", "??", "+=", "-=", "*=", "/=", "%=", "<<", ">>", "++", "--", "=>", "**" })
        {
            if (index + op.Length <= line.Length && string.Equals(line.Substring(index, op.Length), op, StringComparison.Ordinal))
                return op;
        }

        char ch = line[index];
        return ch is '=' or '<' or '>' or '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^' or '?' ? ch.ToString() : string.Empty;
    }

    private static bool ShouldSpaceOperator(string op, char prev, char next)
    {
        if (op is "++" or "--" or "." or "=>")
            return false;

        if ((op is "+" or "-" or "!") && (prev is '\0' or '(' or '[' or '{' or ',' or ':' or '=' or '<' or '>' or '!' or '?' or '+' or '-' or '*' or '/' or '%'))
            return false;

        if (op == "*" && next == '[')
            return false;

        return true;
    }

    private static bool IsUnaryOperator(string op)
        => op is "+" or "-" or "!";

    private static bool IsUnaryContext(StringBuilder sb)
    {
        int index = sb.Length - 1;
        while (index >= 0 && char.IsWhiteSpace(sb[index]))
            index--;

        if (index < 0)
            return true;

        char previous = sb[index];
        if (previous is '(' or '[' or '{' or ',' or ':' or '=' or '<' or '>' or '!' or '?' or '+' or '-' or '*' or '/' or '%')
            return true;

        string token = ReadTrailingIdentifier(sb, index);
        return token is "return" or "throw" or "yield" or "await" or "case";
    }

    private static string ReadTrailingIdentifier(StringBuilder sb, int endIndex)
    {
        int start = endIndex;
        while (start >= 0 && (char.IsLetterOrDigit(sb[start]) || sb[start] == '_'))
            start--;

        return sb.ToString(start + 1, endIndex - start);
    }

    private static void TrimTrailingSpaces(StringBuilder sb)
    {
        while (sb.Length > 0 && char.IsWhiteSpace(sb[^1]))
            sb.Length--;
    }

    private static void CountLineBraces(string line, ref bool inBlockComment, out int opens, out int closes)
    {
        opens = 0;
        closes = 0;
        bool inString = false;
        bool inChar = false;
        bool escape = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            char next = i + 1 < line.Length ? line[i + 1] : '\0';

            if (inBlockComment)
            {
                if (ch == '+' && next == '#')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (inString || inChar)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (inString && ch == '"')
                    inString = false;
                else if (inChar && ch == '\'')
                    inChar = false;

                continue;
            }

            if (ch == '#')
            {
                if (CommentSyntax.IsBlockCommentStart(line, i))
                {
                    inBlockComment = true;
                    i++;
                }
                else
                {
                    break;
                }
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '\'')
            {
                inChar = true;
                continue;
            }

            if (ch == '{')
                opens++;
            else if (ch == '}')
                closes++;
        }
    }
}
