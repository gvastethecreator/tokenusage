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
                "cached-before-schema-four",
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
            command.CommandText =
                """
                DELETE FROM schema_migration WHERE version = 4;
                DROP INDEX ix_usage_event_agent_civil_date;
                DROP INDEX ix_usage_event_occurred_at_utc;
                DROP INDEX ix_daily_usage_rollup_agent_civil_date;
                """;
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
    public async Task RefreshWindowedCompleteRemovesStaleKeysOutsideCurrentScan()
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
                UsageSourceReadStatus.Complete));
        LocalUsageRefreshResult second = await new LocalUsageRefresh(
            folder.DatabasePath,
            updated,
            clock).RefreshAsync();

        Assert.Equal(1, second.Rollups.Sum(r => r.EventCount));
        Assert.Equal(1_100, second.Rollups.Sum(r => r.Tokens.Total));
        Assert.Equal(0.75m, second.Rollups.Sum(r => r.EstimatedCostUsd ?? 0m));
    }

    [Fact]
    public async Task OneBrokenSourceDoesNotDiscardOtherLocalUsage()
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
        var broken = new ThrowingUsageEventSource(new AgentId("broken"));

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
        Assert.Equal(UsageSourceIssueKind.AccessBlocked, diagnostic.Issue);
    }

    private static UsageEvent CreateEvent(
        string agentId,
        string identity,
        DateTimeOffset occurredAtUtc,
        long input,
        long output,
        CostObservation cost,
        string modelId = "model",
        string parserVersion = "test/1")
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
            coverage);
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

    private sealed class ThrowingUsageEventSource(AgentId agentId) : IUsageEventSource
    {
        public AgentId AgentId { get; } = agentId;

        public SourceKind SourceKind => SourceKind.LocalLog;

        public Task<UsageSourceReadResult> ReadAsync(
            CancellationToken cancellationToken = default) =>
            throw new IOException("Synthetic provider failure.");
    }
}
