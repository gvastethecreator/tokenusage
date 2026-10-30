using System.Security.Cryptography;
using System.Text;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Automation;

public sealed class AutomationQueryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UsageReportQueryGroupsDurableUsageByAgentModelAndDay()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateUsageEvent("codex-reported", CostObservation.ProviderReported(0.25m)),
            CreateUsageEvent(
                "opencode-estimated",
                CostObservation.CatalogEstimated(0.50m, "fixture", "claude-test"),
                agentId: "opencode",
                modelProviderId: "anthropic",
                modelId: "claude-test"),
            CreateUsageEvent(
                "codex-unpriced",
                CostObservation.Unavailable(),
                occurredAtUtc: Now.AddDays(-1)),
        ]);

        var query = new UsageReportQuery(folder.DatabasePath);
        UsageReport report = await query.ReadAsync(
            new DateOnly(2026, 7, 24),
            new DateOnly(2026, 7, 25));
        UsageReport codexOnly = await query.ReadAsync(
            new DateOnly(2026, 7, 24),
            new DateOnly(2026, 7, 25),
            new AgentId("codex"));

        Assert.Equal(3, report.Totals.EventCount);
        Assert.Equal(450, report.Totals.Tokens.Total);
        Assert.Equal(0.25m, report.Totals.ReportedCostUsd);
        Assert.Equal(0.50m, report.Totals.EstimatedCostUsd);
        Assert.Equal(150, report.Totals.UnpricedTokens);
        Assert.Equal(66.7m, report.Totals.PriceCoveragePercent);
        Assert.Equal(["opencode", "codex"], report.Agents.Select(item => item.AgentId.Value));
        Assert.Equal(2, report.Models.Count);
        Assert.Equal(
            [new DateOnly(2026, 7, 24), new DateOnly(2026, 7, 25)],
            report.Days.Select(item => item.Date));
        Assert.Equal(
            [
                "2026-07-24:codex",
                "2026-07-25:codex",
                "2026-07-25:opencode",
            ],
            report.AgentDays.Select(item => $"{item.Date:yyyy-MM-dd}:{item.AgentId.Value}"));
        Assert.Equal(2, codexOnly.Totals.EventCount);
        Assert.Single(codexOnly.Agents);
        Assert.Equal("codex", codexOnly.Agents[0].AgentId.Value);
        Assert.Equal(2, codexOnly.AgentDays.Count);
        Assert.Equal(3, report.ModelDays.Count);
        Assert.Equal(report.Totals.Tokens.Total, report.ModelDays.Sum(day => day.Metrics.Tokens.Total));
    }

    [Fact]
    public async Task ModelProfilesUseTheReportRangeAndKeepHostIdentity()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync([
            CreateUsageEvent("profile-openai", CostObservation.ProviderReported(1m)),
            CreateUsageEvent("profile-azure", CostObservation.ProviderReported(2m), modelProviderId: "azure"),
            CreateUsageEvent("profile-unknown", CostObservation.Unavailable(), modelProviderId: null),
            CreateUsageEvent("profile-outside", CostObservation.ProviderReported(9m), occurredAtUtc: Now.AddDays(-1)),
        ]);
        var query = new UsageReportQuery(folder.DatabasePath);
        DateOnly date = DateOnly.FromDateTime(Now.UtcDateTime);
        UsageReport report = await query.ReadAsync(date, date, includeTimeBuckets: true, includeModelProfiles: true);
        Assert.Equal(3, report.ModelProfiles.Count);
        Assert.Equal(report.Totals.Tokens.Total, report.ModelProfiles.Sum(row => row.Tokens));
        Assert.Equal(report.Totals.Tokens.Total, report.TimeBuckets.Sum(row => row.Usage.Tokens.Total));
        Assert.Empty((await query.ReadAsync(date, date)).ModelProfiles);
    }

    [Fact]
    public async Task UsageReportQueryUsesExactBoundariesForResetCycles()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        DateTimeOffset resetAt = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        await repository.IngestAsync(
        [
            CreateUsageEvent(
                "before-reset",
                CostObservation.ProviderReported(0.10m),
                occurredAtUtc: resetAt.AddMinutes(-1)),
            CreateUsageEvent(
                "at-reset",
                CostObservation.ProviderReported(0.20m),
                occurredAtUtc: resetAt),
            CreateUsageEvent(
                "after-reset",
                CostObservation.ProviderReported(0.30m),
                occurredAtUtc: resetAt.AddMinutes(1)),
        ]);

        UsageReport report = await new UsageReportQuery(folder.DatabasePath).ReadExactAsync(
            resetAt,
            resetAt.AddDays(1),
            new AgentId("codex"));

        Assert.Equal(2, report.Totals.EventCount);
        Assert.Equal(300, report.Totals.Tokens.Total);
        Assert.Equal(0.50m, report.Totals.ReportedCostUsd);
        Assert.Equal(2, Assert.Single(report.ModelDays).Metrics.EventCount);
    }

    [Fact]
    public void TinyUnpricedShareDoesNotReportFullPriceCoverage()
    {
        var metrics = new UsageReportMetrics(
            2,
            new TokenBreakdown(1_000_000, 0, 0, 0, 0),
            1m,
            null,
            UnpricedTokens: 1,
            UnavailableCostEventCount: 1,
            CoverageKind.Partial);

        Assert.Equal(99.9m, metrics.PriceCoveragePercent);
    }

    [Fact]
    public async Task LimitsQueryReturnsOnlyTheSelectedCachedProvider()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        ProviderSnapshot first = CreateSnapshot("first", 10m);
        ProviderSnapshot selected = CreateSnapshot("selected", 20m);
        var firstStore = new SnapshotStore(Path.Combine(folder.Root, "first.json"), clock);
        var selectedStore = new SnapshotStore(Path.Combine(folder.Root, "selected.json"), clock);
        await firstStore.UpsertLastGoodAsync(first);
        await selectedStore.UpsertLastGoodAsync(selected);
        var host = new ProviderRefreshHost(
        [
            new ProviderRefreshRegistration(new StubProvider(first), firstStore),
            new ProviderRefreshRegistration(new StubProvider(selected), selectedStore),
        ], clock);
        var query = new LimitsQuery(host);

        IReadOnlyList<ProviderSnapshot> result = await query.ReadAsync(
            selected.ProviderId,
            forceRefresh: false);
        IReadOnlyList<ProviderSnapshot> missing = await query.ReadAsync(
            new ProviderId("missing"),
            forceRefresh: false);

        ProviderSnapshot loaded = Assert.Single(result);
        Assert.Equal(selected.ProviderId, loaded.ProviderId);
        Assert.Equal(selected.DisplayName, loaded.DisplayName);
        Assert.Empty(missing);
    }

    [Fact]
    public async Task ActivitySeparatesIntervalsAndNonPlaceableUsageAtHalfOpenBoundaries()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        DateTimeOffset from = Now.AddHours(-1), to = Now;
        await repository.IngestAsync([
            CreateUsageEvent("activity-start", CostObservation.Unavailable(), occurredAtUtc: from, effort: "low"),
            CreateUsageEvent("activity-end", CostObservation.Unavailable(), occurredAtUtc: to),
            CreateUsageEvent("activity-interval", CostObservation.Unavailable(), occurredAtUtc: from.AddMinutes(20),
                precision: UsageTimePrecision.Interval, intervalStart: from, kind: UsageRecordKind.IntervalDelta),
            CreateUsageEvent("activity-crossing", CostObservation.Unavailable(), occurredAtUtc: from.AddMinutes(5),
                precision: UsageTimePrecision.Interval, intervalStart: from.AddMinutes(-10), kind: UsageRecordKind.IntervalDelta),
            CreateUsageEvent("activity-daily", CostObservation.Unavailable(), occurredAtUtc: from.AddMinutes(30),
                precision: UsageTimePrecision.Daily, kind: UsageRecordKind.DailyAggregate),
            CreateUsageEvent("activity-snapshot", CostObservation.Unavailable(), occurredAtUtc: from.AddMinutes(40),
                kind: UsageRecordKind.Snapshot),
        ]);
        var query = new UsageReportQuery(folder.DatabasePath);
        DateOnly day = DateOnly.FromDateTime(Now.UtcDateTime);
        UsageReport daily = await query.ReadAsync(day, day, includeTimeBuckets: true, includeConfigurations: true);
        Assert.Equal(900, daily.Totals.Tokens.Total);
        Assert.Equal(new UsageActivityCoverage(2, 300, 2, 300, 2, 300, 0, 0), daily.ActivityCoverage);
        Assert.Equal(300, daily.ActivityTimeBuckets.Sum(row => row.Tokens));
        Assert.Equal(300, daily.TimeBuckets.Sum(row => row.Usage.Tokens.Total));
        UsageReport exact = await query.ReadExactAsync(from, to, includeConfigurations: true);
        Assert.Equal(300, exact.Totals.Tokens.Total);
        Assert.Equal(300, await repository.SumTokensAsync(from, to, new AgentId("codex")));
        Assert.Equal(new UsageActivityCoverage(1, 150, 1, 150, 0, 0, 0, 0), exact.ActivityCoverage);
        Assert.Equal(3, exact.ExcludedTimingRecords);
        Assert.Equal(150, exact.ActivityTimeBuckets.Sum(row => row.Tokens));
    }

    [Theory]
    [InlineData("2026-11-01T05:30:00Z", "2026-11-01T06:30:00Z", 0, 0)]
    [InlineData("2026-03-08T06:30:00Z", "2026-03-08T07:30:00Z", 0, 2)]
    public async Task ActivityClockWindowsConserveUsageAcrossDaylightSavingChanges(
        string firstTime, string secondTime, int firstHour, int secondHour)
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        DateTimeOffset first = DateTimeOffset.Parse(firstTime, System.Globalization.CultureInfo.InvariantCulture);
        DateTimeOffset second = DateTimeOffset.Parse(secondTime, System.Globalization.CultureInfo.InvariantCulture);
        const string zone = "America/New_York";
        await repository.IngestAsync([
            CreateUsageEvent("clock-first", CostObservation.Unavailable(), occurredAtUtc: first, zone: zone),
            CreateUsageEvent("clock-second", CostObservation.Unavailable(), occurredAtUtc: second, zone: zone),
            CreateUsageEvent("clock-prior-day", CostObservation.Unavailable(),
                occurredAtUtc: new DateTimeOffset(first.UtcDateTime.Date.AddHours(1), TimeSpan.Zero), zone: zone)
        ]);
        var query = new UsageReportQuery(folder.DatabasePath);
        DateOnly day = DateOnly.FromDateTime(first.UtcDateTime);
        UsageReport daily = await query.ReadAsync(day, day, includeConfigurations: true);
        Assert.Equal(300, daily.Totals.Tokens.Total);
        Assert.Equal(new UsageActivityCoverage(2, 300, 0, 0, 0, 0, 0, 0), daily.ActivityCoverage);
        Assert.Equal(new[] { firstHour, secondHour }.Distinct().Order(),
            daily.ActivityTimeBuckets.Select(row => row.Hour).Distinct().Order());
        Assert.All(daily.ActivityTimeBuckets, row =>
        {
            Assert.Equal(day, row.Date);
            Assert.Equal(zone, row.TimeZoneId);
        });
        Assert.Equal(300, daily.ActivityTimeBuckets.Sum(row => row.Tokens));
        UsageReport exact = await query.ReadExactAsync(first, second, includeConfigurations: true);
        Assert.Equal(150, exact.Totals.Tokens.Total);
        Assert.Equal(150, exact.ActivityTimeBuckets.Sum(row => row.Tokens));
        UsageReport previous = await query.ReadAsync(day.AddDays(-1), day.AddDays(-1), includeConfigurations: true);
        Assert.Equal(150, previous.Totals.Tokens.Total);
        Assert.Equal(20, Assert.Single(previous.ActivityTimeBuckets).Hour);
    }

    private static UsageEvent CreateUsageEvent(
        string key,
        CostObservation cost,
        string agentId = "codex",
        string? modelProviderId = "openai",
        string modelId = "gpt-test",
        DateTimeOffset? occurredAtUtc = null,
        string? observedModel = null,
        string? effort = null,
        string? tier = null, UsageTimePrecision precision = UsageTimePrecision.Timestamp,
        DateTimeOffset? intervalStart = null, UsageRecordKind kind = UsageRecordKind.Unknown, string zone = "UTC",
        TokenBreakdown? tokens = null, UsageDetailMetadata? detail = null) =>
        new(
            new UsageEventKey(Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(key)))),
            new AgentId(agentId),
            modelProviderId is null ? null : new ModelProviderId(modelProviderId),
            new ModelId(modelId),
            occurredAtUtc ?? Now,
            zone,
            tokens ?? new TokenBreakdown(100, 25, 5, 20, 0),
            cost,
            "fixture/1",
            cost.Kind == CostKind.Unavailable
                ? CoverageKind.Unpriced
                : CoverageKind.Complete,
            precision, intervalStart,
            observedModelId: observedModel is null ? null : new ModelId(observedModel),
            reasoningEffort: effort,
            serviceTier: tier, detailMetadata: detail ?? new UsageDetailMetadata(recordKind: kind));

    private static ProviderSnapshot CreateSnapshot(string providerId, decimal used) =>
        new(
            new ProviderId(providerId),
            "Provider " + providerId,
            "Sample",
            Now,
            Now.AddSeconds(-30),
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId("session"),
                    used,
                    100m,
                    Now.AddHours(4),
                    new DataProvenance(
                        SourceKind.Synthetic,
                        MeasurementKind.ProviderReported,
                        "fixture/1")),
            ],
            CoverageKind.Complete,
            1);

    private sealed class StubProvider(ProviderSnapshot snapshot) : IProviderRuntime
    {
        public ProviderDescriptor Descriptor { get; } =
            new(snapshot.ProviderId, snapshot.DisplayName);

        public Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult<ProviderOutcome>(new ProviderOutcome.Success(snapshot));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "wou-query-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "usage.db");
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
