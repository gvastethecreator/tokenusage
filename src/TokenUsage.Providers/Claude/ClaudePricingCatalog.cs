using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Claude;

public static class ClaudePricingCatalog
{
    public const string Version = "anthropic-api-2026-08-28";
    private const decimal TokensPerMillion = 1_000_000m;

    private static readonly Dictionary<string, Rates> RatesByModel =
        new Dictionary<string, Rates>(StringComparer.Ordinal)
        {
            ["claude-fable-5"] = new(10m, 50m, 12.5m, 20m, 1m),
            ["claude-haiku-4-5"] = new(1m, 5m, 1.25m, 2m, 0.1m),
            ["claude-haiku-4-5-20251001"] = new(1m, 5m, 1.25m, 2m, 0.1m),
            // The platform pricing docs list Mythos 5 at 5/25; the marketing page
            // says "starts at $10/$50". The platform docs win for API value.
            ["claude-mythos-5"] = new(5m, 25m, 6.25m, 10m, 0.5m),
            ["claude-opus-4-5"] = new(5m, 25m, 6.25m, 10m, 0.5m),
            ["claude-opus-4-6"] = new(5m, 25m, 6.25m, 10m, 0.5m),
            ["claude-opus-4-7"] = new(5m, 25m, 6.25m, 10m, 0.5m),
            // Fast mode for Opus 4.7 was published at 30 in / 150 out; the
            // remaining legs follow the documented 6x uniform scaling.
            ["claude-opus-4-7-fast"] = new(30m, 150m, 37.5m, 60m, 3m),
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

        if (!TryResolveRates(model, isFast, out Rates rates))
        {
            return CostObservation.Unavailable();
        }

        decimal amount =
            ((tokens.Input * rates.Input)
             + (tokens.Output * rates.Output)
             + (cacheWrite5Minutes * rates.CacheWrite5Minutes)
             + (cacheWrite1Hour * rates.CacheWrite1Hour)
             + (tokens.CacheRead * rates.CacheRead))
            / TokensPerMillion;
        return CostObservation.CatalogEstimated(RoundUsd(amount), Version, model);
    }

    private static bool TryResolveRates(string model, bool isFast, out Rates rates)
    {
        if (!RatesByModel.TryGetValue(model, out Rates? matched) || matched is null)
        {
            // Anthropic ships dated snapshot ids beyond the hardcoded pair; a
            // snapshot prices like its base model.
            if (IsDatedSnapshot(model)
                && RatesByModel.TryGetValue(model[..^9], out Rates? baseModel)
                && baseModel is not null)
            {
                matched = baseModel;
            }
            else
            {
                rates = null!;
                return false;
            }
        }

        if (!isFast)
        {
            rates = matched;
            return true;
        }

        if (RatesByModel.TryGetValue(model + "-fast", out Rates? fast) && fast is not null)
        {
            rates = fast;
            return true;
        }

        // Fast mode is only published for Opus 5 and Opus 4.8; a fast flag on
        // any other model keeps the base rates instead of failing.
        rates = matched;
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
}
