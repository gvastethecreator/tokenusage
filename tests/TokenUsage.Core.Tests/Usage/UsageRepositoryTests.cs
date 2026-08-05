using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageRepositoryTests
{
    [Fact]
    public async Task DuplicateEventsIncrementTheDailyRollupOnce()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent usageEvent = CreateEvent("event-1");

        UsageIngestResult first = await repository.IngestAsync([usageEvent]);
        UsageIngestResult duplicate = await repository.IngestAsync([usageEvent]);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22));

        Assert.Equal(new UsageIngestResult(1, 0), first);
        Assert.Equal(new UsageIngestResult(0, 1), duplicate);
        DailyUsageRollup rollup = Assert.Single(rollups);
        Assert.Equal(1, rollup.EventCount);
        Assert.Equal(150, rollup.Tokens.Total);
        Assert.Equal(0.25m, rollup.ReportedCostUsd);
        Assert.Null(rollup.EstimatedCostUsd);
    }

    [Fact]
    public async Task ReplacingAgentEventsRemovesOldSnapshotsAndKeepsOtherAgents()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent("grok-old", agentId: "grok", parserVersion: "grok-build/1"),
            CreateEvent("claude-kept", agentId: "claude", parserVersion: "claude-jsonl/1"),
        ]);

        UsageEvent replacement = CreateEvent(
            "grok-new",
            agentId: "grok",
            parserVersion: "grok-build/1",
            tokens: new TokenBreakdown(300, 40, 10, 50, 0));
        UsageIngestResult result = await repository.ReplaceAgentEventsAsync(
            new AgentId("grok"),
            [replacement]);

        Assert.Equal(new UsageIngestResult(1, 0), result);
        DailyUsageRollup grok = Assert.Single(await repository.QueryDailyRollupsByAgentAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            new AgentId("grok")));
        Assert.Equal(400, grok.Tokens.Total);
        Assert.Single(await repository.QueryDailyRollupsByAgentAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            new AgentId("claude")));
    }

    [Fact]
    public async Task ReplacingSnapshotRevivesItsTombstonedKeyAndKeepsHistoricalRollup()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "long-lived-grok-session",
                new DateTimeOffset(2025, 6, 16, 12, 0, 0, TimeSpan.Zero),
                agentId: "grok",
                parserVersion: "grok-local/1"),
        ]);
        await repository.ApplyRetentionAsync(
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero));

        UsageIngestResult result = await repository.ReplaceAgentEventsAsync(
            new AgentId("grok"),
            [CreateEvent(
                "long-lived-grok-session",
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
                agentId: "grok",
                parserVersion: "grok-local/1")]);

        Assert.Equal(new UsageIngestResult(1, 0), result);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsByAgentAsync(
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 12, 31),
            new AgentId("grok"));
        Assert.Equal(2, rollups.Count);
        Assert.Contains(rollups, rollup => rollup.Date == new DateOnly(2025, 6, 16));
        Assert.Contains(rollups, rollup => rollup.Date == new DateOnly(2026, 7, 22));
    }

    [Fact]
    public async Task ReplacingAgentRangeUsesExactCurrentSnapshotAndKeepsOlderHistory()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "claude-history",
                new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                agentId: "claude",
                parserVersion: "claude-jsonl/1"),
            CreateEvent(
                "claude-stale",
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
                agentId: "claude",
                parserVersion: "claude-jsonl/1"),
            CreateEvent(
                "claude-retained",
                new DateTimeOffset(2026, 7, 22, 11, 0, 0, TimeSpan.Zero),
                agentId: "claude",
                parserVersion: "claude-jsonl/2"),
        ]);

        UsageIngestResult result = await repository.ReconcileAgentEventRangeAsync(
            new AgentId("claude"),
            "claude-jsonl/2",
            new DateOnly(2026, 6, 23),
            new DateOnly(2026, 7, 22),
            [CreateEvent(
                "claude-final",
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
                new TokenBreakdown(400, 80, 0, 20, 0),
                agentId: "claude",
                parserVersion: "claude-jsonl/2")]);

        Assert.Equal(new UsageIngestResult(1, 0), result);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsByAgentAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 22),
            new AgentId("claude"));
        Assert.Equal(2, rollups.Count);
        Assert.Contains(rollups, rollup => rollup.Date == new DateOnly(2026, 6, 1));
        Assert.Equal(
            500,
            Assert.Single(rollups, rollup => rollup.Date == new DateOnly(2026, 7, 22))
                .Tokens.Total);
    }

    [Fact]
    public async Task UpsertingMutableEventReplacesItsCountersWithoutAddingAnotherEvent()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "claude-stream",
                tokens: new TokenBreakdown(100, 20, 0, 0, 0),
                agentId: "claude",
                parserVersion: "claude-jsonl/2"),
        ]);

        UsageIngestResult result = await repository.UpsertAgentEventsAsync(
            new AgentId("claude"),
            [CreateEvent(
                "claude-stream",
                tokens: new TokenBreakdown(300, 60, 0, 40, 0),
                agentId: "claude",
                parserVersion: "claude-jsonl/2")]);

        Assert.Equal(new UsageIngestResult(1, 0), result);
        DailyUsageRollup rollup = Assert.Single(await repository.QueryDailyRollupsByAgentAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            new AgentId("claude")));
        Assert.Equal(400, rollup.Tokens.Total);
        Assert.Equal(1, rollup.EventCount);
    }

    [Fact]
    public async Task ReplacingAgentRangeRejectsEventsOutsideTheSelectedDays()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.ReconcileAgentEventRangeAsync(
                new AgentId("claude"),
                "claude-jsonl/2",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 22),
                [CreateEvent(
                    "claude-outside",
                    new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero),
                    agentId: "claude",
                    parserVersion: "claude-jsonl/2")]));
    }

    [Fact]
    public async Task EmptyAuthoritativeWindowRemovesAllEventsInsideIt()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "claude-old-history",
                new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
                agentId: "claude",
                parserVersion: "claude-jsonl/1"),
            CreateEvent(
                "claude-stale-window",
                new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
                agentId: "claude",
                parserVersion: "claude-jsonl/1"),
            CreateEvent(
                "claude-current-stale-window",
                new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero),
                agentId: "claude",
                parserVersion: "claude-jsonl/2"),
        ]);

        UsageIngestResult result = await repository.ReconcileAgentEventRangeAsync(
            new AgentId("claude"),
            "claude-jsonl/2",
            new DateOnly(2026, 6, 18),
            new DateOnly(2026, 7, 22),
            []);

        Assert.Equal(new UsageIngestResult(0, 0), result);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsByAgentAsync(
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 7, 22),
            new AgentId("claude"));
        Assert.Single(rollups);
        Assert.Equal(new DateOnly(2026, 5, 1), rollups[0].Date);
    }

    [Fact]
    public async Task InitialMigrationIsCompleteAndIdempotent()
    {
        using var folder = new TemporaryFolder();

        await UsageRepository.OpenAsync(folder.DatabasePath);
        await UsageRepository.OpenAsync(folder.DatabasePath);

        await using var connection = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('schema_migration', 'usage_event', 'daily_usage_rollup',
                           'source_cursor', 'pricing_catalog', 'usage_event_tombstone');
            """;
        Assert.Equal(6L, (long)(await command.ExecuteScalarAsync())!);

        command.CommandText = "SELECT COUNT(*) FROM schema_migration WHERE version IN (1, 2, 3);";
        Assert.Equal(3L, (long)(await command.ExecuteScalarAsync())!);

        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", (string)(await command.ExecuteScalarAsync())!);

        command.CommandText = "PRAGMA table_info(usage_event);";
        var columnNames = new List<string>();
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columnNames.Add(reader.GetString(1));
            }
        }

        string[] forbidden =
        [
            "prompt", "response", "project", "task", "tool", "command",
            "session", "path", "account", "transcript", "content", "text",
        ];
        Assert.DoesNotContain(
            columnNames,
            column => forbidden.Any(term =>
                column.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task NewerSchemaIsRejectedWithoutPartialMigration()
    {
        using var folder = new TemporaryFolder();
        await using (var setup = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await setup.OpenAsync();
            await using SqliteCommand command = setup.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE schema_migration (
                    version INTEGER NOT NULL PRIMARY KEY,
                    applied_at_utc TEXT NOT NULL
                );
                INSERT INTO schema_migration(version, applied_at_utc)
                VALUES (4, '2026-07-22T12:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        UsageSchemaTooNewException error = await Assert.ThrowsAsync<UsageSchemaTooNewException>(
            () => UsageRepository.OpenAsync(folder.DatabasePath));

        Assert.Equal(4, error.ActualVersion);
        await using var verify = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await verify.OpenAsync();
        await using SqliteCommand verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'usage_event';";
        Assert.Equal(0L, (long)(await verifyCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ReadOnlyOpenDoesNotCreateAMissingDatabase()
    {
        using var folder = new TemporaryFolder();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => UsageRepository.OpenReadOnlyAsync(folder.DatabasePath));

        Assert.False(File.Exists(folder.DatabasePath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(folder.DatabasePath)!));
    }

    [Fact]
    public async Task ReadOnlyRepositoryFindsAgentUsageWithoutChangingFiles()
    {
        using var folder = new TemporaryFolder();
        UsageRepository writer = await UsageRepository.OpenAsync(folder.DatabasePath);
        await writer.IngestAsync([CreateEvent("grok-read-only")]);
        byte[] databaseBefore = await File.ReadAllBytesAsync(folder.DatabasePath);
        DateTime lastWriteBefore = File.GetLastWriteTimeUtc(folder.DatabasePath);

        UsageRepository reader = await UsageRepository.OpenReadOnlyAsync(folder.DatabasePath);
        bool hasGrok = await reader.HasUsageForAgentAsync(new AgentId("grok"));
        bool hasClaude = await reader.HasUsageForAgentAsync(new AgentId("claude"));

        Assert.True(hasGrok);
        Assert.False(hasClaude);
        Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(folder.DatabasePath));
        Assert.Equal(lastWriteBefore, File.GetLastWriteTimeUtc(folder.DatabasePath));
        Assert.All(
            Directory.GetFiles(Path.GetDirectoryName(folder.DatabasePath)!),
            path => Assert.True(
                Path.GetFileName(path) is "usage.v1.db" or "usage.v1.db-shm" or "usage.v1.db-wal"));
    }

    [Fact]
    public async Task ReadOnlyOpenRejectsOldSchemaWithoutMigratingIt()
    {
        using var folder = new TemporaryFolder();
        await UsageRepository.OpenAsync(folder.DatabasePath);
        await using (var setup = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await setup.OpenAsync();
            await using SqliteCommand command = setup.CreateCommand();
            command.CommandText = "DELETE FROM schema_migration WHERE version = 3;";
            await command.ExecuteNonQueryAsync();
        }

        UsageSchemaTooOldException error = await Assert.ThrowsAsync<UsageSchemaTooOldException>(
            () => UsageRepository.OpenReadOnlyAsync(folder.DatabasePath));

        Assert.Equal(2, error.ActualVersion);
        await using var verify = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await verify.OpenAsync();
        await using SqliteCommand verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migration;";
        Assert.Equal(2L, (long)(await verifyCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ReadOnlyRepositoryRejectsMutatorsBeforeOpeningAWriteConnection()
    {
        using var folder = new TemporaryFolder();
        await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageRepository reader = await UsageRepository.OpenReadOnlyAsync(folder.DatabasePath);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.IngestAsync([CreateEvent("blocked-write")]));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReplaceAgentEventsAsync(new AgentId("grok"), []));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReconcileAgentEventRangeAsync(
                new AgentId("grok"),
                "grok-local/1",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 22),
                []));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.UpsertAgentEventsAsync(new AgentId("grok"), []));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ApplyRetentionAsync(DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.DeleteAllUsageDataAsync());
    }

    [Fact]
    public async Task ReadOnlyRepositorySeesCommitsMadeAfterItWasCreated()
    {
        using var folder = new TemporaryFolder();
        UsageRepository writer = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageRepository reader = await UsageRepository.OpenReadOnlyAsync(folder.DatabasePath);
        Assert.False(await reader.HasUsageForAgentAsync(new AgentId("grok")));

        await writer.IngestAsync([CreateEvent("late-writer-event")]);

        Assert.True(await reader.HasUsageForAgentAsync(new AgentId("grok")));
    }

    [Fact]
    public async Task VersionOneDatabaseMigratesIncrementallyToCurrentVersion()
    {
        using var folder = new TemporaryFolder();
        await UsageRepository.OpenAsync(folder.DatabasePath);
        await using (var setup = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await setup.OpenAsync();
            await using SqliteCommand command = setup.CreateCommand();
            command.CommandText =
                """
                DELETE FROM schema_migration WHERE version IN (2, 3);
                DROP TABLE usage_event_tombstone;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await UsageRepository.OpenAsync(folder.DatabasePath);

        await using var verify = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await verify.OpenAsync();
        await using SqliteCommand verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText =
            "SELECT COUNT(*) FROM schema_migration WHERE version IN (2, 3);";
        Assert.Equal(2L, (long)(await verifyCommand.ExecuteScalarAsync())!);
        verifyCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'usage_event_tombstone';";
        Assert.Equal(1L, (long)(await verifyCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task VersionThreeRemovesSyntheticEventsAndRebuildsRealRollups()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent("synthetic-event"),
            CreateEvent(
                "claude-event",
                agentId: "claude",
                parserVersion: "claude-jsonl/1"),
        ]);

        await using (var setup = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await setup.OpenAsync();
            await using SqliteCommand command = setup.CreateCommand();
            command.CommandText = "DELETE FROM schema_migration WHERE version = 3;";
            await command.ExecuteNonQueryAsync();
        }

        repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        IReadOnlyList<DailyUsageRollup> all = await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22));
        IReadOnlyList<DailyUsageRollup> claude = await repository.QueryDailyRollupsByAgentAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            new AgentId("claude"));

        Assert.Single(all);
        Assert.Single(claude);
        Assert.Equal("claude", all[0].AgentId.Value);
        Assert.Equal(1, all[0].EventCount);
    }

    [Fact]
    public async Task RetentionDeletesOldEventsInBatchesAndPreservesRollups()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "old-event",
                new DateTimeOffset(2025, 6, 16, 12, 0, 0, TimeSpan.Zero)),
            CreateEvent(
                "recent-event",
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero)),
        ]);

        int deleted = await repository.ApplyRetentionAsync(
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            batchSize: 1);
        int deletedAgain = await repository.ApplyRetentionAsync(
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            batchSize: 1);

        Assert.Equal(1, deleted);
        Assert.Equal(0, deletedAgain);
        Assert.Equal(2, (await repository.QueryDailyRollupsAsync(
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 12, 31))).Count);

        await using var verify = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await verify.OpenAsync();
        await using SqliteCommand command = verify.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM usage_event;";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);

        Assert.Equal(
            new UsageIngestResult(0, 1),
            await repository.IngestAsync(
            [
                CreateEvent(
                    "old-event",
                    new DateTimeOffset(2025, 6, 16, 12, 0, 0, TimeSpan.Zero)),
            ]));
        DailyUsageRollup oldRollup = Assert.Single(
            await repository.QueryDailyRollupsAsync(
                new DateOnly(2025, 6, 16),
                new DateOnly(2025, 6, 16)));
        Assert.Equal(1, oldRollup.EventCount);
    }

    [Fact]
    public async Task IndependentUiAndCliRepositoriesShareOneWalDatabase()
    {
        using var folder = new TemporaryFolder();
        UsageRepository uiRepository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageRepository cliRepository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent[] uiEvents = Enumerable.Range(0, 20)
            .Select(index => CreateEvent($"ui-{index}"))
            .ToArray();
        UsageEvent[] cliEvents = Enumerable.Range(0, 20)
            .Select(index => CreateEvent($"cli-{index}"))
            .ToArray();

        await Task.WhenAll(
            uiRepository.IngestAsync(uiEvents),
            cliRepository.IngestAsync(cliEvents));

        DailyUsageRollup rollup = Assert.Single(await uiRepository.QueryDailyRollupsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22)));
        Assert.Equal(40, rollup.EventCount);
        Assert.Equal(6_000, rollup.Tokens.Total);
        Assert.Equal(10m, rollup.ReportedCostUsd);
    }

    [Fact]
    public async Task RollupOverflowRollsBackTheEventInsert()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "max-event",
                tokens: new TokenBreakdown(long.MaxValue, 0, 0, 0, 0)),
        ]);

        await Assert.ThrowsAsync<OverflowException>(() => repository.IngestAsync(
        [
            CreateEvent(
                "overflow-event",
                tokens: new TokenBreakdown(1, 0, 0, 0, 0)),
        ]));

        DailyUsageRollup rollup = Assert.Single(await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22)));
        Assert.Equal(1, rollup.EventCount);
        Assert.Equal(long.MaxValue, rollup.Tokens.Input);
    }

    [Fact]
    public async Task UserDeletionClearsEventsAndRollupsAtomically()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent usageEvent = CreateEvent("delete-event");
        await repository.IngestAsync([usageEvent]);

        await repository.DeleteAllUsageDataAsync();

        Assert.Empty(await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31)));
        Assert.Equal(
            new UsageIngestResult(1, 0),
            await repository.IngestAsync([usageEvent]));
    }

    private static UsageEvent CreateEvent(
        string localIdentity,
        DateTimeOffset? occurredAtUtc = null,
        TokenBreakdown? tokens = null,
        string agentId = "grok",
        string parserVersion = "fixture/1") =>
        new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(localIdentity))).ToLowerInvariant()),
            new AgentId(agentId),
            new ModelProviderId("xai"),
            new ModelId("grok-4.5"),
            occurredAtUtc ?? new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            "Argentina Standard Time",
            tokens ?? new TokenBreakdown(100, 25, 5, 20, 0),
            CostObservation.ProviderReported(0.25m),
            parserVersion,
            CoverageKind.Complete);

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryFolder() => Directory.CreateDirectory(_path);

        public string DatabasePath => Path.Combine(_path, "usage.v1.db");

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
