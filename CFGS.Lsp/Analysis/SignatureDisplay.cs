using System.Globalization;
using CFGS_VM.Analytic.Tree;

namespace CFGS.Lsp;

internal static class SignatureDisplay
{
    public static IReadOnlyList<string> GetParameterDisplayList(
        IReadOnlyList<string> loweredParameters,
        IReadOnlyList<FunctionParameterSpec>? parameterSpecs,
        string? restParameter)
    {
        if (parameterSpecs is { Count: > 0 })
            return parameterSpecs.Select(FormatParameterSpec).ToList();

        return loweredParameters
            .Select(parameter => string.Equals(parameter, restParameter, StringComparison.Ordinal) ? $"*{parameter}" : parameter)
            .ToList();
    }

    public static string BuildFunctionLabel(
        string name,
        bool isAsync,
        IReadOnlyList<string> loweredParameters,
        IReadOnlyList<FunctionParameterSpec>? parameterSpecs,
        string? restParameter,
        bool includeName = true)
    {
        string prefix = isAsync ? "async func" : "func";
        string parameterText = string.Join(", ", GetParameterDisplayList(loweredParameters, parameterSpecs, restParameter));
        return includeName ? $"{prefix} {name}({parameterText})" : $"{prefix} ({parameterText})";
    }

    private static string FormatParameterSpec(FunctionParameterSpec parameter)
    {
        string label = parameter.DestructurePattern is null
            ? parameter.Name
            : RenderPattern(parameter.DestructurePattern);

        return parameter.IsRest ? $"*{label}" : label;
    }

    private static string RenderPattern(MatchPattern pattern)
    {
        return pattern switch
        {
            WildcardMatchPattern => "_",
            BindingMatchPattern binding => binding.Name,
            ValueMatchPattern value => RenderExpr(value.Value),
            ArrayMatchPattern array => $"[{string.Join(", ", array.Elements.Select(RenderPattern))}]",
            DictMatchPattern dict => "{" + string.Join(", ", dict.Entries.Select(RenderDictEntry)) + "}",
            _ => "?"
        };
    }

    private static string RenderDictEntry((string Key, MatchPattern Pattern) entry)
    {
        if (entry.Pattern is BindingMatchPattern binding &&
            string.Equals(binding.Name, entry.Key, StringComparison.Ordinal))
        {
            return entry.Key;
        }

        return $"{entry.Key}: {RenderPattern(entry.Pattern)}";
    }

    private static string RenderExpr(Expr expr)
    {
        return expr switch
        {
            NullExpr => "null",
            NumberExpr number => Convert.ToString(number.Value, CultureInfo.InvariantCulture) ?? "0",
            StringExpr text => $"\"{text.Value}\"",
            CharExpr ch => $"'{ch.Value}'",
            BoolExpr boolean => boolean.Value ? "true" : "false",
            VarExpr variable => variable.Name,
            _ => "?"
        };
    }
}
