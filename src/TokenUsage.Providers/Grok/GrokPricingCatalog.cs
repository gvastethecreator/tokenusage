using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Grok;

/// <summary>
/// Local Grok and Composer cost estimates using official xAI API rates, plus
/// Cursor-published rates for Composer and named Fast variants.
/// </summary>
public static class GrokPricingCatalog
{
    public const string Version = "xai-api-2026-08-12";
    private const decimal TokensPerMillion = 1_000_000m;
    private const long LongContextThreshold = 200_000;

    private static readonly Dictionary<string, Rates> RatesByModel =
        new(StringComparer.Ordinal)
        {
            ["composer-2"] = new("composer-2", 0.5m, 0.2m, 2.5m, false),
            ["composer-2-fast"] = new("composer-2-fast", 1.5m, 0.35m, 7.5m, false),
            ["composer-2.5"] = new("composer-2.5", 0.5m, 0.2m, 2.5m, false),
            ["composer-2.5-fast"] = new("composer-2.5-fast", 3m, 0.5m, 15m, false),
            ["grok-build"] = new("grok-build", 1m, 0.2m, 2m, true),
            ["grok-composer-2"] = new("composer-2", 0.5m, 0.2m, 2.5m, false),
            ["grok-composer-2-fast"] = new("composer-2-fast", 1.5m, 0.35m, 7.5m, false),
            ["grok-composer-2.5"] = new("composer-2.5", 0.5m, 0.2m, 2.5m, false),
            ["grok-composer-2.5-fast"] = new("composer-2.5-fast", 3m, 0.5m, 15m, false),
            ["grok-4.1-fast"] = new("xai/grok-4-1-fast", 0.2m, 0.05m, 0.5m, false),
            ["grok-4.3"] = new("grok-4.3", 1.25m, 0.2m, 2.5m, true),
            ["grok-4.5"] = new("grok-4.5", 2m, 0.3m, 6m, true),
            ["grok-4.5-fast"] = new("grok-4.5-fast", 4m, 1m, 12m, false),
            ["grok-4.6"] = new("grok-4.6", 2m, 0.5m, 6m, true),
            ["grok-4.6-fast"] = new("grok-4.6-fast", 4m, 1m, 12m, false),
            ["grok-4.20-0309-reasoning"] = new("grok-4.3", 1.25m, 0.2m, 2.5m, true),
            ["grok-4.20-0309-non-reasoning"] = new("grok-4.3", 1.25m, 0.2m, 2.5m, true),
            ["grok-4.20-multi-agent-0309"] = new("grok-4.3", 1.25m, 0.2m, 2.5m, true),
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

        decimal multiplier = 1m;
        if (rates.HasLongContext
            && checked(tokens.Input + tokens.CacheRead + tokens.CacheWrite)
                >= LongContextThreshold)
        {
            multiplier = 2m;
        }

        decimal amount =
            (((tokens.Input + tokens.CacheWrite) * rates.Input * multiplier)
             + (tokens.CacheRead * rates.CacheRead * multiplier)
             + ((tokens.Output + tokens.Reasoning) * rates.Output * multiplier))
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

        if (normalized.StartsWith("grok-4.6-", StringComparison.Ordinal))
        {
            return normalized.Contains("-fast", StringComparison.Ordinal)
                ? "grok-4.6-fast"
                : "grok-4.6";
        }

        if (normalized.StartsWith("grok-4.3-", StringComparison.Ordinal))
        {
            return "grok-4.3";
        }

        return normalized;
    }

    private sealed record Rates(
        string PriceMatch,
        decimal Input,
        decimal CacheRead,
        decimal Output,
        bool HasLongContext);
}
