using System.Globalization;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Cursor;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Tests.Pricing;

public sealed class PricingEvidenceCatalogTests
{
    [Fact]
    public void EveryCatalogRateHasTypedOfficialEvidence()
    {
        Assert.NotEmpty(PricingEvidenceCatalog.AllRates);
        Assert.All(PricingEvidenceCatalog.AllRates, evidence =>
        {
            Assert.True(evidence.Source.OfficialUri.IsAbsoluteUri);
            Assert.Equal(Uri.UriSchemeHttps, evidence.Source.OfficialUri.Scheme);
            Assert.NotEqual(default, evidence.Source.ReviewedOn);
            Assert.True(Enum.IsDefined(evidence.Source.BillingScope));
        });
    }

    [Fact]
    public void MissingEvidenceFailsCoverageValidation()
    {
        PricingRateEvidence present = PricingEvidence.Ongoing(
            "catalog-v1",
            "known-a",
            PricingOfficialSources.OpenAi);

        Assert.Throws<InvalidDataException>(() => PricingCatalogAudit.ValidateCoverage(
            "catalog-v1",
            ["known-a", "known-b"],
            [present]));
    }

    [Fact]
    public void DiagnosticsReportStaleSourcesAndPromotionsNearExpiry()
    {
        var staleSource = new PricingSourceEvidence(
            "stale",
            new Uri("https://example.test/pricing"),
            new DateOnly(2026, 1, 1),
            PricingBillingScope.DirectProviderApi);
        DateTimeOffset now = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
        PricingRateEvidence promotion = new(
            "catalog-v1",
            "model-a",
            staleSource,
            PricingEvidence.HistoricalStartUtc,
            now.AddDays(5),
            PricingValidityEndRule.Exclusive,
            isPromotional: true);

        IReadOnlyList<PricingDiagnostic> diagnostics = PricingCatalogAudit.Evaluate(
            [promotion],
            now,
            TimeSpan.FromDays(45),
            TimeSpan.FromDays(30));

        Assert.Contains(diagnostics, item => item.Kind == PricingDiagnosticKind.StaleSource);
        Assert.Contains(
            diagnostics,
            item => item.Kind == PricingDiagnosticKind.PromotionNearExpiry
                    && item.ExactPriceMatch == "model-a");
    }

    [Fact]
    public void ExpiredPromotionWithoutSuccessorFailsTheCatalogDiagnostics()
    {
        DateTimeOffset now = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
        PricingRateEvidence expired = PricingEvidence.Promotion(
            "catalog-v1",
            "model-a",
            PricingOfficialSources.OpenAi,
            now.AddTicks(-1));

        PricingDiagnostic diagnostic = Assert.Single(PricingCatalogAudit.Evaluate(
            [expired],
            now,
            TimeSpan.FromDays(45),
            TimeSpan.FromDays(30)));

        Assert.Equal(PricingDiagnosticKind.ExpiredPromotionWithoutSuccessor, diagnostic.Kind);
    }

    [Fact]
    public void PromotionalEndBoundaryIsExclusiveAndSuccessorStartsAtTheSameInstant()
    {
        DateTimeOffset cutoff = new(2026, 9, 9, 16, 0, 0, TimeSpan.Zero);
        PricingRateEvidence promotion = PricingEvidence.Promotion(
            "catalog-v1",
            "model-a",
            PricingOfficialSources.Zai,
            cutoff);
        PricingRateEvidence successor = PricingEvidence.FollowOn(
            "catalog-v1",
            "model-a",
            PricingOfficialSources.Zai,
            cutoff);

        Assert.True(promotion.IsEffectiveAt(cutoff.AddTicks(-1)));
        Assert.False(promotion.IsEffectiveAt(cutoff));
        Assert.False(successor.IsEffectiveAt(cutoff.AddTicks(-1)));
        Assert.True(successor.IsEffectiveAt(cutoff));
    }

    [Theory]
    [InlineData("gemini-3.8-flash", true)]
    [InlineData("gemini-4-carbon", true)]
    [InlineData("gemini-4-hydrogen", false)]
    public void HostSpecificGoogleRateCannotReplaceTheDirectProviderRate(string model, bool isPromotional)
    {
        DateTimeOffset occurredAt = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
        PricingRateEvidence direct = Assert.Single(
            PricingEvidenceCatalog.AllRates,
            item => item.CatalogVersion == GooglePricingCatalog.Version
                    && item.ExactPriceMatch == model
                    && item.IsEffectiveAt(occurredAt));
        PricingRateEvidence hosted = Assert.Single(
            PricingEvidenceCatalog.AllRates,
            item => item.CatalogVersion == CursorPricingCatalog.Version
                    && item.ExactPriceMatch == model
                    && item.IsEffectiveAt(occurredAt));

        Assert.Equal(PricingBillingScope.DirectProviderApi, direct.Source.BillingScope);
        Assert.Equal(PricingBillingScope.HostSpecific, hosted.Source.BillingScope);
        Assert.Equal(isPromotional, direct.IsPromotional);
        Assert.NotEqual(direct.CatalogVersion, hosted.CatalogVersion);
    }

