using System.Collections.Generic;

namespace CFGS_VM.Analytic.Tokens
{
    public abstract record InterpolatedStringTokenPart;

    public sealed record InterpolatedStringTextTokenPart(string Text) : InterpolatedStringTokenPart;

    public sealed record InterpolatedStringExpressionTokenPart(
        string ExpressionText,
        int Line,
        int Column,
        int? Alignment = null,
        string? Format = null) : InterpolatedStringTokenPart;

    public sealed record InterpolatedStringTokenValue(IReadOnlyList<InterpolatedStringTokenPart> Parts);
}
