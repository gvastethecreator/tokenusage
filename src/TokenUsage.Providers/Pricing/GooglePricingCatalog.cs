using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Pricing;

/// <summary>
/// Official Gemini Developer API rates. This is raw API value, not a
/// Google subscription or Antigravity credit charge.
/// </summary>
public static class GooglePricingCatalog
{
    public const string Version = "google-api-2026-08-12";
    private const decimal TokensPerMillion = 1_000_000m;

    private static readonly Dictionary<string, Rates> RatesByModel =
        new(StringComparer.Ordinal)
        {
            ["gemini-2.5-flash"] = new("gemini-2.5-flash", 0.3m, 0.03m, 2.5m),
            ["gemini-3-flash"] = new("gemini-3-flash", 0.5m, 0.05m, 3m),
            ["gemini-3-flash-preview"] = new("gemini-3-flash", 0.5m, 0.05m, 3m),
            ["gemini-3-pro"] = new("gemini-3-pro", 2m, 0.2m, 12m),
            ["gemini-3-pro-preview"] = new("gemini-3-pro", 2m, 0.2m, 12m),
            ["gemini-3-pro-image-preview"] = new("gemini-3-pro", 2m, 0.2m, 12m),
            ["gemini-3.1-pro"] = new("gemini-3.1-pro", 2m, 0.2m, 12m),
            ["gemini-3.1-pro-preview"] = new("gemini-3.1-pro", 2m, 0.2m, 12m),
            ["gemini-3.5-flash"] = new("gemini-3.5-flash", 1.5m, 0.15m, 9m),
            ["gemini-3.6-flash"] = new("gemini-3.6-flash", 1.5m, 0.15m, 7.5m),
            ["gemini-3.7-flash"] = new("gemini-3.7-flash", 0.75m, 0.075m, 3.75m),
        };

    public static CostObservation Resolve(string model, TokenBreakdown tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        string normalized = model.Trim().ToLowerInvariant();
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
            rates.PriceMatch);
    }

    private sealed record Rates(
        string PriceMatch,
        decimal Input,
        decimal CacheRead,
        decimal Output);
}
