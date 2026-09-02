using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Pricing;

/// <summary>
/// Official Gemini Developer API rates. This is raw API value, not a
/// Google subscription or Antigravity credit charge.
/// </summary>
public static class GooglePricingCatalog
{
    public const string Version = "google-api-2026-09-02";
    private const decimal TokensPerMillion = 1_000_000m;
    private const long LongContextThreshold = 200_000;

    // The Gemini 3.6 and 3.7 Flash promotional rates end on 2027-01-01. A
    // resolve without a timestamp prices at the rate valid now.
    private static readonly Dictionary<string, DatedRates> ListRatesFromUtc =
        new(StringComparer.Ordinal)
        {
            ["gemini-3.6-flash"] = new(
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new("gemini-3.6-flash", 1.5m, 0.15m, 7.5m)),
            ["gemini-3.7-flash"] = new(
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new("gemini-3.7-flash", 1.5m, 0.15m, 7.5m)),
        };

    private static readonly Dictionary<string, Rates> RatesByModel =
        new(StringComparer.Ordinal)
        {
            ["gemini-2.5-flash"] = new("gemini-2.5-flash", 0.3m, 0.03m, 2.5m),
            ["gemini-2.5-pro"] = new(
                "gemini-2.5-pro",
                1.25m, 0.125m, 10m,
                LongContext: new(2.5m, 0.25m, 15m)),
            ["gemini-3-flash"] = new("gemini-3-flash", 0.5m, 0.05m, 3m),
            ["gemini-3-flash-preview"] = new("gemini-3-flash", 0.5m, 0.05m, 3m),
            ["gemini-3-pro"] = new(
                "gemini-3-pro",
                2m, 0.2m, 12m,
                LongContext: new(4m, 0.4m, 18m)),
            ["gemini-3-pro-preview"] = new(
                "gemini-3-pro",
                2m, 0.2m, 12m,
                LongContext: new(4m, 0.4m, 18m)),
            ["gemini-3-pro-image-preview"] = new(
                "gemini-3-pro",
                2m, 0.2m, 12m,
                LongContext: new(4m, 0.4m, 18m)),
            ["gemini-3.1-pro"] = new(
                "gemini-3.1-pro",
                2m, 0.2m, 12m,
                LongContext: new(4m, 0.4m, 18m)),
            ["gemini-3.1-pro-preview"] = new(
                "gemini-3.1-pro",
                2m, 0.2m, 12m,
                LongContext: new(4m, 0.4m, 18m)),
            // Placeholder id the Gemini CLI writes for its default pro profile.
            ["gemini-pro-default"] = new(
                "gemini-3.1-pro",
                2m, 0.2m, 12m,
                LongContext: new(4m, 0.4m, 18m)),
            ["gemini-3.5-flash"] = new("gemini-3.5-flash", 1.5m, 0.15m, 9m),
            ["gemini-3.6-flash"] = new("gemini-3.6-flash", 0.75m, 0.075m, 3.75m),
            ["gemini-3.7-flash"] = new("gemini-3.7-flash", 0.75m, 0.075m, 3.75m),
        };

    public static CostObservation Resolve(
        string model,
        TokenBreakdown tokens,
        DateTimeOffset? occurredAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        string normalized = model.Trim().ToLowerInvariant();
        if (!RatesByModel.TryGetValue(normalized, out Rates? rates))
        {
            return CostObservation.Unavailable();
        }

        if (ListRatesFromUtc.TryGetValue(rates.PriceMatch, out DatedRates? dated)
            && (occurredAtUtc ?? DateTimeOffset.UtcNow) >= dated.ListEffectiveFromUtc)
        {
            rates = dated.ListRates;
        }

        // Google publishes higher pro rates for prompts over 200k tokens; the
        // higher tier then applies to the whole request. The boundary itself
        // (exactly 200k) stays on the standard tier.
        TieredRates? tier = rates.LongContext;
        bool longContext = tier is not null
            && checked(tokens.Input + tokens.CacheRead + tokens.CacheWrite)
                > LongContextThreshold;
        decimal inputRate = longContext ? tier!.Input : rates.Input;
        decimal cacheRate = longContext ? tier!.CacheRead : rates.CacheRead;
        decimal outputRate = longContext ? tier!.Output : rates.Output;

        decimal amount =
            (((tokens.Input + tokens.CacheWrite) * inputRate)
             + (tokens.CacheRead * cacheRate)
             + ((tokens.Output + tokens.Reasoning) * outputRate))
            / TokensPerMillion;
        return CostObservation.CatalogEstimated(
            decimal.Round(amount, 6, MidpointRounding.AwayFromZero),
            Version,
            rates.PriceMatch);
    }

    private sealed record TieredRates(
        decimal Input,
        decimal CacheRead,
        decimal Output);

    private sealed record Rates(
        string PriceMatch,
        decimal Input,
        decimal CacheRead,
        decimal Output,
        TieredRates? LongContext = null);

    private sealed record DatedRates(
        DateTimeOffset ListEffectiveFromUtc,
        Rates ListRates);
}
