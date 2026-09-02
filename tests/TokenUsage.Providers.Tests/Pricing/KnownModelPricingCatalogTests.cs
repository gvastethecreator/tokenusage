using System.Globalization;
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
    [InlineData("grok-4.6", 2.6, "grok-4.6")]
    [InlineData("gemini-3.6-flash", 1.125, "gemini-3.6-flash")]
    [InlineData("gemini-2.5-flash", 0.55, "gemini-2.5-flash")]
    [InlineData("gemini-3-pro-preview", 5.8, "gemini-3-pro")]
    [InlineData("antigravity-gemini-3-pro-high", 5.8, "gemini-3-pro")]
    [InlineData("gemini-3-6-flash", 1.125, "gemini-3.6-flash")]
    [InlineData("gemini-3.7-flash", 1.125, "gemini-3.7-flash")]
    [InlineData("gemini-3.7-flash-control", 1.125, "gemini-3.7-flash")]
    [InlineData("claude-sonnet-4.6", 4.5, "claude-sonnet-4-6")]
    [InlineData("grok-4-5", 2.6, "grok-4.5")]
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
    [InlineData("composer-2.5", "xai-api-2026-09-02")]
    [InlineData("claude-4.5-sonnet", "anthropic-api-2026-09-02")]
    [InlineData("gpt-5.1-codex", "openai-api-2026-09-02")]
    [InlineData("gemini-3.6-flash", "google-api-2026-09-02")]
    [InlineData("glm-5.3-flash", "zai-api-2026-09-02")]
    [InlineData("kimi-k3", "moonshot-api-2026-09-02")]
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
    [InlineData("kimi-k3-max")]
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
        Assert.Equal(1.125m, cost.EstimatedCostUsd);
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

    [Fact]
    public void CursorGrokUsesCursorCachedInputWithoutTheXaiLongContextSurcharge()
    {
        CostObservation cached = CursorPricingCatalog.Resolve(
            "grok-4.5",
            OccurredAtUtc,
            new TokenBreakdown(0, 0, 0, 100_000, 0));
        CostObservation longContext = CursorPricingCatalog.Resolve(
            "grok-4.5",
            OccurredAtUtc,
            new TokenBreakdown(200_000, 0, 0, 0, 0));

        Assert.Equal(0.05m, cached.EstimatedCostUsd);
        Assert.Equal(0.4m, longContext.EstimatedCostUsd);
        Assert.Equal(CursorPricingCatalog.Version, cached.CatalogVersion);
        Assert.Equal(CursorPricingCatalog.Version, longContext.CatalogVersion);
    }

    [Fact]
    public void GrokFourFiveFastUsesTheOfficialOutputRate()
    {
        CostObservation cost = GrokPricingCatalog.Resolve(
            "grok-4.5-fast",
            MillionInHundredKOut);

        Assert.Equal(5.8m, cost.EstimatedCostUsd);
        Assert.Equal("grok-4.5-fast", cost.ExactPriceMatch);
    }

    [Fact]
    public void XaiPrefixedGrokIdsResolveLikeTheBareId()
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "xai-grok-4.5",
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal("grok-4.5", cost.ExactPriceMatch);
        Assert.Equal(5.2m, cost.EstimatedCostUsd);
    }

    [Fact]
    public void GrokBuildVariantsUseTheOnlyPublishedBuildRate()
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "grok-4.6-build",
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal("grok-build", cost.ExactPriceMatch);
        Assert.Equal(2.4m, cost.EstimatedCostUsd);
    }

    [Fact]
    public void GlmFiveThreeFlashPricesAtTheZaiPromoRate()
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "glm-5.3-flash",
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(0.1m, cost.EstimatedCostUsd);
    }

    [Fact]
    public void GeminiProDefaultPricesLikeTheCurrentProModel()
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "gemini-pro-default",
            OccurredAtUtc,
            MillionInHundredKOut);

        CostObservation pro = KnownModelPricingCatalog.Resolve(
            "gemini-3.1-pro",
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(pro.EstimatedCostUsd, cost.EstimatedCostUsd);
        Assert.Equal("gemini-3.1-pro", cost.ExactPriceMatch);
    }

    [Fact]
    public void GeminiProChargesTheLongContextTierOnlyAboveTwoHundredKPromptTokens()
    {
        CostObservation atTheLine = GooglePricingCatalog.Resolve(
            "gemini-3.1-pro",
            new TokenBreakdown(200_000, 0, 0, 0, 0));
        CostObservation justAbove = GooglePricingCatalog.Resolve(
            "gemini-3.1-pro",
            new TokenBreakdown(200_001, 0, 0, 0, 0));
        CostObservation longCached = GooglePricingCatalog.Resolve(
            "gemini-3-pro",
            new TokenBreakdown(0, 0, 0, 200_001, 0));

        Assert.Equal(0.4m, atTheLine.EstimatedCostUsd);
        Assert.Equal(0.800004m, justAbove.EstimatedCostUsd);
        Assert.Equal(0.08m, longCached.EstimatedCostUsd);
    }

    [Fact]
    public void GeminiTwoFiveProPricesOnBothPublishedTiers()
    {
        CostObservation baseTier = GooglePricingCatalog.Resolve(
            "gemini-2.5-pro",
            new TokenBreakdown(200_000, 0, 0, 0, 0));
        CostObservation longTier = GooglePricingCatalog.Resolve(
            "gemini-2.5-pro",
            new TokenBreakdown(200_001, 0, 0, 0, 0));

        Assert.Equal(0.25m, baseTier.EstimatedCostUsd);
        Assert.Equal(0.500003m, longTier.EstimatedCostUsd);
    }

    [Fact]
    public void GeminiFlashModelsKeepOneRateAtAnyPromptSize()
    {
        CostObservation huge = GooglePricingCatalog.Resolve(
            "gemini-3.6-flash",
            new TokenBreakdown(1_000_000, 0, 0, 0, 0));

        Assert.Equal(0.75m, huge.EstimatedCostUsd);
    }

    [Fact]
    public void ClaudeFastFlagOnAModelWithoutAFastEntryKeepsBaseRates()
    {
        CostObservation cost = ClaudePricingCatalog.Resolve(
            "claude-sonnet-4-6",
            OccurredAtUtc,
            MillionInHundredKOut,
            cacheWrite5Minutes: 0,
            cacheWrite1Hour: 0,
            reportedCostUsd: null,
            isFast: true);

        CostObservation baseCost = ClaudePricingCatalog.Resolve(
            "claude-sonnet-4-6",
            OccurredAtUtc,
            MillionInHundredKOut,
            cacheWrite5Minutes: 0,
            cacheWrite1Hour: 0,
            reportedCostUsd: null,
            isFast: false);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(baseCost.EstimatedCostUsd, cost.EstimatedCostUsd);
    }

    [Fact]
    public void ClaudeOpusFourSevenFastStaysUnpricedBecauseTheApiRejectsIt()
    {
        CostObservation cost = ClaudePricingCatalog.Resolve(
            "claude-opus-4-7-fast",
            OccurredAtUtc,
            new TokenBreakdown(1_000_000, 0, 0, 0, 0),
            cacheWrite5Minutes: 0,
            cacheWrite1Hour: 0,
            reportedCostUsd: null,
            isFast: true);

        Assert.Equal(CostKind.Unavailable, cost.Kind);
        Assert.Null(cost.EstimatedCostUsd);
    }

    [Fact]
    public void ClaudeUnseenDatedSnapshotPricesAsItsBaseModel()
    {
        CostObservation snapshot = ClaudePricingCatalog.Resolve(
            "claude-sonnet-5-20260115",
            OccurredAtUtc,
            new TokenBreakdown(1_000_000, 0, 0, 0, 0),
            cacheWrite5Minutes: 0,
            cacheWrite1Hour: 0,
            reportedCostUsd: null,
            isFast: false);

        CostObservation baseModel = ClaudePricingCatalog.Resolve(
            "claude-sonnet-5",
            OccurredAtUtc,
            new TokenBreakdown(1_000_000, 0, 0, 0, 0),
            cacheWrite5Minutes: 0,
            cacheWrite1Hour: 0,
            reportedCostUsd: null,
            isFast: false);

        Assert.Equal(CostKind.CatalogEstimated, snapshot.Kind);
        Assert.Equal(baseModel.EstimatedCostUsd, snapshot.EstimatedCostUsd);
        Assert.Equal(2m, snapshot.EstimatedCostUsd);
    }

    [Theory]
    [InlineData("2026-11-21T12:00:00Z", 0.6)]
    [InlineData("2026-11-22T00:00:00Z", 0.8)]
    public void GptFiveSixSolSwitchesToTheListRateAfterThePromoCutoff(
        string occurredAt,
        decimal expectedUsd)
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "gpt-5.6-sol",
            DateTimeOffset.Parse(occurredAt, CultureInfo.InvariantCulture),
            new TokenBreakdown(100_000, 10_000, 0, 0, 0));

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(expectedUsd, cost.EstimatedCostUsd);
    }

    [Theory]
    [InlineData("2026-09-09T15:59:59Z", 0.1)]
    [InlineData("2026-09-09T16:00:00Z", 0.2)]
    public void GlmFiveThreeFlashSwitchesToTheListRateAfterThePromoCutoff(
        string occurredAt,
        decimal expectedUsd)
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "glm-5.3-flash",
            DateTimeOffset.Parse(occurredAt, CultureInfo.InvariantCulture),
            MillionInHundredKOut);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(expectedUsd, cost.EstimatedCostUsd);
    }

    [Theory]
    [InlineData("2026-12-31T23:59:59Z", 1.125)]
    [InlineData("2027-01-01T00:00:00Z", 2.25)]
    public void GeminiThreeSevenFlashDoublesAfterTheIntroductoryPeriod(
        string occurredAt,
        decimal expectedUsd)
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "gemini-3.7-flash",
            DateTimeOffset.Parse(occurredAt, CultureInfo.InvariantCulture),
            MillionInHundredKOut);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(expectedUsd, cost.EstimatedCostUsd);
    }

    [Theory]
    [InlineData("2026-12-31T23:59:59Z", 1.125)]
    [InlineData("2027-01-01T00:00:00Z", 2.25)]
    public void GeminiThreeSixFlashUsesTheSamePromotionalCutoff(
        string occurredAt,
        decimal expectedUsd)
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "gemini-3.6-flash",
            DateTimeOffset.Parse(occurredAt, CultureInfo.InvariantCulture),
            MillionInHundredKOut);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(expectedUsd, cost.EstimatedCostUsd);
    }

    [Fact]
    public void ClaudeMythosFiveUsesThePlatformDocsRate()
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "claude-mythos-5",
            OccurredAtUtc,
            MillionInHundredKOut);

        // 10 in / 50 out with the standard 100k output share of the fixture.
        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(15m, cost.EstimatedCostUsd);
    }

    [Fact]
    public void ClaudeFiveOneModelsUseThePublishedLowerCacheReadRate()
    {
        foreach (string model in (string[])["claude-fable-5-1", "claude-mythos-5-1"])
        {
            CostObservation cost = KnownModelPricingCatalog.Resolve(
                model,
                OccurredAtUtc,
                new TokenBreakdown(0, 0, 0, 1_000_000, 0));

            Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
            Assert.Equal(0.25m, cost.EstimatedCostUsd);
        }
    }

    [Theory]
    [InlineData("kimi-k2.6", 1.35)]
    [InlineData("kimi-k2.7-code-highspeed", 2.7)]
    public void PricesCurrentKimiCodingModels(string model, decimal expectedUsd)
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            model,
            OccurredAtUtc,
            MillionInHundredKOut);

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(expectedUsd, cost.EstimatedCostUsd);
        Assert.Equal(MoonshotPricingCatalog.Version, cost.CatalogVersion);
    }
}
