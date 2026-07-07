namespace CFGS_VM.Tooling;

internal enum CfgsLintPreset
{
    Recommended,
    Strict
}

internal readonly record struct CfgsLintRuleConfiguration(bool Enabled, CfgsToolDiagnosticSeverity Severity);

internal readonly record struct CfgsLintRuleOverride(bool? Enabled = null, CfgsToolDiagnosticSeverity? Severity = null);

internal sealed record CfgsLintOptions(
    int MaxLineLength = 140,
    bool WarningsAsErrors = false,
    bool Fix = false,
    bool AllowTabs = false,
    int IndentSize = 4)
{
    public CfgsLintPreset Preset { get; init; } = CfgsLintPreset.Recommended;
    public IReadOnlyDictionary<string, CfgsLintRuleOverride> RuleOverrides { get; init; } =
        new Dictionary<string, CfgsLintRuleOverride>(StringComparer.OrdinalIgnoreCase);
}

internal static class CfgsLintRuleRegistry
{
    private static readonly Dictionary<string, CfgsLintRuleDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CFGS8001"] = new("CFGS8001", CfgsToolDiagnosticSeverity.Warning, false, true),
        ["CFGS8002"] = new("CFGS8002", CfgsToolDiagnosticSeverity.Warning, true, true),
        ["CFGS8003"] = new("CFGS8003", CfgsToolDiagnosticSeverity.Info, true, true),
        ["CFGS8004"] = new("CFGS8004", CfgsToolDiagnosticSeverity.Warning, true, true),
        ["CFGS8005"] = new("CFGS8005", CfgsToolDiagnosticSeverity.Warning, false, true),
        ["CFGS8011"] = new("CFGS8011", CfgsToolDiagnosticSeverity.Info, true, true),
        ["CFGS8012"] = new("CFGS8012", CfgsToolDiagnosticSeverity.Warning, true, true),
        ["CFGS8013"] = new("CFGS8013", CfgsToolDiagnosticSeverity.Info, true, true),
        ["CFGS8014"] = new("CFGS8014", CfgsToolDiagnosticSeverity.Info, true, true),
        ["CFGS8015"] = new("CFGS8015", CfgsToolDiagnosticSeverity.Warning, true, true),
        ["CFGS8016"] = new("CFGS8016", CfgsToolDiagnosticSeverity.Info, true, true),
        ["CFGS8017"] = new("CFGS8017", CfgsToolDiagnosticSeverity.Warning, true, true),
        ["CFGS8018"] = new("CFGS8018", CfgsToolDiagnosticSeverity.Warning, true, true),
        ["CFGS8019"] = new("CFGS8019", CfgsToolDiagnosticSeverity.Warning, false, true),
        ["CFGS8020"] = new("CFGS8020", CfgsToolDiagnosticSeverity.Warning, true, true),
        ["CFGS8021"] = new("CFGS8021", CfgsToolDiagnosticSeverity.Warning, false, true)
    };

    public static bool ContainsRule(string code)
        => Definitions.ContainsKey(code);

    public static IReadOnlyDictionary<string, CfgsLintRuleConfiguration> ResolveRules(
        CfgsLintPreset preset,
        IReadOnlyDictionary<string, CfgsLintRuleOverride>? overrides)
    {
        Dictionary<string, CfgsLintRuleConfiguration> resolved = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string code, CfgsLintRuleDefinition definition) in Definitions)
        {
            CfgsLintRuleOverride ruleOverride = default;
            overrides?.TryGetValue(code, out ruleOverride);

            bool enabled = IsEnabledInPreset(definition, preset);
            if (ruleOverride.Enabled.HasValue)
                enabled = ruleOverride.Enabled.Value;
            else if (ruleOverride.Severity.HasValue)
                enabled = true;

            CfgsToolDiagnosticSeverity severity = ruleOverride.Severity ?? definition.DefaultSeverity;
            resolved[code] = new CfgsLintRuleConfiguration(enabled, severity);
        }

        return resolved;
    }

    public static bool TryParsePreset(string? value, out CfgsLintPreset preset)
    {
        if (string.Equals(value, "recommended", StringComparison.OrdinalIgnoreCase))
        {
            preset = CfgsLintPreset.Recommended;
            return true;
        }

        if (string.Equals(value, "strict", StringComparison.OrdinalIgnoreCase))
        {
            preset = CfgsLintPreset.Strict;
            return true;
        }

        preset = CfgsLintPreset.Recommended;
        return false;
    }

    public static bool TryParseSeverity(string? value, out CfgsToolDiagnosticSeverity severity)
    {
        if (string.Equals(value, "info", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "information", StringComparison.OrdinalIgnoreCase))
        {
            severity = CfgsToolDiagnosticSeverity.Info;
            return true;
        }

        if (string.Equals(value, "warning", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "warn", StringComparison.OrdinalIgnoreCase))
        {
            severity = CfgsToolDiagnosticSeverity.Warning;
            return true;
        }

        if (string.Equals(value, "error", StringComparison.OrdinalIgnoreCase))
        {
            severity = CfgsToolDiagnosticSeverity.Error;
            return true;
        }

        severity = CfgsToolDiagnosticSeverity.Info;
        return false;
    }

    private static bool IsEnabledInPreset(CfgsLintRuleDefinition definition, CfgsLintPreset preset)
        => preset switch
        {
            CfgsLintPreset.Strict => definition.EnabledInStrict,
            _ => definition.EnabledInRecommended
        };

    private sealed record CfgsLintRuleDefinition(
        string Code,
        CfgsToolDiagnosticSeverity DefaultSeverity,
        bool EnabledInRecommended,
        bool EnabledInStrict);
}
