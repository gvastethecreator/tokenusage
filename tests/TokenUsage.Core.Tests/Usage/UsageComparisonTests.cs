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
    public void RatesCatalogDatesReloadWithoutReferenceOverlayAndKeepZeroSplitsAvailable()
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
        Assert.EndsWith(" UTC", left);
        Assert.EndsWith(" UTC", right);
        Assert.Contains("2026", left, StringComparison.Ordinal);
        Assert.NotEqual(left, right);
        UsageReport baseline = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([Event("a", 1_000, cost: 10)]));
        UsageReport doubled = UsageReportQuery.Build(UsageRollupAggregator.Aggregate(
            [Event("b", 1_000, cost: 10), Event("c", 1_000, cost: 10, day: 1)]));
        UsageCostChangeSplit split = UsageComparison.SplitKnownCost(baseline, doubled);
        Assert.NotNull(split.Volume);
        Assert.NotNull(split.Mix);
        Assert.Equal(0, split.Mix);
        Assert.False(split.Mix is null);
        Assert.NotEqual(split.Volume, split.Mix);
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
    public void KnownCostSplitKeepsVolumeMixAndRateDistinctAndLeavesGapsUnavailable()
    {
        UsageReport doubledVolume = UsageReportQuery.Build(UsageRollupAggregator.Aggregate(
            [Event("a", 1_000, cost: 10), Event("a", 1_000, cost: 10, day: 1)]));
        UsageReport baseline = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([Event("a", 1_000, cost: 10)]));
        UsageCostChangeSplit volume = UsageComparison.SplitKnownCost(baseline, doubledVolume);
        Assert.Equal(10, volume.Volume);
        Assert.Equal(0, volume.Mix);
        Assert.Equal(0, volume.Rate);

        UsageReport doubledRate = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([Event("a", 1_000, cost: 20)]));
        UsageCostChangeSplit rate = UsageComparison.SplitKnownCost(baseline, doubledRate);
        Assert.Equal(0, rate.Volume);
        Assert.Equal(0, rate.Mix);
        Assert.Equal(10, rate.Rate);

        UsageReport unpriced = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([Event("a", 1_000)]));
        UsageCostChangeSplit missing = UsageComparison.SplitKnownCost(baseline, unpriced);
        Assert.Null(missing.Volume);
        Assert.Null(missing.Mix);
        Assert.Null(missing.Rate);
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
            UsageEvent timestamp = Event("precise", 600);
            UsageEvent daily = Event("daily", 400, precision: UsageTimePrecision.Daily);
            var crossing = new UsageEvent(new UsageEventKey(new string('e', 64)), new AgentId("codex"),
                new ModelProviderId("openai"), new ModelId("unknown"), At.AddDays(5), "UTC",
                new TokenBreakdown(500, 0, 0, 0, 0), CostObservation.Unavailable(), "measurement-test/1",
                CoverageKind.Unpriced, UsageTimePrecision.Interval, At.AddDays(-1));
            await repository.IngestAsync([timestamp, daily, crossing]);
            var query = new UsageReportQuery(path);
            var exact = await query.ReadExactAsync(At.AddHours(-1), At.AddHours(1));
            Assert.Equal(600, exact.Totals.Tokens.Total);
            Assert.Equal(2, exact.ExcludedTimingRecords);
            UsageElapsedBucket bucket = Assert.Single(exact.ElapsedTwoHourBuckets);
            Assert.Equal(0, bucket.Index); // One hour after reset, not a midnight-aligned bucket.
            Assert.Equal(600, bucket.Metrics.Tokens.Total);
            Assert.True(exact.HasTimingGaps);
            await repository.ApplyRetentionIfDueAsync(At.AddYears(3), TimeSpan.Zero);
            var retained = await query.ReadExactAsync(At.AddHours(-1), At.AddHours(1));
            Assert.Empty(retained.Models);
            Assert.True(retained.HasTimingGaps);
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
            var report = UsageReportQuery.Build(UsageRollupAggregator.Aggregate([Event("saved", 600)]));
            var date = DateOnly.FromDateTime(At.Date);
            var definition = new UsageComparisonDefinition("Periods", "LastCompleteWeek", date, date, date, date,
                "UTC", At, null, null, "Partial local evidence", UsageRepository.ComparisonDataRevision(report, report))
                { BaselineLabel = "A", CurrentLabel = "B", RateBaselineUtc = At.AddDays(-30) };
            var cycles = Enumerable.Range(0, 4).Select(index => new UsageCycleComparisonEntry(
                "cycle-" + index, "codex", "Codex", ((char)('A' + index)).ToString(), At.AddDays(-index * 7),
                At.AddDays(-index * 7).AddHours(2), report, new("primary", false, null, 600, null, 1, 0))).ToArray();
            var saved = new SavedUsageComparison("revision-1", At, definition, report, report) { Cycles = cycles };
            await repository.SaveComparisonAsync(saved);
            await repository.IngestAsync([Event("new", 400)]);
            SavedUsageComparison restored = await repository.ReadComparisonAsync("revision-1");
            Assert.Equal(600, restored.Baseline.Totals.Tokens.Total);
            Assert.Equal(definition.DataRevision, UsageRepository.ComparisonDataRevision(restored.Baseline, restored.Current));
            Assert.Equal(definition, restored.Definition);
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

    private static readonly DateTimeOffset At = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    private static UsageEvent Event(string id, long tokens, UsageTimePrecision precision = UsageTimePrecision.Timestamp,
        string host = "openai", decimal? cost = null, int day = 0) => new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant()),
            new AgentId("codex"), new ModelProviderId(host), new ModelId("gpt-5.6-sol"), At.AddDays(day), "UTC",
            new TokenBreakdown(tokens, 0, 0, 0, 0),
            cost is { } value ? CostObservation.CatalogEstimated(value, "test-catalog", "gpt-5.6-sol") : CostObservation.Unavailable(),
            "measurement-test/1",
            cost is null ? CoverageKind.Unpriced : CoverageKind.Partial, precision);
}
