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
    public async Task UsageQueryReadsAndSummarizesTheSharedRepository()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateUsageEvent("reported", CostObservation.ProviderReported(0.25m)),
            CreateUsageEvent("unpriced", CostObservation.Unavailable()),
        ]);

        UsageSummary result = await new UsageQuery(folder.DatabasePath).ReadAsync(
            new DateOnly(2026, 7, 25),
            new DateOnly(2026, 7, 25));

        Assert.Equal(2, result.EventCount);
        Assert.Equal(300, result.TotalTokens);
        Assert.Equal(0.25m, result.ReportedCostUsd);
        Assert.Null(result.EstimatedCostUsd);
        Assert.Equal(150, result.UnpricedTokens);
    }

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
        (DateOnly From, DateOnly To)? availableRange = await query.ReadAvailableDateRangeAsync(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 25));

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
        Assert.Equal(
            (new DateOnly(2026, 7, 24), new DateOnly(2026, 7, 25)),
            availableRange);

        UsageReport filtered = UsageReportQuery.FilterByAgent(report, new AgentId("codex"));
        Assert.Equal(codexOnly.Totals, filtered.Totals);
        Assert.Equal(codexOnly.Agents, filtered.Agents);
        Assert.Equal(codexOnly.Models, filtered.Models);
        Assert.Equal(codexOnly.Days, filtered.Days);
        Assert.Equal(codexOnly.AgentDays, filtered.AgentDays);
        Assert.Equal(codexOnly.ModelDays, filtered.ModelDays);
        Assert.Equal(3, report.ModelDays.Count);
        Assert.Equal(report.Totals.Tokens.Total, report.ModelDays.Sum(day => day.Metrics.Tokens.Total));
    }

    [Fact]
    public async Task ModelSelectionSeparatesHostsAndKeepsOneAnalyticalPopulation()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateUsageEvent("direct", CostObservation.ProviderReported(0m)),
            CreateUsageEvent("hosted", CostObservation.CatalogEstimated(0.5m, "catalog", "gpt-test"), modelProviderId: "azure"),
            CreateUsageEvent("unknown", CostObservation.Unavailable(), modelProviderId: null),
            CreateUsageEvent("other-tool", CostObservation.ProviderReported(1m), agentId: "opencode"),
        ]);
        var query = new UsageReportQuery(folder.DatabasePath);
        var day = new DateOnly(2026, 7, 25);
        UsageReport report = await query.ReadAsync(day, day, includeTimeBuckets: true);
        UsageReport dailyOnly = await query.ReadAsync(day, day);
        Assert.False(dailyOnly.HasTimeBucketDetails);
        Assert.Empty(dailyOnly.TimeBuckets);
        Assert.True(report.HasTimeBucketDetails);
        Assert.Equal(report.Totals, dailyOnly.Totals);
        Assert.Equal(report.DataRevision, dailyOnly.DataRevision);
        UsageReport emptyLoaded = await query.ReadAsync(day.AddDays(1), day.AddDays(1), includeTimeBuckets: true);
        Assert.True(emptyLoaded.HasTimeBucketDetails);
        Assert.Empty(emptyLoaded.TimeBuckets);
        Assert.Equal(4, report.Models.Count);
        Assert.Equal(600, report.TimeBuckets.Sum(row => row.Usage.Tokens.Total));

        UsageReport selected = UsageReportQuery.FilterByModel(report, new AgentId("codex"),
            new ModelProviderId("openai"), new ModelId("gpt-test"));
        Assert.Equal(150, selected.Totals.Tokens.Total);
        Assert.Equal(0m, selected.Totals.ReportedCostUsd);
        Assert.Null(selected.Totals.EstimatedCostUsd);
        Assert.Equal(600, selected.PopulationTokens);
        Assert.Equal(150, Assert.Single(selected.Models).Metrics.Tokens.Total);
        Assert.Equal(150, Assert.Single(selected.Days).Metrics.Tokens.Total);
        Assert.Equal(150, Assert.Single(selected.TimeBuckets).Usage.Tokens.Total);
        Assert.True(selected.HasTimeBucketDetails);

        UsageReport unknown = UsageReportQuery.Select(report, new UsageReportSelection { ModelProviders = [null] });
        Assert.Null(Assert.Single(unknown.Models).ModelProviderId);
        Assert.Null(unknown.Totals.ReportedCostUsd);
        Assert.Equal(150, unknown.Totals.UnpricedTokens);
        UsageReport hosted = UsageReportQuery.Select(report, new UsageReportSelection { Search = " AZURE " });
        Assert.Equal(150, hosted.Totals.Tokens.Total);
        Assert.Equal(0.5m, hosted.Totals.EstimatedCostUsd);
        UsageReport bothHosts = UsageReportQuery.Select(report, new UsageReportSelection
        {
            Agents = [new AgentId("codex")],
            ModelProviders = [new ModelProviderId("openai"), new ModelProviderId("azure")],
        });
        Assert.Equal(300, bothHosts.Totals.Tokens.Total);
        Assert.Equal(2, bothHosts.Models.Count);
        Assert.Equal(report.Totals, UsageReportQuery.Select(report, new UsageReportSelection()).Totals);
        Assert.Empty(UsageReportQuery.Select(report, new UsageReportSelection { Search = "missing-model" }).Models);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TopModelsConservesTheWholePopulationAndDailyTimeBuckets(bool byCost)
    {
        var day = new DateOnly(2026, 9, 1);
        var agent = new AgentId("codex");
        DailyUsageRollup[] rows = Enumerable.Range(0, 30).Select(i =>
        {
            long tokens = (29 - i) * 10;
            bool unpriced = i % 7 == 0;
            return new DailyUsageRollup(day.AddDays(i % 2), "UTC", agent,
                i == 0 ? null : new ModelProviderId(i % 2 == 0 ? "azure" : "openai"),
                new ModelId($"model-{i / 2:00}"), new TokenBreakdown(tokens, 0, 0, 0, 0),
                unpriced || i % 3 == 0 ? null : i,
                !unpriced && i % 3 == 0 ? i / 10m : null,
                unpriced ? tokens : 0, unpriced ? 1 : 0, 1, unpriced ? CoverageKind.Unpriced : CoverageKind.Complete);
        }).ToArray();
        UsageReport source = UsageReportQuery.Build(rows) with
        {
            TimeBuckets = rows.Select(row => new UsageTimeRollup(row, 12)).ToArray(),
        };
        IReadOnlyList<UsageModelChartGroup> groups = UsageReportQuery.TopModels(source, agent, 5, byCost);
        Assert.Equal(6, groups.Count);
        Assert.Equal(25, groups[^1].ModelCount);
        Assert.True(groups[^1].IsOther);
        Assert.Equal(30, groups.Sum(group => group.ModelCount));
        Assert.Equal(source.Totals.Tokens.Total, groups.Sum(group => group.Report.Totals.Tokens.Total));
        Assert.Equal(source.Totals.ReportedCostUsd, groups.Sum(group => group.Report.Totals.ReportedCostUsd));
        Assert.Equal(source.Totals.EstimatedCostUsd, groups.Sum(group => group.Report.Totals.EstimatedCostUsd));
        Assert.Equal(source.Totals.UnpricedTokens, groups.Sum(group => group.Report.Totals.UnpricedTokens));
        Assert.Equal(source.Totals.UnavailableCostEventCount, groups.Sum(group => group.Report.Totals.UnavailableCostEventCount));
        Assert.Equal(source.Totals.EventCount, groups.Sum(group => group.Report.Totals.EventCount));
        foreach (UsageDayReport expected in source.Days)
        {
            Assert.Equal(expected.Metrics.Tokens.Total, groups.SelectMany(group => group.Report.Days)
                .Where(row => row.Date == expected.Date).Sum(row => row.Metrics.Tokens.Total));
            Assert.Equal(expected.Metrics.Tokens.Total, groups.SelectMany(group => group.Report.TimeBuckets)
                .Where(row => row.Usage.Date == expected.Date).Sum(row => row.Usage.Tokens.Total));
        }
        Assert.Equal(groups.Select(group => group.Model),
            UsageReportQuery.TopModels(UsageReportQuery.Build(rows.Reverse()), agent, 5, byCost).Select(group => group.Model));
        Assert.Single(groups.SelectMany(group => group.Report.Models), model => model.ModelProviderId is null);
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
        UsageReport selected = UsageReportQuery.FilterByModel(report, new AgentId("codex"),
            new ModelProviderId("azure"), new ModelId("gpt-test"));
        Assert.Equal("azure", Assert.Single(selected.ModelProfiles).ModelProviderId?.Value);
        Assert.Equal(selected.Totals.Tokens.Total, selected.ModelProfiles.Sum(row => row.Tokens));
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
    public void UsageReportQuerySubtractsComparableTotals()
    {
        var current = new UsageReportMetrics(
            4,
            new TokenBreakdown(200, 40, 0, 10, 0),
            2m,
            1m,
            UnpricedTokens: 20,
            UnavailableCostEventCount: 1,
            CoverageKind.Partial);
        var baseline = new UsageReportMetrics(
            2,
            new TokenBreakdown(100, 10, 0, 0, 0),
            1m,
            null,
            UnpricedTokens: 5,
            UnavailableCostEventCount: 0,
            CoverageKind.Complete);

        UsageReportMetricDelta delta = UsageReportQuery.Subtract(current, baseline);

        Assert.Equal(2, delta.EventCount);
        Assert.Equal(140, delta.Tokens);
        Assert.Equal(2m, delta.TotalCostUsd);
        Assert.Equal(1m, delta.ReportedCostUsd);
        Assert.Equal(1m, delta.EstimatedCostUsd);
        Assert.Equal(15, delta.UnpricedTokens);
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
    public async Task ConfigurationFiltersUseRetainedEvidenceAndKeepExpiredDetailSeparateFromUnknown()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateUsageEvent("expired-config", CostObservation.ProviderReported(9m), occurredAtUtc: Now.AddDays(-3)),
            CreateUsageEvent("unknown-config", CostObservation.ProviderReported(0m)),
            CreateUsageEvent("low-fast", CostObservation.ProviderReported(2m), observedModel: "observed-a", effort: "low", tier: "fast"),
            CreateUsageEvent("high-fast", CostObservation.Unavailable(), observedModel: "observed-b", effort: "high", tier: "fast"),
            CreateUsageEvent("low-standard", CostObservation.ProviderReported(4m), observedModel: "observed-a", effort: "low", tier: "standard"),
        ]);
        Assert.Equal(1, await repository.ApplyRetentionAsync(Now, retentionDays: 2));
        var query = new UsageReportQuery(folder.DatabasePath);
        DateOnly to = DateOnly.FromDateTime(Now.UtcDateTime);
        UsageReport source = await query.ReadAsync(to.AddDays(-3), to, includeConfigurations: true);
        Assert.Equal(750, source.Totals.Tokens.Total);
        Assert.Equal(new UsageConfigurationCoverage(5, 4, 1, to, to), source.ConfigurationCoverage);
        Assert.Equal(new UsageActivityCoverage(4, 600, 0, 0, 0, 0, 1, 150), source.ActivityCoverage);
        UsageReport unknown = UsageReportQuery.Select(source, new() { ReasoningEfforts = [null] });
        Assert.Equal(150, unknown.Totals.Tokens.Total);
        Assert.Equal(0m, unknown.Totals.ReportedCostUsd);
        Assert.Null(Assert.Single(unknown.Configurations).Effort);
        Assert.Equal(source.ConfigurationCoverage, unknown.ConfigurationCoverage);
        UsageReport fast = UsageReportQuery.Select(source, new()
        {
            ReasoningEfforts = ["low", "high"], ServiceTiers = ["fast"],
            ObservedModels = [new ModelId("observed-a"), new ModelId("observed-b")],
        });
        Assert.Equal(300, fast.Totals.Tokens.Total);
        Assert.Equal(new UsageActivityCoverage(2, 300, 0, 0, 0, 0, 0, 0), fast.ActivityCoverage);
        Assert.Equal(300, fast.ActivityTimeBuckets.Sum(row => row.Tokens));
        Assert.Equal(2m, fast.Totals.ReportedCostUsd);
        Assert.Equal(150, fast.Totals.UnpricedTokens);
        Assert.Equal(300, fast.TimeBuckets.Sum(row => row.Usage.Tokens.Total));
        Assert.Equal(300, fast.ModelDays.Sum(row => row.Metrics.Tokens.Total));
        Assert.Equal(300, fast.Configurations.Sum(row => row.Metrics.Tokens.Total));
        Assert.False(fast.HasTimingGaps);
        Assert.Equal(source.Totals, UsageReportQuery.Select(source, new()).Totals);
        UsageReport repricedUnknown = await query.RepriceAtAsync(unknown, to.AddDays(-3), to, Now, (row, _) =>
        {
            Assert.Null(row.ReasoningEffort);
            return CostObservation.CatalogEstimated(3m, "configuration-test", "gpt-test");
        });
        Assert.Equal(150, repricedUnknown.Totals.Tokens.Total);
        Assert.Equal(unknown.ActivityCoverage, repricedUnknown.ActivityCoverage);
        Assert.Equal(unknown.ActivityTimeBuckets, repricedUnknown.ActivityTimeBuckets);
        Assert.Equal(3m, repricedUnknown.Totals.EstimatedCostUsd);
        Assert.Equal(0, repricedUnknown.Totals.UnpricedTokens);
        UsageRateScenarioComparison rates = await query.CompareCatalogDatesAsync(to.AddDays(-3), to,
            Now.AddDays(-1), Now, (row, at) =>
            {
                Assert.Equal("low", row.ReasoningEffort);
                Assert.Equal("standard", row.ServiceTier);
                return CostObservation.CatalogEstimated(at == Now ? 5m : 2m, "configuration-test", "gpt-test");
            }, selection: new() { ReasoningEfforts = ["low"] });
        Assert.Equal(300, rates.Baseline.Totals.Tokens.Total);
        Assert.Equal(150, rates.ComparableTokens);
        Assert.Equal(150, rates.Baseline.Totals.UnpricedTokens);
        Assert.Equal(2m, rates.Baseline.Totals.EstimatedCostUsd);
        Assert.Equal(5m, rates.Current.Totals.EstimatedCostUsd);
        Assert.Equal(rates.Current.Totals.Tokens.Total, rates.Current.Configurations.Sum(row => row.Metrics.Tokens.Total));
        Assert.Equal(5m, rates.Current.Configurations.Sum(row => row.Metrics.EstimatedCostUsd));
        Assert.Equal(150, rates.Current.Configurations.Sum(row => row.Metrics.UnpricedTokens));
        var definition = new UsageComparisonDefinition("Rates", "", to, to, to, to,
            "UTC", Now, null, null, "configuration filter", "fixture");
        await repository.SaveComparisonAsync(new("configuration-revision", Now, definition, rates.Baseline, rates.Current));
        SavedUsageComparison saved = await repository.ReadComparisonAsync("configuration-revision");
        Assert.Equal("low", Assert.Single(saved.Baseline.ConfigurationSelection!.ReasoningEfforts));
        Assert.Equal(rates.Baseline.ConfigurationCoverage, saved.Baseline.ConfigurationCoverage);
        Assert.Equal(rates.Current.Totals, saved.Current.Totals);
        UsageReport withoutDetail = await query.ReadAsync(to.AddDays(-3), to);
        Assert.Throws<InvalidOperationException>(() => UsageReportQuery.Select(withoutDetail,
            new() { ServiceTiers = [null] }));
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
        UsageReport low = UsageReportQuery.Select(daily, new() { ReasoningEfforts = ["low"] });
        Assert.Equal(new UsageActivityCoverage(1, 150, 0, 0, 0, 0, 0, 0), low.ActivityCoverage);
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

    [Fact]
    public async Task CacheShareUsesMeasuredWeightedInputAndKeepsExclusionsWithSelection()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var measured = new UsageDetailMetadata(input: UsageComponentAvailability.Measured,
            cacheRead: UsageComponentAvailability.Measured, cacheWrite: UsageComponentAvailability.Measured);
        var unavailable = new UsageDetailMetadata(cacheRead: UsageComponentAvailability.Unavailable);
        await repository.IngestAsync([
            CreateUsageEvent("cache-large", CostObservation.Unavailable(), effort: "high", tokens: new(100, 0, 0, 900, 0), detail: measured),
            CreateUsageEvent("cache-small", CostObservation.Unavailable(), effort: "low", tokens: new(10, 0, 0, 0, 0), detail: measured),
            CreateUsageEvent("cache-zero", CostObservation.Unavailable(), effort: "none", tokens: new(0, 0, 0, 0, 0), detail: measured),
            CreateUsageEvent("cache-unknown", CostObservation.Unavailable()),
            CreateUsageEvent("cache-unavailable", CostObservation.Unavailable(), detail: unavailable),
            CreateUsageEvent("cache-expired", CostObservation.Unavailable(), occurredAtUtc: Now.AddDays(-3))
        ]);
        await repository.ApplyRetentionAsync(Now, retentionDays: 2);
        var query = new UsageReportQuery(folder.DatabasePath);
        DateOnly day = DateOnly.FromDateTime(Now.UtcDateTime);
        UsageReport report = await query.ReadAsync(day.AddDays(-3), day, includeConfigurations: true);
        Assert.Equal(new UsageCacheComposition(900, 1010, 3, 1, 1, 1), report.CacheComposition);
        Assert.Equal(900m / 1010m * 100m, report.CacheComposition!.CacheReadPercent);
        UsageReport small = UsageReportQuery.Select(report, new() { ReasoningEfforts = ["low"] });
        Assert.Equal(new UsageCacheComposition(0, 10, 1, 0, 0, 0), small.CacheComposition);
        Assert.Equal(0m, small.CacheComposition!.CacheReadPercent);
        UsageReport zero = UsageReportQuery.Select(report, new() { ReasoningEfforts = ["none"] });
        Assert.Equal(1, zero.CacheComposition!.EligibleRecords);
        Assert.Null(zero.CacheComposition.CacheReadPercent);
        UsageReport unknown = UsageReportQuery.Select(report, new() { ReasoningEfforts = [null] });
        Assert.Equal(new UsageCacheComposition(0, 0, 0, 1, 1, 0), unknown.CacheComposition);
        UsageReport repriced = await query.RepriceAtAsync(report, day.AddDays(-3), day, Now,
            (row, _) => CostObservation.CatalogEstimated(1m, "cache-test", row.ModelId.Value));
        Assert.Equal(report.CacheComposition, repriced.CacheComposition);
        UsageReport exact = await query.ReadExactAsync(Now, Now.AddMinutes(1), includeConfigurations: true);
        Assert.Equal(new UsageCacheComposition(900, 1010, 3, 1, 1, 0), exact.CacheComposition);
        Assert.Null((await query.ReadAsync(day, day)).CacheComposition);
    }

    [Fact]
    public async Task LinearPriceQueryKeepsRetiredDetailOutsideFilteredScenarioAndSavesExactResult()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var measured = new UsageDetailMetadata(representationRevision: 1, input: UsageComponentAvailability.Measured,
            output: UsageComponentAvailability.Measured, reasoning: UsageComponentAvailability.Measured,
            cacheRead: UsageComponentAvailability.Measured, cacheWrite: UsageComponentAvailability.Measured);
        await repository.IngestAsync([
            CreateUsageEvent("linear-a", CostObservation.Unavailable(), occurredAtUtc: Now.AddDays(-1),
                tier: "standard", tokens: new(100, 0, 0, 0, 0), detail: measured),
            CreateUsageEvent("linear-b", CostObservation.Unavailable(), tier: "standard", tokens: new(200, 0, 0, 0, 0), detail: measured),
            CreateUsageEvent("linear-interval-a", CostObservation.Unavailable(), occurredAtUtc: Now.AddDays(-1),
                tier: "standard", tokens: new(50, 0, 0, 0, 0), detail: measured,
                precision: UsageTimePrecision.Interval, intervalStart: Now.AddDays(-1).AddHours(-1)),
            CreateUsageEvent("linear-crossing-b", CostObservation.Unavailable(), tier: "standard", tokens: new(100, 0, 0, 0, 0), detail: measured,
                precision: UsageTimePrecision.Interval, intervalStart: Now.AddDays(-1)),
            CreateUsageEvent("linear-unknown-time", CostObservation.Unavailable(), tier: "standard", tokens: new(25, 0, 0, 0, 0), detail: measured,
                precision: UsageTimePrecision.Unknown),
            CreateUsageEvent("linear-other", CostObservation.Unavailable(), modelId: "other-model", tokens: new(1000, 0, 0, 0, 0)),
            CreateUsageEvent("linear-retired", CostObservation.Unavailable(), occurredAtUtc: Now.AddDays(-3))
        ]);
        await repository.ApplyRetentionAsync(Now, retentionDays: 2);
        var day = DateOnly.FromDateTime(Now.UtcDateTime);
        var selection = new UsageReportSelection { Models = [new("gpt-test")], ServiceTiers = ["standard"] };
        UsageHistoricalPriceScenario scenario = await new UsageReportQuery(folder.DatabasePath).CompareLinearPricesAsync(
            day.AddDays(-3), day.AddDays(-1), day, day, Now.AddDays(-1), Now,
            (_, at) => new(new("catalog", "test", "linear", at == Now ? 2.123456m : 1.123456m, 2, 2, 0, 1), null),
            selection, selection);
        Assert.Equal(scenario.Baseline.DataRevision, scenario.Current.DataRevision);
        Assert.Equal(150, scenario.Result.EligibleTokensA);
        Assert.Equal(200, scenario.Result.EligibleTokensB);
        Assert.Equal(150, scenario.Baseline.Totals.Tokens.Total);
        Assert.Equal(new UsagePriceExcluded(UsagePriceExclusion.MissingDetail, 150, 0, 1, 0),
            Assert.Single(scenario.Result.Exclusions, row => row.Reason == UsagePriceExclusion.MissingDetail));
        Assert.Equal(new UsagePriceExcluded(UsagePriceExclusion.UnsupportedTiming, 0, 125, 0, 2),
            Assert.Single(scenario.Result.Exclusions, row => row.Reason == UsagePriceExclusion.UnsupportedTiming));
        Assert.Equal(0.000169m, scenario.Result.CostA);
        Assert.Equal(0.000425m, scenario.Result.CostB);
        var definition = new UsageComparisonDefinition("Periods", "SelectedPeriod", day.AddDays(-3), day.AddDays(-1),
            day, day, "UTC", Now, null, null, "Linear scenario", "revision") { MethodId = UsageLinearPriceScenario.MethodId };
        UsageLinearPriceResult frozen = scenario.Result with { MethodId = "historical-linear/v0" };
        await repository.SaveComparisonAsync(new("linear-snapshot", Now, definition, scenario.Baseline, scenario.Current)
            { PriceScenario = frozen });
        SavedUsageComparison saved = await repository.ReadComparisonAsync("linear-snapshot");
        Assert.Equal(frozen.MethodId, saved.PriceScenario!.MethodId);
        Assert.Equal(frozen.RoundingPolicyId, saved.PriceScenario.RoundingPolicyId);
        Assert.Equal(frozen.CostA, saved.PriceScenario.CostA);
        Assert.Equal(frozen.CostB, saved.PriceScenario.CostB);
        Assert.Equal(frozen.Cells, saved.PriceScenario.Cells);
        Assert.Equal(frozen.Exclusions, saved.PriceScenario.Exclusions);
        Assert.Equal(frozen.Volume + frozen.Mix + frozen.Price + frozen.RoundingResidual,
            saved.PriceScenario.CostB - saved.PriceScenario.CostA);
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

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

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
