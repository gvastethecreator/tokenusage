using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Pricing;

/// <summary>
/// The per-million-token arithmetic every list-price catalog shares. Cache writes bill at the
/// input rate unless a catalog publishes a separate cache-write rate, and reasoning tokens bill
/// as output. Multipliers carry long-context surcharges. The result is rounded to six decimals.
/// </summary>
internal static class LinearTokenPrice
{
    private const decimal TokensPerMillion = 1_000_000m;

    public static decimal Estimate(
        TokenBreakdown tokens,
        decimal input,
        decimal cacheRead,
        decimal output,
        decimal? cacheWrite = null,
        decimal inputMultiplier = 1m,
        decimal outputMultiplier = 1m)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        decimal amount =
            (((tokens.Input * input)
              + (tokens.CacheWrite * (cacheWrite ?? input))
              + (tokens.CacheRead * cacheRead)) * inputMultiplier)
            + ((tokens.Output + tokens.Reasoning) * output * outputMultiplier);
        return decimal.Round(amount / TokensPerMillion, 6, MidpointRounding.AwayFromZero);
    }
}
