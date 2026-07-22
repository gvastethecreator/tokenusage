using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace WOpenUsage.Providers.Codex;

public sealed record CodexRateLimitWindow
{
    public CodexRateLimitWindow(
        int usedPercent,
        DateTimeOffset? resetsAtUtc,
        long? windowDurationMinutes)
    {
        if (usedPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usedPercent),
                "Used percent must be between zero and one hundred.");
        }

        if (resetsAtUtc is not null && resetsAtUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Reset time must use the UTC offset.", nameof(resetsAtUtc));
        }

        if (windowDurationMinutes is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowDurationMinutes),
                "Window duration must be positive when present.");
        }

        UsedPercent = usedPercent;
        ResetsAtUtc = resetsAtUtc;
        WindowDurationMinutes = windowDurationMinutes;
    }

    public int UsedPercent { get; }

    public DateTimeOffset? ResetsAtUtc { get; }

    public long? WindowDurationMinutes { get; }
}

public sealed record CodexRateLimitBucket(
    string? PlanType,
    CodexRateLimitWindow? Primary,
    CodexRateLimitWindow? Secondary);

public sealed record CodexRateLimitsSnapshot
{
    public CodexRateLimitsSnapshot(
        CodexRateLimitBucket rateLimits,
        IReadOnlyDictionary<string, CodexRateLimitBucket> rateLimitsByLimitId)
    {
        RateLimits = rateLimits ?? throw new ArgumentNullException(nameof(rateLimits));
        ArgumentNullException.ThrowIfNull(rateLimitsByLimitId);
        if (rateLimitsByLimitId.Any(pair => pair.Key is null || pair.Value is null))
        {
            throw new ArgumentException(
                "Additional rate limits cannot contain null keys or values.",
                nameof(rateLimitsByLimitId));
        }

        RateLimitsByLimitId = new ReadOnlyDictionary<string, CodexRateLimitBucket>(
            new Dictionary<string, CodexRateLimitBucket>(rateLimitsByLimitId, StringComparer.Ordinal));
    }

    public CodexRateLimitBucket RateLimits { get; }

    public IReadOnlyDictionary<string, CodexRateLimitBucket> RateLimitsByLimitId { get; }
}

internal static class CodexRateLimitsParser
{
    private const int MaximumAdditionalLimits = 64;
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

    public static CodexRateLimitsSnapshot Parse(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("rateLimits", out JsonElement rateLimitsElement)
            || rateLimitsElement.ValueKind != JsonValueKind.Object)
        {
            throw ContractFailure();
        }

        CodexRateLimitBucket rateLimits = ParseBucket(rateLimitsElement);
        var additionalLimits = new Dictionary<string, CodexRateLimitBucket>(StringComparer.Ordinal);

        if (result.TryGetProperty("rateLimitsByLimitId", out JsonElement additionalElement)
            && additionalElement.ValueKind != JsonValueKind.Null)
        {
            if (additionalElement.ValueKind != JsonValueKind.Object)
            {
                throw ContractFailure();
            }

            foreach (JsonProperty property in additionalElement.EnumerateObject())
            {
                if (additionalLimits.Count >= MaximumAdditionalLimits
                    || !IsSafeLimitId(property.Name)
                    || property.Value.ValueKind != JsonValueKind.Object
                    || !additionalLimits.TryAdd(property.Name, ParseBucket(property.Value)))
                {
                    throw ContractFailure();
                }
            }
        }

        return new CodexRateLimitsSnapshot(rateLimits, additionalLimits);
    }

    private static CodexRateLimitBucket ParseBucket(JsonElement element)
    {
        string? planType = null;
        if (element.TryGetProperty("planType", out JsonElement planElement)
            && planElement.ValueKind != JsonValueKind.Null)
        {
            if (planElement.ValueKind != JsonValueKind.String)
            {
                throw ContractFailure();
            }

            string? candidate = planElement.GetString();
            planType = candidate is not null && KnownPlanTypes.Contains(candidate)
                ? candidate
                : "unknown";
        }

        return new CodexRateLimitBucket(
            planType,
            ParseOptionalWindow(element, "primary"),
            ParseOptionalWindow(element, "secondary"));
    }

    private static CodexRateLimitWindow? ParseOptionalWindow(
        JsonElement bucket,
        string propertyName)
    {
        if (!bucket.TryGetProperty(propertyName, out JsonElement window)
            || window.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("usedPercent", out JsonElement usedElement)
            || !usedElement.TryGetInt32(out int usedPercent)
            || usedPercent is < 0 or > 100)
        {
            throw ContractFailure();
        }

        DateTimeOffset? resetsAtUtc = null;
        if (window.TryGetProperty("resetsAt", out JsonElement resetElement)
            && resetElement.ValueKind != JsonValueKind.Null)
        {
            if (!resetElement.TryGetInt64(out long resetSeconds))
            {
                throw ContractFailure();
            }

            try
            {
                resetsAtUtc = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw ContractFailure();
            }
        }

        long? durationMinutes = null;
        if (window.TryGetProperty("windowDurationMins", out JsonElement durationElement)
            && durationElement.ValueKind != JsonValueKind.Null)
        {
            if (!durationElement.TryGetInt64(out long duration) || duration <= 0)
            {
                throw ContractFailure();
            }

            durationMinutes = duration;
        }

        return new CodexRateLimitWindow(usedPercent, resetsAtUtc, durationMinutes);
    }

    private static bool IsSafeLimitId(string value) =>
        value.Length is > 0 and <= 64
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static CodexProtocolException ContractFailure() =>
        new("Codex app-server returned an unsupported rate-limit response.");
}
