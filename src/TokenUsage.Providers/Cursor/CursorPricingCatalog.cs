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
    public const string Version = "cursor-models-2026-09-22";
    private const decimal TokensPerMillion = 1_000_000m;
    private const long GrokFourSevenFastLongContextThreshold = 200_000;

    private static readonly Dictionary<string, Rates> FirstPartyRatesByModel =
        new(StringComparer.Ordinal)
        {
            ["composer-2.5"] = new(0.5m, 0.2m, 2.5m),
            ["composer-2.5-fast"] = new(3m, 0.5m, 15m),
            ["gemini-3.8-flash"] = new(0.75m, 0.075m, 3.5m),
            ["grok-4.5"] = new(2m, 0.5m, 6m),
            ["grok-4.5-fast"] = new(4m, 1m, 18m),
            ["grok-4.6"] = new(2m, 0.5m, 6m),
            ["grok-4.6-fast"] = new(4m, 1m, 12m),
            ["grok-4.7"] = new(2m, 0.5m, 6m),
            ["grok-4.7-fast"] = new(4m, 1m, 12m),
        };

    public static IReadOnlyList<PricingRateEvidence> EvidenceEntries { get; } =
        BuildEvidence();

    public static UsageLinearTariffResolution ResolveFirstPartyLinearTariff(string model, DateTimeOffset atUtc)
    {
        string normalized = KnownModelPricingCatalog.Canonicalize(model);
        if (!FirstPartyRatesByModel.TryGetValue(normalized, out Rates? rates)
            || !EvidenceEntries.Any(item => item.ExactPriceMatch == normalized && item.IsEffectiveAt(atUtc)))
            return new(null, UsagePriceExclusion.MissingTariff);
        if (normalized == "grok-4.7-fast")
            return new(null, UsagePriceExclusion.NonLinearRegime);
        return new(new(Version, normalized, "cursor-published-linear/v1", rates.Input,
            rates.Output, rates.Output, rates.CacheRead, rates.Input), null);
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

        decimal multiplier = normalized == "grok-4.7-fast"
            && checked(tokens.Input + tokens.CacheRead + tokens.CacheWrite) > GrokFourSevenFastLongContextThreshold
                ? 1.5m : 1m;
        decimal amount =
            (((tokens.Input + tokens.CacheWrite) * rates.Input)
             + (tokens.CacheRead * rates.CacheRead)
             + ((tokens.Output + tokens.Reasoning) * rates.Output)) * multiplier
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
        string[] priceMatches = FirstPartyRatesByModel.Keys.ToArray();
        PricingRateEvidence[] evidence = priceMatches
            .Select(priceMatch => PricingEvidence.Ongoing(
                Version,
                priceMatch,
                priceMatch == "gemini-3.8-flash"
                    ? PricingOfficialSources.CursorGemini
                    : PricingOfficialSources.Cursor))
            .ToArray();
        PricingCatalogAudit.ValidateCoverage(Version, priceMatches, evidence);
        return evidence;
    }
}
