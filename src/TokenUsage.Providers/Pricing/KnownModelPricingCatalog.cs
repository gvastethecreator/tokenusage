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
        };

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

    private static string Canonicalize(string model)
    {
        string normalized = model.Trim().ToLowerInvariant().Replace(' ', '-');
        if (normalized.StartsWith("cursor-", StringComparison.Ordinal))
        {
            normalized = normalized["cursor-".Length..];
        }

        int separator = normalized.LastIndexOf('/');
        if (separator >= 0 && separator + 1 < normalized.Length)
        {
            normalized = normalized[(separator + 1)..];
        }

        return OfficialAliases.TryGetValue(normalized, out string? canonical)
            ? canonical
            : normalized;
    }
}
