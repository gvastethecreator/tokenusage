using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.Grok;

namespace TokenUsage.Providers.Pricing;

/// <summary>
/// Resolves raw API value for local tools that persist a concrete upstream
/// model and real token counters. Rates come from the official Anthropic,
/// OpenAI, Google, and xAI catalogs. This is not a subscription-charge
/// estimate.
/// </summary>
public static class KnownModelPricingCatalog
{
    private static readonly Dictionary<string, string> OfficialAliases =
        new(StringComparer.Ordinal)
        {
            ["claude-4-sonnet"] = "claude-sonnet-4",
            ["claude-4-sonnet-thinking"] = "claude-sonnet-4",
            ["claude-4.5-haiku"] = "claude-haiku-4-5",
            ["claude-4.5-opus"] = "claude-opus-4-5",
            ["claude-4.5-opus-thinking"] = "claude-opus-4-5",
            ["claude-4.5-sonnet"] = "claude-sonnet-4-5",
            ["claude-4.5-sonnet-thinking"] = "claude-sonnet-4-5",
            ["claude-4.6-opus"] = "claude-opus-4-6",
            ["claude-4.6-opus-thinking"] = "claude-opus-4-6",
            ["claude-4.6-sonnet"] = "claude-sonnet-4-6",
            ["claude-4.6-sonnet-thinking"] = "claude-sonnet-4-6",
            ["claude-4.7-opus"] = "claude-opus-4-7",
            ["claude-4.7-opus-thinking"] = "claude-opus-4-7",
            ["claude-4.8-opus"] = "claude-opus-4-8",
            ["claude-sonnet-4-5-thinking"] = "claude-sonnet-4-5",
            ["claude-sonnet-4-6-thinking"] = "claude-sonnet-4-6",
            ["claude-opus-4-5-thinking"] = "claude-opus-4-5",
            ["claude-opus-4-6-thinking"] = "claude-opus-4-6",
            ["claude-opus-4-7-thinking"] = "claude-opus-4-7",
            ["gemini-3.7-flash-control"] = "gemini-3.7-flash",
        };

    /// <summary>
    /// A reported <c>0</c> usually means "no rate", not a free turn. Prefer the
    /// catalog when the source wrote zero or omitted cost.
    /// </summary>
    public static CostObservation ResolveReportedOrCatalog(
        decimal? reportedUsd,
        string model,
        DateTimeOffset occurredAtUtc,
        TokenBreakdown tokens)
    {
        if (reportedUsd is decimal reported && reported > 0m)
        {
            return CostObservation.ProviderReported(
                decimal.Round(reported, 6, MidpointRounding.AwayFromZero));
        }

        return Resolve(model, occurredAtUtc, tokens);
    }

    public static CostObservation Resolve(
        string model,
        DateTimeOffset occurredAtUtc,
        TokenBreakdown tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        string normalized = Canonicalize(model);
        if (normalized.Contains("claude", StringComparison.Ordinal))
        {
            return ClaudePricingCatalog.Resolve(
                normalized,
                occurredAtUtc,
                tokens,
                tokens.CacheWrite,
                cacheWrite1Hour: 0,
                reportedCostUsd: null,
                isFast: false);
        }

        if (normalized.StartsWith("gpt-", StringComparison.Ordinal)
            || normalized.StartsWith('o'))
        {
            return CodexPricingCatalog.Resolve(normalized, tokens);
        }

        if (normalized.Contains("gemini", StringComparison.Ordinal))
        {
            return GooglePricingCatalog.Resolve(normalized, tokens);
        }

        if (normalized.Contains("grok", StringComparison.Ordinal)
            || normalized.StartsWith("composer-", StringComparison.Ordinal))
        {
            return GrokPricingCatalog.Resolve(normalized, tokens);
        }

        return CostObservation.Unavailable();
    }

    public static string Canonicalize(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        string normalized = model.Trim().ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');
        if (normalized.StartsWith("cursor-", StringComparison.Ordinal))
        {
            normalized = normalized["cursor-".Length..];
        }

        if (normalized.StartsWith("antigravity-", StringComparison.Ordinal))
        {
            normalized = normalized["antigravity-".Length..];
        }

        int separator = normalized.LastIndexOf('/');
        if (separator >= 0 && separator + 1 < normalized.Length)
        {
            normalized = normalized[(separator + 1)..];
        }

        foreach (string suffix in (string[])["-xhigh", "-thinking", "-medium", "-high", "-low"])
        {
            if (normalized.EndsWith(suffix, StringComparison.Ordinal)
                && normalized.Length > suffix.Length)
            {
                normalized = normalized[..^suffix.Length];
                break;
            }
        }

        if (OfficialAliases.TryGetValue(normalized, out string? aliased))
        {
            return aliased;
        }

        string transformed = normalized.StartsWith("claude", StringComparison.Ordinal)
            ? ReplaceDigitSeparator(normalized, from: '.', to: '-')
            : ReplaceDigitSeparator(normalized, from: '-', to: '.');
        return OfficialAliases.TryGetValue(transformed, out string? canonical)
            ? canonical
            : transformed;
    }

    private static string ReplaceDigitSeparator(string value, char from, char to)
    {
        if (value.Length < 3)
        {
            return value;
        }

        char[] characters = value.ToCharArray();
        for (int index = 1; index < characters.Length - 1; index++)
        {
            if (characters[index] == from
                && char.IsAsciiDigit(characters[index - 1])
                && char.IsAsciiDigit(characters[index + 1])
                && (index < 2 || !char.IsAsciiDigit(characters[index - 2]))
                && (index + 2 >= characters.Length || !char.IsAsciiDigit(characters[index + 2])))
            {
                characters[index] = to;
            }
        }

        return new string(characters);
    }
}
