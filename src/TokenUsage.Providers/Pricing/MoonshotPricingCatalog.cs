using TokenUsage.Core.Usage;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Pricing;

/// <summary>
/// Official Moonshot API rates for Kimi models that agent tools can select.
/// kimi-k3-max has no published rate (not on Cursor's model page nor on
/// Moonshot's pricing page), so it stays unavailable instead of guessed.
/// </summary>
public static class MoonshotPricingCatalog
{
    public const string Version = "moonshot-api-2026-09-02";
    private const decimal TokensPerMillion = 1_000_000m;

    private static readonly Dictionary<string, Rates> RatesByModel =
        new(StringComparer.Ordinal)
        {
            ["kimi-k3"] = new(3m, 0.3m, 15m),
            ["kimi-k2.7-code"] = new(0.95m, 0.19m, 4m),
            ["kimi-k2.7-code-highspeed"] = new(1.9m, 0.38m, 8m),
            ["kimi-k2.6"] = new(0.95m, 0.16m, 4m),
        };

    public static IReadOnlyList<PricingRateEvidence> EvidenceEntries { get; } =
        BuildEvidence();

    public static CostObservation Resolve(string model, TokenBreakdown tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        string normalized = KnownModelPricingCatalog.Canonicalize(model);
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
            normalized);
    }

    private sealed record Rates(
        decimal Input,
        decimal CacheRead,
        decimal Output);

    private static PricingRateEvidence[] BuildEvidence()
    {
        string[] priceMatches = RatesByModel.Keys.ToArray();
        PricingRateEvidence[] evidence = priceMatches
            .Select(priceMatch => PricingEvidence.Ongoing(
                Version,
                priceMatch,
                PricingOfficialSources.Moonshot))
            .ToArray();
        PricingCatalogAudit.ValidateCoverage(Version, priceMatches, evidence);
        return evidence;
    }
}
