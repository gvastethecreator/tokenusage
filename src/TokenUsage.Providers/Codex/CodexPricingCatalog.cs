using System.Globalization;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Codex;

public static class CodexPricingCatalog
{
    public const string Version = "openai-api-2026-09-02";
    private const decimal TokensPerMillion = 1_000_000m;
    private const long LongContextThreshold = 272_000;

    // Promotional rates switch to the list rate on the day after the published
    // cutoff. A resolve without a timestamp prices at the rate valid now.
    private static readonly Dictionary<string, DatedRates> ListRatesFromUtc =
        new(StringComparer.Ordinal)
        {
            ["gpt-5.6-sol"] = new(
                new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero),
                new("gpt-5.6-sol", 5m, 0.5m, 30m, 6.25m, true)),
        };

    private static readonly Dictionary<string, Rates> RatesByModel =
        new(StringComparer.Ordinal)
        {
            ["gpt-5"] = new("gpt-5", 1.25m, 0.125m, 10m, 1.25m, false),
            ["gpt-5-codex"] = new("gpt-5-codex", 1.25m, 0.125m, 10m, 1.25m, false),
            ["gpt-5-fast"] = new("gpt-5-fast", 2.5m, 0.25m, 20m, 2.5m, false),
            ["gpt-5-mini"] = new("gpt-5-mini", 0.25m, 0.025m, 2m, 0.25m, false),
            ["gpt-5-nano"] = new("gpt-5-nano", 0.05m, 0.005m, 0.4m, 0.05m, false),
            ["gpt-5.1"] = new("gpt-5.1", 1.25m, 0.125m, 10m, 1.25m, false),
            ["gpt-5.1-codex"] = new("gpt-5.1-codex", 1.25m, 0.125m, 10m, 1.25m, false),
            ["gpt-5.1-codex-max"] = new("gpt-5.1-codex-max", 1.25m, 0.125m, 10m, 1.25m, false),
            ["gpt-5.1-codex-mini"] = new("gpt-5.1-codex-mini", 0.25m, 0.025m, 2m, 0.25m, false),
            ["gpt-5.2"] = new("gpt-5.2", 1.75m, 0.175m, 14m, 1.75m, false),
            ["gpt-5.2-codex"] = new("gpt-5.2-codex", 1.75m, 0.175m, 14m, 1.75m, false),
            ["gpt-5.3-codex"] = new("gpt-5.3-codex", 1.75m, 0.175m, 14m, 1.75m, false),
            ["gpt-5.4"] = new("gpt-5.4", 2.5m, 0.25m, 15m, 2.5m, true),
            ["gpt-5.4-mini"] = new("gpt-5.4-mini", 0.75m, 0.075m, 4.5m, 0.75m, false),
            ["gpt-5.4-nano"] = new("gpt-5.4-nano", 0.2m, 0.02m, 1.25m, 0.2m, false),
            ["gpt-5.5"] = new("gpt-5.5", 5m, 0.5m, 30m, 5m, true),
            ["gpt-5.6"] = new("gpt-5.6-sol", 4m, 0.4m, 20m, 5m, true),
            ["gpt-5.6-luna"] = new("gpt-5.6-luna", 0.2m, 0.02m, 1.2m, 0.25m, true),
            ["gpt-5.6-sol"] = new("gpt-5.6-sol", 4m, 0.4m, 20m, 5m, true),
            ["gpt-5.6-terra"] = new("gpt-5.6-terra", 2m, 0.2m, 12m, 2.5m, true),
        };

    public static CostObservation Resolve(
        string model,
        TokenBreakdown tokens,
        DateTimeOffset? occurredAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        string normalized = model.Trim().ToLowerInvariant();
        if (!TryResolveRates(normalized, out Rates rates))
        {
            return CostObservation.Unavailable();
        }

        if (ListRatesFromUtc.TryGetValue(rates.PriceMatch, out DatedRates? dated)
            && (occurredAtUtc ?? DateTimeOffset.UtcNow) >= dated.ListEffectiveFromUtc)
        {
            rates = dated.ListRates;
        }

        decimal inputMultiplier = 1m;
        decimal outputMultiplier = 1m;
        if (rates.HasLongContext
            && checked(tokens.Input + tokens.CacheRead + tokens.CacheWrite)
                > LongContextThreshold)
        {
            inputMultiplier = 2m;
            outputMultiplier = 1.5m;
        }

        decimal amount =
            ((tokens.Input * rates.Input * inputMultiplier)
             + (tokens.CacheWrite * rates.CacheWrite * inputMultiplier)
             + (tokens.CacheRead * rates.CachedInput * inputMultiplier)
             + ((tokens.Output + tokens.Reasoning) * rates.Output * outputMultiplier))
            / TokensPerMillion;
        return CostObservation.CatalogEstimated(
            decimal.Round(amount, 6, MidpointRounding.AwayFromZero),
            Version,
            rates.PriceMatch);
    }

    private static bool TryResolveRates(string model, out Rates rates)
    {
        if (RatesByModel.TryGetValue(model, out Rates? exact) && exact is not null)
        {
            rates = exact;
            return true;
        }

        foreach ((string baseModel, Rates candidate) in RatesByModel)
        {
            if (IsDatedSnapshot(model, baseModel))
            {
                rates = candidate;
                return true;
            }
        }

        rates = null!;
        return false;
    }

    private static bool IsDatedSnapshot(string model, string baseModel)
    {
        if (!model.StartsWith(baseModel + '-', StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> suffix = model.AsSpan(baseModel.Length + 1);
        return suffix.Length == 10
               && suffix[4] == '-'
               && suffix[7] == '-'
               && DateOnly.TryParseExact(
                   suffix,
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out _);
    }

    private sealed record Rates(
        string PriceMatch,
        decimal Input,
        decimal CachedInput,
        decimal Output,
        decimal CacheWrite,
        bool HasLongContext);

    private sealed record DatedRates(
        DateTimeOffset ListEffectiveFromUtc,
        Rates ListRates);
}
