using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Claude;

public static class ClaudePricingCatalog
{
    public const string Version = "anthropic-api-2026-07-22";
    public const string SonnetFiveStandardVersion = "anthropic-api-2026-09-01";
    private const decimal TokensPerMillion = 1_000_000m;

    private static readonly Dictionary<string, Rates> RatesByModel =
        new Dictionary<string, Rates>(StringComparer.Ordinal)
        {
            ["claude-sonnet-5"] = new(2m, 10m, 2.5m, 4m, 0.2m),
            ["claude-sonnet-4-6"] = new(3m, 15m, 3.75m, 6m, 0.3m),
            ["claude-sonnet-4-5"] = new(3m, 15m, 3.75m, 6m, 0.3m),
            ["claude-sonnet-4-5-20250929"] = new(3m, 15m, 3.75m, 6m, 0.3m),
            ["claude-haiku-4-5"] = new(1m, 5m, 1.25m, 2m, 0.1m),
            ["claude-haiku-4-5-20251001"] = new(1m, 5m, 1.25m, 2m, 0.1m),
            ["claude-opus-4-6"] = new(5m, 25m, 6.25m, 10m, 0.5m),
            ["claude-opus-4-5"] = new(5m, 25m, 6.25m, 10m, 0.5m),
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

        if (reportedCostUsd is decimal reported)
        {
            return CostObservation.ProviderReported(RoundUsd(reported));
        }

        if (isFast || !RatesByModel.TryGetValue(model, out Rates? rates))
        {
            return CostObservation.Unavailable();
        }

        string catalogVersion = Version;
        if (string.Equals(model, "claude-sonnet-5", StringComparison.Ordinal)
            && occurredAtUtc >= new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero))
        {
            rates = new Rates(3m, 15m, 3.75m, 6m, 0.3m);
            catalogVersion = SonnetFiveStandardVersion;
        }

        decimal amount =
            ((tokens.Input * rates.Input)
             + (tokens.Output * rates.Output)
             + (cacheWrite5Minutes * rates.CacheWrite5Minutes)
             + (cacheWrite1Hour * rates.CacheWrite1Hour)
             + (tokens.CacheRead * rates.CacheRead))
            / TokensPerMillion;
        return CostObservation.CatalogEstimated(RoundUsd(amount), catalogVersion, model);
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
