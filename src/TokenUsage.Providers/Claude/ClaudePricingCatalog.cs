using TokenUsage.Core.Usage;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Claude;

public static class ClaudePricingCatalog
{
    public const string Version = "anthropic-api-2026-09-03";
    private const decimal TokensPerMillion = 1_000_000m;

    private static readonly Dictionary<string, DatedRates> ListRatesFromUtc =
        new(StringComparer.Ordinal)
        {
            ["claude-sonnet-5"] = new(
                new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                new(3m, 15m, 3.75m, 6m, 0.3m)),
        };

    private static readonly Dictionary<string, Rates> RatesByModel =
        new Dictionary<string, Rates>(StringComparer.Ordinal)
        {
            ["claude-fable-5"] = new(10m, 50m, 12.5m, 20m, 1m),
            ["claude-fable-5-1"] = new(10m, 50m, 12.5m, 20m, 0.25m),
            ["claude-haiku-4-5"] = new(1m, 5m, 1.25m, 2m, 0.1m),
            ["claude-haiku-4-5-20251001"] = new(1m, 5m, 1.25m, 2m, 0.1m),
            ["claude-mythos-5"] = new(10m, 50m, 12.5m, 20m, 1m),
            ["claude-mythos-5-1"] = new(10m, 50m, 12.5m, 20m, 0.25m),
            ["claude-opus-4-5"] = new(5m, 25m, 6.25m, 10m, 0.5m),
            ["claude-opus-4-6"] = new(5m, 25m, 6.25m, 10m, 0.5m),
            ["claude-opus-4-7"] = new(5m, 25m, 6.25m, 10m, 0.5m),
            ["claude-opus-4-8"] = new(5m, 25m, 6.25m, 10m, 0.5m),
            ["claude-opus-4-8-fast"] = new(10m, 50m, 12.5m, 20m, 1m),
            ["claude-opus-5"] = new(5m, 25m, 6.25m, 10m, 0.5m),
            ["claude-opus-5-fast"] = new(10m, 50m, 12.5m, 20m, 1m),
            ["claude-sonnet-4"] = new(3m, 15m, 3.75m, 6m, 0.3m),
            ["claude-sonnet-4-5"] = new(3m, 15m, 3.75m, 6m, 0.3m),
            ["claude-sonnet-4-5-20250929"] = new(3m, 15m, 3.75m, 6m, 0.3m),
            ["claude-sonnet-4-6"] = new(3m, 15m, 3.75m, 6m, 0.3m),
            ["claude-sonnet-5"] = new(2m, 10m, 2.5m, 4m, 0.2m),
        };

    public static IReadOnlyList<PricingRateEvidence> EvidenceEntries { get; } =
        BuildEvidence();

    public static CostObservation Resolve(
        string model,
        DateTimeOffset occurredAtUtc,
        TokenBreakdown tokens,
        long cacheWrite5Minutes,
        long cacheWrite1Hour,
        decimal? reportedCostUsd,
        bool isFast)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must use the UTC offset.", nameof(occurredAtUtc));
        }

        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheWrite5Minutes);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheWrite1Hour);

        if (reportedCostUsd is decimal reported && reported > 0m)
        {
            return CostObservation.ProviderReported(RoundUsd(reported));
        }

        if (!TryResolveRates(model, isFast, out Rates rates, out string priceMatch))
        {
            return CostObservation.Unavailable();
        }

        if (ListRatesFromUtc.TryGetValue(priceMatch, out DatedRates? dated)
            && occurredAtUtc >= dated.ListEffectiveFromUtc)
        {
            rates = dated.ListRates;
        }

        decimal amount =
            ((tokens.Input * rates.Input)
             + (tokens.Output * rates.Output)
             + (cacheWrite5Minutes * rates.CacheWrite5Minutes)
             + (cacheWrite1Hour * rates.CacheWrite1Hour)
             + (tokens.CacheRead * rates.CacheRead))
            / TokensPerMillion;
        return CostObservation.CatalogEstimated(RoundUsd(amount), Version, priceMatch);
    }

    private static bool TryResolveRates(
        string model,
        bool isFast,
        out Rates rates,
        out string priceMatch)
    {
        string matchedKey = model;
        if (!RatesByModel.TryGetValue(model, out Rates? matched) || matched is null)
        {
            // Anthropic ships dated snapshot ids beyond the hardcoded pair; a
            // snapshot prices like its base model.
            if (IsDatedSnapshot(model)
                && RatesByModel.TryGetValue(model[..^9], out Rates? baseModel)
                && baseModel is not null)
            {
                matched = baseModel;
                matchedKey = model[..^9];
            }
            else
            {
                rates = null!;
                priceMatch = string.Empty;
                return false;
            }
        }

        if (!isFast)
        {
            rates = matched;
            priceMatch = matchedKey;
            return true;
        }

        if (RatesByModel.TryGetValue(matchedKey + "-fast", out Rates? fast) && fast is not null)
        {
            rates = fast;
            priceMatch = matchedKey + "-fast";
            return true;
        }

        // Fast mode is only published for Opus 5 and Opus 4.8; a fast flag on
        // any other model keeps the base rates instead of failing.
        rates = matched;
        priceMatch = matchedKey;
        return true;
    }

    private static bool IsDatedSnapshot(string model) =>
        model.Length > 9
        && model[^9] == '-'
        && IsEightAsciiDigits(model[^8..]);

    private static bool IsEightAsciiDigits(string value)
    {
        foreach (char character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static decimal RoundUsd(decimal amount) =>
        decimal.Round(amount, 6, MidpointRounding.AwayFromZero);

    private sealed record Rates(
        decimal Input,
        decimal Output,
        decimal CacheWrite5Minutes,
        decimal CacheWrite1Hour,
        decimal CacheRead);

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
                    PricingOfficialSources.Anthropic,
                    dated.ListEffectiveFromUtc));
                evidence.Add(PricingEvidence.FollowOn(
                    Version,
                    priceMatch,
                    PricingOfficialSources.Anthropic,
                    dated.ListEffectiveFromUtc));
            }
            else
            {
                evidence.Add(PricingEvidence.Ongoing(
                    Version,
                    priceMatch,
                    PricingOfficialSources.Anthropic));
            }
        }

        PricingCatalogAudit.ValidateCoverage(Version, priceMatches, evidence);
        return evidence;
    }
}
