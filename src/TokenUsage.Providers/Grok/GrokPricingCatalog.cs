using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Grok;

/// <summary>
/// Local Grok CLI cost estimates using the same model aliases and rates as the
/// pinned OpenUsage reference used by this project.
/// </summary>
public static class GrokPricingCatalog
{
    public const string Version = "openusage-9d2bf09f-2026-07-14";
    private const decimal TokensPerMillion = 1_000_000m;

    private static readonly Dictionary<string, Rates> RatesByModel =
        new(StringComparer.Ordinal)
        {
            ["grok-build"] = new("grok-build", 1m, 0.2m, 2m),
            ["grok-composer-2"] = new("composer-2", 0.5m, 0.2m, 2.5m),
            ["grok-composer-2-fast"] = new("composer-2-fast", 1.5m, 0.35m, 7.5m),
            ["grok-composer-2.5"] = new("composer-2.5", 0.5m, 0.2m, 2.5m),
            ["grok-composer-2.5-fast"] = new("composer-2.5-fast", 3m, 0.5m, 15m),
            ["grok-4.1-fast"] = new("xai/grok-4-1-fast", 0.2m, 0.05m, 0.5m),
            ["grok-4.5"] = new("grok-4.5", 2m, 0.5m, 6m),
            ["grok-4.5-fast"] = new("grok-4.5-fast", 4m, 1m, 18m),
        };

    public static CostObservation Resolve(string model, TokenBreakdown tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        string normalized = Normalize(model);
        if (!RatesByModel.TryGetValue(normalized, out Rates? rates))
        {
            return CostObservation.Unavailable();
        }

        decimal amount =
            (((tokens.Input + tokens.CacheWrite) * rates.Input)
             + (tokens.CacheRead * rates.CacheRead)
             + ((tokens.Output + tokens.Reasoning) * rates.Output))
            / TokensPerMillion;
        return CostObservation.CatalogEstimated(
            decimal.Round(amount, 6, MidpointRounding.AwayFromZero),
            Version,
            rates.PriceMatch);
    }

    private static string Normalize(string model)
    {
        string normalized = model.Trim().ToLowerInvariant();
        if (normalized.StartsWith("cursor-", StringComparison.Ordinal))
        {
            normalized = normalized["cursor-".Length..];
        }

        if (normalized.StartsWith("grok-build-", StringComparison.Ordinal))
        {
            return "grok-build";
        }

        if (normalized.StartsWith("grok-4.5-", StringComparison.Ordinal))
        {
            return normalized.Contains("-fast", StringComparison.Ordinal)
                ? "grok-4.5-fast"
                : "grok-4.5";
        }

        return normalized;
    }

    private sealed record Rates(
        string PriceMatch,
        decimal Input,
        decimal CacheRead,
        decimal Output);
}
