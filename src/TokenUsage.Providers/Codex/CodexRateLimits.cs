using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace TokenUsage.Providers.Codex;

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
    CodexRateLimitWindow? Secondary)
{
    public string? LimitId { get; init; }

    public string? LimitName { get; init; }
}

public sealed record CodexResetCredit
{
    public CodexResetCredit(
        string resetType,
        string status,
        DateTimeOffset? grantedAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        RequireUtc(grantedAtUtc, nameof(grantedAtUtc));
        RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
        ResetType = resetType;
        Status = status;
        GrantedAtUtc = grantedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string ResetType { get; }

    public string Status { get; }

    public DateTimeOffset? GrantedAtUtc { get; }

    public DateTimeOffset? ExpiresAtUtc { get; }

    private static void RequireUtc(DateTimeOffset? value, string paramName)
    {
        if (value is not null && value.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Credit timestamps must use UTC.", paramName);
        }
    }
}

public sealed record CodexResetCreditInventory
{
    public CodexResetCreditInventory(
        int availableCount,
        IReadOnlyList<CodexResetCredit>? credits)
    {
        if (availableCount is < 0 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(availableCount));
        }

        if (credits?.Any(credit => credit is null) is true)
        {
            throw new ArgumentException("Reset credits cannot contain null items.", nameof(credits));
        }

        AvailableCount = availableCount;
        Credits = credits is null
            ? null
            : new ReadOnlyCollection<CodexResetCredit>(credits.ToArray());
    }

    public int AvailableCount { get; }

    public IReadOnlyList<CodexResetCredit>? Credits { get; }
}

public sealed record CodexRateLimitsSnapshot
{
    public CodexRateLimitsSnapshot(
        CodexRateLimitBucket rateLimits,
        IReadOnlyDictionary<string, CodexRateLimitBucket> rateLimitsByLimitId,
        CodexResetCreditInventory? resetCredits = null)
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
        ResetCredits = resetCredits;
    }

    public CodexRateLimitBucket RateLimits { get; }

    public IReadOnlyDictionary<string, CodexRateLimitBucket> RateLimitsByLimitId { get; }

    public CodexResetCreditInventory? ResetCredits { get; }
}

internal static class CodexRateLimitsParser
{
    private const int MaximumAdditionalLimits = 64;
    private const int MaximumResetCredits = 64;
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

        CodexResetCreditInventory? resetCredits = ParseOptionalResetCredits(result);
        return new CodexRateLimitsSnapshot(rateLimits, additionalLimits, resetCredits);
    }

    private static CodexResetCreditInventory? ParseOptionalResetCredits(JsonElement result)
    {
        if (!result.TryGetProperty("rateLimitResetCredits", out JsonElement inventory)
            || inventory.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (inventory.ValueKind != JsonValueKind.Object
            || !inventory.TryGetProperty("availableCount", out JsonElement countElement)
            || !countElement.TryGetInt32(out int availableCount)
            || availableCount is < 0 or > 1_000_000)
        {
            throw ContractFailure();
        }

        IReadOnlyList<CodexResetCredit>? credits = null;
        if (inventory.TryGetProperty("credits", out JsonElement creditsElement)
            && creditsElement.ValueKind != JsonValueKind.Null)
        {
            if (creditsElement.ValueKind != JsonValueKind.Array
                || creditsElement.GetArrayLength() > MaximumResetCredits)
            {
                throw ContractFailure();
            }

            var parsed = new List<CodexResetCredit>(creditsElement.GetArrayLength());
            foreach (JsonElement credit in creditsElement.EnumerateArray())
            {
                if (credit.ValueKind != JsonValueKind.Object)
                {
                    throw ContractFailure();
                }

                parsed.Add(new CodexResetCredit(
                    ParseRequiredToken(credit, "resetType"),
                    ParseRequiredToken(credit, "status"),
                    ParseOptionalUnixTimestamp(credit, "grantedAt"),
                    ParseOptionalUnixTimestamp(credit, "expiresAt")));
            }

            credits = parsed;
        }

        return new CodexResetCreditInventory(availableCount, credits);
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

            planType = CodexPlanTypes.Normalize(planElement.GetString());
        }

        return new CodexRateLimitBucket(
            planType,
            ParseOptionalWindow(element, "primary"),
            ParseOptionalWindow(element, "secondary"))
        {
            LimitId = ParseOptionalLimitId(element, "limitId"),
            LimitName = ParseOptionalLabel(element, "limitName"),
        };
    }

    private static string? ParseOptionalLimitId(JsonElement element, string propertyName)
    {
        string? value = ParseOptionalLabel(element, propertyName);
        if (value is not null && !IsSafeLimitId(value))
        {
            throw ContractFailure();
        }

        return value;
    }

    private static string? ParseOptionalLabel(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw ContractFailure();
        }

        string? label = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(label)
            || label.Length > 64
            || label.Any(char.IsControl))
        {
            throw ContractFailure();
        }

        return label;
    }

    private static string ParseRequiredToken(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw ContractFailure();
        }

        string? token = value.GetString();
        if (string.IsNullOrWhiteSpace(token)
            || token.Length > 64
            || !token.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw ContractFailure();
        }

        return token;
    }

    private static DateTimeOffset? ParseOptionalUnixTimestamp(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!value.TryGetInt64(out long seconds))
        {
            throw ContractFailure();
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw ContractFailure();
        }
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
