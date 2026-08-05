using System.Text.Json;

namespace WOpenUsage.Providers.Codex;

public enum CodexAccountKind
{
    None,
    ChatGpt,
    ApiKey,
    AmazonBedrock,
    Other,
}

public sealed record CodexAccountStatus
{
    public CodexAccountStatus(
        CodexAccountKind kind,
        bool requiresOpenAiAuth,
        string? planType)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        RequiresOpenAiAuth = requiresOpenAiAuth;
        PlanType = planType;
    }

    public CodexAccountKind Kind { get; }

    public bool RequiresOpenAiAuth { get; }

    public string? PlanType { get; }
}

internal static class CodexAccountStatusParser
{
    public static CodexAccountStatus Parse(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("requiresOpenaiAuth", out JsonElement requiresElement)
            || requiresElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw ContractFailure();
        }

        bool requiresOpenAiAuth = requiresElement.GetBoolean();
        if (!result.TryGetProperty("account", out JsonElement account)
            || account.ValueKind == JsonValueKind.Null)
        {
            return new CodexAccountStatus(CodexAccountKind.None, requiresOpenAiAuth, planType: null);
        }

        if (account.ValueKind != JsonValueKind.Object
            || !account.TryGetProperty("type", out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            throw ContractFailure();
        }

        string? accountType = typeElement.GetString();
        CodexAccountKind kind = accountType switch
        {
            "chatgpt" => CodexAccountKind.ChatGpt,
            "apiKey" => CodexAccountKind.ApiKey,
            "amazonBedrock" => CodexAccountKind.AmazonBedrock,
            _ => CodexAccountKind.Other,
        };

        string? planType = null;
        if (kind == CodexAccountKind.ChatGpt)
        {
            if (!account.TryGetProperty("planType", out JsonElement planElement)
                || planElement.ValueKind != JsonValueKind.String)
            {
                throw ContractFailure();
            }

            planType = CodexPlanTypes.Normalize(planElement.GetString());
        }

        return new CodexAccountStatus(kind, requiresOpenAiAuth, planType);
    }

    private static CodexProtocolException ContractFailure() =>
        new("Codex app-server returned an unsupported account response.");
}

internal static class CodexPlanTypes
{
    private static readonly HashSet<string> KnownPlanTypes =
    [
        "free",
        "go",
        "plus",
        "pro",
        "prolite",
        "team",
        "self_serve_business_usage_based",
        "business",
        "enterprise_cbp_usage_based",
        "enterprise",
        "edu",
        "unknown",
    ];

    public static string Normalize(string? value) =>
        value is not null && KnownPlanTypes.Contains(value) ? value : "unknown";
}
