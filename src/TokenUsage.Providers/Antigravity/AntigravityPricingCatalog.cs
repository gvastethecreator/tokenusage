using System;
using System.Collections.Generic;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Antigravity;

public static class AntigravityPricingCatalog
{
    public const string Version = "google-anthropic-2026-08-04";
    private const decimal TokensPerMillion = 1_000_000m;

    private static readonly Dictionary<string, Rates> RatesByModel =
        new Dictionary<string, Rates>(StringComparer.Ordinal)
        {
            ["gemini-3.6-flash"] = new("gemini-3.6-flash", 1.5m, 0.15m, 7.5m),
            ["claude-sonnet-4-6"] = new("claude-sonnet-4-6", 3m, 0.3m, 15m),
        };

    public static CostObservation Resolve(string model, TokenBreakdown tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        if (!RatesByModel.TryGetValue(model, out Rates? rates))
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

    private sealed record Rates(
        string PriceMatch,
        decimal Input,
        decimal CacheRead,
        decimal Output);
}
