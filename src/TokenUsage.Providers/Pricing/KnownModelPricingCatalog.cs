using TokenUsage.Core.Usage;
using TokenUsage.Providers.Antigravity;
using TokenUsage.Providers.Claude;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.Grok;

namespace TokenUsage.Providers.Pricing;

/// <summary>
/// Resolves raw API value for local tools that persist a concrete upstream
/// model and real token counters. This is not a subscription-charge estimate.
/// </summary>
public static class KnownModelPricingCatalog
{
    public static CostObservation Resolve(
        string model,
        DateTimeOffset occurredAtUtc,
        TokenBreakdown tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        string normalized = NormalizeModel(model);
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
            return AntigravityPricingCatalog.Resolve(normalized, tokens);
        }

        if (normalized.Contains("grok", StringComparison.Ordinal))
        {
            return GrokPricingCatalog.Resolve(normalized, tokens);
        }

        return CostObservation.Unavailable();
    }

    private static string NormalizeModel(string model)
    {
        string normalized = model.Trim().ToLowerInvariant();
        if (normalized.StartsWith("cursor-", StringComparison.Ordinal))
        {
            normalized = normalized["cursor-".Length..];
        }

        int separator = normalized.LastIndexOf('/');
        return separator >= 0 && separator + 1 < normalized.Length
            ? normalized[(separator + 1)..]
            : normalized;
    }
}
