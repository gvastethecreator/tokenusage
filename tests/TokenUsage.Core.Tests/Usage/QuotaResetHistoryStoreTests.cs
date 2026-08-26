using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class QuotaResetHistoryStoreTests
{
    private static readonly DateTimeOffset InitialObservation =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FirstObservationStartsCurrentCycleWithoutInventingAReset()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);

        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 68m,
            resetAtUtc: InitialObservation.AddDays(2),
            windowMinutes: 10_080m));

        Assert.Empty(history.Resets);
        QuotaResetWindowState window = Assert.Single(history.Windows);
        Assert.Equal(InitialObservation.AddDays(-5), window.CurrentCycleStartedAtUtc);
        Assert.Equal(68m, window.UsedPercent);

        QuotaResetCycle cycle = Assert.Single(QuotaResetCycleQuery.Build(
            history,
            "codex",
            InitialObservation.AddHours(1)));
        Assert.True(cycle.IsCurrent);
        Assert.Equal(window.CurrentCycleStartedAtUtc, cycle.FromUtc);
    }

    [Fact]
    public async Task CrossingReportedBoundaryLogsScheduledReset()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddHours(2);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 92m,
            expectedReset,
            windowMinutes: 300m));

        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            expectedReset.AddMinutes(3),
            usedPercent: 0m,
            resetAtUtc: expectedReset.AddHours(5),
            windowMinutes: 300m));

        QuotaResetRecord reset = Assert.Single(history.Resets);
        Assert.Equal(QuotaResetDetectionKind.Scheduled, reset.DetectionKind);
        Assert.Equal(expectedReset, reset.OccurredAtUtc);
        Assert.Equal(expectedReset, Assert.Single(history.Windows).CurrentCycleStartedAtUtc);
    }

    [Fact]
    public async Task ScheduledBoundaryStartsANewCycleWhenUsageResumesBeforeTheNextObservation()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddHours(2);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 92m,
            expectedReset,
            windowMinutes: 300m));

        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            expectedReset.AddMinutes(3),
            usedPercent: 4m,
            resetAtUtc: expectedReset.AddHours(5),
            windowMinutes: 300m));

        QuotaResetRecord reset = Assert.Single(history.Resets);
        Assert.Equal(QuotaResetDetectionKind.Scheduled, reset.DetectionKind);
        Assert.Equal(expectedReset, reset.OccurredAtUtc);
        Assert.Equal(4m, reset.CurrentUsedPercent);
        Assert.Equal(expectedReset, Assert.Single(history.Windows).CurrentCycleStartedAtUtc);
    }

    [Fact]
    public async Task ReturnFromZeroToFullRemainingBeforeBoundaryLogsEarlyReset()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddDays(3);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 100m,
            expectedReset,
            windowMinutes: 10_080m));
        DateTimeOffset detectedAt = InitialObservation.AddHours(4);

        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            detectedAt,
            usedPercent: 0m,
            expectedReset,
            windowMinutes: 10_080m));

        QuotaResetRecord reset = Assert.Single(history.Resets);
        Assert.Equal(QuotaResetDetectionKind.Early, reset.DetectionKind);
        Assert.Equal(detectedAt, reset.OccurredAtUtc);
        Assert.Equal(expectedReset, reset.CurrentExpectedResetAtUtc);
    }

    [Fact]
    public async Task SmallProviderRoundingChangeDoesNotCreateReset()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddDays(3);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 40m,
            expectedReset,
            windowMinutes: 10_080m));

        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddMinutes(10),
            usedPercent: 39.75m,
            expectedReset,
            windowMinutes: 10_080m));

        Assert.Empty(history.Resets);
        Assert.Equal(39.75m, Assert.Single(history.Windows).UsedPercent);
    }

    [Fact]
    public async Task MaterialUsageDropThatDoesNotReturnToFullRemainingDoesNotCreateReset()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddDays(3);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 76m,
            expectedReset,
            windowMinutes: 10_080m));

        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(4),
            usedPercent: 2m,
            expectedReset,
            windowMinutes: 10_080m));

        Assert.Empty(history.Resets);
        Assert.Equal(2m, Assert.Single(history.Windows).UsedPercent);
    }

    [Fact]
    public async Task ReturningToFullRemainingIsCountedOnlyOnceWhileItStaysFull()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddDays(3);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 76m,
            expectedReset,
            windowMinutes: 10_080m));

        await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(4),
            usedPercent: 0m,
            expectedReset,
            windowMinutes: 10_080m));
        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(5),
            usedPercent: 0m,
            expectedReset,
            windowMinutes: 10_080m));

        Assert.Single(history.Resets);
    }

    [Fact]
    public void ResetCountUsesHalfOpenRangeAndCanFilterOfficialWindow()
    {
        DateTimeOffset fromUtc = InitialObservation;
        QuotaResetHistory history = new(
            [],
            [
                CreateResetRecord("quota.primary", fromUtc),
                CreateResetRecord("quota.codex-bengalfox.primary", fromUtc.AddDays(1)),
                CreateResetRecord("quota.primary", fromUtc.AddDays(2)),
            ]);

        Assert.Equal(2, QuotaResetCountQuery.Count(
            history,
            "codex",
            fromUtc,
            fromUtc.AddDays(2)));
        Assert.Equal(1, QuotaResetCountQuery.Count(
            history,
            "codex",
            fromUtc,
            fromUtc.AddDays(2),
            metricId: "quota.primary"));
    }

    [Fact]
    public async Task PersistedHistoryBuildsCurrentAndCompletedReportCycles()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddHours(2);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 88m,
            expectedReset,
            windowMinutes: 300m));
        await store.ObserveAsync(CreateSnapshot(
            expectedReset.AddMinutes(1),
            usedPercent: 0m,
            resetAtUtc: expectedReset.AddHours(5),
            windowMinutes: 300m));

        QuotaResetHistory loaded = await new QuotaResetHistoryStore(folder.DocumentPath)
            .LoadAsync();
        QuotaResetCycle[] cycles = QuotaResetCycleQuery.Build(
                loaded,
                "codex",
                expectedReset.AddHours(1))
            .ToArray();

        Assert.Equal(2, cycles.Length);
        Assert.True(cycles[0].IsCurrent);
        Assert.False(cycles[1].IsCurrent);
        Assert.Equal(QuotaResetDetectionKind.Scheduled, cycles[1].EndingResetKind);
        Assert.Equal(expectedReset, cycles[1].ToUtc);
    }

    private static ProviderSnapshot CreateSnapshot(
        DateTimeOffset observedAtUtc,
        decimal usedPercent,
        DateTimeOffset? resetAtUtc,
        decimal windowMinutes,
        string metricId = "quota.primary")
    {
        var provenance = new DataProvenance(
            SourceKind.OfficialLocalApi,
            MeasurementKind.ProviderReported,
            "test/1");
        return new ProviderSnapshot(
            new ProviderId("codex"),
            "Codex",
            "Pro",
            observedAtUtc,
            observedAtUtc,
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId(metricId),
                    usedPercent,
                    100m,
                    resetAtUtc,
                    provenance),
                new ScalarMetricSnapshot(
                    new MetricId($"{metricId}.window-minutes"),
                    windowMinutes,
                    "minutes",
                    provenance),
            ],
            CoverageKind.Complete,
            adapterContractVersion: 1);
    }

    private static QuotaResetRecord CreateResetRecord(
        string metricId,
        DateTimeOffset occurredAtUtc) => new(
            "codex",
            metricId,
            occurredAtUtc,
            occurredAtUtc,
            occurredAtUtc.AddDays(-1),
            occurredAtUtc.AddMinutes(-1),
            PreviousUsedPercent: 50m,
            CurrentUsedPercent: 0m,
            PreviousExpectedResetAtUtc: null,
            CurrentExpectedResetAtUtc: null,
            QuotaResetDetectionKind.Observed);

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "TokenUsage.QuotaResetHistory.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string DocumentPath => Path.Combine(Root, QuotaResetHistoryStore.DefaultFileName);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