    [Fact]
    public void AstraUsesItsOwnReviewedSource()
    {
        PricingRateEvidence evidence = Assert.Single(PricingEvidenceCatalog.AllRates,
            item => item.ExactPriceMatch == "gpt-6-astra");

        Assert.Equal(PricingOfficialSources.OpenAiAstra, evidence.Source);
        Assert.False(evidence.IsPromotional);
        Assert.Equal(new DateOnly(2026, 10, 19), evidence.Source.ReviewedOn);
    }

    [Fact]
    public void StoredVersionAndExactMatchReproduceTheApplicableEvidence()
    {
        DateTimeOffset occurredAt = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "gpt-5.6-sol",
            occurredAt,
            new TokenBreakdown(1_000_000, 100_000, 0, 0, 0));

        Assert.True(PricingCatalogAudit.CanReproduce(
            cost,
            occurredAt,
            PricingEvidenceCatalog.AllRates));
        Assert.False(PricingCatalogAudit.CanReproduce(
            cost,
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            PricingEvidenceCatalog.AllRates.Where(item => item.IsPromotional).ToArray()));
    }

    [Theory]
    [InlineData("gpt-6.1-terra", 0.151, 7)]
    [InlineData("gpt-6-cyber", 3.04, 9)]
    [InlineData("kimi-k3.1-pro", 0.304, 7)]
    [InlineData("kimi-k3.1-pro-highspeed", 0.604, 7)]
    [InlineData("glm-5.3-flashx-legal", 0.051, 7)]
    [InlineData("claude-fable-5-2", 1.204, 9)]
    [InlineData("claude-fable-5-2-fast", 2.408, 9)]
    [InlineData("claude-mythos-5-2", 1.204, 9)]
    [InlineData("claude-mythos-5-2-fast", 2.408, 9)]
    public void NovemberModelsUseTheirReviewedInputCacheAndOutputPrices(
        string model, decimal expectedUsd, int reviewedDay)
    {
        DateTimeOffset occurredAt = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            model,
            occurredAt,
            new TokenBreakdown(100_000, 10_000, 0, 20_000, 0));

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(expectedUsd, cost.EstimatedCostUsd);
        PricingRateEvidence evidence = Assert.Single(PricingEvidenceCatalog.AllRates,
            item => item.CatalogVersion == cost.CatalogVersion && item.ExactPriceMatch == cost.ExactPriceMatch);
        Assert.Equal(new DateOnly(2026, 11, reviewedDay), evidence.Source.ReviewedOn);
        if (evidence.EffectiveFromUtc > PricingEvidence.HistoricalStartUtc)
        {
            Assert.Equal(CostKind.Unavailable, KnownModelPricingCatalog.Resolve(
                model, evidence.EffectiveFromUtc.AddTicks(-1),
                new TokenBreakdown(100_000, 10_000, 0, 20_000, 0)).Kind);
        }
    }

    [Theory]
    [InlineData("kimi-k3.1-pro")]
    [InlineData("kimi-k3.1-pro-highspeed")]
    [InlineData("gemini-4-argon")]
    [InlineData("gemini-4-carbon")]
    [InlineData("gemini-4-hydrogen")]
    public void DirectCacheWritesWithoutPublishedApplicableRatesRemainUnpriced(string model)
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            model,
            new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero),
            new TokenBreakdown(100, 10, 0, 0, 1));

        Assert.Equal(CostKind.Unavailable, cost.Kind);
    }

    [Theory]
    [InlineData(252_000, 20_000, 0, true, "5.081")]
    [InlineData(252_001, 20_000, 0, true, "10.16154")]
    [InlineData(252_001, 20_000, 1, true, null)]
    [InlineData(252_001, 20_000, 0, false, null)]
    public void CyberSixPricesOnlyPublishedLongContextCategories(
        long input, long cacheRead, long cacheWrite, bool perRequest, string? expectedUsd)
    {
        CostObservation cost = KnownModelPricingCatalog.Resolve(
            "gpt-6-cyber",
            new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero),
            new TokenBreakdown(input, 10, 0, cacheRead, cacheWrite),
            perRequestTokenCounters: perRequest);

        Assert.Equal(expectedUsd is null ? CostKind.Unavailable : CostKind.CatalogEstimated, cost.Kind);
        decimal? expectedCost = expectedUsd is null
            ? null
            : decimal.Parse(expectedUsd, CultureInfo.InvariantCulture);
        Assert.Equal(expectedCost, cost.EstimatedCostUsd);
    }
}
