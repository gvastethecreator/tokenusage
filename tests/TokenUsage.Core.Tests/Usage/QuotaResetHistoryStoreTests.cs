using System.Text.Json;
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
        Assert.Equal(68m, cycle.UsedPercent);
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
        Assert.Equal(QuotaResetCause.Scheduled, reset.Cause);
        Assert.Equal(QuotaChangeEvidenceKind.ExpectedBoundaryCrossed, reset.EvidenceKind);
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
        Assert.Equal(QuotaResetCause.Unknown, reset.Cause);
        Assert.Equal(QuotaChangeEvidenceKind.ReturnedToFull, reset.EvidenceKind);
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
        Assert.Empty(history.Replenishments);
        Assert.Equal(39.75m, Assert.Single(history.Windows).UsedPercent);
    }

    [Fact]
    public async Task MovingResetScheduleDoesNotInventAResetOrMoveTheCycleStart()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset firstReset = InitialObservation.AddHours(5);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 40m,
            firstReset,
            windowMinutes: 300m));
        DateTimeOffset observedAt = InitialObservation.AddHours(1);
        DateTimeOffset movedReset = firstReset.AddMinutes(30);

        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            observedAt,
            usedPercent: 40m,
            movedReset,
            windowMinutes: 300m));

        Assert.Empty(history.Resets);
        Assert.Equal(
            firstReset.AddMinutes(-300),
            Assert.Single(history.Windows).CurrentCycleStartedAtUtc);
    }

    [Fact]
    public async Task CompleteSnapshotRemovesWindowsThatTheProviderNoLongerReports()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 40m,
            InitialObservation.AddHours(5),
            windowMinutes: 300m));

        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddMinutes(1),
            usedPercent: 20m,
            InitialObservation.AddDays(7),
            windowMinutes: 10_080m,
            metricId: "quota.secondary"));

        QuotaResetWindowState window = Assert.Single(history.Windows);
        Assert.Equal("quota.secondary", window.MetricId);
    }

    [Fact]
    public async Task MaterialUsageDropRecordsReplenishmentWithoutEndingCycle()
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
        QuotaReplenishmentRecord replenishment = Assert.Single(history.Replenishments);
        Assert.Equal(76m, replenishment.PreviousUsedPercent);
        Assert.Equal(2m, replenishment.CurrentUsedPercent);
        Assert.Equal(QuotaChangeEvidenceKind.PartialReplenishment, replenishment.EvidenceKind);
        Assert.Equal(
            expectedReset.AddMinutes(-10_080),
            Assert.Single(history.Windows).CurrentCycleStartedAtUtc);
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
    public async Task OfficialEvidenceCanNameManualCauseButSyntheticEvidenceCannot()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddDays(3);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 80m,
            expectedReset,
            windowMinutes: 10_080m));
        DateTimeOffset resetAt = InitialObservation.AddHours(1);

        QuotaResetHistory official = await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(2),
            usedPercent: 70m,
            expectedReset,
            windowMinutes: 10_080m,
            resetEvidence: new ProviderResetEvidence(
                ProviderReportedResetCause.Manual,
                resetAt)));

        QuotaResetRecord reset = Assert.Single(official.Resets);
        Assert.Equal(QuotaResetCause.Manual, reset.Cause);
        Assert.Equal(QuotaChangeEvidenceKind.OfficialManualSignal, reset.EvidenceKind);

        using var creditFolder = new TemporaryFolder();
        var creditStore = new QuotaResetHistoryStore(creditFolder.DocumentPath);
        await creditStore.ObserveAsync(CreateSnapshot(
            InitialObservation, 80m, expectedReset, 10_080m, creditsAvailable: 2m));
        QuotaResetHistory manualWithCredits = await creditStore.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(2),
            70m,
            expectedReset,
            10_080m,
            resetEvidence: new ProviderResetEvidence(ProviderReportedResetCause.Manual, resetAt),
            creditsAvailable: 2m));
        Assert.Equal(QuotaResetCause.Manual, Assert.Single(manualWithCredits.Resets).Cause);

        using var syntheticFolder = new TemporaryFolder();
        var syntheticStore = new QuotaResetHistoryStore(syntheticFolder.DocumentPath);
        await syntheticStore.ObserveAsync(CreateSnapshot(
            InitialObservation,
            80m,
            expectedReset,
            10_080m,
            sourceKind: SourceKind.Synthetic));
        QuotaResetHistory synthetic = await syntheticStore.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(2),
            70m,
            expectedReset,
            10_080m,
            resetEvidence: new ProviderResetEvidence(
                ProviderReportedResetCause.ResetCredit,
                resetAt),
            sourceKind: SourceKind.Synthetic));

        Assert.Empty(synthetic.Resets);
        Assert.Single(synthetic.Replenishments);
    }

    [Fact]
    public async Task DuplicateAndOutOfOrderObservationsDoNotDuplicateChanges()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddDays(3);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            80m,
            expectedReset,
            10_080m));
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(2),
            0m,
            expectedReset,
            10_080m));
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(2),
            0m,
            expectedReset,
            10_080m));
        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(1),
            50m,
            expectedReset,
            10_080m));

        Assert.Single(history.Resets);
        Assert.Empty(history.Replenishments);
    }

    [Fact]
    public async Task LegacyDocumentMigratesOnceAndWritesOnlySchemaTwo()
    {
        using var folder = new TemporaryFolder();
        string legacyPath = Path.Combine(folder.Root, QuotaResetHistoryStore.LegacyFileName);
        string legacy = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            windows = new[]
            {
                new
                {
                    providerId = "codex",
                    metricId = "quota.primary",
                    usedPercent = 0m,
                    observedAtUtc = InitialObservation,
                    currentCycleStartedAtUtc = InitialObservation.AddDays(-1),
                    expectedResetAtUtc = InitialObservation.AddDays(1),
                    windowDurationMinutes = 300m,
                },
            },
            resets = new[]
            {
                new
                {
                    providerId = "codex",
                    metricId = "quota.primary",
                    occurredAtUtc = InitialObservation.AddHours(-1),
                    detectedAtUtc = InitialObservation,
                    previousCycleStartedAtUtc = InitialObservation.AddDays(-1),
                    previousObservedAtUtc = InitialObservation.AddHours(-2),
                    previousUsedPercent = 50m,
                    currentUsedPercent = 0m,
                    previousExpectedResetAtUtc = InitialObservation.AddHours(-1),
                    currentExpectedResetAtUtc = InitialObservation.AddDays(1),
                    windowDurationMinutes = 300m,
                    detectionKind = "scheduled",
                },
            },
        });
        await File.WriteAllTextAsync(legacyPath, legacy);

        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        QuotaResetHistory migrated = await store.LoadAsync();

        QuotaResetRecord reset = Assert.Single(migrated.Resets);
        Assert.Equal(QuotaResetCause.Scheduled, reset.Cause);
        Assert.Empty(migrated.Replenishments);
        using JsonDocument v2 = JsonDocument.Parse(await File.ReadAllTextAsync(folder.DocumentPath));
        Assert.Equal(QuotaResetHistoryStore.CurrentSchemaVersion, v2.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(v2.RootElement.TryGetProperty("replenishments", out _));

        await File.WriteAllTextAsync(legacyPath, "invalid legacy data");
        QuotaResetHistory loadedAgain = await new QuotaResetHistoryStore(folder.DocumentPath)
            .LoadAsync();
        Assert.Single(loadedAgain.Resets);
    }

    [Fact]
    public async Task InvalidEvidenceIsReportedWithoutReplacingTheOriginal()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddHours(1);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            80m,
            expectedReset,
            300m));
        await store.ObserveAsync(CreateSnapshot(
            expectedReset.AddMinutes(1),
            0m,
            expectedReset.AddHours(5),
            300m));
        string json = await File.ReadAllTextAsync(folder.DocumentPath);
        Assert.Contains("\"cause\": \"scheduled\"", json, StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            folder.DocumentPath,
            json.Replace(
                "\"cause\": \"scheduled\"",
                "\"cause\": \"manual\"",
                StringComparison.Ordinal));

        string invalid = await File.ReadAllTextAsync(folder.DocumentPath);
        QuotaHistoryReadResult result = await new QuotaResetHistoryStore(folder.DocumentPath).ReadAsync();
        Assert.Equal(QuotaHistoryAvailability.InvalidData, result.Availability);
        Assert.Null(result.History);
        Assert.Equal(invalid, await File.ReadAllTextAsync(folder.DocumentPath));
    }

    [Fact]
    public void ResetCountUsesHalfOpenRangeAndCanFilterOfficialWindow()
    {
        DateTimeOffset fromUtc = InitialObservation;
        QuotaResetHistory history = new(
            [
                CreateWindow("quota.primary", 300m),
                CreateWindow("quota.codex-bengalfox.primary", 300m),
            ],
            [
                CreateResetRecord("quota.primary", fromUtc, QuotaResetDetectionKind.Scheduled),
                CreateResetRecord("quota.codex-bengalfox.primary", fromUtc.AddDays(1), QuotaResetDetectionKind.Early),
                CreateResetRecord("quota.primary", fromUtc.AddDays(2), QuotaResetDetectionKind.Observed),
            ],
            []);

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
        Assert.Equal(
            new QuotaResetCountSummary(2, Scheduled: 1, Early: 1, Observed: 0),
            QuotaResetCountQuery.Summarize(
                history,
                "codex",
                fromUtc,
                fromUtc.AddDays(2)));
    }

    [Fact]
    public async Task ChangedWindowDurationKeepsOldCadenceInHistoryWithoutCountingItAsCurrent()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset firstReset = InitialObservation.AddHours(5);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation,
            usedPercent: 80m,
            firstReset,
            windowMinutes: 300m));
        await store.ObserveAsync(CreateSnapshot(
            firstReset.AddMinutes(1),
            usedPercent: 0m,
            firstReset.AddHours(5),
            windowMinutes: 300m));

        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            firstReset.AddHours(1),
            usedPercent: 5m,
            firstReset.AddDays(7),
            windowMinutes: 10_080m));

        QuotaResetCycle[] cycles = QuotaResetCycleQuery.Build(
            history,
            "codex",
            firstReset.AddHours(2)).ToArray();
        Assert.Equal(3, cycles.Length);
        Assert.True(cycles[0].IsCurrent);
        Assert.Equal(10_080m, cycles[0].WindowDurationMinutes);
        Assert.False(cycles[1].IsCurrent);
        Assert.Equal(300m, cycles[1].WindowDurationMinutes);
        Assert.Equal(1, QuotaResetCountQuery.Count(
            history,
            "codex",
            InitialObservation,
            firstReset.AddDays(1)));
        Assert.Equal(0, QuotaResetCountQuery.Summarize(history, "codex", InitialObservation,
            firstReset.AddDays(1), currentWindowsOnly: true).Total);
    }

    [Fact]
    public void CompletedCyclesRemainAvailableAfterTheirMetricLeavesTheActiveSnapshot()
    {
        DateTimeOffset resetAt = InitialObservation.AddDays(7);
        QuotaResetHistory history = new(
            [CreateWindow("quota.primary", 300m)],
            [CreateResetRecord(
                "quota.secondary",
                resetAt,
                QuotaResetDetectionKind.Scheduled)],
            []);

        QuotaResetCycle[] cycles = QuotaResetCycleQuery.Build(
                history,
                "codex",
                resetAt.AddHours(1))
            .ToArray();

        Assert.Equal(2, cycles.Length);
        Assert.Contains(cycles, cycle => cycle.IsCurrent && cycle.MetricId == "quota.primary");
        Assert.Contains(cycles, cycle => !cycle.IsCurrent && cycle.MetricId == "quota.secondary");
    }

    [Fact]
    public void CycleQueryKeepsTwoSameDaySessionsSeparateFromWeeklyHistory()
    {
        DateTimeOffset dayStart = new(InitialObservation.Date, TimeSpan.Zero);
        QuotaResetHistory history = new(
            [
                CreateWindow(
                    "quota.primary",
                    300m) with
                {
                    CurrentCycleStartedAtUtc = dayStart.AddHours(10),
                    ObservedAtUtc = dayStart.AddHours(11),
                },
                CreateWindow("quota.secondary", 10_080m),
            ],
            [
                CreateResetRecord(
                    "quota.primary",
                    dayStart.AddHours(5),
                    QuotaResetDetectionKind.Scheduled,
                    dayStart,
                    300m),
                CreateResetRecord(
                    "quota.primary",
                    dayStart.AddHours(10),
                    QuotaResetDetectionKind.Scheduled,
                    dayStart.AddHours(5),
                    300m),
                CreateResetRecord(
                    "quota.secondary",
                    dayStart.AddDays(-1),
                    QuotaResetDetectionKind.Scheduled,
                    dayStart.AddDays(-8),
                    10_080m),
            ],
            []);

        QuotaResetCycle[] cycles = QuotaResetCycleQuery.Build(
            history,
            "codex",
            dayStart.AddHours(12)).ToArray();

        QuotaResetCycle[] sessions = cycles
            .Where(cycle => cycle.MetricId == "quota.primary")
            .OrderBy(cycle => cycle.FromUtc)
            .ToArray();
        Assert.Equal(3, sessions.Length);
        Assert.Equal(dayStart, sessions[0].FromUtc);
        Assert.Equal(dayStart.AddHours(5), sessions[0].ToUtc);
        Assert.Equal(dayStart.AddHours(5), sessions[1].FromUtc);
        Assert.Equal(dayStart.AddHours(10), sessions[1].ToUtc);
        Assert.True(sessions[2].IsCurrent);
        Assert.All(sessions, cycle => Assert.Equal(300m, cycle.WindowDurationMinutes));
        Assert.All(
            cycles.Where(cycle => cycle.MetricId == "quota.secondary"),
            cycle => Assert.Equal(10_080m, cycle.WindowDurationMinutes));
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
        Assert.Equal(0m, cycles[0].UsedPercent);
        Assert.Equal(expectedReset.AddMinutes(1), cycles[0].ObservedAtUtc);
        Assert.Equal(expectedReset.AddHours(5), cycles[0].ExpectedResetAtUtc);
        Assert.False(cycles[1].IsCurrent);
        Assert.Equal(88m, cycles[1].UsedPercent);
        Assert.Equal(InitialObservation, cycles[1].ObservedAtUtc);
        Assert.Equal(expectedReset, cycles[1].ExpectedResetAtUtc);
        Assert.Equal(QuotaResetDetectionKind.Scheduled, cycles[1].EndingResetKind);
        Assert.Equal(QuotaResetCause.Scheduled, cycles[1].EndingResetCause);
        Assert.Equal(expectedReset, cycles[1].ToUtc);
    }

    [Fact]
    public async Task StaleCompleteTopologyCannotRemoveOrResurrectPoolsAndJournalIsIdempotent()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        ProviderSnapshot old = CreateSnapshot(InitialObservation, 20, InitialObservation.AddHours(5), 300, "quota.old");
        await store.ObserveAsync(old);
        ProviderSnapshot current = CreateSnapshot(InitialObservation.AddMinutes(5), 30, InitialObservation.AddHours(5), 300, "quota.new");
        await store.ObserveAsync(current);
        await store.ObserveAsync(current);
        QuotaResetHistory history = await store.ObserveAsync(old);
        Assert.Equal("quota.new", Assert.Single(history.Windows).MetricId);
        Assert.Equal("quota.old", Assert.Single(history.RetiredWindows).MetricId);
        Assert.Contains(QuotaResetCycleQuery.Build(history, "codex", InitialObservation.AddHours(1)),
            cycle => cycle.MetricId == "quota.old" && !cycle.IsCurrent);
        var readings = await store.Journal.ReadAsync("codex", "quota.new", InitialObservation, InitialObservation.AddHours(1));
        Assert.Single(readings.Observations);
        Assert.False(readings.Truncated);
        Assert.Equal(QuotaWindowSemantics.Unknown, readings.Observations[0].Semantics);
    }

    [Fact]
    public async Task ExpiredUnchangedScheduleDoesNotInventScheduledResetAcrossGap()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset boundary = InitialObservation.AddHours(1);
        await store.ObserveAsync(CreateSnapshot(InitialObservation, 20, boundary, 300));
        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(InitialObservation.AddDays(2), 30, boundary, 300));
        Assert.Empty(history.Resets);
    }

    [Fact]
    public async Task EarlyResetWithCreditDropIsBankedAndExpiryAloneIsNot()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddDays(3);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation, 80m, expectedReset, 10_080m, creditsAvailable: 3m));
        DateTimeOffset detectedAt = InitialObservation.AddHours(4);

        QuotaResetHistory banked = await store.ObserveAsync(CreateSnapshot(
            detectedAt, 0m, expectedReset, 10_080m, creditsAvailable: 2m));
        QuotaResetRecord reset = Assert.Single(banked.Resets);
        Assert.Equal(QuotaResetDetectionKind.Early, reset.DetectionKind);
        Assert.Equal(QuotaResetCause.Manual, reset.Cause);
        Assert.Equal(QuotaChangeEvidenceKind.InferredCreditDrop, reset.EvidenceKind);
        Assert.Equal(2m, Assert.Single(banked.CreditInventories).ReportedAvailable);

        using var expiryFolder = new TemporaryFolder();
        var expiryStore = new QuotaResetHistoryStore(expiryFolder.DocumentPath);
        await expiryStore.ObserveAsync(CreateSnapshot(
            InitialObservation, 80m, expectedReset, 10_080m, creditsAvailable: 3m));
        QuotaResetHistory grant = await expiryStore.ObserveAsync(CreateSnapshot(
            detectedAt, 0m, expectedReset, 10_080m, creditsAvailable: 3m));
        QuotaResetRecord extra = Assert.Single(grant.Resets);
        Assert.Equal(QuotaResetCause.ResetCredit, extra.Cause);
        Assert.Equal(QuotaChangeEvidenceKind.InferredNoCreditDrop, extra.EvidenceKind);
    }

    [Fact]
    public async Task OfficialResetCreditUsesInventoryToDistinguishBankedFromGrant()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddDays(3);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation, 80m, expectedReset, 10_080m, creditsAvailable: 2m));
        DateTimeOffset resetAt = InitialObservation.AddHours(1);

        QuotaResetHistory banked = await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(2),
            70m,
            expectedReset,
            10_080m,
            resetEvidence: new ProviderResetEvidence(ProviderReportedResetCause.ResetCredit, resetAt),
            creditsAvailable: 1m));
        QuotaResetRecord spent = Assert.Single(banked.Resets);
        Assert.Equal(QuotaResetCause.Manual, spent.Cause);
        Assert.Equal(QuotaChangeEvidenceKind.OfficialResetCreditSignal, spent.EvidenceKind);

        using var grantFolder = new TemporaryFolder();
        var grantStore = new QuotaResetHistoryStore(grantFolder.DocumentPath);
        await grantStore.ObserveAsync(CreateSnapshot(
            InitialObservation, 80m, expectedReset, 10_080m, creditsAvailable: 2m));
        QuotaResetHistory grant = await grantStore.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(2),
            70m,
            expectedReset,
            10_080m,
            resetEvidence: new ProviderResetEvidence(ProviderReportedResetCause.ResetCredit, resetAt),
            creditsAvailable: 2m));
        QuotaResetRecord extra = Assert.Single(grant.Resets);
        Assert.Equal(QuotaResetCause.ResetCredit, extra.Cause);
        Assert.Equal(QuotaChangeEvidenceKind.OfficialResetCreditSignal, extra.EvidenceKind);
    }

    [Fact]
    public async Task CreditDropWithoutResetStaysAReplenishment()
    {
        using var folder = new TemporaryFolder();
        var store = new QuotaResetHistoryStore(folder.DocumentPath);
        DateTimeOffset expectedReset = InitialObservation.AddDays(3);
        await store.ObserveAsync(CreateSnapshot(
            InitialObservation, 80m, expectedReset, 10_080m, creditsAvailable: 3m));
        QuotaResetHistory history = await store.ObserveAsync(CreateSnapshot(
            InitialObservation.AddHours(1), 50m, expectedReset, 10_080m, creditsAvailable: 2m));
        Assert.Empty(history.Resets);
        Assert.Single(history.Replenishments);
        Assert.Equal(2m, Assert.Single(history.CreditInventories).ReportedAvailable);
    }

    private static ProviderSnapshot CreateSnapshot(
        DateTimeOffset observedAtUtc,
        decimal usedPercent,
        DateTimeOffset? resetAtUtc,
        decimal windowMinutes,
        string metricId = "quota.primary",
        ProviderResetEvidence? resetEvidence = null,
        SourceKind sourceKind = SourceKind.OfficialLocalApi,
        decimal? creditsAvailable = null)
    {
        var provenance = new DataProvenance(
            sourceKind,
            MeasurementKind.ProviderReported,
            "test/1");
        var metrics = new List<MetricSnapshot>
        {
            new ProgressMetricSnapshot(
                new MetricId(metricId),
                usedPercent,
                100m,
                resetAtUtc,
                provenance,
                resetEvidence: resetEvidence),
            new ScalarMetricSnapshot(
                new MetricId($"{metricId}.window-minutes"),
                windowMinutes,
                "minutes",
                provenance),
        };
        if (creditsAvailable is { } credits)
        {
            metrics.Add(new ScalarMetricSnapshot(
                new MetricId(QuotaResetHistoryStore.ResetCreditsReportedAvailableMetricId),
                credits,
                "credits",
                provenance));
        }

        return new ProviderSnapshot(
            new ProviderId("codex"),
            "Codex",
            "Pro",
            observedAtUtc,
            observedAtUtc,
            "UTC",
            metrics,
            CoverageKind.Complete,
            adapterContractVersion: 1);
    }

    private static QuotaResetRecord CreateResetRecord(
        string metricId,
        DateTimeOffset occurredAtUtc,
        QuotaResetDetectionKind detectionKind = QuotaResetDetectionKind.Observed,
        DateTimeOffset? previousCycleStartedAtUtc = null,
        decimal windowDurationMinutes = 300m) => new(
            "codex",
            metricId,
            occurredAtUtc,
            occurredAtUtc,
            previousCycleStartedAtUtc ?? occurredAtUtc.AddDays(-1),
            occurredAtUtc.AddMinutes(-1),
            PreviousUsedPercent: 50m,
            CurrentUsedPercent: 0m,
            PreviousExpectedResetAtUtc: null,
            CurrentExpectedResetAtUtc: null,
            WindowDurationMinutes: windowDurationMinutes,
            detectionKind,
            detectionKind == QuotaResetDetectionKind.Scheduled
                ? QuotaResetCause.Scheduled
                : QuotaResetCause.Unknown,
            detectionKind == QuotaResetDetectionKind.Scheduled
                ? QuotaChangeEvidenceKind.ExpectedBoundaryCrossed
                : QuotaChangeEvidenceKind.ReturnedToFull);

    private static QuotaResetWindowState CreateWindow(
        string metricId,
        decimal windowMinutes) => new(
            "codex",
            metricId,
            UsedPercent: 50m,
            InitialObservation,
            InitialObservation,
            InitialObservation.AddMinutes((double)windowMinutes),
            windowMinutes);

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
