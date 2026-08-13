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
    public static CostObservation Resolve(
        string model,
        DateTimeOffset occurredAtUtc,
        TokenBreakdown tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(tokens);

        return KnownModelPricingCatalog.Resolve(model, occurredAtUtc, tokens);
    }
}
