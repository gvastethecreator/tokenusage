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

    [Fact]
    public void HostSpecificGoogleRateCannotReplaceTheDirectProviderRate()
    {
        PricingRateEvidence direct = Assert.Single(
            PricingEvidenceCatalog.AllRates,
            item => item.CatalogVersion == GooglePricingCatalog.Version
                    && item.ExactPriceMatch == "gemini-3.8-flash"
                    && item.IsPromotional);
        PricingRateEvidence hosted = Assert.Single(
            PricingEvidenceCatalog.AllRates,
            item => item.CatalogVersion == CursorPricingCatalog.Version
                    && item.ExactPriceMatch == "gemini-3.8-flash");

        Assert.Equal(PricingBillingScope.DirectProviderApi, direct.Source.BillingScope);
        Assert.Equal(PricingBillingScope.HostSpecific, hosted.Source.BillingScope);
        Assert.NotEqual(direct.CatalogVersion, hosted.CatalogVersion);
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
            new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero),
            PricingEvidenceCatalog.AllRates.Where(item => item.IsPromotional).ToArray()));
    }
}
