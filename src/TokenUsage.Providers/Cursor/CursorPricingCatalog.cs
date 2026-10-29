using TokenUsage.Core.Usage;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Cursor;

/// <summary>
/// Estimates raw API value from official Anthropic, OpenAI, Google, and xAI
/// rates when Cursor stores a concrete model and real per-turn token counters.
/// It does not estimate a Cursor subscription or credit-pool charge.
/// </summary>
public static class CursorPricingCatalog
{
    public const string Version = "cursor-models-2026-10-19";
    private const long GrokFourSevenLongContextThreshold = 256_000;

    private static readonly Dictionary<string, Rates> FirstPartyRatesByModel =
        new(StringComparer.Ordinal)
        {
            ["composer-2.5"] = new(0.5m, 0.2m, 2.5m),
            ["composer-2.5-fast"] = new(3m, 0.5m, 15m),
            ["gemini-3.8-flash"] = new(0.75m, 0.075m, 3.75m),
            ["gemini-4-argon"] = new(2m, 0.1m, 10m, CacheWrite: 0m),
            ["grok-4.5"] = new(2m, 0.5m, 6m),
            ["grok-4.5-fast"] = new(4m, 1m, 18m),
            ["grok-4.6"] = new(2m, 0.5m, 6m),
            ["grok-4.6-fast"] = new(4m, 1m, 12m),
            ["grok-4.7"] = new(2m, 0.5m, 6m),
            ["grok-4.7-fast"] = new(4m, 1m, 12m),
            ["muse-spark-1.3"] = new(1.25m, 0.15m, 4.25m, CacheWrite: 0m),
            ["muse-wonder-1.0"] = new(4m, 0.2m, 20m, CacheWrite: 0m),
        };

    public static IReadOnlyList<PricingRateEvidence> EvidenceEntries { get; } =
        BuildEvidence();

    public static UsageLinearTariffResolution ResolveFirstPartyLinearTariff(string model, DateTimeOffset atUtc)
    {
        string normalized = KnownModelPricingCatalog.Canonicalize(model);
        if (!FirstPartyRatesByModel.TryGetValue(normalized, out Rates? rates)
            || !EvidenceEntries.Any(item => item.ExactPriceMatch == normalized && item.IsEffectiveAt(atUtc)))
            return new(null, UsagePriceExclusion.MissingTariff);
        if (normalized is "grok-4.7" or "grok-4.7-fast")
            return new(null, UsagePriceExclusion.NonLinearRegime);
        return new(new(Version, normalized, "cursor-published-linear/v1", rates.Input,
            rates.Output, rates.Output, rates.CacheRead, rates.CacheWrite ?? rates.Input), null);
    }

    public static CostObservation Resolve(
        string model,
        DateTimeOffset occurredAtUtc,
        TokenBreakdown tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        string normalized = KnownModelPricingCatalog.Canonicalize(model);
        if (!FirstPartyRatesByModel.TryGetValue(normalized, out Rates? rates))
        {
            return KnownModelPricingCatalog.Resolve(model, occurredAtUtc, tokens);
        }

        // Cursor publishes a 256k input threshold, distinct from the xAI API's 200k tier.
        bool longContext = checked(tokens.Input + tokens.CacheRead + tokens.CacheWrite)
            > GrokFourSevenLongContextThreshold;
        decimal multiplier = longContext ? normalized switch
        {
            "grok-4.7" => 2m,
            "grok-4.7-fast" => 1.5m,
            _ => 1m,
        } : 1m;
        return CostObservation.CatalogEstimated(
            LinearTokenPrice.Estimate(tokens, rates.Input, rates.CacheRead, rates.Output,
                cacheWrite: rates.CacheWrite, inputMultiplier: multiplier, outputMultiplier: multiplier),
            Version,
            normalized);
    }

    private sealed record Rates(
        decimal Input,
        decimal CacheRead,
        decimal Output,
        decimal? CacheWrite = null);

    private static PricingRateEvidence[] BuildEvidence()
    {
        string[] priceMatches = FirstPartyRatesByModel.Keys.ToArray();
        PricingRateEvidence[] evidence = priceMatches
            .Select(priceMatch => PricingEvidence.Ongoing(
                Version,
                priceMatch,
                priceMatch switch
                {
                    "gemini-3.8-flash" => PricingOfficialSources.CursorGemini,
                    "gemini-4-argon" => PricingOfficialSources.CursorArgon,
                    "grok-4.7" or "grok-4.7-fast" => PricingOfficialSources.CursorGrok,
                    "muse-spark-1.3" => PricingOfficialSources.CursorMuse,
                    "muse-wonder-1.0" => PricingOfficialSources.CursorWonder,
                    _ => PricingOfficialSources.Cursor,
                }))
            .ToArray();
        PricingCatalogAudit.ValidateCoverage(Version, priceMatches, evidence);
        return evidence;
    }
}
