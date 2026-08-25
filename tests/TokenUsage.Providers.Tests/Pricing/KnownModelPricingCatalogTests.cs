using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.Cursor;
using TokenUsage.Providers.Grok;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Tests.Pricing;

public sealed class KnownModelPricingCatalogTests
{
    private static readonly DateTimeOffset OccurredAtUtc =
        new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly TokenBreakdown MillionInHundredKOut =
        new(1_000_000, 100_000, 0, 0, 0);

    [Theory]
    [InlineData("composer-2.5", 0.75, "composer-2.5")]
    [InlineData("cursor-composer-2.5", 0.75, "composer-2.5")]
    [InlineData("claude-4.5-sonnet", 4.5, "claude-sonnet-4-5")]
    [InlineData("anthropic/claude-4.6-sonnet", 4.5, "claude-sonnet-4-6")]
    [InlineData("claude-4.5-sonnet-thinking", 4.5, "claude-sonnet-4-5")]
    [InlineData("claude-opus-4-7", 7.5, "claude-opus-4-7")]
    [InlineData("claude-sonnet-5", 3.0, "claude-sonnet-5")]
    [InlineData("gpt-5.1-codex", 2.25, "gpt-5.1-codex")]
    [InlineData("gpt-5-mini", 0.45, "gpt-5-mini")]
    [InlineData("gpt-5.6 sol", 11.0, "gpt-5.6-sol")]
    [InlineData("gpt-5.6-luna", 0.58, "gpt-5.6-luna")]
    [InlineData("gpt-5.6-terra", 5.8, "gpt-5.6-terra")]
    [InlineData("grok-4.6", 5.2, "grok-4.6")]
    [InlineData("gemini-3.6-flash", 2.25, "gemini-3.6-flash")]
    [InlineData("gemini-2.5-flash", 0.55, "gemini-2.5-flash")]
    [InlineData("gemini-3-pro-preview", 3.2, "gemini-3-pro")]
    [InlineData("antigravity-gemini-3-pro-high", 3.2, "gemini-3-pro")]
    [InlineData("gemini-3-6-flash", 2.25, "gemini-3.6-flash")]
    [InlineData("gemini-3.7-flash", 1.125, "gemini-3.7-flash")]
    [InlineData("gemini-3.7-flash-control", 1.125, "gemini-3.7-flash")]
    [InlineData("claude-sonnet-4.6", 4.5, "claude-sonnet-4-6")]
    [InlineData("grok-4-5", 5.2, "grok-4.5")]
    public void PricesCursorModelIdsFromOfficialProviderRates(
        string model,
        decimal expectedUsd,
        string exactPriceMatch)
    {
        CostObservation cost = CursorPricingCatalog.Resolve(
            model,
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(expectedUsd, cost.EstimatedCostUsd);
        Assert.Equal(exactPriceMatch, cost.ExactPriceMatch);
    }

    [Theory]
    [InlineData("composer-2.5", "xai-api-2026-08-12")]
    [InlineData("claude-4.5-sonnet", "anthropic-api-2026-08-12")]
    [InlineData("gpt-5.1-codex", "openai-api-2026-08-25")]
    [InlineData("gemini-3.6-flash", "google-api-2026-08-12")]
    public void UsesTheOfficialCatalogVersion(string model, string catalogVersion)
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            model,
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(catalogVersion, cost.CatalogVersion);
    }

    [Theory]
    [InlineData("cursor-auto")]
    [InlineData("auto")]
    [InlineData("unknown-model")]
    public void LeavesUnknownAndAutoModelsUnpriced(string model)
    {
        CostObservation cost = CursorPricingCatalog.Resolve(
            model,
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(CostKind.Unavailable, cost.Kind);
        Assert.Null(cost.EstimatedCostUsd);
    }

    [Fact]
    public void ClaudeOpusFastUsesTheDocumentedFastApiRate()
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "claude-opus-5-fast",
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(15m, cost.EstimatedCostUsd);
        Assert.Equal("claude-opus-5-fast", cost.ExactPriceMatch);
        Assert.Equal(ClaudePricingCatalog.Version, cost.CatalogVersion);
    }

    [Fact]
    public void OpenAiGptFiveRemainsPricedAtTheOfficialRate()
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "gpt-5",
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(2.25m, cost.EstimatedCostUsd);
        Assert.Equal(CodexPricingCatalog.Version, cost.CatalogVersion);
    }

    [Fact]
    public void GrokComposerAliasKeepsTheSameRateAsTheCursorModelId()
    {
        CostObservation cursorName = GrokPricingCatalog.Resolve(
            "composer-2.5",
            MillionInHundredKOut);
        CostObservation grokName = GrokPricingCatalog.Resolve(
            "grok-composer-2.5",
            MillionInHundredKOut);

        Assert.Equal(cursorName.EstimatedCostUsd, grokName.EstimatedCostUsd);
        Assert.Equal("composer-2.5", cursorName.ExactPriceMatch);
        Assert.Equal("composer-2.5", grokName.ExactPriceMatch);
    }

    [Fact]
    public void ReportedZeroPrefersTheCatalogForAKnownModel()
    {
        CostObservation cost = KnownModelPricingCatalog.ResolveReportedOrCatalog(
            0m,
            "gemini-3.6-flash",
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(2.25m, cost.EstimatedCostUsd);
        Assert.Null(cost.ReportedCostUsd);
    }

    [Theory]
    [InlineData("Gemini 3.6 Flash (High)", "gemini-3.6-flash")]
    [InlineData("claude-sonnet-4-6@20250929", "claude-sonnet-4-6-20250929")]
    [InlineData("gpt-5.1-codex_max", "gpt-5.1-codex-max")]
    public void SanitizesRawModelStringsIntoReadableStableIds(string raw, string expected)
    {
        Assert.Equal(expected, ModelIdentity.ForStorage(raw));
        Assert.False(ModelIdentity.ForStorage(raw).StartsWith("unknown-", StringComparison.Ordinal));
    }

    [Fact]
    public void GrokFourFiveUsesTheOfficialCachedInputRate()
    {
        CostObservation cost = GrokPricingCatalog.Resolve(
            "grok-4.5",
            new TokenBreakdown(0, 0, 0, 100_000, 0));

        Assert.Equal(0.03m, cost.EstimatedCostUsd);
        Assert.Equal(GrokPricingCatalog.Version, cost.CatalogVersion);
    }

    [Fact]
    public void GrokFourFiveDoublesWhenThePromptCrossesTheOfficialLongContextLine()
    {
        CostObservation shortContext = GrokPricingCatalog.Resolve(
            "grok-4.5",
            new TokenBreakdown(199_999, 0, 0, 0, 0));
        CostObservation longContext = GrokPricingCatalog.Resolve(
            "grok-4.5",
            new TokenBreakdown(200_000, 0, 0, 0, 0));

        Assert.Equal(0.399998m, shortContext.EstimatedCostUsd);
        Assert.Equal(0.8m, longContext.EstimatedCostUsd);
    }
}
