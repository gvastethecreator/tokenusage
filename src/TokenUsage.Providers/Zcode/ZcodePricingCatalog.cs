using TokenUsage.Core.Usage;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Zcode;

/// <summary>
/// Local ZCode cost estimates using the official Z.ai API rates for the GLM
/// models the agent runs on. ZCode itself bills through plan credits, so
/// these estimates show pay-as-you-go value, not the plan charge.
/// </summary>
public static class ZcodePricingCatalog
{
    public const string Version = "zai-api-2026-09-02";
    private const decimal TokensPerMillion = 1_000_000m;

    // The Z.ai promo ends at 24:00 on 2026-09-09 in Singapore (UTC+8).
    // A resolve without a timestamp prices at the rate valid now.
    private static readonly Dictionary<string, DatedRates> ListRatesFromUtc =
        new(StringComparer.Ordinal)
        {
            ["glm-5.3-flash"] = new(
                new DateTimeOffset(2026, 9, 9, 16, 0, 0, TimeSpan.Zero),
                new(0.15m, 0.03m, 0.5m)),
        };

    private static readonly Dictionary<string, Rates> RatesByModel =
        new(StringComparer.Ordinal)
        {
            // Z.ai promo rate through 2026-09-09; the list rate is 0.15/0.03/0.50.
            ["glm-5.3-flash"] = new(0.075m, 0.015m, 0.25m),
            ["glm-5.3"] = new(1.4m, 0.26m, 4.4m),
            ["glm-5.2"] = new(1.4m, 0.26m, 4.4m),
            ["glm-5.1"] = new(1.4m, 0.26m, 4.4m),
            ["glm-5"] = new(1m, 0.2m, 3.2m),
            ["glm-5-turbo"] = new(1.2m, 0.24m, 4m),
            ["glm-5v-turbo"] = new(1.2m, 0.24m, 4m),
            ["glm-4.7"] = new(0.6m, 0.11m, 2.2m),
            ["glm-4.7-flash"] = new(0m, 0m, 0m),
            ["glm-4.7-flashx"] = new(0.07m, 0.01m, 0.4m),
            ["glm-4.6"] = new(0.6m, 0.11m, 2.2m),
            ["glm-4.5"] = new(0.6m, 0.11m, 2.2m),
            ["glm-4.5-x"] = new(2.2m, 0.45m, 8.9m),
            ["glm-4.5-air"] = new(0.2m, 0.03m, 1.1m),
            ["glm-4.5-airx"] = new(1.1m, 0.22m, 4.5m),
            ["glm-4.5-flash"] = new(0m, 0m, 0m),
        };

    public static IReadOnlyList<PricingRateEvidence> EvidenceEntries { get; } =
        BuildEvidence();

    public static CostObservation Resolve(
        string model,
        TokenBreakdown tokens,
        DateTimeOffset? occurredAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        string normalized = KnownModelPricingCatalog.Canonicalize(model);
        if (!RatesByModel.TryGetValue(normalized, out Rates? rates))
        {
            return CostObservation.Unavailable();
        }

        if (ListRatesFromUtc.TryGetValue(normalized, out DatedRates? dated)
            && (occurredAtUtc ?? DateTimeOffset.UtcNow) >= dated.ListEffectiveFromUtc)
        {
            rates = dated.ListRates;
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

    private sealed record DatedRates(
        DateTimeOffset ListEffectiveFromUtc,
        Rates ListRates);

    private static List<PricingRateEvidence> BuildEvidence()
    {
        string[] priceMatches = RatesByModel.Keys.ToArray();
        var evidence = new List<PricingRateEvidence>();
        foreach (string priceMatch in priceMatches)
        {
            if (ListRatesFromUtc.TryGetValue(priceMatch, out DatedRates? dated))
            {
                evidence.Add(PricingEvidence.Promotion(
                    Version,
                    priceMatch,
                    PricingOfficialSources.Zai,
                    dated.ListEffectiveFromUtc));
                evidence.Add(PricingEvidence.FollowOn(
                    Version,
                    priceMatch,
                    PricingOfficialSources.Zai,
                    dated.ListEffectiveFromUtc));
            }
            else
            {
                evidence.Add(PricingEvidence.Ongoing(
                    Version,
                    priceMatch,
                    PricingOfficialSources.Zai));
            }
        }

        PricingCatalogAudit.ValidateCoverage(Version, priceMatches, evidence);
        return evidence;
    }
}
