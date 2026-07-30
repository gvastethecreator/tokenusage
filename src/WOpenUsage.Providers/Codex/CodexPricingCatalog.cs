using System.Globalization;
using WOpenUsage.Core.Usage;

namespace WOpenUsage.Providers.Codex;

public static class CodexPricingCatalog
{
    public const string Version = "openai-api-2026-07-27";
    private const decimal TokensPerMillion = 1_000_000m;
    private const long Gpt55LongContextThreshold = 272_000;

    private static readonly Dictionary<string, Rates> RatesByModel =
        new(StringComparer.Ordinal)
        {
            ["gpt-5.6"] = new("gpt-5.6-sol", 5m, 0.5m, 30m),
            ["gpt-5.6-sol"] = new("gpt-5.6-sol", 5m, 0.5m, 30m),
            ["gpt-5.6-terra"] = new("gpt-5.6-terra", 2.5m, 0.25m, 15m),
            ["gpt-5.6-luna"] = new("gpt-5.6-luna", 1m, 0.1m, 6m),
            ["gpt-5.5"] = new("gpt-5.5", 5m, 0.5m, 30m),
            ["gpt-5.4"] = new("gpt-5.4", 2.5m, 0.25m, 15m),
            ["gpt-5.4-mini"] = new("gpt-5.4-mini", 0.75m, 0.075m, 4.5m),
            ["gpt-5.3-codex"] = new("gpt-5.3-codex", 1.75m, 0.175m, 14m),
            ["gpt-5.2-codex"] = new("gpt-5.2-codex", 1.75m, 0.175m, 14m),
            ["gpt-5"] = new("gpt-5", 1.25m, 0.125m, 10m),
        };

    public static CostObservation Resolve(string model, TokenBreakdown tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        string normalized = model.Trim().ToLowerInvariant();
        if (!TryResolveRates(normalized, out Rates rates))
        {
            return CostObservation.Unavailable();
        }

        decimal inputMultiplier = 1m;
        decimal outputMultiplier = 1m;
        if (string.Equals(rates.PriceMatch, "gpt-5.5", StringComparison.Ordinal)
            && checked(tokens.Input + tokens.CacheRead + tokens.CacheWrite)
                > Gpt55LongContextThreshold)
        {
            inputMultiplier = 2m;
            outputMultiplier = 1.5m;
        }

        decimal amount =
            (((tokens.Input + tokens.CacheWrite) * rates.Input * inputMultiplier)
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
        decimal Output);
}
