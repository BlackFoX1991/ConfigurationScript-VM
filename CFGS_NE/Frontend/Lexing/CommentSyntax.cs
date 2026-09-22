namespace CFGS_VM.Analytic.Tokens
{
    /// <summary>
    /// The comment rules shared by the lexer and by every tool that scans source text on its own
    /// (formatters, linter). A line comment starts with '#'. A block comment starts with "#+" followed by
    /// whitespace or the end of the text, and ends at the next "+#".
    /// <para>
    /// Requiring the whitespace keeps an ordinary line comment that happens to start with '+', such as
    /// "#+1 for this", from opening a block comment that silently swallows the code up to the next "+#".
    /// </para>
    /// </summary>
    public static class CommentSyntax
    {
        public const string BlockCommentOpen = "#+";

        public const string BlockCommentClose = "+#";

        /// <summary>
        /// Gets whether a block comment starts at <paramref name="index"/> of <paramref name="text"/>.
        /// </summary>
        public static bool IsBlockCommentStart(string text, int index)
        {
            if (index < 0 || index + 1 >= text.Length || text[index] != '#' || text[index + 1] != '+')
                return false;

            int after = index + 2;
            return after >= text.Length || char.IsWhiteSpace(text[after]);
        }
    }
}
