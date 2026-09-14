using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageComparisonTests
{
    [Fact]
    public void LinearScenarioUsesUnionRatesAndSeparatesMixFromVolumeAndPrice()
    {
        var dateA = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var dateB = dateA.AddMonths(1);
        UsageEvent Row(string id, string model, long input, long output = 0, string? tier = "standard",
            UsageDetailMetadata? detail = null) => new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant()),
            new AgentId("codex"), new ModelProviderId("openai"), new ModelId(model), dateA, "UTC",
            new(input, output, 0, 0, 0), CostObservation.Unavailable(), "parser", CoverageKind.Partial,
            timePrecision: UsageTimePrecision.Timestamp, serviceTier: tier, detailMetadata: detail ?? new(representationRevision: 1, input: UsageComponentAvailability.Measured,
                output: UsageComponentAvailability.Measured, reasoning: UsageComponentAvailability.Measured,
                cacheRead: UsageComponentAvailability.Measured, cacheWrite: UsageComponentAvailability.Measured));
        UsageLinearTariffResolution Resolve(UsageEvent row, DateTimeOffset date) => row.ModelId.Value switch
        {
            "missing-base" when date == dateA => new(null, UsagePriceExclusion.MissingTariff),
            "threshold" => new(null, UsagePriceExclusion.NonLinearRegime),
            _ => new(new(date == dateA ? "catalog-a" : "catalog-b", row.ModelId.Value, "linear",
                row.ModelId.Value == "new-model" ? 3 : date == dateA ? 1 : 2, 4, 4, 0, 1), null),
        };
        UsageEvent[] a = [Row("a", "old-model", 1_000_000)];
        UsageEvent[] b = [Row("b", "old-model", 1_000_000), Row("c", "new-model", 1_000_000),
            Row("d", "missing-base", 300), Row("e", "threshold", 400),
            Row("f", "old-model", 500, tier: null), Row("g", "unknown-model", 600, detail: UsageDetailMetadata.Unknown)];
        UsageLinearPriceResult result = UsageLinearPriceScenario.Compare(a, b, dateA, dateB, Resolve);
        Assert.Equal(1m, result.CostA); Assert.Equal(5m, result.CostB);
        Assert.Equal(1m, result.Volume); Assert.Equal(2m, result.Mix); Assert.Equal(1m, result.Price);
        Assert.Equal(0m, result.RoundingResidual);
        Assert.Equal(result.CostB - result.CostA, result.Volume + result.Mix + result.Price + result.RoundingResidual);
        Assert.Equal(UsagePriceEffectStatus.Available, result.EffectStatus);
        Assert.Equal(2_000_000, result.EligibleTokensB);
        Assert.Equal(1_800, result.Exclusions.Sum(row => row.TokensB));
        Assert.Equal(4, result.Exclusions.Sum(row => row.RecordsB));
        Assert.Equal(0, Assert.Single(result.Cells, row => row.Cell.Model == "new-model").TokensA);
        UsageLinearPriceResult zero = UsageLinearPriceScenario.Compare(a, [Row("zero", "old-model", 0)], dateA, dateB, Resolve);
        Assert.Equal(0m, zero.CostB); Assert.Null(zero.Volume); Assert.Null(zero.Mix); Assert.Null(zero.Price);
        Assert.Equal(UsagePriceEffectStatus.ZeroVolumeCurrent, zero.EffectStatus);
        UsageLinearPriceResult components = UsageLinearPriceScenario.Compare([Row("in", "old-model", 3)],
            [Row("out", "old-model", 0, 7)], dateA, dateB, Resolve);
        Assert.Equal(0.000004m, components.Volume);
        Assert.Equal(0.000021m, components.Mix);
        Assert.Equal(0m, components.Price);
        Assert.Equal(components.CostB - components.CostA,
            components.Volume + components.Mix + components.Price + components.RoundingResidual);
        var revisionTwo = new UsageDetailMetadata(representationRevision: 2, input: UsageComponentAvailability.Measured,
            output: UsageComponentAvailability.Measured, reasoning: UsageComponentAvailability.Measured,
            cacheRead: UsageComponentAvailability.Measured, cacheWrite: UsageComponentAvailability.Measured);
        UsageLinearPriceResult changedMethod = UsageLinearPriceScenario.Compare(a,
            [Row("revision-change", "old-model", 500, detail: revisionTwo)], dateA, dateB, Resolve);
        Assert.Equal(new UsagePriceExcluded(UsagePriceExclusion.IncompatibleMeasurement, 1_000_000, 500, 1, 1),
            Assert.Single(changedMethod.Exclusions));
        Assert.Null(changedMethod.Volume);
    }

    [Fact]
    public void RatesCatalogDatesReloadWithoutReferenceOverlay()
    {
        Assert.True(UsageComparison.ReloadsForCatalogDate(useReferencePrices: false, isRatesAxis: true));
        Assert.False(UsageComparison.ReloadsForCatalogDate(useReferencePrices: false, isRatesAxis: false));
        Assert.True(UsageComparison.ReloadsForCatalogDate(useReferencePrices: true, isRatesAxis: false));
        Assert.False(UsageComparison.OverlaysSingleReferencePrice(
            useReferencePrices: true, isCyclesAxis: false, isRatesAxis: true));
        Assert.True(UsageComparison.OverlaysSingleReferencePrice(
            useReferencePrices: true, isCyclesAxis: false, isRatesAxis: false));
        Assert.False(UsageComparison.OverlaysSingleReferencePrice(
            useReferencePrices: true, isCyclesAxis: true, isRatesAxis: false));
        var utc = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
        var later = utc.AddDays(30);
        string left = UsageComparison.CatalogDateLabel(utc, CultureInfo.InvariantCulture);
        string right = UsageComparison.CatalogDateLabel(later, CultureInfo.InvariantCulture);
        Assert.StartsWith("Catalog value at ", left, StringComparison.Ordinal);
        Assert.EndsWith(" UTC", left);
        Assert.EndsWith(" UTC", right);
        Assert.Contains("2026", left, StringComparison.Ordinal);
        Assert.NotEqual(left, right);

    }

    [Fact]
    public void WinnerMarksOnlyDirectedMetricsAndNeverTiesOrMissingValues()
    {
        Assert.Null(UsageComparison.Winner(4, 1, UsageBestDirection.None));
        Assert.Null(UsageComparison.Winner(4, 4, UsageBestDirection.Lower));
        Assert.Null(UsageComparison.Winner(null, 1, UsageBestDirection.Lower));
        Assert.Equal(1, UsageComparison.Winner(4, 1, UsageBestDirection.Lower));
        Assert.Equal(-1, UsageComparison.Winner(4, 9, UsageBestDirection.Lower));
        Assert.Equal(1, UsageComparison.Winner(10, 90, UsageBestDirection.Higher));
        Assert.Equal(-1, UsageComparison.Winner(90, 10, UsageBestDirection.Higher));
        Assert.Null(UsageComparison.Winner(100, 50, UsageBestDirection.None));
    }

    [Fact]
    public void WeeksUseTheSameCompletedWeekdaysAndDoNotInventAnElapsedMonday()
    {
        var today = new DateOnly(2026, 9, 9);
        var range = UsageComparisonPeriods.Resolve(UsageComparisonPreset.CurrentWeek, today, today, today);
        Assert.Equal(new DateOnly(2026, 9, 7), range.CurrentStart);
        Assert.Equal(new DateOnly(2026, 9, 8), range.CurrentEnd);
        Assert.Equal(new DateOnly(2026, 8, 31), range.BaselineStart);
        Assert.Equal(new DateOnly(2026, 9, 1), range.BaselineEnd);
        Assert.False(UsageComparisonPeriods.Resolve(UsageComparisonPreset.CurrentWeek, today.AddDays(-2), today, today).HasElapsedDays);
        var dstWeek = UsageComparisonPeriods.Resolve(UsageComparisonPreset.LastCompleteWeek,
            new DateOnly(2026, 3, 9), today, today);
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        DateTime from = TimeZoneInfo.ConvertTimeToUtc(dstWeek.CurrentStart.ToDateTime(TimeOnly.MinValue), zone);
        DateTime to = TimeZoneInfo.ConvertTimeToUtc(dstWeek.CurrentEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        Assert.Equal(TimeSpan.FromHours(167), to - from); // Seven calendar dates, not seven fixed 24-hour blocks.
    }

    [Fact]
    public void ChangesKeepMissingAndZeroBaselinesDistinctAndMergeModelHostContributions()
    {
        Assert.Null(UsageNumericChange.Between(0, 4).RelativePercent);
        Assert.Equal(4, UsageNumericChange.Between(0, 4).Absolute);
        Assert.Null(UsageNumericChange.Between(null, 4).Absolute);
        Assert.Equal(50, UsageNumericChange.Between(20, 30).RelativePercent);
        UsageReport baseline = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([Event("a", 600)]));
        UsageEvent alternateHost = Event("b", 400, host: "another-host");
        UsageReport current = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([Event("a", 600), alternateHost]));
        var contribution = Assert.Single(UsageComparison.Contributions(baseline, current));
        Assert.Equal(400, contribution.Tokens.Absolute);
        Assert.Null(contribution.Cost.Absolute);
        Assert.Equal(1, UsageComparison.ActiveDays(current));
    }

    [Fact]
    public void ReferencePricingKeepsOriginalTotalsAndExcludesUnsupportedHistoricalAndTierEvidence()
    {
        UsageEvent supported = Event("supported", 600);
        UsageEvent legacy = Event("legacy", 400, precision: UsageTimePrecision.Unknown);
        UsageReport original = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([supported, legacy]));
        UsageReport priced = UsageReferencePricing.Apply(original, [supported, legacy], _ =>
            CostObservation.CatalogEstimated(6, "fixed-catalog", "gpt-5.6-sol"));
        Assert.Equal(1_000, priced.Totals.Tokens.Total);
        Assert.Equal(400, priced.PriceReferenceExcludedTokens);
        Assert.Equal(6, priced.Totals.EstimatedCostUsd);
        Assert.Equal(10_000, UsageComparison.CostPerMillionPricedTokens(priced.Totals));
        Assert.Equal(["fixed-catalog"], priced.PricingVersions);
    }

    [Fact]
    public void ConsumptionNeedsCompleteAccountingAndMatchingPoolNotJustLevels()
    {
        var evidence = new QuotaIntervalEvidence(QuotaWindowSemantics.Fixed, true, true, true);
        var accounting = QuotaIntervalAccounting.Calculate([0, 40, 20, 60], evidence);
        Assert.Equal(80, accounting.ConsumedPoints);
        Assert.Equal(20, accounting.ReplenishedPoints);
        Assert.Null(QuotaIntervalAccounting.Calculate([0, 40, 20, 60], evidence with { HasCompleteAccounting = false }).ConsumedPoints);
        Assert.Null(QuotaIntervalAccounting.Calculate([0, 40], evidence with { Semantics = QuotaWindowSemantics.Rolling }).ConsumedPoints);
        Assert.Null(accounting.TokensPerPoint(1_000_000, evidence with { HasMatchingPoolUsage = false }));
        var a = QuotaIntervalAccounting.Calculate([0, 20], evidence);
        var b = QuotaIntervalAccounting.Calculate([0, 30], evidence);
        Assert.Equal(50, UsageNumericChange.Between(a.PointsPerMillionTokens(1_000_000, evidence), b.PointsPerMillionTokens(1_000_000, evidence)).RelativePercent);
        Assert.Equal(-33.333m, decimal.Round(UsageNumericChange.Between(a.TokensPerPoint(1_000_000, evidence), b.TokensPerPoint(1_000_000, evidence)).RelativePercent!.Value, 3));
    }

    [Fact]
    public async Task ExactQueriesExcludeDailyAndCrossBoundaryEvidenceAndRetainedHistoryIsNotZero()
    {
        string root = Path.Combine(Path.GetTempPath(), "tokenusage-comparison-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "usage.db");
            var repository = await UsageRepository.OpenAsync(path);
            UsageEvent timestamp = Event("precise", 600, tier: "fast");
            UsageEvent daily = Event("daily", 400, precision: UsageTimePrecision.Daily, host: "azure");
            var crossing = new UsageEvent(new UsageEventKey(new string('e', 64)), new AgentId("codex"),
                new ModelProviderId("openai"), new ModelId("unknown"), At.AddDays(5), "UTC",
                new TokenBreakdown(500, 0, 0, 0, 0), CostObservation.Unavailable(), "measurement-test/1",
                CoverageKind.Unpriced, UsageTimePrecision.Interval, At.AddDays(-1));
            await repository.IngestAsync([timestamp, daily, crossing]);
            var query = new UsageReportQuery(path);
            var exact = await query.ReadExactAsync(At.AddHours(-1), At.AddHours(1), includeConfigurations: true);
            Assert.Equal(600, exact.Totals.Tokens.Total);
            Assert.Equal(2, exact.ExcludedTimingRecords);
            UsageElapsedBucket bucket = Assert.Single(exact.ElapsedTwoHourBuckets);
            Assert.Equal(0, bucket.Index); // One hour after reset, not a midnight-aligned bucket.
            Assert.Equal(600, bucket.Metrics.Tokens.Total);
            Assert.True(exact.HasTimingGaps);
            var preciseModel = UsageReportQuery.FilterByModel(exact, new AgentId("codex"),
                new ModelProviderId("openai"), new ModelId("gpt-5.6-sol"));
            Assert.False(preciseModel.HasTimingGaps);
            Assert.Equal(0, preciseModel.ExcludedTimingRecords);
            Assert.Equal(600, Assert.Single(preciseModel.ElapsedTwoHourBuckets).Metrics.Tokens.Total);
            var dailyModel = UsageReportQuery.FilterByModel(exact, new AgentId("codex"),
                new ModelProviderId("azure"), new ModelId("gpt-5.6-sol"));
            Assert.True(dailyModel.HasTimingGaps);
            Assert.Equal(1, dailyModel.ExcludedTimingRecords);
            Assert.Empty(dailyModel.ElapsedTwoHourBuckets);
            var fast = UsageReportQuery.Select(exact, new() { ServiceTiers = ["fast"] });
            Assert.Equal(600, fast.Totals.Tokens.Total);
            Assert.Equal(600, Assert.Single(fast.ElapsedTwoHourBuckets).Metrics.Tokens.Total);
            Assert.Equal(0, fast.ExcludedTimingRecords);
            Assert.False(fast.HasTimingGaps);
            var unknownTier = UsageReportQuery.Select(exact, new() { ServiceTiers = [null] });
            Assert.Equal(0, unknownTier.Totals.Tokens.Total);
            Assert.Empty(unknownTier.ElapsedTwoHourBuckets);
            Assert.Equal(2, unknownTier.ExcludedTimingRecords);
            Assert.True(unknownTier.HasTimingGaps);
            await repository.ApplyRetentionIfDueAsync(At.AddYears(3), TimeSpan.Zero);
            var retained = await query.ReadExactAsync(At.AddHours(-1), At.AddHours(1), includeConfigurations: true);
            Assert.Empty(retained.Models);
            Assert.True(retained.HasTimingGaps);
            var expiredFast = UsageReportQuery.Select(retained, new() { ServiceTiers = ["fast"] });
            Assert.True(expiredFast.HasTimingGaps);
            Assert.Equal(0, expiredFast.ExcludedTimingRecords);
            Assert.Empty(expiredFast.Configurations);
            Assert.True(UsageReportQuery.FilterByModel(retained, new AgentId("codex"),
                new ModelProviderId("openai"), new ModelId("gpt-5.6-sol")).HasTimingGaps);
            Assert.False(UsageReportQuery.FilterByModel(retained, new AgentId("codex"),
                new ModelProviderId("other"), new ModelId("gpt-5.6-sol")).HasTimingGaps);
            Assert.Equal(1_000, (await query.ReadAsync(DateOnly.FromDateTime(At.Date), DateOnly.FromDateTime(At.Date))).Totals.Tokens.Total);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SavedRevisionsRoundTripRemainImmutableAndAreIncludedInUserDeletion()
    {
        string root = Path.Combine(Path.GetTempPath(), "tokenusage-comparison-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repository = await UsageRepository.OpenAsync(Path.Combine(root, "usage.db"));
            var report = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([Event("saved", 600)])) with
            {
                CacheComposition = new(0, 0, 0, 1, 0, 0) { MethodId = "historical-cache/v0" }
            };
            var date = DateOnly.FromDateTime(At.Date);
            var definition = new UsageComparisonDefinition("Periods", "LastCompleteWeek", date, date, date, date,
                "UTC", At, null, null, "Partial local evidence", UsageRepository.ComparisonDataRevision(report, report))
                { BaselineLabel = "A", CurrentLabel = "B", RateBaselineUtc = At.AddDays(-30),
                    MethodId = UsageReferencePricing.FixedCohortMethodId };
            var cycles = Enumerable.Range(0, 4).Select(index => new UsageCycleComparisonEntry(
                "cycle-" + index, "codex", "Codex", ((char)('A' + index)).ToString(), At.AddDays(-index * 7),
                At.AddDays(-index * 7).AddHours(2), report, new("primary", false, null, 600, null, 1, 0))).ToArray();
            var explanation = UsageExplanation.Compare(report, report, definition, UsageExplanationMetric.Tokens)
                with { RuleVersion = "historical-explanation/v0" };
            var saved = new SavedUsageComparison("revision-1", At, definition, report, report)
                { Cycles = cycles, Explanation = explanation };
            await repository.SaveComparisonAsync(saved);
            await repository.IngestAsync([Event("new", 400)]);
            SavedUsageComparison restored = await repository.ReadComparisonAsync("revision-1");
            Assert.Equal(600, restored.Baseline.Totals.Tokens.Total);
            Assert.Equal("historical-cache/v0", restored.Baseline.CacheComposition!.MethodId);
            Assert.Equal(explanation.RuleVersion, restored.Explanation!.RuleVersion);
            Assert.Equal(explanation.Selection, restored.Explanation.Selection);
            Assert.Equal(explanation.Issues, restored.Explanation.Issues);
            Assert.Equal(definition.DataRevision, UsageRepository.ComparisonDataRevision(restored.Baseline, restored.Current));
            Assert.Equal(definition, restored.Definition);
            Assert.Equal(UsageReferencePricing.FixedCohortMethodId, restored.Definition.MethodId);
            Assert.Equal(4, restored.Cycles.Count);
            Assert.Equal(cycles.Select(cycle => (cycle.Id, cycle.ProviderId, cycle.FromUtc, cycle.ToUtc)),
                restored.Cycles.Select(cycle => (cycle.Id, cycle.ProviderId, cycle.FromUtc, cycle.ToUtc)));
            Assert.All(restored.Cycles, cycle => Assert.Equal(600, cycle.Report.Totals.Tokens.Total));
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => repository.SaveComparisonAsync(saved));
            Assert.Single(await repository.ReadComparisonsAsync());
            await repository.DeleteAllUsageDataAsync();
            Assert.Empty(await repository.ReadComparisonsAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TwoToFourDistinctCyclesShareTheShortestSupportedDuration()
    {
        (string Id, TimeSpan Duration)[] cycles =
        [
            ("a", TimeSpan.FromDays(7)), ("b", TimeSpan.FromDays(5)),
            ("c", TimeSpan.FromHours(30)), ("d", TimeSpan.FromHours(10)),
        ];
        Assert.Equal(TimeSpan.FromDays(5), UsageCycleComparisonSet.MatchedDuration(cycles[..2]));
        Assert.Equal(TimeSpan.FromHours(30), UsageCycleComparisonSet.MatchedDuration(cycles[..3]));
        Assert.Equal(TimeSpan.FromHours(10), UsageCycleComparisonSet.MatchedDuration(cycles));
        Assert.Throws<ArgumentException>(() => UsageCycleComparisonSet.MatchedDuration([cycles[0], cycles[0]]));
        Assert.Equal(TimeSpan.Zero, UsageCycleComparisonSet.MatchedDuration([cycles[0], ("empty", TimeSpan.Zero)]));
    }

    [Fact]
    public void FixedCohortRatesCompareIntersectionOnlyAndKeepZeroDistinctFromUnavailable()
    {
        UsageEvent shared = Event("shared", 600);
        UsageReport one = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([shared]));
        UsageRateScenarioComparison identical = UsageReferencePricing.CompareCatalogDates(one, [shared],
            _ => CostObservation.CatalogEstimated(6, "fixed-catalog", "gpt-5.6-sol"),
            _ => CostObservation.CatalogEstimated(6, "fixed-catalog", "gpt-5.6-sol"));
        Assert.Equal(UsageReferencePricing.FixedCohortMethodId, identical.MethodId);
        Assert.Equal(UsageReferencePricing.FixedCohortPolicyId, identical.PricingPolicyId);
        Assert.Equal(1, identical.ComparableEvents);
        Assert.Equal(600, identical.ComparableTokens);
        Assert.Equal(6, identical.CostA);
        Assert.Equal(0, identical.Delta);

        UsageEvent leftOnly = Event("x", 1_000);
        UsageEvent rightOnly = Event("y", 1_000);
        UsageReport disjointOriginal = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([leftOnly, rightOnly]));
        UsageRateScenarioComparison disjoint = UsageReferencePricing.CompareCatalogDates(disjointOriginal, [leftOnly, rightOnly],
            row => row.EventKey == leftOnly.EventKey ? CostObservation.CatalogEstimated(10, "a", "gpt-5.6-sol") : CostObservation.Unavailable(),
            row => row.EventKey == rightOnly.EventKey ? CostObservation.CatalogEstimated(12, "b", "gpt-5.6-sol") : CostObservation.Unavailable());
        Assert.Equal(0, disjoint.ComparableEvents);
        Assert.Equal(2_000, disjoint.Baseline.Totals.Tokens.Total);
        Assert.Null(disjoint.CostA);
        Assert.Null(disjoint.CostB);
        Assert.Null(disjoint.Delta);
        Assert.Null(UsageComparison.KnownCost(disjoint.Baseline.Totals));
        Assert.Null(UsageComparison.KnownCost(disjoint.Current.Totals));

        UsageEvent both = Event("both", 500);
        UsageEvent onlyA = Event("a-only", 400);
        UsageEvent onlyB = Event("b-only", 300);
        UsageEvent daily = Event("daily", 200, UsageTimePrecision.Daily);
        UsageEvent fast = Event("fast", 100, tier: "fast");
        UsageReport mixed = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([both, onlyA, onlyB, daily, fast]));
        UsageRateScenarioComparison partial = UsageReferencePricing.CompareCatalogDates(mixed, [both, onlyA, onlyB, daily, fast],
            row => row.EventKey == both.EventKey || row.EventKey == onlyA.EventKey
                ? CostObservation.CatalogEstimated(5, "a", "gpt-5.6-sol") : CostObservation.Unavailable(),
            row => row.EventKey == both.EventKey || row.EventKey == onlyB.EventKey
                ? CostObservation.CatalogEstimated(8, "b", "gpt-5.6-sol") : CostObservation.Unavailable());
        Assert.Equal(1, partial.ComparableEvents);
        Assert.Equal(500, partial.ComparableTokens);
        Assert.Equal(5, partial.CostA);
        Assert.Equal(8, partial.CostB);
        Assert.Equal(3, partial.Delta);
        Assert.Equal(1_000, partial.Baseline.Totals.UnpricedTokens);
        Assert.Equal(partial.Baseline.Totals.UnpricedTokens, partial.Current.Totals.UnpricedTokens);

        UsageEvent zero = Event("zero", 250);
        UsageEvent missing = Event("missing", 250);
        UsageReport pricedZero = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([zero, missing]));
        UsageRateScenarioComparison zeroPrice = UsageReferencePricing.CompareCatalogDates(pricedZero, [zero, missing],
            row => row.EventKey == zero.EventKey ? CostObservation.CatalogEstimated(0, "zero-catalog", "gpt-5.6-sol") : CostObservation.Unavailable(),
            row => row.EventKey == zero.EventKey ? CostObservation.CatalogEstimated(0, "zero-catalog", "gpt-5.6-sol") : CostObservation.Unavailable());
        Assert.Equal(1, zeroPrice.ComparableEvents);
        Assert.Equal(0m, zeroPrice.CostA);
        Assert.Equal(0m, zeroPrice.CostB);
        Assert.Equal(0, zeroPrice.Delta);
        Assert.False(zeroPrice.CostA is null);
        Assert.Equal(250, zeroPrice.Baseline.Totals.UnpricedTokens);

        UsageRateScenarioComparison unavailable = UsageReferencePricing.CompareCatalogDates(
            UsageReportQuery.Build(UsageRollupAggregator.Aggregate([missing])), [missing],
            _ => CostObservation.Unavailable(), _ => CostObservation.Unavailable());
        Assert.Equal(0, unavailable.ComparableEvents);
        Assert.Null(unavailable.CostA);
        Assert.Null(unavailable.Delta);

        UsageEvent selected = Event("selected-day", 600);
        UsageEvent before = Event("before-day", 600, day: -1);
        UsageEvent after = Event("after-day", 600, day: 1);
        UsageReport oneDay = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([selected]));
        UsageRateScenarioComparison adjacent = UsageReferencePricing.CompareCatalogDates(
            oneDay, [before, selected, after],
            _ => CostObservation.CatalogEstimated(6, "fixed-catalog", "gpt-5.6-sol"),
            _ => CostObservation.CatalogEstimated(6, "fixed-catalog", "gpt-5.6-sol"));
        Assert.Equal(1, adjacent.ComparableEvents);
        Assert.Equal(600, adjacent.ComparableTokens);
        Assert.Equal(6, adjacent.CostA);

        UsageRateScenarioComparison outsideOnly = UsageReferencePricing.CompareCatalogDates(
            UsageReportQuery.Build([]), [before, after],
            _ => CostObservation.CatalogEstimated(6, "fixed-catalog", "gpt-5.6-sol"),
            _ => CostObservation.CatalogEstimated(6, "fixed-catalog", "gpt-5.6-sol"));
        Assert.Equal(0, outsideOnly.ComparableEvents);
        Assert.Equal(0, outsideOnly.ComparableTokens);
        Assert.Null(outsideOnly.CostA);
    }

    [Fact]
    public void FixedCohortRowsFollowMethodIdWithoutClaimingLegacySnapshots()
    {
        Assert.True(UsageComparison.UsesFixedCohortRows(UsageReferencePricing.FixedCohortMethodId));
        Assert.False(UsageComparison.UsesFixedCohortRows(null));
        Assert.False(UsageComparison.UsesFixedCohortRows(""));
        Assert.False(UsageComparison.UsesFixedCohortRows("unknown"));
        Assert.False(UsageComparison.UsesFixedCohortRows("volume-mix-rate/v0"));
    }

    [Fact]
    public async Task FixedCohortQuerySeamReadsOnceAndIgnoresLaterIngest()
    {
        string root = Path.Combine(Path.GetTempPath(), "tokenusage-comparison-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "usage.db");
            var repository = await UsageRepository.OpenAsync(path);
            var query = new UsageReportQuery(path);
            DateOnly date = DateOnly.FromDateTime(At.Date);
            UsageEvent first = Event("first", 600);
            await repository.IngestAsync([first]);
            UsageReportReadSnapshot frozen = await repository.ReadReportSnapshotAsync(
                date, date,
                UsageReportQuery.RetainedObservationWindow(date, date).FromInclusiveUtc,
                UsageReportQuery.RetainedObservationWindow(date, date).ToExclusiveUtc);
            await repository.IngestAsync([Event("later", 400)]);
            UsageRateScenarioComparison held = UsageReferencePricing.CompareCatalogDates(
                UsageReportQuery.Build(frozen.Rollups) with
                {
                    PricingVersions = frozen.PricingVersions,
                    ParserVersions = frozen.ParserVersions,
                    AccountUsage = frozen.AccountUsage,
                    CollectionState = frozen.CollectionState,
                },
                frozen.Events,
                _ => CostObservation.CatalogEstimated(1, "fixed-catalog", "gpt-5.6-sol"),
                _ => CostObservation.CatalogEstimated(2, "fixed-catalog", "gpt-5.6-sol"));
            Assert.Single(frozen.Events);
            Assert.Equal(1, held.ComparableEvents);
            Assert.Equal(600, held.ComparableTokens);
            Assert.Equal(1, held.CostA);
            Assert.Equal(2, held.CostB);

            UsageEvent adjacentBefore = Event("adjacent-before", 600, day: -1);
            UsageEvent selected = Event("selected", 600);
            UsageEvent adjacentAfter = Event("adjacent-after", 600, day: 1);
            string adjacentPath = Path.Combine(root, "adjacent.db");
            var adjacentRepository = await UsageRepository.OpenAsync(adjacentPath);
            await adjacentRepository.IngestAsync([adjacentBefore, selected, adjacentAfter]);
            var adjacentQuery = new UsageReportQuery(adjacentPath);
            UsageRateScenarioComparison adjacent = await adjacentQuery.CompareCatalogDatesAsync(
                date, date, At, At,
                (_, _) => CostObservation.CatalogEstimated(1, "fixed-catalog", "gpt-5.6-sol"));
            Assert.Equal(1, adjacent.ComparableEvents);
            Assert.Equal(600, adjacent.ComparableTokens);
            Assert.Equal(1, adjacent.CostA);
            Assert.Equal(0, adjacent.Delta);

            string outsidePath = Path.Combine(root, "outside.db");
            var outsideRepository = await UsageRepository.OpenAsync(outsidePath);
            await outsideRepository.IngestAsync([adjacentBefore, adjacentAfter]);
            var outsideQuery = new UsageReportQuery(outsidePath);
            UsageRateScenarioComparison outside = await outsideQuery.CompareCatalogDatesAsync(
                date, date, At, At,
                (_, _) => CostObservation.CatalogEstimated(1, "fixed-catalog", "gpt-5.6-sol"));
            Assert.Equal(0, outside.ComparableEvents);
            Assert.Equal(0, outside.ComparableTokens);
            Assert.Null(outside.CostA);
            Assert.Null(outside.Delta);

            string stalePath = Path.Combine(root, "stale.db");
            var staleRepository = await UsageRepository.OpenAsync(stalePath);
            var staleQuery = new UsageReportQuery(stalePath);
            await staleRepository.IngestAsync([Event("stale-first", 600)]);
            await staleQuery.ReadAsync(date, date);
            await staleRepository.IngestAsync([Event("stale-second", 600)]);
            UsageRateScenarioComparison coherent = await staleQuery.CompareCatalogDatesAsync(
                date, date, At, At,
                (_, _) => CostObservation.CatalogEstimated(1, "fixed-catalog", "gpt-5.6-sol"));
            Assert.Equal(2, coherent.ComparableEvents);
            Assert.Equal(1_200, coherent.ComparableTokens);
            Assert.Equal(2, coherent.CostA);
            Assert.False(coherent.CostA is null);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ExplanationsKeepOpposingContributionsAndPrioritizeCoverageAndMethodChanges()
    {
        UsageReport Report(params UsageEvent[] events) => UsageReportQuery.Build(UsageRollupAggregator.Aggregate(events)) with
        {
            ParserVersions = ["measurement-test/1"],
            ActivityCoverage = new(events.Length, events.Sum(row => row.Tokens.Total), 0, 0, 0, 0, 0, 0)
        };
        UsageReport a = Report(Event("a", 100, model: "a"), Event("b", 200, model: "b"), Event("c", 50, model: "c"));
        UsageReport b = Report(Event("d", 200, model: "a"), Event("e", 100, model: "b"), Event("f", 75, model: "c"));
        DateOnly day = DateOnly.FromDateTime(At.UtcDateTime);
        var selection = new UsageComparisonDefinition("Periods", "SelectedPeriod", day, day, day, day,
            "UTC", null, null, null, "synthetic", "synthetic-revision");
        UsageExplanationResult result = UsageExplanation.Compare(a, b, selection, UsageExplanationMetric.Tokens);
        Assert.Empty(result.Issues);
        Assert.Equal(25m, result.TotalChange);
        Assert.Equal(new[] { 100m, -100m }, result.LeadingModels.Select(row => row.Tokens.Absolute!.Value));
        Assert.Equal(25m, result.OtherModelsChange);
        Assert.Equal(selection, result.Selection);
        UsageReport equal = Report(Event("g", 200, model: "a"), Event("h", 100, model: "b"), Event("i", 50, model: "c"));
        Assert.Equal(0m, UsageExplanation.Compare(a, equal, selection, UsageExplanationMetric.Tokens).TotalChange);
        UsageReport changed = b with
        {
            ParserVersions = ["new-parser"],
            ActivityCoverage = new(1, 200, 0, 0, 0, 0, 2, 175),
            Totals = b.Totals with { UnpricedTokens = 0 }
        };
        UsageExplanationResult warning = UsageExplanation.Compare(a, changed, selection, UsageExplanationMetric.Tokens);
        Assert.Equal(UsageExplanationIssue.MethodChanged, warning.Issues[0]);
        Assert.Contains(UsageExplanationIssue.PriceCoverageChanged, warning.Issues);
        Assert.Contains(UsageExplanationIssue.DetailCoverageChanged, warning.Issues);
        Assert.Empty(warning.LeadingModels);
        Assert.Null(warning.TotalChange);
        Assert.Contains(UsageExplanationIssue.UnpricedCost,
            UsageExplanation.Compare(a, b, selection, UsageExplanationMetric.KnownCost).Issues);
        Assert.Contains(UsageExplanationIssue.UnequalPeriods,
            UsageExplanation.Compare(a, b, selection with { CurrentEnd = day.AddDays(1) }, UsageExplanationMetric.Tokens).Issues);
        Assert.Contains(UsageExplanationIssue.UnknownMethod,
            UsageExplanation.Compare(a with { ParserVersions = [] }, b, selection, UsageExplanationMetric.Tokens).Issues);
        Assert.Contains(UsageExplanationIssue.ModelTotalsDoNotReconcile,
            UsageExplanation.Compare(a, b with { ModelDays = [] }, selection, UsageExplanationMetric.Tokens).Issues);
        Assert.Contains(UsageExplanationIssue.ModelTotalsDoNotReconcile,
            UsageExplanation.Compare(a with { ModelDays = [] }, equal with { ModelDays = [] }, selection, UsageExplanationMetric.Tokens).Issues);
    }

    [Fact]
    public async Task OversizedComparisonIsRejectedAndLeavesNoSavedRow()
    {
        string root = Path.Combine(Path.GetTempPath(), "tokenusage-comparison-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repository = await UsageRepository.OpenAsync(Path.Combine(root, "usage.db"));
            var report = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([Event("size", 1)]));
            var date = DateOnly.FromDateTime(At.Date);
            var definition = new UsageComparisonDefinition(
                "Periods",
                "SelectedPeriod",
                date,
                date,
                date,
                date,
                "UTC",
                At,
                null,
                null,
                new string('x', 4 * 1024 * 1024),
                UsageRepository.ComparisonDataRevision(report, report))
            {
                BaselineLabel = "A",
                CurrentLabel = "B",
            };
            var saved = new SavedUsageComparison("too-large", At, definition, report, report);
            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
                () => repository.SaveComparisonAsync(saved));
            Assert.Contains("4 MiB", error.Message, StringComparison.Ordinal);
            Assert.Empty(await repository.ReadComparisonsAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static readonly DateTimeOffset At = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    private static UsageEvent Event(string id, long tokens, UsageTimePrecision precision = UsageTimePrecision.Timestamp,
        string host = "openai", decimal? cost = null, int day = 0, string? tier = null, string model = "gpt-5.6-sol") => new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant()),
            new AgentId("codex"), new ModelProviderId(host), new ModelId(model), At.AddDays(day), "UTC",
            new TokenBreakdown(tokens, 0, 0, 0, 0),
            cost is { } value ? CostObservation.CatalogEstimated(value, "test-catalog", "gpt-5.6-sol") : CostObservation.Unavailable(),
            "measurement-test/1",
            cost is null ? CoverageKind.Unpriced : CoverageKind.Partial, precision, serviceTier: tier);
}
