using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageRepositoryTests
{
    [Fact]
    public async Task TokenSumIncludesAllCountersForOnlyTheAgentAndHalfOpenRange()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var start = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = start.AddHours(1);
        await repository.IngestAsync(
        [
            CreateEvent("before", start.AddTicks(-1)),
            CreateEvent("at-start", start, tokens: new TokenBreakdown(10, 20, 30, 40, 50)),
            CreateEvent("inside", start.AddMinutes(1)),
            CreateEvent("at-end", end),
            CreateEvent("other-agent", start, agentId: "claude"),
        ]);

        UsageRepository readOnly = await UsageRepository.OpenReadOnlyAsync(folder.DatabasePath);
        Assert.Equal(300, await readOnly.SumTokensAsync(start, end, new AgentId("grok")));
        Assert.Equal(0, await readOnly.SumTokensAsync(start, end, new AgentId("codex")));
    }

    [Fact]
    public async Task TwoHourReportBucketsKeepCivilDatesBoundariesAndDailyTotals()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var start = new DateTimeOffset(2026, 7, 22, 3, 0, 0, TimeSpan.Zero);
        UsageEvent first = CreateEvent("midnight", start);
        await repository.IngestAsync([
            first, first,
            CreateEvent("same-slot", start.AddHours(2).AddTicks(-1)),
            CreateEvent("next-slot", start.AddHours(2)),
            CreateEvent("last-slot", start.AddDays(1).AddTicks(-1)),
            CreateEvent("next-day", start.AddDays(1)),
            CreateEvent("other-agent", start, agentId: "claude"),
            CreateEvent("unknown", start, agentId: "codex", cost: CostObservation.Unavailable()),
        ]);
        var query = new TokenUsage.Core.Automation.UsageReportQuery(folder.DatabasePath);
        var date = new DateOnly(2026, 7, 22);
        var report = await query.ReadAsync(date, date, includeTimeBuckets: true);
        var filtered = await query.ReadAsync(date, date, new AgentId("grok"), includeTimeBuckets: true);
        Assert.Equal([0, 2, 22], filtered.TimeBuckets.Select(item => item.Hour));
        Assert.Equal(2, filtered.TimeBuckets[0].Usage.EventCount);
        Assert.All(filtered.TimeBuckets, item => Assert.Equal(date, item.Usage.Date));
        Assert.Equal(filtered.Totals.Tokens.Total, filtered.TimeBuckets.Sum(item => item.Usage.Tokens.Total));
        Assert.Equal(filtered.Totals.TotalCostUsd, filtered.TimeBuckets.Sum(item => item.Usage.ReportedCostUsd));
        Assert.Equal(4, filtered.Totals.EventCount);
        Assert.Equal(5, report.TimeBuckets.Count);
        var unknown = Assert.Single(report.TimeBuckets, item => item.Usage.AgentId.Value == "codex").Usage;
        Assert.Null(unknown.ReportedCostUsd);
        Assert.Equal(unknown.Tokens.Total, unknown.UnpricedTokens);
        Assert.Equal(1, unknown.UnavailableCostEventCount);
        var exact = await query.ReadExactAsync(start.AddHours(1), start.AddHours(3), new AgentId("grok"));
        Assert.Equal(2, exact.TimeBuckets.Count);
        Assert.Equal(2, exact.Totals.EventCount);
        Assert.Empty((await query.ReadAsync(date, date)).TimeBuckets);
    }

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
    public async Task ReconcilingAfterRetentionKeepsHistoricalRollup()
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

        var retiredDay = new DateOnly(2025, 6, 16);
        DailyUsageRollup retained = Assert.Single(await repository.QueryDailyRollupsAsync(retiredDay, retiredDay));
        UsageDataRevision retainedRevision = await repository.ReadDataRevisionAsync();
        var source = new UsageSourceInstanceId(new string('a', 64));
        UsageSourceStoreResult empty = await repository.StoreSourceObservationsAsync(
            new AgentId("grok"), source, "grok-local/1", retiredDay, retiredDay, [], complete: true);
        Assert.True(empty.HasUnresolvedHistory);
        UsageSourceStoreResult changed = await repository.StoreSourceObservationsAsync(
            new AgentId("grok"), source, "grok-local/1", retiredDay, retiredDay,
            [CreateEvent("changed-parser-identity", new DateTimeOffset(2025, 6, 16, 12, 0, 0, TimeSpan.Zero),
                parserVersion: "grok-local/1", detailMetadata: new(source))], complete: true);
        Assert.Equal(new UsageSourceStoreResult(0, 1, true), changed);
        Assert.Equal(retained, Assert.Single(await repository.QueryDailyRollupsAsync(retiredDay, retiredDay)));
        Assert.Equal(retainedRevision, await repository.ReadDataRevisionAsync());

        UsageSourceStoreResult result = await repository.StoreSourceObservationsAsync(
            new AgentId("grok"),
            source,
            "grok-local/1",
            new DateOnly(2026, 6, 23),
            new DateOnly(2026, 7, 22),
            [CreateEvent(
                "recent-grok-session",
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
                agentId: "grok",
                parserVersion: "grok-local/1",
                detailMetadata: new(source))],
            complete: true);

        Assert.Equal(new UsageSourceStoreResult(1, 0, false), result);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsByAgentAsync(
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 12, 31),
            new AgentId("grok"));
        Assert.Equal(2, rollups.Count);
        Assert.Contains(rollups, rollup => rollup.Date == new DateOnly(2025, 6, 16));
        Assert.Contains(rollups, rollup => rollup.Date == new DateOnly(2026, 7, 22));
    }

    [Fact]
    public async Task UpsertingAfterRetentionKeepsHistoricalRollup()
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

        var retiredDay = new DateOnly(2025, 6, 16);
        DailyUsageRollup retained = Assert.Single(await repository.QueryDailyRollupsAsync(retiredDay, retiredDay));
        UsageDataRevision before = await repository.ReadDataRevisionAsync();
        var source = new UsageSourceInstanceId(new string('a', 64));
        UsageEvent replay = CreateEvent("long-lived-grok-session",
            new DateTimeOffset(2025, 6, 16, 12, 0, 0, TimeSpan.Zero),
            parserVersion: "grok-local/1", detailMetadata: new(source));
        Assert.Equal(new UsageIngestResult(0, 1), await repository.UpsertAgentEventsAsync(replay.AgentId, [replay]));
        Assert.Equal(new UsageIngestResult(0, 1), await repository.IngestAsync([replay]));
        UsageEvent changedKey = CreateEvent("new-key-old-usage", replay.OccurredAtUtc,
            parserVersion: "grok-local/2", detailMetadata: new(source));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.UpsertAgentEventsAsync(replay.AgentId, [changedKey]));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.IngestAsync([changedKey]));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.UpsertAgentEventsAsync(replay.AgentId,
            [CreateEvent("legacy-new-key", replay.OccurredAtUtc)]));
        Assert.Equal(before, await repository.ReadDataRevisionAsync());
        Assert.Equal(retained, Assert.Single(await repository.QueryDailyRollupsAsync(retiredDay, retiredDay)));

        UsageIngestResult result = await repository.UpsertAgentEventsAsync(
            new AgentId("grok"),
            [CreateEvent(
                "recent-grok-session",
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

        UsageEvent updated = CreateEvent(
            "claude-stream",
            tokens: new TokenBreakdown(300, 60, 0, 40, 0),
            agentId: "claude",
            parserVersion: "claude-jsonl/2");
        UsageIngestResult result = await repository.UpsertAgentEventsAsync(updated.AgentId, [updated]);

        Assert.Equal(new UsageIngestResult(1, 0), result);
        DailyUsageRollup rollup = Assert.Single(await repository.QueryDailyRollupsByAgentAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            new AgentId("claude")));
        Assert.Equal(400, rollup.Tokens.Total);
        Assert.Equal(1, rollup.EventCount);
        UsageDataRevision revision = await repository.ReadDataRevisionAsync();
        Assert.Equal(new UsageIngestResult(0, 1),
            await repository.UpsertAgentEventsAsync(updated.AgentId, [updated]));
        Assert.Equal(revision, await repository.ReadDataRevisionAsync());
        Assert.Equal(rollup, Assert.Single(await repository.QueryDailyRollupsByAgentAsync(
            rollup.Date, rollup.Date, updated.AgentId)));
    }

    [Fact]
    public async Task UpsertingMutableEventOnAnotherDayRemovesItsPreviousRollup()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "claude-stream",
                new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
                agentId: "claude",
                parserVersion: "claude-jsonl/2"),
        ]);

        await repository.UpsertAgentEventsAsync(
            new AgentId("claude"),
            [CreateEvent(
                "claude-stream",
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
                agentId: "claude",
                parserVersion: "claude-jsonl/2")]);

        DailyUsageRollup rollup = Assert.Single(
            await repository.QueryDailyRollupsByAgentAsync(
                new DateOnly(2026, 7, 21),
                new DateOnly(2026, 7, 22),
                new AgentId("claude")));
        Assert.Equal(new DateOnly(2026, 7, 22), rollup.Date);
        Assert.Equal(1, rollup.EventCount);
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

        command.CommandText = "SELECT COUNT(*) FROM schema_migration WHERE version IN (1, 2, 3, 4);";
        Assert.Equal(4L, (long)(await command.ExecuteScalarAsync())!);

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
                VALUES (14, '2026-07-22T12:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        UsageSchemaTooNewException error = await Assert.ThrowsAsync<UsageSchemaTooNewException>(
            () => UsageRepository.OpenAsync(folder.DatabasePath));

        Assert.Equal(14, error.ActualVersion);
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
            await RemoveRevisionSchemaAsync(setup);
            command.CommandText = "DELETE FROM schema_migration WHERE version IN (4, 5);";
            await command.ExecuteNonQueryAsync();
        }

        UsageSchemaTooOldException error = await Assert.ThrowsAsync<UsageSchemaTooOldException>(
            () => UsageRepository.OpenReadOnlyAsync(folder.DatabasePath));

        Assert.Equal(3, error.ActualVersion);
        await using var verify = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await verify.OpenAsync();
        await using SqliteCommand verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migration;";
        Assert.Equal(3L, (long)(await verifyCommand.ExecuteScalarAsync())!);
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
            () => reader.StoreSourceObservationsAsync(
                new AgentId("grok"),
                new UsageSourceInstanceId(new string('a', 64)),
                "grok-local/1",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 22),
                [],
                complete: true));
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
                DROP INDEX ix_usage_event_model_time;
                ALTER TABLE usage_event DROP COLUMN time_precision;
                ALTER TABLE usage_event DROP COLUMN interval_started_at_utc;
                ALTER TABLE usage_event DROP COLUMN observed_model_id;
                ALTER TABLE usage_event DROP COLUMN reasoning_effort;
                ALTER TABLE usage_event DROP COLUMN service_tier;
                DROP TABLE account_usage_daily;
                DROP TABLE usage_collection_state;
                DELETE FROM schema_migration WHERE version IN (2, 3, 4, 5);
                DROP TABLE usage_event_tombstone;
                """;
            await RemoveRevisionSchemaAsync(setup);
            await command.ExecuteNonQueryAsync();
        }

        await UsageRepository.OpenAsync(folder.DatabasePath);

        await using var verify = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await verify.OpenAsync();
        await using SqliteCommand verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText =
            "SELECT COUNT(*) FROM schema_migration WHERE version IN (2, 3, 4);";
        Assert.Equal(3L, (long)(await verifyCommand.ExecuteScalarAsync())!);
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
            command.CommandText = """
                DROP INDEX ix_usage_event_model_time;
                ALTER TABLE usage_event DROP COLUMN time_precision;
                ALTER TABLE usage_event DROP COLUMN interval_started_at_utc;
                ALTER TABLE usage_event DROP COLUMN observed_model_id;
                ALTER TABLE usage_event DROP COLUMN reasoning_effort;
                ALTER TABLE usage_event DROP COLUMN service_tier;
                DROP TABLE account_usage_daily;
                DROP TABLE usage_collection_state;
                DELETE FROM schema_migration WHERE version IN (3, 4, 5);
                """;
            await RemoveRevisionSchemaAsync(setup);
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
        var migrated = Assert.Single(await repository.QueryUsageEventsAsync(
            new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(UsageTimePrecision.Unknown, migrated.TimePrecision);
        Assert.Null(migrated.ObservedModelId);
    }

    [Fact]
    public async Task VersionFourCreatesUsageQueryIndexes()
    {
        using var folder = new TemporaryFolder();
        await UsageRepository.OpenAsync(folder.DatabasePath);

        await using var connection = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name FROM sqlite_master
            WHERE type = 'index'
              AND name IN (
                'ix_usage_event_agent_civil_date',
                'ix_usage_event_occurred_at_utc',
                'ix_daily_usage_rollup_agent_civil_date');
            """;
        var names = new List<string>();
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
        }

        Assert.Equal(13, UsageRepository.CurrentSchemaVersion);
        Assert.Contains("ix_usage_event_agent_civil_date", names);
        Assert.Contains("ix_usage_event_occurred_at_utc", names);
        Assert.Contains("ix_daily_usage_rollup_agent_civil_date", names);
    }

    [Fact]
    public async Task QueryPlansUseSchemaFourIndexes()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent("indexed-event"),
            CreateEvent("other-agent", agentId: "claude", parserVersion: "claude-jsonl/1"),
        ]);

        await using var connection = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        string eventPlan = await ExplainAsync(
            connection,
            """
            EXPLAIN QUERY PLAN
            SELECT event_key FROM usage_event
            WHERE agent_id = 'grok' AND civil_date BETWEEN '2026-01-01' AND '2026-12-31';
            """);
        string retentionPlan = await ExplainAsync(
            connection,
            """
            EXPLAIN QUERY PLAN
            SELECT event_key FROM usage_event
            WHERE occurred_at_utc < '2026-01-01T00:00:00.0000000+00:00';
            """);
        string rollupPlan = await ExplainAsync(
            connection,
            """
            EXPLAIN QUERY PLAN
            SELECT civil_date FROM daily_usage_rollup
            WHERE agent_id = 'grok' AND civil_date >= '2026-01-01' AND civil_date <= '2026-12-31';
            """);

        Assert.Contains("ix_usage_event_agent_civil_date", eventPlan, StringComparison.Ordinal);
        Assert.Contains("ix_usage_event_occurred_at_utc", retentionPlan, StringComparison.Ordinal);
        Assert.Contains("ix_daily_usage_rollup_agent_civil_date", rollupPlan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetentionIfDueSkipsWithinTheIntervalAndRunsAfterIt()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "old-event",
                new DateTimeOffset(2025, 6, 16, 12, 0, 0, TimeSpan.Zero)),
        ]);
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

        int first = await repository.ApplyRetentionIfDueAsync(now, TimeSpan.FromHours(6));
        int skipped = await repository.ApplyRetentionIfDueAsync(
            now.AddHours(1),
            TimeSpan.FromHours(6));
        int later = await repository.ApplyRetentionIfDueAsync(
            now.AddHours(6),
            TimeSpan.FromHours(6),
            batchSize: 1);

        Assert.Equal(1, first);
        Assert.Equal(-1, skipped);
        Assert.Equal(0, later);
    }

    [Fact]
    public async Task RetentionPreservesDailyHistoryRegardlessOfParserVersion()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "fossil-account-aggregate",
                new DateTimeOffset(2025, 9, 17, 12, 0, 0, TimeSpan.Zero),
                agentId: "codex",
                parserVersion: "codex-official/2"),
            CreateEvent(
                "kept-current-parser-history",
                new DateTimeOffset(2025, 9, 18, 12, 0, 0, TimeSpan.Zero),
                agentId: "codex",
                parserVersion: "codex-local/7"),
            CreateEvent(
                "kept-recent-event",
                new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero),
                agentId: "codex",
                parserVersion: "codex-official/2"),
        ]);
        var now = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

        int deleted = await repository.ApplyRetentionIfDueAsync(
            now,
            TimeSpan.Zero, retentionDays: 35);

        Assert.Equal(2, deleted);
        IReadOnlyList<DailyUsageRollup> rollups = await repository
            .QueryDailyRollupsByAgentAsync(
                new DateOnly(2025, 1, 1),
                new DateOnly(2026, 12, 31),
                new AgentId("codex"));
        Assert.Contains(rollups, rollup => rollup.Date == new DateOnly(2025, 9, 17));
        Assert.Contains(rollups, rollup => rollup.Date == new DateOnly(2025, 9, 18));
        Assert.Contains(rollups, rollup => rollup.Date == new DateOnly(2026, 8, 27));
    }

    [Fact]
    public async Task LargeIngestBatchSkipsTombstonedKeysAndKeepsOneRollup()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent retired = CreateEvent(
            "retired-key",
            new DateTimeOffset(2025, 6, 16, 12, 0, 0, TimeSpan.Zero));
        await repository.IngestAsync([retired]);
        await repository.ApplyRetentionAsync(
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero));

        UsageEvent[] batch = Enumerable.Range(0, 500)
            .Select(index => CreateEvent($"batch-{index}"))
            .Append(retired)
            .ToArray();
        UsageIngestResult result = await repository.IngestAsync(batch);

        Assert.Equal(new UsageIngestResult(500, 1), result);
        Assert.Equal(501, (await repository.QueryDailyRollupsAsync(
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 12, 31))).Sum(rollup => rollup.EventCount));
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

        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var query = new UsageReportQuery(folder.DatabasePath);
        UsageReport before = await query.ReadAsync(new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31), includeConfigurations: true);

        int deleted = await repository.ApplyRetentionAsync(
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            batchSize: 1);
        int deletedAgain = await repository.ApplyRetentionAsync(
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            batchSize: 1);

        Assert.Equal(1, deleted);
        Assert.Equal(0, deletedAgain);
        UsageReport after = await query.ReadAsync(new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31), includeConfigurations: true);
        Assert.Equal(before.Totals, after.Totals);
        Assert.NotNull(before.ConfigurationCoverage);
        Assert.NotNull(after.ConfigurationCoverage);
        Assert.Equal(0, before.ConfigurationCoverage.MissingRecords);
        Assert.Equal(1, after.ConfigurationCoverage.MissingRecords);
        Assert.Equal(1, after.ConfigurationCoverage.AvailableRecords);
        Assert.NotEqual(before.DataRevision, after.DataRevision);
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

    [Fact]
    public async Task UpgradeFencesAnExistingLegacyConnectionAndRevisionCommitsWithFacts()
    {
        using var folder = new TemporaryFolder();
        UsageRepository initial = await UsageRepository.OpenAsync(folder.DatabasePath);
        await initial.IngestAsync([CreateEvent("legacy-fact")]);
        await using var legacy = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False");
        await legacy.OpenAsync();
        await RemoveRevisionSchemaAsync(legacy);
        UsageRepository upgraded = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageDataRevision baseline = await upgraded.ReadDataRevisionAsync();
        Assert.Equal(1, baseline.Sequence); // Schema 7 invalidates existing report cursors.
        await using SqliteCommand command = legacy.CreateCommand();
        command.CommandText = "UPDATE usage_event SET parser_version = 'old-writer';";
        SqliteException rejected = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("tokenusage_writer_schema", rejected.Message, StringComparison.Ordinal);
        Assert.Equal(baseline, await upgraded.ReadDataRevisionAsync());
        await upgraded.IngestAsync([CreateEvent("new-fact")]);
        UsageDataRevision committed = await upgraded.ReadDataRevisionAsync();
        Assert.Equal(baseline.DatabaseId, committed.DatabaseId);
        Assert.True(committed.Sequence > baseline.Sequence);
        var day = new DateOnly(2026, 7, 22);
        UsageReportReadSnapshot snapshot = await upgraded.ReadDailyReportSnapshotAsync(day, day);
        Assert.Equal(committed, snapshot.DataRevision);
        Assert.Equal(2, snapshot.Rollups.Sum(row => row.EventCount));
        command.CommandText = """
            CREATE TRIGGER fail_rollup BEFORE INSERT ON daily_usage_rollup
            BEGIN SELECT RAISE(ABORT, 'synthetic interrupted ingest'); END;
            """;
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<SqliteException>(() => upgraded.IngestAsync([CreateEvent("rolled-back-fact")]));
        Assert.Equal(committed, await upgraded.ReadDataRevisionAsync());
        Assert.Equal(2, (await upgraded.QueryDailyRollupsAsync(day, day)).Sum(row => row.EventCount));
        Assert.Equal(2, (await upgraded.QueryUsageEventsAsync(
            new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero))).Count);
    }

    [Fact]
    public async Task DetailUpgradeKeepsLegacyFactsAndRoundTripsTypedMetadata()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent original = CreateEvent("detail-legacy");
        await repository.IngestAsync([original]);
        UsageDataRevision before = await repository.ReadDataRevisionAsync();
        await using (var setup = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await setup.OpenAsync();
            setup.CreateFunction("tokenusage_writer_schema", () => UsageRepository.CurrentSchemaVersion);
            await using SqliteCommand command = setup.CreateCommand();
            command.CommandText = """
                ALTER TABLE usage_event DROP COLUMN source_instance_id;
                ALTER TABLE usage_event DROP COLUMN record_kind;
                ALTER TABLE usage_event DROP COLUMN representation_revision;
                ALTER TABLE usage_event DROP COLUMN input_availability;
                ALTER TABLE usage_event DROP COLUMN output_availability;
                ALTER TABLE usage_event DROP COLUMN reasoning_availability;
                ALTER TABLE usage_event DROP COLUMN cache_read_availability;
                ALTER TABLE usage_event DROP COLUMN cache_write_availability;
                ALTER TABLE usage_collection_state DROP COLUMN last_successful_at_utc;
                DELETE FROM schema_migration WHERE version >= 7;
                CREATE TRIGGER fail_detail_upgrade BEFORE UPDATE ON usage_data_revision
                BEGIN SELECT RAISE(ABORT, 'synthetic migration failure'); END;
                """;
            await command.ExecuteNonQueryAsync();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = 'fail_detail_upgrade';";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
            command.CommandText = "UPDATE usage_data_revision SET sequence = sequence + 1;";
            await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        }
        await using (var inspect = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await inspect.OpenAsync();
            await using SqliteCommand check = inspect.CreateCommand();
            check.CommandText = "SELECT MAX(version) FROM schema_migration;";
            Assert.Equal(6L, await check.ExecuteScalarAsync());
        }
        await Assert.ThrowsAsync<SqliteException>(() => UsageRepository.OpenAsync(folder.DatabasePath));
        string backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(folder.DatabasePath)!, $"*.pre-v{UsageRepository.CurrentSchemaVersion}-*.db"));
        byte[] backupHash = SHA256.HashData(await File.ReadAllBytesAsync(backup));
        await using (var verify = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await verify.OpenAsync();
            await using SqliteCommand check = verify.CreateCommand();
            check.CommandText = "SELECT MAX(version) FROM schema_migration;";
            Assert.Equal(6L, await check.ExecuteScalarAsync());
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('usage_event') WHERE name = 'record_kind';";
            Assert.Equal(0L, await check.ExecuteScalarAsync());
            check.CommandText = "DROP TRIGGER fail_detail_upgrade;";
            await check.ExecuteNonQueryAsync();
        }
        await using (var copy = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False"))
        {
            await copy.OpenAsync();
            await using SqliteCommand check = copy.CreateCommand();
            check.CommandText = "SELECT MAX(version) FROM schema_migration;";
            Assert.Equal(6L, await check.ExecuteScalarAsync());
            check.CommandText = "SELECT event_key FROM usage_event;";
            Assert.Equal(original.EventKey.Value, await check.ExecuteScalarAsync());
        }
        repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        DateTimeOffset from = original.OccurredAtUtc.AddHours(-1), to = original.OccurredAtUtc.AddHours(1);
        Assert.Equal(original, Assert.Single(await repository.QueryUsageEventsAsync(from, to)));
        UsageDataRevision after = await repository.ReadDataRevisionAsync();
        Assert.Equal(before.DatabaseId, after.DatabaseId);
        Assert.True(after.Sequence > before.Sequence);
        var detail = new UsageDetailMetadata(null,
            UsageRecordKind.IntervalDelta, 1, UsageComponentAvailability.Measured,
            UsageComponentAvailability.Measured, UsageComponentAvailability.Unknown,
            UsageComponentAvailability.Measured, UsageComponentAvailability.Unavailable);
        UsageEvent enriched = CreateEvent("detail-legacy", detailMetadata: detail);
        await repository.UpsertAgentEventsAsync(enriched.AgentId, [enriched]);
        Assert.Equal(enriched, Assert.Single(await repository.QueryUsageEventsAsync(from, to)));
        Assert.Equal(original.Tokens, enriched.Tokens);
        Assert.Equal(original.Cost, enriched.Cost);
        await using var invalid = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False");
        await invalid.OpenAsync();
        invalid.CreateFunction("tokenusage_writer_schema", () => UsageRepository.CurrentSchemaVersion);
        await using SqliteCommand invalidCommand = invalid.CreateCommand();
        invalidCommand.CommandText = "UPDATE usage_event SET input_availability = 9;";
        await Assert.ThrowsAsync<SqliteException>(() => invalidCommand.ExecuteNonQueryAsync());
        Assert.Equal(enriched, Assert.Single(await repository.QueryUsageEventsAsync(from, to)));
        Assert.Equal(backupHash, SHA256.HashData(await File.ReadAllBytesAsync(backup)));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(folder.DatabasePath)!, $"*.pre-v{UsageRepository.CurrentSchemaVersion}-*.db").Length);
        string cleanBackup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(folder.DatabasePath)!, $"*.pre-v{UsageRepository.CurrentSchemaVersion}-*.db"), path => path != backup);
        string recoveredPath = Path.Combine(Path.GetDirectoryName(folder.DatabasePath)!, "recovered.db");
        UsageRepository recovered = await UsageRepository.RecoverCopyAsync(cleanBackup, recoveredPath);
        Assert.Equal(original, Assert.Single(await recovered.QueryUsageEventsAsync(from, to)));
        Assert.NotEqual((await repository.ReadDataRevisionAsync()).DatabaseId, (await recovered.ReadDataRevisionAsync()).DatabaseId);
        await Assert.ThrowsAsync<IOException>(() => UsageRepository.RecoverCopyAsync(cleanBackup, folder.DatabasePath));
        Assert.Equal(enriched, Assert.Single(await repository.QueryUsageEventsAsync(from, to)));
    }

    private static async Task RemoveRevisionSchemaAsync(SqliteConnection connection)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'trigger' AND (name LIKE 'guard_%' OR name LIKE 'revision_%');";
        var names = new List<string>();
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        foreach (string name in names)
        {
            command.CommandText = "DROP TRIGGER \"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\";";
            await command.ExecuteNonQueryAsync();
        }
        command.CommandText = "DROP TABLE usage_data_revision; ALTER TABLE usage_event DROP COLUMN source_instance_id; ALTER TABLE usage_event DROP COLUMN record_kind; ALTER TABLE usage_event DROP COLUMN representation_revision; ALTER TABLE usage_event DROP COLUMN input_availability; ALTER TABLE usage_event DROP COLUMN output_availability; ALTER TABLE usage_event DROP COLUMN reasoning_availability; ALTER TABLE usage_event DROP COLUMN cache_read_availability; ALTER TABLE usage_event DROP COLUMN cache_write_availability; ALTER TABLE usage_collection_state DROP COLUMN last_successful_at_utc; DELETE FROM schema_migration WHERE version >= 6;";
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task VersionThirteenDropsRemovedTablesAndMarksClaudeMessageTimes()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent("claude-message", agentId: "claude", parserVersion: "claude-jsonl/4"),
            CreateEvent("grok-session"),
        ]);
        await using (var setup = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await setup.OpenAsync();
            setup.CreateFunction("tokenusage_writer_schema", () => UsageRepository.CurrentSchemaVersion);
            await using SqliteCommand command = setup.CreateCommand();
            command.CommandText = """
                CREATE TABLE operation_fact (event_key TEXT);
                CREATE TABLE saved_usage_comparison (revision_id TEXT);
                UPDATE usage_event SET time_precision = 0;
                DELETE FROM schema_migration WHERE version = 13;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await UsageRepository.OpenAsync(folder.DatabasePath);

        await using var verify = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False");
        await verify.OpenAsync();
        await using SqliteCommand check = verify.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('operation_fact', 'saved_usage_comparison');";
        Assert.Equal(0L, await check.ExecuteScalarAsync());
        check.CommandText = "SELECT group_concat(agent_id || ':' || time_precision, ',') FROM (SELECT agent_id, time_precision FROM usage_event ORDER BY agent_id);";
        Assert.Equal("claude:1,grok:0", await check.ExecuteScalarAsync());
    }

    [Fact]
    public async Task CollectionFreshnessMigrationAndOutOfOrderAttemptsPreserveLastSuccess()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var first = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        await repository.RecordCollectionAsync(new("codex", first, UsageSourceReadStatus.Complete, UsageSourceIssueKind.None));
        await repository.RecordCollectionAsync(new("claude", first, UsageSourceReadStatus.Partial, UsageSourceIssueKind.ReadFailed));
        await using (var setup = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await setup.OpenAsync();
            setup.CreateFunction("tokenusage_writer_schema", () => UsageRepository.CurrentSchemaVersion);
            await using var command = setup.CreateCommand();
            command.CommandText = """
                ALTER TABLE usage_collection_state DROP COLUMN last_successful_at_utc;
                DELETE FROM schema_migration WHERE version >= 8;
                CREATE TRIGGER fail_freshness BEFORE UPDATE ON usage_collection_state
                BEGIN SELECT RAISE(ABORT, 'freshness migration failure'); END;
                """;
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<SqliteException>(() => UsageRepository.OpenAsync(folder.DatabasePath));
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('usage_collection_state') WHERE name = 'last_successful_at_utc';";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
            command.CommandText = "SELECT MAX(version) FROM schema_migration;";
            Assert.Equal(7L, await command.ExecuteScalarAsync());
            command.CommandText = "DROP TRIGGER fail_freshness;";
            await command.ExecuteNonQueryAsync();
        }
        repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var migrated = await repository.ReadCollectionStateAsync();
        Assert.Equal(first, Assert.Single(migrated, row => row.AgentId == "codex").LastSuccessfulAtUtc);
        Assert.Null(Assert.Single(migrated, row => row.AgentId == "claude").LastSuccessfulAtUtc);
        await repository.RecordCollectionAsync(new("codex", first.AddHours(3), UsageSourceReadStatus.Partial, UsageSourceIssueKind.ReadFailed));
        await repository.RecordCollectionAsync(new("codex", first.AddHours(1), UsageSourceReadStatus.Complete, UsageSourceIssueKind.None));
        UsageCollectionState latest = Assert.Single(await repository.ReadCollectionStateAsync(), row => row.AgentId == "codex");
        Assert.Equal(first.AddHours(3), latest.AttemptedAtUtc);
        Assert.Equal(first.AddHours(1), latest.LastSuccessfulAtUtc);
        Assert.Equal(UsageSourceReadStatus.Partial, latest.Status);
        Assert.Equal(UsageSourceIssueKind.ReadFailed, latest.Issue);
        UsageDataRevision revision = await repository.ReadDataRevisionAsync();
        await repository.RecordCollectionAsync(new("codex", first, UsageSourceReadStatus.Complete, UsageSourceIssueKind.None));
        Assert.Equal(revision, await repository.ReadDataRevisionAsync());
        await repository.RecordCollectionAsync(new("codex", first.AddHours(4), UsageSourceReadStatus.NoData, UsageSourceIssueKind.Empty));
        latest = Assert.Single(await repository.ReadCollectionStateAsync(), row => row.AgentId == "codex");
        Assert.Equal(first.AddHours(4), latest.LastSuccessfulAtUtc);
        Assert.Equal(latest.AttemptedAtUtc, latest.LastSuccessfulAtUtc);
    }

    [Fact]
    public async Task LegacyAssociationRequiresMatchingIdentityAndRepresentationAndKeepsTotals()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var source = new UsageSourceInstanceId(new string('c', 64));
        UsageEvent original = CreateEvent("associate");
        await repository.IngestAsync([original]);
        var day = new DateOnly(2026, 7, 22);
        DailyUsageRollup totals = Assert.Single(await repository.QueryDailyRollupsAsync(day, day));
        UsageDataRevision revision = await repository.ReadDataRevisionAsync();
        Assert.Equal(0, await repository.AssociateLegacyObservationsAsync(original.AgentId, source,
            [CreateEvent("different-identity", detailMetadata: new(source)),
             CreateEvent("associate", parserVersion: "different/2", detailMetadata: new(source)),
             CreateEvent("associate", cost: CostObservation.ProviderReported(0.5m), detailMetadata: new(source)),
             CreateEvent("associate", tokens: new TokenBreakdown(101, 25, 5, 20, 0), detailMetadata: new(source))]));
        Assert.Equal(revision, await repository.ReadDataRevisionAsync());
        UsageEvent observed = CreateEvent("associate", detailMetadata: new(source));
        Assert.Equal(1, await repository.AssociateLegacyObservationsAsync(original.AgentId, source, [observed]));
        Assert.Equal(0, await repository.AssociateLegacyObservationsAsync(original.AgentId, source, [observed]));
        Assert.Equal(totals, Assert.Single(await repository.QueryDailyRollupsAsync(day, day)));
        Assert.Equal(observed, Assert.Single(await repository.QueryUsageEventsAsync(original.OccurredAtUtc.AddHours(-1), original.OccurredAtUtc.AddHours(1))));
        Assert.NotEqual(revision, await repository.ReadDataRevisionAsync());
        var other = new UsageSourceInstanceId(new string('d', 64));
        Assert.Equal(0, await repository.AssociateLegacyObservationsAsync(original.AgentId, other,
            [CreateEvent("associate", detailMetadata: new(other))]));
    }

    [Fact]
    public async Task SourceAdmissionAssociatesExactHistoryAndWithholdsOverlapAtomically()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var source = new UsageSourceInstanceId(new string('c', 64));
        UsageEvent legacy = CreateEvent("admission-match");
        UsageEvent ambiguous = CreateEvent("admission-unknown");
        await repository.IngestAsync([legacy, ambiguous]);
        UsageEvent matched = CreateEvent("admission-match", detailMetadata: new(source));
        UsageEvent overlap = CreateEvent("admission-other-key", detailMetadata: new(source));
        UsageEvent fresh = CreateEvent("admission-next-day", legacy.OccurredAtUtc.AddDays(1), detailMetadata: new(source));
        var day = new DateOnly(2026, 7, 22);
        UsageDataRevision before = await repository.ReadDataRevisionAsync();
        await using var setup = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False");
        await setup.OpenAsync();
        await using var trigger = setup.CreateCommand();
        trigger.CommandText = "CREATE TRIGGER fail_source_admission BEFORE INSERT ON usage_event BEGIN SELECT RAISE(ABORT, 'admission failure'); END;";
        await trigger.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<SqliteException>(() => repository.StoreSourceObservationsAsync(legacy.AgentId,
            source, legacy.ParserVersion, day, day.AddDays(1), [matched, overlap, fresh], complete: true));
        Assert.Equal(before, await repository.ReadDataRevisionAsync());
        var from = legacy.OccurredAtUtc.AddHours(-12);
        Assert.All(await repository.QueryUsageEventsAsync(from, from.AddDays(2)), row => Assert.Null(row.DetailMetadata.SourceInstance));
        trigger.CommandText = "DROP TRIGGER fail_source_admission;";
        await trigger.ExecuteNonQueryAsync();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            UsageSourceStoreResult result = await repository.StoreSourceObservationsAsync(legacy.AgentId,
                source, legacy.ParserVersion, day, day.AddDays(1), [matched, overlap, fresh], complete: true);
            Assert.Equal(1, result.WithheldCount);
            Assert.True(result.HasUnresolvedHistory);
            var rows = await repository.QueryUsageEventsAsync(from, from.AddDays(2));
            Assert.Equal(3, rows.Count);
            Assert.Contains(ambiguous, rows);
            Assert.Contains(matched, rows);
            Assert.Contains(fresh, rows);
            Assert.Equal(450, (await repository.QueryDailyRollupsAsync(day, day.AddDays(1))).Sum(row => row.Tokens.Total));
        }
        var empty = await repository.StoreSourceObservationsAsync(legacy.AgentId, source, legacy.ParserVersion,
            day, day.AddDays(1), [], complete: true);
        Assert.True(empty.HasUnresolvedHistory);
        Assert.Equal(3, (await repository.QueryUsageEventsAsync(from, from.AddDays(2))).Count);
        await repository.UpsertAgentEventsAsync(legacy.AgentId, [fresh], [ambiguous.EventKey]);
        var resolved = await repository.StoreSourceObservationsAsync(legacy.AgentId, source, legacy.ParserVersion,
            day, day.AddDays(1), [fresh], complete: true);
        Assert.False(resolved.HasUnresolvedHistory);
        Assert.Equal(fresh, Assert.Single(await repository.QueryUsageEventsAsync(from, from.AddDays(2))));
    }

    [Fact]
    public async Task SourceReconciliationPreservesOtherSourcesAndRejectsKeyReassignment()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var sourceA = new UsageSourceInstanceId(new string('a', 64));
        var sourceB = new UsageSourceInstanceId(new string('b', 64));
        UsageEvent a = CreateEvent("scope-a", detailMetadata: new(sourceA));
        UsageEvent b = CreateEvent("scope-b", detailMetadata: new(sourceB));
        UsageEvent legacy = CreateEvent("scope-legacy", a.OccurredAtUtc.AddDays(1));
        await repository.IngestAsync([a, b, legacy]);
        var day = new DateOnly(2026, 7, 22);
        UsageSourceStoreResult reconciled = await repository.StoreSourceObservationsAsync(a.AgentId, sourceA,
            a.ParserVersion, day, day, [], complete: true);
        Assert.False(reconciled.HasUnresolvedHistory);
        var from = new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<UsageEvent> kept = await repository.QueryUsageEventsAsync(from, from.AddDays(2));
        Assert.Equal(2, kept.Count);
        Assert.Contains(b, kept);
        Assert.Contains(legacy, kept);
        Assert.Equal(1, Assert.Single(await repository.QueryDailyRollupsAsync(day, day)).EventCount);
        UsageDataRevision before = await repository.ReadDataRevisionAsync();
        UsageEvent stolen = CreateEvent("scope-b", detailMetadata: new(sourceA));
        UsageEvent claimedLegacy = CreateEvent("scope-legacy", legacy.OccurredAtUtc, detailMetadata: new(sourceA));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.UpsertAgentEventsAsync(a.AgentId, [claimedLegacy]));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.IngestAsync([stolen]));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.UpsertAgentEventsAsync(a.AgentId, [stolen]));
        Assert.Equal(before, await repository.ReadDataRevisionAsync());
        Assert.Equal(b, Assert.Single(await repository.QueryUsageEventsAsync(from, from.AddDays(1))));
    }

    [Fact]
    public async Task HistoryRecoveryPreservesRecordedPricesAndRetiredTotalsWhileMergingNumericGrowth()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var agent = new AgentId("claude");
        UsageEvent baseline = CreateEvent("stream", now.AddMinutes(-10), agentId: "claude",
            cost: CostObservation.CatalogEstimated(0.5m, "original", "model-rate"));
        await repository.IngestAsync([baseline]);
        UsageEvent replay = CreateEvent("stream", now.AddMinutes(-9), agentId: "claude",
            cost: CostObservation.CatalogEstimated(9m, "new", "model-rate"),
            detailMetadata: new UsageDetailMetadata(input: UsageComponentAvailability.Measured,
                output: UsageComponentAvailability.Measured, reasoning: UsageComponentAvailability.Unavailable));
        UsageRecoveryResult existing = await repository.RecoverAgentEventsAsync(agent, [replay], now);
        Assert.Equal(1, existing.Existing);
        UsageEvent saved = Assert.Single(await repository.QueryUsageEventsAsync(now.AddHours(-1), now));
        Assert.Equal(baseline.Cost, saved.Cost);
        Assert.Equal(baseline.OccurredAtUtc, saved.OccurredAtUtc);
        Assert.Equal(UsageComponentAvailability.Measured, saved.DetailMetadata.Input);
        Assert.Equal(UsageComponentAvailability.Measured, saved.DetailMetadata.Output);
        Assert.Equal(UsageComponentAvailability.Unavailable, saved.DetailMetadata.Reasoning);

        UsageEvent growth = CreateEvent("stream", now.AddMinutes(-8),
            tokens: new TokenBreakdown(100, 30, 5, 20, 0), agentId: "claude",
            cost: CostObservation.CatalogEstimated(0.75m, "original", "model-rate"));
        await repository.RecoverAgentEventsAsync(agent, [growth], now);
        saved = Assert.Single(await repository.QueryUsageEventsAsync(now.AddHours(-1), now));
        Assert.Equal(30, saved.Tokens.Output);
        Assert.Equal(0.75m, saved.Cost.EstimatedCostUsd);
        growth = CreateEvent("stream", now.AddMinutes(-7),
            tokens: new TokenBreakdown(100, 35, 5, 20, 0), agentId: "claude",
            cost: CostObservation.CatalogEstimated(9m, "new", "model-rate"));
        UsageRecoveryResult preserved = await repository.RecoverAgentEventsAsync(agent, [growth], now);
        Assert.Equal(1, preserved.UnpricedGrowth);
        Assert.Equal(1, preserved.Exclusions.ConflictingPriceEvidence);
        Assert.Equal(1, preserved.Excluded);
        saved = Assert.Single(await repository.QueryUsageEventsAsync(now.AddHours(-1), now));
        Assert.Equal(30, saved.Tokens.Output);
        Assert.Equal(0.75m, saved.Cost.EstimatedCostUsd);
        Assert.Equal("original", saved.Cost.CatalogVersion);

        UsageEvent retired = CreateEvent("retired", now.AddDays(-2), agentId: "claude");
        await repository.IngestAsync([retired]);
        await repository.ApplyRetentionAsync(now, retentionDays: 1);
        DateOnly day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(retired.OccurredAtUtc,
            TimeZoneInfo.FindSystemTimeZoneById(retired.GroupingTimeZoneId)).DateTime);
        DailyUsageRollup before = Assert.Single(await repository.QueryDailyRollupsByAgentAsync(day, day, agent));
        UsageRecoveryResult excluded = await repository.RecoverAgentEventsAsync(agent,
            [retired, CreateEvent("new-on-retired-day", now.AddDays(-2), agentId: "claude"),
                CreateEvent("outside-retention", now.AddDays(-401), agentId: "claude")], now);
        Assert.Equal(3, excluded.Excluded);
        Assert.Equal(1, excluded.Exclusions.OutsideRetention);
        Assert.Equal(1, excluded.Exclusions.RetiredDetails);
        Assert.Equal(1, excluded.Exclusions.Tombstones);
        Assert.Equal(0, excluded.Added);
        Assert.Equal(before, Assert.Single(await repository.QueryDailyRollupsByAgentAsync(day, day, agent)));
    }

    private static UsageEvent CreateEvent(
        string localIdentity,
        DateTimeOffset? occurredAtUtc = null,
        TokenBreakdown? tokens = null,
        string agentId = "grok",
        string parserVersion = "fixture/1",
        CostObservation? cost = null, UsageDetailMetadata? detailMetadata = null,
        string modelId = "grok-4.5") =>
        new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(localIdentity))).ToLowerInvariant()),
            new AgentId(agentId),
            new ModelProviderId("xai"),
            new ModelId(modelId),
            occurredAtUtc ?? new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            "Argentina Standard Time",
            tokens ?? new TokenBreakdown(100, 25, 5, 20, 0),
            cost ?? CostObservation.ProviderReported(0.25m),
            parserVersion,
            CoverageKind.Complete,
            UsageTimePrecision.Timestamp, detailMetadata: detailMetadata);

    private static async Task<string> ExplainAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        var details = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(reader.FieldCount - 1));
        }

        return string.Join('\n', details);
    }

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
