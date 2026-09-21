using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class LocalUsageRefreshTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshIngestsSourceEventsAndReturnsStructuredRollupsWithoutUiTypes()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var source = new ScriptedUsageEventSource(
            new AgentId("synthetic"),
            SourceKind.Synthetic,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "synthetic",
                        "evt-1",
                        new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero),
                        input: 100,
                        output: 50,
                        CostObservation.ProviderReported(1.25m)),
                    CreateEvent(
                        "synthetic",
                        "evt-2",
                        new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
                        input: 200,
                        output: 80,
                        CostObservation.CatalogEstimated(0.40m, "cat/1", "exact")),
                ],
                UsageSourceReadStatus.Complete));

        var refresh = new LocalUsageRefresh(folder.DatabasePath, source, clock);
        LocalUsageRefreshResult result = await refresh.RefreshAsync();

        Assert.Equal(SourceKind.Synthetic, result.SourceKind);
        Assert.Equal(UsageSourceReadStatus.Complete, result.OverallStatus);
        Assert.Equal(2, result.Rollups.Sum(rollup => rollup.EventCount));
        Assert.Equal(430, result.Rollups.Sum(rollup => rollup.Tokens.Total));
        Assert.Single(result.SourceDiagnostics);
        Assert.Equal("synthetic", result.SourceDiagnostics[0].AgentId.Value);
        Assert.False(result.HasMultipleRealSources);
        Assert.True(result.FromInclusive <= result.ToInclusive);
        Assert.Equal(new DateOnly(2026, 7, 22), result.ToInclusive);
    }

    [Fact]
    public void DetectSourcesSeparatesInstalledToolsFromAbsentOnesWithoutAStore()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var refresh = new LocalUsageRefresh(
            folder.DatabasePath,
            [
                new ScriptedRootDetectingSource(
                    new AgentId("codex"),
                    isRootAvailable: true,
                    new UsageSourceReadResult([], UsageSourceReadStatus.NoData)),
                new ScriptedRootDetectingSource(
                    new AgentId("claude"),
                    isRootAvailable: false,
                    new UsageSourceReadResult([], UsageSourceReadStatus.NoData)),
            ],
            clock);

        IReadOnlyList<UsageSourceDiagnostic> detection = refresh.DetectSources();

        Assert.Equal(UsageSourceIssueKind.Empty, Find(detection, "codex").Issue);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, Find(detection, "claude").Issue);
        Assert.All(detection, diagnostic =>
        {
            Assert.Equal(UsageSourceReadStatus.NoData, diagnostic.Status);
            Assert.False(diagnostic.RetainsLastReliableSnapshot);
        });
        Assert.False(File.Exists(folder.DatabasePath));
    }

    [Fact]
    public async Task ReadCachedReportsAnAbsentRootEvenWhenHistoryRemains()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        UsageSourceReadResult codexRead = new(
            [
                CreateEvent(
                    "codex",
                    "cached-1",
                    new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
                    input: 100,
                    output: 50,
                    CostObservation.ProviderReported(1m)),
            ],
            UsageSourceReadStatus.Complete);
        var seed = new LocalUsageRefresh(
            folder.DatabasePath,
            [
                new ScriptedRootDetectingSource(new AgentId("codex"), true, codexRead),
                new ScriptedRootDetectingSource(
                    new AgentId("claude"),
                    isRootAvailable: true,
                    new UsageSourceReadResult([], UsageSourceReadStatus.NoData)),
            ],
            clock);
        await seed.RefreshAsync();

        // The same store, read after both tools were uninstalled.
        var afterUninstall = new LocalUsageRefresh(
            folder.DatabasePath,
            [
                new ScriptedRootDetectingSource(new AgentId("codex"), false, codexRead),
                new ScriptedRootDetectingSource(
                    new AgentId("claude"),
                    isRootAvailable: false,
                    new UsageSourceReadResult([], UsageSourceReadStatus.NoData)),
            ],
            clock);
        LocalUsageRefreshResult? cached = await afterUninstall.ReadCachedAsync();

        Assert.NotNull(cached);
        UsageSourceDiagnostic codex = Find(cached.SourceDiagnostics, "codex");
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, codex.Issue);
        Assert.True(codex.RetainsLastReliableSnapshot);
        UsageSourceDiagnostic claude = Find(cached.SourceDiagnostics, "claude");
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, claude.Issue);
        Assert.False(claude.RetainsLastReliableSnapshot);
    }

    [Fact]
    public async Task ReadCachedKeepsAnInstalledToolWithoutHistoryDistinctFromAnAbsentOne()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var sources = new IUsageEventSource[]
        {
            new ScriptedRootDetectingSource(
                new AgentId("codex"),
                isRootAvailable: true,
                new UsageSourceReadResult(
                    [
                        CreateEvent(
                            "codex",
                            "cached-2",
                            new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
                            input: 10,
                            output: 5,
                            CostObservation.ProviderReported(0.1m)),
                    ],
                    UsageSourceReadStatus.Complete)),
            new ScriptedRootDetectingSource(
                new AgentId("claude"),
                isRootAvailable: true,
                new UsageSourceReadResult([], UsageSourceReadStatus.NoData)),
            new ScriptedRootDetectingSource(
                new AgentId("grok"),
                isRootAvailable: false,
                new UsageSourceReadResult(
                    [],
                    UsageSourceReadStatus.NoData,
                    UsageSourceIssueKind.RootUnavailable)),
        };
        var refresh = new LocalUsageRefresh(folder.DatabasePath, sources, clock);
        await refresh.RefreshAsync();

        LocalUsageRefreshResult? cached = await refresh.ReadCachedAsync();

        Assert.NotNull(cached);
        Assert.Equal(
            UsageSourceReadStatus.Complete,
            Find(cached.SourceDiagnostics, "codex").Status);
        Assert.Equal(UsageSourceIssueKind.Empty, Find(cached.SourceDiagnostics, "claude").Issue);
        Assert.Equal(
            UsageSourceIssueKind.RootUnavailable,
            Find(cached.SourceDiagnostics, "grok").Issue);
    }

    [Fact]
    public async Task ReadCachedMigratesAnOlderOwnedDatabaseBeforeReturningHistory()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "codex",
                "cached-before-revision-schema",
                new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
                input: 100,
                output: 50,
                CostObservation.ProviderReported(1m)),
        ]);
        await using (var setup = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await setup.OpenAsync();
            await using SqliteCommand command = setup.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'trigger' AND (name LIKE 'guard_%' OR name LIKE 'revision_%');";
            var triggers = new List<string>();
            await using (SqliteDataReader reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) triggers.Add(reader.GetString(0));
            foreach (string trigger in triggers)
            {
                command.CommandText = "DROP TRIGGER \"" + trigger.Replace("\"", "\"\"", StringComparison.Ordinal) + "\";";
                await command.ExecuteNonQueryAsync();
            }
            command.CommandText = "DROP TABLE IF EXISTS operation_fact; DROP TABLE IF EXISTS project_attribution; DROP TABLE IF EXISTS session_attribution; DROP TABLE usage_data_revision; ALTER TABLE usage_event DROP COLUMN source_instance_id; ALTER TABLE usage_event DROP COLUMN record_kind; ALTER TABLE usage_event DROP COLUMN representation_revision; ALTER TABLE usage_event DROP COLUMN input_availability; ALTER TABLE usage_event DROP COLUMN output_availability; ALTER TABLE usage_event DROP COLUMN reasoning_availability; ALTER TABLE usage_event DROP COLUMN cache_read_availability; ALTER TABLE usage_event DROP COLUMN cache_write_availability; ALTER TABLE usage_collection_state DROP COLUMN last_successful_at_utc; DELETE FROM schema_migration WHERE version >= 6;";
            await command.ExecuteNonQueryAsync();
        }

        var refresh = new LocalUsageRefresh(
            folder.DatabasePath,
            new ScriptedRootDetectingSource(
                new AgentId("codex"),
                isRootAvailable: true,
                new UsageSourceReadResult([], UsageSourceReadStatus.NoData)),
            new FixedTimeProvider(Now));

        LocalUsageRefreshResult? cached = await refresh.ReadCachedAsync();

        Assert.NotNull(cached);
        Assert.Equal(1, cached.Rollups.Sum(rollup => rollup.EventCount));
        Assert.Equal(150, cached.Rollups.Sum(rollup => rollup.Tokens.Total));
        await using var verify = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Mode=ReadOnly;Pooling=False");
        await verify.OpenAsync();
        await using SqliteCommand verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migration;";
        Assert.Equal(UsageRepository.CurrentSchemaVersion, (long)(await verifyCommand.ExecuteScalarAsync())!);
    }

    private static UsageSourceDiagnostic Find(
        IReadOnlyList<UsageSourceDiagnostic> diagnostics,
        string agentId) =>
        diagnostics.Single(diagnostic => diagnostic.AgentId.Value == agentId);

    [Fact]
    public async Task RefreshWindowedSnapshotReconcilesOnlyAuthoritativeWindow()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var source = new ScriptedWindowedSource(
            new AgentId("claude"),
            eventParserVersion: "test/1",
            reconciliationWindowDays: 7,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "claude",
                        "win-1",
                        new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.Zero),
                        input: 10,
                        output: 5,
                        CostObservation.ProviderReported(0.10m)),
                ],
                UsageSourceReadStatus.Complete));

        var refresh = new LocalUsageRefresh(folder.DatabasePath, source, clock);
        LocalUsageRefreshResult first = await refresh.RefreshAsync();
        Assert.Equal(1, first.Rollups.Sum(r => r.EventCount));

        var updated = new ScriptedWindowedSource(
            new AgentId("claude"),
            eventParserVersion: "test/1",
            reconciliationWindowDays: 7,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "claude",
                        "win-1",
                        new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.Zero),
                        input: 20,
                        output: 10,
                        CostObservation.ProviderReported(0.20m)),
                ],
                UsageSourceReadStatus.Complete));
        var refresh2 = new LocalUsageRefresh(folder.DatabasePath, updated, clock);
        LocalUsageRefreshResult second = await refresh2.RefreshAsync();

        Assert.Equal(1, second.Rollups.Sum(r => r.EventCount));
        Assert.Equal(30, second.Rollups.Sum(r => r.Tokens.Total));
        Assert.Equal(0.20m, second.Rollups.Sum(r => r.ReportedCostUsd ?? 0m));
    }

    [Fact]
    public async Task RefreshWindowedSnapshotRewritesOlderEventsTheSourceStillReturns()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var firstSource = new ScriptedWindowedSource(
            new AgentId("antigravity"),
            eventParserVersion: "test/1",
            reconciliationWindowDays: 35,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "antigravity",
                        "old-1",
                        new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero),
                        input: 1_000,
                        output: 100,
                        CostObservation.Unavailable(),
                        modelId: "antigravity-unknown"),
                ],
                UsageSourceReadStatus.Complete));
        await new LocalUsageRefresh(folder.DatabasePath, firstSource, clock).RefreshAsync();

        var updated = new ScriptedWindowedSource(
            new AgentId("antigravity"),
            eventParserVersion: "test/2",
            reconciliationWindowDays: 7,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "antigravity",
                        "old-1",
                        new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero),
                        input: 1_000,
                        output: 100,
                        CostObservation.CatalogEstimated(0.75m, "google-api-2026-08-12", "gemini-3.6-flash"),
                        modelId: "gemini-3.6-flash",
                        parserVersion: "test/2"),
                ],
                UsageSourceReadStatus.Complete));
        LocalUsageRefreshResult second = await new LocalUsageRefresh(
            folder.DatabasePath,
            updated,
            clock).RefreshAsync();

        DailyUsageRollup rollup = Assert.Single(second.Rollups);
        Assert.Equal("gemini-3.6-flash", rollup.ModelId.Value);
        Assert.Equal(0.75m, rollup.EstimatedCostUsd);
        Assert.Equal(1, rollup.EventCount);
    }

    [Fact]
    public async Task EmptyOrPartialNewParserCannotEraseOrDuplicateThePreviousBaseline()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        UsageEvent original = CreateEvent("codex", "old", Now.AddMinutes(-1), 600, 0, CostObservation.Unavailable());
        var originalSource = new ScriptedWindowedSource(new AgentId("codex"), "test/1", 35,
            new UsageSourceReadResult([original], UsageSourceReadStatus.Complete));
        await new LocalUsageRefresh(folder.DatabasePath, originalSource, clock).RefreshAsync();
        var empty = new ScriptedWindowedSource(new AgentId("codex"), "test/2", 35,
            new UsageSourceReadResult([], UsageSourceReadStatus.NoData, UsageSourceIssueKind.Empty));
        var retained = await new LocalUsageRefresh(folder.DatabasePath, empty, clock).RefreshAsync();
        Assert.Equal(600, retained.Rollups.Sum(row => row.Tokens.Total));
        var partial = new ScriptedWindowedSource(new AgentId("codex"), "test/2", 35,
            new UsageSourceReadResult([CreateEvent("codex", "new-key", Now.AddMinutes(-1), 600, 0,
                CostObservation.Unavailable(), parserVersion: "test/2")], UsageSourceReadStatus.Partial));
        var protectedBaseline = await new LocalUsageRefresh(folder.DatabasePath, partial, clock).RefreshAsync();
        Assert.Equal(600, protectedBaseline.Rollups.Sum(row => row.Tokens.Total));
        Assert.Equal(1, protectedBaseline.Rollups.Sum(row => row.EventCount));
    }

    [Fact]
    public async Task RefreshWindowedPartialUpsertsExistingEventCosts()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var firstSource = new ScriptedWindowedSource(
            new AgentId("cursor"),
            eventParserVersion: "test/1",
            reconciliationWindowDays: 35,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "cursor",
                        "bubble-1",
                        new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.Zero),
                        input: 1_000,
                        output: 100,
                        CostObservation.Unavailable()),
                ],
                UsageSourceReadStatus.Partial));
        await new LocalUsageRefresh(folder.DatabasePath, firstSource, clock).RefreshAsync();

        var updated = new ScriptedWindowedSource(
            new AgentId("cursor"),
            eventParserVersion: "test/1",
            reconciliationWindowDays: 35,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "cursor",
                        "bubble-1",
                        new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.Zero),
                        input: 1_000,
                        output: 100,
                        CostObservation.CatalogEstimated(0.75m, "xai-api-2026-08-12", "composer-2.5")),
                ],
                UsageSourceReadStatus.Partial));
        LocalUsageRefreshResult second = await new LocalUsageRefresh(
            folder.DatabasePath,
            updated,
            clock).RefreshAsync();

        Assert.Equal(1, second.Rollups.Sum(r => r.EventCount));
        Assert.Equal(1_100, second.Rollups.Sum(r => r.Tokens.Total));
        Assert.Equal(0.75m, second.Rollups.Sum(r => r.EstimatedCostUsd ?? 0m));
    }

    [Fact]
    public async Task RefreshWindowedCompleteRemovesOnlyExplicitlySupersededKeys()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var firstSource = new ScriptedWindowedSource(
            new AgentId("cursor"),
            eventParserVersion: "test/1",
            reconciliationWindowDays: 35,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "cursor",
                        "composer-state",
                        new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.Zero),
                        input: 90_000,
                        output: 0,
                        CostObservation.Unavailable()),
                    CreateEvent("cursor", "cleaned-up", Now.AddMinutes(-1), 50, 0,
                        CostObservation.Unavailable()),
                ],
                UsageSourceReadStatus.Complete));
        await new LocalUsageRefresh(folder.DatabasePath, firstSource, clock).RefreshAsync();

        var updated = new ScriptedWindowedSource(
            new AgentId("cursor"),
            eventParserVersion: "test/1",
            reconciliationWindowDays: 35,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "cursor",
                        "bubble-turn",
                        new DateTimeOffset(2026, 7, 21, 10, 5, 0, TimeSpan.Zero),
                        input: 1_000,
                        output: 100,
                        CostObservation.CatalogEstimated(0.75m, "xai-api-2026-08-12", "composer-2.5")),
                ],
                UsageSourceReadStatus.Complete)
            {
                SupersededEventKeys = [CreateEvent("cursor", "composer-state", Now, 0, 0,
                    CostObservation.Unavailable()).EventKey],
            });
        LocalUsageRefreshResult second = await new LocalUsageRefresh(
            folder.DatabasePath,
            updated,
            clock).RefreshAsync();

        Assert.Equal(2, second.Rollups.Sum(r => r.EventCount));
        Assert.Equal(1_150, second.Rollups.Sum(r => r.Tokens.Total));
        Assert.Equal(0.75m, second.Rollups.Sum(r => r.EstimatedCostUsd ?? 0m));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupAndRecreatedSourcesMergeHistoryAndRepeatedUpdates(bool snapshot)
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var agent = new AgentId("zcode");
        UsageEvent Row(string key, long input) => CreateEvent(agent.Value, key, Now.AddMinutes(-1),
            input, 0, CostObservation.Unavailable());
        async Task<LocalUsageRefreshResult> Read(UsageSourceReadResult result)
        {
            IUsageEventSource source = snapshot
                ? new ScriptedSnapshotSource(agent, result)
                : new ScriptedWindowedSource(agent, "test/1", 35, result);
            return await new LocalUsageRefresh(folder.DatabasePath, source, clock).RefreshAsync();
        }
        await Read(new([Row("old", 100), Row("surviving", 20)], UsageSourceReadStatus.Complete));
        var partialCleanup = await Read(new([Row("surviving", 30), Row("new", 40)], UsageSourceReadStatus.Complete));
        Assert.Equal(170, partialCleanup.Rollups.Sum(row => row.Tokens.Total));
        await Read(new([], UsageSourceReadStatus.NoData, UsageSourceIssueKind.RootUnavailable));
        for (int repeat = 0; repeat < 2; repeat++)
        {
            var recreated = await Read(new([Row("recreated", 50)], UsageSourceReadStatus.Complete));
            Assert.Equal(220, recreated.Rollups.Sum(row => row.Tokens.Total));
            Assert.Equal(4, recreated.Rollups.Sum(row => row.EventCount));
        }
    }

    [Theory]
    [InlineData(false, UsageSourceIssueKind.AccessBlocked)]
    [InlineData(true, UsageSourceIssueKind.ReadFailed)]
    public async Task OneBrokenSourceDoesNotDiscardOtherLocalUsage(bool checkpointFailure, UsageSourceIssueKind expectedIssue)
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var healthy = new ScriptedUsageEventSource(
            new AgentId("healthy"),
            SourceKind.LocalDatabase,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "healthy",
                        "healthy-event",
                        new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
                        input: 12,
                        output: 3,
                        CostObservation.ProviderReported(0.25m)),
                ],
                UsageSourceReadStatus.Complete));
        var broken = new ThrowingUsageEventSource(new AgentId("broken"), checkpointFailure);

        var refresh = new LocalUsageRefresh(folder.DatabasePath, [broken, healthy], clock);
        LocalUsageRefreshResult result = await refresh.RefreshAsync();

        Assert.Equal(UsageSourceReadStatus.Partial, result.OverallStatus);
        Assert.Equal(SourceKind.LocalLog, result.SourceKind);
        Assert.True(result.HasMultipleRealSources);
        Assert.Equal(15, result.Rollups.Sum(rollup => rollup.Tokens.Total));
        UsageSourceDiagnostic diagnostic = Assert.Single(
            result.SourceDiagnostics,
            item => item.AgentId.Value == "broken");
        Assert.Equal(UsageSourceReadStatus.NoData, diagnostic.Status);
        Assert.Equal(expectedIssue, diagnostic.Issue);
    }

    [Fact]
    public async Task ScopedRefreshChecksOnlyItsOwnParserAndPreservesOtherAuthorities()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var a = new UsageSourceInstanceId(new string('a', 64));
        var b = new UsageSourceInstanceId(new string('b', 64));
        UsageEvent Fact(string key, long tokens, string parser, UsageSourceInstanceId? source) =>
            CreateEvent("codex", key, Now.AddMinutes(-1), tokens, 0, CostObservation.Unavailable(),
                parserVersion: parser, sourceInstance: source);
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent other = Fact("other", 200, "other/2", b);
        await repository.IngestAsync([Fact("first", 100, "test/1", a), other, Fact("legacy", 50, "legacy/1", null)]);
        var checkpoint = new TrackingCheckpoint();
        var partial = new ScopedSource(a, "test/1", new([Fact("added", 30, "test/1", a)], UsageSourceReadStatus.Partial)
            { Checkpoint = checkpoint });
        LocalUsageRefreshResult unresolved = await new LocalUsageRefresh(folder.DatabasePath, partial, clock).RefreshAsync();
        Assert.Equal(350, unresolved.Rollups.Sum(row => row.Tokens.Total));
        Assert.Equal(UsageSourceIssueKind.UnresolvedHistory, Assert.Single(unresolved.SourceDiagnostics).Issue);
        Assert.Equal(0, checkpoint.Commits);
        Assert.Equal(1, await repository.AssociateLegacyObservationsAsync(other.AgentId, b,
            [Fact("legacy", 50, "legacy/1", b)]));
        Assert.Equal(380, (await new LocalUsageRefresh(folder.DatabasePath, partial, clock).RefreshAsync()).Rollups.Sum(row => row.Tokens.Total));
        Assert.Equal(1, checkpoint.Commits);
        var complete = new ScopedSource(a, "test/1", new([Fact("replacement", 70, "test/1", a)], UsageSourceReadStatus.Complete));
        Assert.Equal(320, (await new LocalUsageRefresh(folder.DatabasePath, complete, clock).RefreshAsync()).Rollups.Sum(row => row.Tokens.Total));
        var changedParser = new ScopedSource(a, "test/3", new([Fact("unproved", 70, "test/3", a)], UsageSourceReadStatus.Partial));
        Assert.Equal(320, (await new LocalUsageRefresh(folder.DatabasePath, changedParser, clock).RefreshAsync()).Rollups.Sum(row => row.Tokens.Total));
        Assert.Contains(other, await repository.QueryUsageEventsAsync(Now.AddDays(-1), Now.AddDays(1)));
        var missing = new ScopedSource(a, "test/1", new([], UsageSourceReadStatus.NoData));
        Assert.Equal(320, (await new LocalUsageRefresh(folder.DatabasePath, missing, clock).RefreshAsync()).Rollups.Sum(row => row.Tokens.Total));
        var emptyComplete = new ScopedSource(a, "test/1", new([], UsageSourceReadStatus.Complete));
        Assert.Equal(250, (await new LocalUsageRefresh(folder.DatabasePath, emptyComplete, clock).RefreshAsync()).Rollups.Sum(row => row.Tokens.Total));
    }

    [Fact]
    public async Task RevokeBeforePersistDropsStaleSessionLinksAndKeepsNumericTotals()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var sourceInstance = new UsageSourceInstanceId(new string('c', 64));
        UsageEvent usageEvent = CreateEvent("codex", "linked-event", Now.AddMinutes(-1), 40, 10,
            CostObservation.Unavailable(), sourceInstance: sourceInstance);
        var sessionKey = new HmacOpaqueKeyDeriver(Enumerable.Repeat((byte)0x11, 32).ToArray())
            .Derive(OpaqueKeyDomains.CodexSession, "codex", "stale-write");
        var consent = new MutableConsent(new AttributionConsent(
            AttributionCapability.CodexSession,
            AttributionConsentState.Enabled,
            Epoch: 4,
            UpdatedAtUtc: Now));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpoint = new GatedCheckpoint(gate.Task);
        var source = new ScopedSource(
            sourceInstance,
            "test/1",
            new UsageSourceReadResult([usageEvent], UsageSourceReadStatus.Complete)
            {
                Checkpoint = checkpoint,
                SessionLinks =
                [
                    new UsageSessionLink(usageEvent.EventKey, sessionKey, parentSessionKey: null, 4),
                ],
            });
        Task<LocalUsageRefreshResult> refresh = new LocalUsageRefresh(
            folder.DatabasePath,
            source,
            clock,
            consent).RefreshAsync();
        consent.Current = consent.Current with
        {
            State = AttributionConsentState.DisabledAfterPurge,
            Epoch = 5,
        };
        gate.SetResult();
        LocalUsageRefreshResult result = await refresh;
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(folder.DatabasePath);
        Assert.Equal(50, result.Rollups.Sum(row => row.Tokens.Total));
        Assert.Equal(0, await repository.CountSessionLinksAsync());
        Assert.Equal(1, checkpoint.Commits);
    }

    [Fact]
    public async Task RevokeBeforePersistDropsStaleProjectLinksAndKeepsNumericTotals()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var sourceInstance = new UsageSourceInstanceId(new string('d', 64));
        UsageEvent usageEvent = CreateEvent("codex", "project-event", Now.AddMinutes(-1), 40, 10,
            CostObservation.Unavailable(), sourceInstance: sourceInstance);
        var projectKey = new HmacOpaqueKeyDeriver(Enumerable.Repeat((byte)0x12, 32).ToArray())
            .Derive(OpaqueKeyDomains.CodexProject, "codex", "stale-project");
        var consent = new MutableConsent(new AttributionConsent(
            AttributionCapability.CodexProject,
            AttributionConsentState.Enabled,
            Epoch: 4,
            UpdatedAtUtc: Now));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpoint = new GatedCheckpoint(gate.Task);
        var source = new ScopedSource(
            sourceInstance,
            "test/1",
            new UsageSourceReadResult([usageEvent], UsageSourceReadStatus.Complete)
            {
                Checkpoint = checkpoint,
                ProjectLinks =
                [
                    new UsageProjectLink(usageEvent.EventKey, projectKey, 4, ProjectMappingKind.Observed),
                ],
            });
        Task<LocalUsageRefreshResult> refresh = new LocalUsageRefresh(
            folder.DatabasePath,
            source,
            clock,
            consent).RefreshAsync();
        consent.Current = consent.Current with
        {
            State = AttributionConsentState.DisabledAfterPurge,
            Epoch = 5,
        };
        gate.SetResult();
        LocalUsageRefreshResult result = await refresh;
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(folder.DatabasePath);
        Assert.Equal(50, result.Rollups.Sum(row => row.Tokens.Total));
        Assert.Equal(0, await repository.CountProjectLinksAsync());
        Assert.Equal(1, checkpoint.Commits);
    }

    [Fact]
    public async Task SecondConsentLoadDropsQueuedSessionLinks()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var sourceInstance = new UsageSourceInstanceId(new string('e', 64));
        UsageEvent usageEvent = CreateEvent("codex", "queued-event", Now.AddMinutes(-1), 40, 10,
            CostObservation.Unavailable(), sourceInstance: sourceInstance);
        var sessionKey = new HmacOpaqueKeyDeriver(Enumerable.Repeat((byte)0x13, 32).ToArray())
            .Derive(OpaqueKeyDomains.CodexSession, "codex", "queued-write");
        var consent = new CountingConsent(
            new AttributionConsent(
                AttributionCapability.CodexSession,
                AttributionConsentState.Enabled,
                Epoch: 4,
                UpdatedAtUtc: Now),
            new AttributionConsent(
                AttributionCapability.CodexSession,
                AttributionConsentState.DisabledAfterPurge,
                Epoch: 5,
                UpdatedAtUtc: Now),
            enabledLoads: 2);
        var source = new ScopedSource(
            sourceInstance,
            "test/1",
            new UsageSourceReadResult([usageEvent], UsageSourceReadStatus.Complete)
            {
                SessionLinks =
                [
                    new UsageSessionLink(usageEvent.EventKey, sessionKey, parentSessionKey: null, 4),
                ],
            });
        LocalUsageRefreshResult result = await new LocalUsageRefresh(
            folder.DatabasePath,
            source,
            clock,
            consent).RefreshAsync();
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(folder.DatabasePath);
        Assert.Equal(50, result.Rollups.Sum(row => row.Tokens.Total));
        Assert.Equal(0, await repository.CountSessionLinksAsync());
    }

    private sealed class TrackingCheckpoint : IUsageReadCheckpoint
    {
        public int Commits { get; private set; }
        public async Task PersistAsync(Func<Task<bool>> persist, CancellationToken cancellationToken = default)
        {
            if (await persist()) Commits++;
        }
    }

    private sealed class GatedCheckpoint(Task gate) : IUsageReadCheckpoint
    {
        public int Commits { get; private set; }

        public async Task PersistAsync(Func<Task<bool>> persist, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            if (await persist())
            {
                Commits++;
            }
        }
    }

    private sealed class CountingConsent(
        AttributionConsent enabled,
        AttributionConsent revoked,
        int enabledLoads) : IAttributionConsentSource
    {
        private int _loads;

        public Task<AttributionConsent> LoadAsync(
            AttributionCapability capability,
            CancellationToken cancellationToken = default)
        {
            int load = Interlocked.Increment(ref _loads);
            return Task.FromResult(load <= enabledLoads ? enabled : revoked);
        }
    }

    private sealed class MutableConsent(AttributionConsent current) : IAttributionConsentSource
    {
        public AttributionConsent Current { get; set; } = current;

        public Task<AttributionConsent> LoadAsync(
            AttributionCapability capability,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);
    }

    private sealed class ScopedSource(UsageSourceInstanceId sourceInstance, string parser,
        UsageSourceReadResult result) : ISourceScopedUsageEventSource
    {
        public UsageSourceInstanceId SourceInstance => sourceInstance;
        public AgentId AgentId { get; } = new("codex");
        public SourceKind SourceKind => SourceKind.LocalLog;
        public string EventParserVersion => parser;
        public int ReconciliationWindowDays => 35;
        public Task<UsageSourceReadResult> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(result with { SourceInstance = sourceInstance });
    }

    private static UsageEvent CreateEvent(
        string agentId,
        string identity,
        DateTimeOffset occurredAtUtc,
        long input,
        long output,
        CostObservation cost,
        string modelId = "model",
        string parserVersion = "test/1", UsageSourceInstanceId? sourceInstance = null)
    {
        string eventKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
        CoverageKind coverage = cost.Kind == CostKind.Unavailable
            ? CoverageKind.Unpriced
            : CoverageKind.Complete;
        return new UsageEvent(
            new UsageEventKey(eventKey),
            new AgentId(agentId),
            new ModelProviderId("test"),
            new ModelId(modelId),
            occurredAtUtc,
            "UTC",
            new TokenBreakdown(input, output, 0, 0, 0),
            cost,
            parserVersion,
            coverage, detailMetadata: new(sourceInstance));
    }

    private sealed class ScriptedRootDetectingSource(
        AgentId agentId,
        bool isRootAvailable,
        UsageSourceReadResult result) : IRootDetectingUsageEventSource
    {
        public AgentId AgentId { get; } = agentId;

        public SourceKind SourceKind => SourceKind.LocalLog;

        public bool IsRootAvailable { get; } = isRootAvailable;

        public Task<UsageSourceReadResult> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "wou-local-refresh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "usage.v1.db");
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
                // best effort
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class ScriptedUsageEventSource : IUsageEventSource
    {
        private readonly UsageSourceReadResult _result;

        public ScriptedUsageEventSource(
            AgentId agentId,
            SourceKind sourceKind,
            UsageSourceReadResult result)
        {
            AgentId = agentId;
            SourceKind = sourceKind;
            _result = result;
        }

        public AgentId AgentId { get; }

        public SourceKind SourceKind { get; }

        public Task<UsageSourceReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class ScriptedWindowedSource : IWindowedSnapshotUsageEventSource
    {
        private readonly UsageSourceReadResult _result;

        public ScriptedWindowedSource(
            AgentId agentId,
            string eventParserVersion,
            int reconciliationWindowDays,
            UsageSourceReadResult result)
        {
            AgentId = agentId;
            EventParserVersion = eventParserVersion;
            ReconciliationWindowDays = reconciliationWindowDays;
            _result = result;
        }

        public AgentId AgentId { get; }

        public SourceKind SourceKind => SourceKind.LocalLog;

        public string EventParserVersion { get; }

        public int ReconciliationWindowDays { get; }

        public Task<UsageSourceReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class ScriptedSnapshotSource(AgentId agentId, UsageSourceReadResult result) : ISnapshotUsageEventSource
    {
        public AgentId AgentId => agentId;
        public SourceKind SourceKind => SourceKind.LocalLog;
        public Task<UsageSourceReadResult> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class ThrowingUsageEventSource(AgentId agentId, bool checkpointFailure) : IUsageEventSource
    {
        public AgentId AgentId { get; } = agentId;

        public SourceKind SourceKind => SourceKind.LocalLog;

        public Task<UsageSourceReadResult> ReadAsync(
            CancellationToken cancellationToken = default) =>
            throw (checkpointFailure
                ? new InvalidOperationException("Synthetic checkpoint write failure.")
                : new IOException("Synthetic provider failure."));
    }
}
