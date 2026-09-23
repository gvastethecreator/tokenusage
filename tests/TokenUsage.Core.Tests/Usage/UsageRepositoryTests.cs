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
        var filtered = TokenUsage.Core.Automation.UsageReportQuery.FilterByAgent(report, new AgentId("grok"));
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
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReconcileSourceEventRangeAsync(
            new AgentId("grok"), source, "grok-local/1", retiredDay, retiredDay, []));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReconcileSourceEventRangeAsync(
            new AgentId("grok"), source, "grok-local/1", retiredDay, retiredDay,
            [CreateEvent("changed-parser-identity", new DateTimeOffset(2025, 6, 16, 12, 0, 0, TimeSpan.Zero),
                parserVersion: "grok-local/1", detailMetadata: new(source))]));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReconcileAgentEventRangeAsync(
            new AgentId("grok"), "grok-local/1", retiredDay, retiredDay, []));
        Assert.Equal(retained, Assert.Single(await repository.QueryDailyRollupsAsync(retiredDay, retiredDay)));
        Assert.Equal(retainedRevision, await repository.ReadDataRevisionAsync());

        UsageIngestResult result = await repository.ReconcileAgentEventRangeAsync(
            new AgentId("grok"),
            "grok-local/1",
            new DateOnly(2026, 6, 23),
            new DateOnly(2026, 7, 22),
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
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReplaceAgentEventsAsync(replay.AgentId,
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
    public async Task ReconcilingEventMovedIntoRangeRemovesItsPreviousRollup()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateEvent(
                "claude-stream",
                new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                agentId: "claude",
                parserVersion: "claude-jsonl/2"),
        ]);

        await repository.ReconcileAgentEventRangeAsync(
            new AgentId("claude"),
            "claude-jsonl/2",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 22),
            [CreateEvent(
                "claude-stream",
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
                agentId: "claude",
                parserVersion: "claude-jsonl/2")]);

        DailyUsageRollup rollup = Assert.Single(
            await repository.QueryDailyRollupsByAgentAsync(
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 7, 22),
                new AgentId("claude")));
        Assert.Equal(new DateOnly(2026, 7, 22), rollup.Date);
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
                VALUES (13, '2026-07-22T12:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        UsageSchemaTooNewException error = await Assert.ThrowsAsync<UsageSchemaTooNewException>(
            () => UsageRepository.OpenAsync(folder.DatabasePath));

        Assert.Equal(13, error.ActualVersion);
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
                DROP INDEX ix_usage_event_model_time;
                ALTER TABLE usage_event DROP COLUMN time_precision;
                ALTER TABLE usage_event DROP COLUMN interval_started_at_utc;
                ALTER TABLE usage_event DROP COLUMN observed_model_id;
                ALTER TABLE usage_event DROP COLUMN reasoning_effort;
                ALTER TABLE usage_event DROP COLUMN service_tier;
                DROP TABLE account_usage_daily;
                DROP TABLE saved_usage_comparison;
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
                DROP TABLE saved_usage_comparison;
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

        Assert.Equal(12, UsageRepository.CurrentSchemaVersion);
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
        UsageEventPage firstPage = await repository.ReadUsageEventPageAsync(from, to, pageSize: 1);
        Assert.NotNull(firstPage.Next);
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
        await Assert.ThrowsAsync<UsageDataChangedException>(() => repository.ReadUsageEventPageAsync(from, to, cursor: firstPage.Next));
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
    public async Task EventPagesKeepStableTiesAndRejectChangedDataOrSelection()
    {
        using var folder = new TemporaryFolder();
        UsageRepository writer = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent[] events = [CreateEvent("page-a"), CreateEvent("page-b"),
            CreateEvent("page-c", agentId: "codex"), CreateEvent("page-d")];
        await writer.IngestAsync(events);
        UsageRepository reader = await UsageRepository.OpenReadOnlyAsync(folder.DatabasePath);
        var from = new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = from.AddDays(1);
        UsageEventPage first = await reader.ReadUsageEventPageAsync(from, to, pageSize: 2);
        Assert.Equal(2, first.Events.Count);
        Assert.NotNull(first.Next);
        UsageEventPage second = await reader.ReadUsageEventPageAsync(from, to, pageSize: 2, cursor: first.Next);
        Assert.Equal(2, second.Events.Count);
        Assert.Null(second.Next);
        Assert.Equal((await reader.QueryUsageEventsAsync(from, to)).Select(row => row.EventKey),
            first.Events.Concat(second.Events).Select(row => row.EventKey));
        Assert.Equal(first.Revision, second.Revision);
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadUsageEventPageAsync(from, to,
            new AgentId("grok"), cursor: first.Next));
        using var replacementFolder = new TemporaryFolder();
        UsageRepository replacement = await UsageRepository.OpenAsync(replacementFolder.DatabasePath);
        await replacement.IngestAsync(events);
        Assert.Equal(first.Revision.Sequence, (await replacement.ReadDataRevisionAsync()).Sequence);
        await Assert.ThrowsAsync<UsageDataChangedException>(() => replacement.ReadUsageEventPageAsync(from, to, cursor: first.Next));
        await writer.IngestAsync([CreateEvent("page-later")]);
        UsageDataChangedException changed = await Assert.ThrowsAsync<UsageDataChangedException>(() =>
            reader.ReadUsageEventPageAsync(from, to, cursor: first.Next));
        Assert.Equal(first.Revision, changed.Expected);
        Assert.Equal(await writer.ReadDataRevisionAsync(), changed.Actual);
        UsageEventPage restarted = await reader.ReadUsageEventPageAsync(from, to);
        Assert.Equal(5, restarted.Events.Count);
        Assert.Null(restarted.Next);
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
                DROP TABLE IF EXISTS session_attribution;
                DROP TABLE IF EXISTS project_attribution;
                DROP TABLE IF EXISTS operation_fact;
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
        string backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(folder.DatabasePath)!, "*.pre-v12-*.db"));
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
        Assert.Equal(enriched, Assert.Single((await repository.ReadUsageEventPageAsync(from, to)).Events));
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
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(folder.DatabasePath)!, "*.pre-v12-*.db").Length);
        string cleanBackup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(folder.DatabasePath)!, "*.pre-v12-*.db"), path => path != backup);
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
        command.CommandText = "DROP TABLE IF EXISTS operation_fact; DROP TABLE IF EXISTS project_attribution; DROP TABLE IF EXISTS session_attribution; DROP TABLE usage_data_revision; ALTER TABLE usage_event DROP COLUMN source_instance_id; ALTER TABLE usage_event DROP COLUMN record_kind; ALTER TABLE usage_event DROP COLUMN representation_revision; ALTER TABLE usage_event DROP COLUMN input_availability; ALTER TABLE usage_event DROP COLUMN output_availability; ALTER TABLE usage_event DROP COLUMN reasoning_availability; ALTER TABLE usage_event DROP COLUMN cache_read_availability; ALTER TABLE usage_event DROP COLUMN cache_write_availability; ALTER TABLE usage_collection_state DROP COLUMN last_successful_at_utc; DELETE FROM schema_migration WHERE version >= 6;";
        await command.ExecuteNonQueryAsync();
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
                DROP TABLE IF EXISTS session_attribution;
                DROP TABLE IF EXISTS project_attribution;
                DROP TABLE IF EXISTS operation_fact;
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
        await repository.ReconcileAgentEventRangeAsync(legacy.AgentId, legacy.ParserVersion, day, day, []);
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
        UsageEvent legacy = CreateEvent("scope-legacy");
        await repository.IngestAsync([a, b, legacy]);
        var day = new DateOnly(2026, 7, 22);
        await repository.ReconcileSourceEventRangeAsync(a.AgentId, sourceA, a.ParserVersion, day, day, []);
        var from = new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, (await repository.QueryUsageEventsAsync(from, from.AddDays(1))).Count);
        Assert.Equal(2, Assert.Single(await repository.QueryDailyRollupsAsync(day, day)).EventCount);
        UsageDataRevision before = await repository.ReadDataRevisionAsync();
        UsageEvent stolen = CreateEvent("scope-b", detailMetadata: new(sourceA));
        UsageEvent claimedLegacy = CreateEvent("scope-legacy", detailMetadata: new(sourceA));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.UpsertAgentEventsAsync(a.AgentId, [claimedLegacy]));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReconcileSourceEventRangeAsync(
            a.AgentId, sourceA, a.ParserVersion, day, day, [stolen]));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.UpsertAgentEventsAsync(a.AgentId, [stolen]));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReplaceAgentEventsAsync(a.AgentId, [legacy]));
        Assert.Equal(before, await repository.ReadDataRevisionAsync());
        await repository.ReconcileAgentEventRangeAsync(a.AgentId, a.ParserVersion, day, day, []);
        Assert.Equal(b, Assert.Single(await repository.QueryUsageEventsAsync(from, from.AddDays(1))));
    }

    [Fact]
    public async Task SessionLinksStayOffUntilConsentAndPurgeKeepsRollupTotals()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent linked = CreateEvent("session-link", agentId: "codex");
        UsageEvent unassigned = CreateEvent("session-unassigned", agentId: "codex");
        await repository.IngestAsync([linked, unassigned]);
        DailyUsageRollup before = Assert.Single(await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22)));
        var sessionKey = DeriveKey(OpaqueKeyDomains.CodexSession, "alpha");
        await repository.ReplaceSessionLinksAsync(
        [
            new UsageSessionLink(linked.EventKey, sessionKey, parentSessionKey: null, consentEpoch: 1),
        ]);
        Assert.Equal(1, await repository.CountSessionLinksAsync(1));
        IReadOnlyList<UsageSessionContribution> rows = await repository.ReadSessionContributionsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            1,
            new AgentId("codex"));
        Assert.Contains(rows, row => row.IsUnassigned && row.SelectedTokens.Total == unassigned.Tokens.Total);
        UsageSessionContribution session = Assert.Single(rows, row => !row.IsUnassigned);
        Assert.Equal(sessionKey, session.SessionKey);
        Assert.Equal(linked.Tokens.Total, session.SelectedTokens.Total);
        Assert.Equal(linked.Tokens.Total, session.SessionTokens.Total);
        IReadOnlyList<UsageSessionContribution> staleEpoch = await repository.ReadSessionContributionsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            2,
            new AgentId("codex"));
        Assert.All(staleEpoch, row => Assert.True(row.IsUnassigned));
        Assert.Equal(1, await repository.PurgeSessionLinksAsync());
        Assert.Equal(0, await repository.CountSessionLinksAsync());
        DailyUsageRollup after = Assert.Single(await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22)));
        Assert.Equal(before.Tokens.Total, after.Tokens.Total);
        Assert.Equal(before.EventCount, after.EventCount);
        Assert.Equal(before.ReportedCostUsd, after.ReportedCostUsd);
    }

    [Fact]
    public async Task AttributionContributionsHonorModelSearch()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent sol = CreateEvent("search-sol", agentId: "codex", modelId: "gpt-6-sol");
        UsageEvent astra = CreateEvent("search-astra", agentId: "codex", modelId: "gpt-6-astra");
        await repository.IngestAsync([sol, astra]);
        OpaqueAttributionKey solSession = DeriveKey(OpaqueKeyDomains.CodexSession, "search-sol");
        OpaqueAttributionKey astraSession = DeriveKey(OpaqueKeyDomains.CodexSession, "search-astra");
        OpaqueAttributionKey solProject = DeriveKey(OpaqueKeyDomains.CodexProject, "search-sol");
        OpaqueAttributionKey astraProject = DeriveKey(OpaqueKeyDomains.CodexProject, "search-astra");
        await repository.ReplaceSessionLinksAsync([
            new UsageSessionLink(sol.EventKey, solSession, parentSessionKey: null, consentEpoch: 1),
            new UsageSessionLink(astra.EventKey, astraSession, parentSessionKey: null, consentEpoch: 1),
        ]);
        await repository.ReplaceProjectLinksAsync([
            new UsageProjectLink(sol.EventKey, solProject, 1, ProjectMappingKind.Observed),
            new UsageProjectLink(astra.EventKey, astraProject, 1, ProjectMappingKind.Observed),
        ]);
        DateOnly day = new(2026, 7, 22);
        IReadOnlyList<UsageSessionContribution> sessions = await repository.ReadSessionContributionsAsync(
            day, day, 1, new AgentId("codex"), modelSearch: "sol");
        IReadOnlyList<UsageProjectContribution> projects = await repository.ReadProjectContributionsAsync(
            day, day, 1, new AgentId("codex"), modelSearch: "sol");
        Assert.Equal(sol.Tokens.Total, Assert.Single(sessions).SelectedTokens.Total);
        Assert.Equal(sol.Tokens.Total, Assert.Single(projects).SelectedTokens.Total);
    }

    [Fact]
    public async Task RestoredBackupLinksStayHiddenFromANewConsentEpoch()
    {
        using var folder = new TemporaryFolder();
        string recoveredPath = Path.Combine(Path.GetDirectoryName(folder.DatabasePath)!, "recovered.v1.db");
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent usageEvent = CreateEvent("restored-link", agentId: "codex");
        await repository.IngestAsync([usageEvent]);
        await repository.ReplaceSessionLinksAsync(
        [
            new UsageSessionLink(
                usageEvent.EventKey,
                DeriveKey(OpaqueKeyDomains.CodexSession, "restored"),
                parentSessionKey: null,
                consentEpoch: 1),
        ]);
        UsageRepository recovered = await UsageRepository.RecoverCopyAsync(folder.DatabasePath, recoveredPath);
        Assert.Equal(1, await recovered.CountSessionLinksAsync(1));
        await repository.PurgeSessionLinksAsync();
        Assert.Equal(0, await repository.CountSessionLinksAsync());
        IReadOnlyList<UsageSessionContribution> hidden = await recovered.ReadSessionContributionsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            3,
            new AgentId("codex"));
        Assert.All(hidden, row => Assert.True(row.IsUnassigned));
        Assert.Equal(usageEvent.Tokens.Total, hidden.Sum(row => row.SelectedTokens.Total));
        IReadOnlyList<UsageSessionContribution> disabled = await recovered.ReadSessionContributionsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            0,
            new AgentId("codex"));
        Assert.All(disabled, row => Assert.True(row.IsUnassigned));
        Assert.Equal(usageEvent.Tokens.Total, disabled.Sum(row => row.SelectedTokens.Total));
    }

    [Fact]
    public async Task PurgeCursorSessionLinksLeavesCodexLinksAndTotals()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent codex = CreateEvent("codex-keep", agentId: "codex");
        UsageEvent cursor = CreateEvent("cursor-drop", agentId: "cursor");
        await repository.IngestAsync([codex, cursor]);
        await repository.ReplaceSessionLinksAsync(
        [
            new UsageSessionLink(
                codex.EventKey,
                DeriveKey(OpaqueKeyDomains.CodexSession, "codex-keep"),
                parentSessionKey: null,
                consentEpoch: 1,
                AttributionCapability.CodexSession),
            new UsageSessionLink(
                cursor.EventKey,
                DeriveKey(OpaqueKeyDomains.CursorSession, "cursor-drop"),
                parentSessionKey: null,
                consentEpoch: 1,
                AttributionCapability.CursorSession),
        ]);
        Assert.Equal(2, await repository.CountSessionLinksAsync());
        Assert.Equal(1, await repository.PurgeSessionLinksAsync(AttributionCapability.CursorSession));
        Assert.Equal(1, await repository.CountSessionLinksAsync());
        IReadOnlyList<UsageSessionContribution> codexRows = await repository.ReadSessionContributionsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            1,
            new AgentId("codex"),
            capability: AttributionCapability.CodexSession);
        Assert.Contains(codexRows, row => !row.IsUnassigned);
        IReadOnlyList<UsageSessionContribution> cursorRows = await repository.ReadSessionContributionsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            1,
            new AgentId("cursor"),
            capability: AttributionCapability.CursorSession);
        Assert.All(cursorRows, row => Assert.True(row.IsUnassigned));
        Assert.Equal(
            codex.Tokens.Total + cursor.Tokens.Total,
            (await repository.QueryDailyRollupsAsync(
                new DateOnly(2026, 7, 22),
                new DateOnly(2026, 7, 22))).Sum(row => row.Tokens.Total));
    }

    [Fact]
    public async Task SessionContributionsHonorSelectedEffortWithoutChangingWholeSessionTotals()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent low = new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("effort-low"))).ToLowerInvariant()),
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId("gpt-5"),
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            "UTC",
            new TokenBreakdown(10, 0, 0, 0, 0),
            CostObservation.ProviderReported(0.01m),
            "fixture/1",
            CoverageKind.Complete,
            UsageTimePrecision.Timestamp,
            reasoningEffort: "low");
        UsageEvent high = new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("effort-high"))).ToLowerInvariant()),
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId("gpt-5"),
            new DateTimeOffset(2026, 7, 22, 13, 0, 0, TimeSpan.Zero),
            "UTC",
            new TokenBreakdown(90, 0, 0, 0, 0),
            CostObservation.ProviderReported(0.09m),
            "fixture/1",
            CoverageKind.Complete,
            UsageTimePrecision.Timestamp,
            reasoningEffort: "high");
        await repository.IngestAsync([low, high]);
        var sessionKey = DeriveKey(OpaqueKeyDomains.CodexSession, "effort-session");
        await repository.ReplaceSessionLinksAsync(
        [
            new UsageSessionLink(low.EventKey, sessionKey, parentSessionKey: null, 1),
            new UsageSessionLink(high.EventKey, sessionKey, parentSessionKey: null, 1),
        ]);
        UsageSessionContribution selected = Assert.Single(
            await repository.ReadSessionContributionsAsync(
                new DateOnly(2026, 7, 22),
                new DateOnly(2026, 7, 22),
                1,
                new AgentId("codex"),
                detail: new UsageDetailSelection([], ["low"], [])));
        Assert.Equal(10, selected.SelectedTokens.Total);
        Assert.Equal(100, selected.SessionTokens.Total);
    }

    [Fact]
    public async Task ProjectLinksKeepObservedAmbiguousAliasAndPurgeWithoutChangingTotals()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent first = CreateEvent("project-a", agentId: "codex", tokens: new TokenBreakdown(80, 20, 0, 0, 0));
        UsageEvent second = CreateEvent("project-b", agentId: "codex", tokens: new TokenBreakdown(40, 10, 0, 0, 0));
        UsageEvent unassigned = CreateEvent("project-unassigned", agentId: "codex");
        UsageEvent ambiguous = CreateEvent("project-ambiguous", agentId: "codex", tokens: new TokenBreakdown(10, 0, 0, 0, 0));
        await repository.IngestAsync([first, second, unassigned, ambiguous]);
        DailyUsageRollup before = Assert.Single(await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22)));
        var projectA = DeriveKey(OpaqueKeyDomains.CodexProject, "workspace-a");
        var projectB = DeriveKey(OpaqueKeyDomains.CodexProject, "workspace-b");
        await repository.ReplaceProjectLinksAsync(
        [
            new UsageProjectLink(first.EventKey, projectA, 1, ProjectMappingKind.Observed),
            new UsageProjectLink(second.EventKey, projectB, 1, ProjectMappingKind.Observed),
            new UsageProjectLink(ambiguous.EventKey, null, 1, ProjectMappingKind.Ambiguous),
        ]);
        IReadOnlyList<UsageProjectContribution> rows = await repository.ReadProjectContributionsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            1,
            new AgentId("codex"));
        Assert.Contains(rows, row => row.IsUnassigned && row.SelectedTokens.Total == unassigned.Tokens.Total);
        Assert.Contains(rows, row => row.IsAmbiguous && row.SelectedTokens.Total == ambiguous.Tokens.Total);
        Assert.Equal(first.Tokens.Total, Assert.Single(rows, row => row.ProjectKey == projectA).SelectedTokens.Total);
        Assert.Equal(second.Tokens.Total, Assert.Single(rows, row => row.ProjectKey == projectB).SelectedTokens.Total);
        await repository.MapEventsToProjectAsync([second.EventKey], projectA, 1);
        UsageProjectContribution remapped = Assert.Single(
            await repository.ReadProjectContributionsAsync(
                new DateOnly(2026, 7, 22),
                new DateOnly(2026, 7, 22),
                1,
                new AgentId("codex")),
            row => row.ProjectKey == projectA && row.MappingKind == ProjectMappingKind.UserMapped);
        Assert.Equal(second.Tokens.Total, remapped.SelectedTokens.Total);
        int firstBackfill = await repository.BackfillProjectLinksAsync(
        [
            new UsageProjectLink(second.EventKey, projectB, 1, ProjectMappingKind.Observed),
        ],
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            1);
        Assert.Equal(1, firstBackfill);
        Assert.Equal(
            ProjectMappingKind.UserMapped,
            Assert.Single(
                await repository.ReadProjectContributionsAsync(
                    new DateOnly(2026, 7, 22),
                    new DateOnly(2026, 7, 22),
                    1,
                    new AgentId("codex")),
                row => row.ProjectKey == projectA && row.MappingKind == ProjectMappingKind.UserMapped).MappingKind);
        int secondBackfill = await repository.BackfillProjectLinksAsync(
        [
            new UsageProjectLink(second.EventKey, projectB, 1, ProjectMappingKind.Observed),
        ],
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            1);
        Assert.Equal(1, secondBackfill);
        Assert.Equal(
            second.Tokens.Total,
            Assert.Single(
                await repository.ReadProjectContributionsAsync(
                    new DateOnly(2026, 7, 22),
                    new DateOnly(2026, 7, 22),
                    1,
                    new AgentId("codex")),
                row => row.ProjectKey == projectA && row.MappingKind == ProjectMappingKind.UserMapped).SelectedTokens.Total);
        DailyUsageRollup afterLinks = Assert.Single(await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22)));
        Assert.Equal(before.Tokens.Total, afterLinks.Tokens.Total);
        Assert.Equal(3, await repository.PurgeProjectLinksAsync());
        Assert.Equal(0, await repository.CountProjectLinksAsync());
        DailyUsageRollup afterPurge = Assert.Single(await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22)));
        Assert.Equal(before.Tokens.Total, afterPurge.Tokens.Total);
        Assert.Equal(before.EventCount, afterPurge.EventCount);
        await using var connection = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE sql LIKE '%cwd%' OR sql LIKE '%private-project-path%';";
        Assert.Null(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task RemapUnassignedProjectEventsHonorsSelectedEffort()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageEvent observed = new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("remap-observed"))).ToLowerInvariant()),
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId("gpt-5"),
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            "UTC",
            new TokenBreakdown(10, 0, 0, 0, 0),
            CostObservation.ProviderReported(0.01m),
            "fixture/1",
            CoverageKind.Complete,
            UsageTimePrecision.Timestamp,
            reasoningEffort: "low");
        UsageEvent unassignedLow = new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("remap-low"))).ToLowerInvariant()),
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId("gpt-5"),
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            "UTC",
            new TokenBreakdown(20, 0, 0, 0, 0),
            CostObservation.ProviderReported(0.02m),
            "fixture/1",
            CoverageKind.Complete,
            UsageTimePrecision.Timestamp,
            reasoningEffort: "low");
        UsageEvent unassignedHigh = new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("remap-high"))).ToLowerInvariant()),
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId("gpt-5"),
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            "UTC",
            new TokenBreakdown(90, 0, 0, 0, 0),
            CostObservation.ProviderReported(0.09m),
            "fixture/1",
            CoverageKind.Complete,
            UsageTimePrecision.Timestamp,
            reasoningEffort: "high");
        await repository.IngestAsync([observed, unassignedLow, unassignedHigh]);
        var project = DeriveKey(OpaqueKeyDomains.CodexProject, "workspace-kept");
        await repository.ReplaceProjectLinksAsync(
        [
            new UsageProjectLink(observed.EventKey, project, 1, ProjectMappingKind.Observed),
        ]);
        IReadOnlyList<UsageEventKey> selected = await repository.ReadUnassignedProjectEventKeysAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            1,
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId("gpt-5"),
            new UsageDetailSelection([], ["low"], []));
        Assert.Equal(unassignedLow.EventKey, Assert.Single(selected));
        await repository.MapEventsToProjectAsync(selected, project, 1);
        IReadOnlyList<UsageProjectContribution> rows = await repository.ReadProjectContributionsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            1,
            new AgentId("codex"));
        Assert.Equal(
            10,
            Assert.Single(rows, row => row.ProjectKey == project && row.MappingKind == ProjectMappingKind.Observed)
                .SelectedTokens.Total);
        Assert.Equal(
            20,
            Assert.Single(rows, row => row.ProjectKey == project && row.MappingKind == ProjectMappingKind.UserMapped)
                .SelectedTokens.Total);
        Assert.Equal(90, Assert.Single(rows, row => row.IsUnassigned).SelectedTokens.Total);
    }

    [Fact]
    public async Task RequestFinalDistributionsUseNearestRankThresholds()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        UsageDistributionEligibility empty = await repository.ReadDistributionEligibilityAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            new AgentId("grok"));
        Assert.Equal(UsageStatisticAvailability.Unavailable, empty.Availability);
        UsageEvent[] finals = Enumerable.Range(1, 100)
            .Select(index => CreateEvent(
                $"final-{index}",
                tokens: new TokenBreakdown(index, 0, 0, 0, 0),
                detailMetadata: new UsageDetailMetadata(
                    recordKind: UsageRecordKind.RequestFinal,
                    input: UsageComponentAvailability.Measured)))
            .ToArray();
        await repository.IngestAsync(finals);
        UsageDistributionEligibility measured = await repository.ReadDistributionEligibilityAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            new AgentId("grok"));
        Assert.Equal(UsageStatisticAvailability.Measured, measured.Availability);
        Assert.Equal(100, measured.EligibleFinalCount);
        Assert.Equal(50, measured.MedianInputTokens);
        Assert.Equal(95, measured.Percentile95InputTokens);
    }

    [Fact]
    public async Task AttributedSessionOutliersDoNotRequireRequestFinal()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        var events = new List<UsageEvent>();
        var links = new List<UsageSessionLink>();
        for (int index = 0; index < 15; index++)
        {
            UsageEvent usageEvent = CreateEvent($"lo-{index}", tokens: new TokenBreakdown(50, 0, 0, 0, 0), agentId: "codex");
            events.Add(usageEvent);
            links.Add(new UsageSessionLink(
                usageEvent.EventKey,
                DeriveKey(OpaqueKeyDomains.CodexSession, $"lo-{index}"),
                parentSessionKey: null,
                consentEpoch: 1));
        }

        for (int index = 0; index < 15; index++)
        {
            UsageEvent usageEvent = CreateEvent($"hi-{index}", tokens: new TokenBreakdown(150, 0, 0, 0, 0), agentId: "codex");
            events.Add(usageEvent);
            links.Add(new UsageSessionLink(
                usageEvent.EventKey,
                DeriveKey(OpaqueKeyDomains.CodexSession, $"hi-{index}"),
                parentSessionKey: null,
                consentEpoch: 1));
        }

        UsageEvent spike = CreateEvent("spike", tokens: new TokenBreakdown(10_000, 0, 0, 0, 0), agentId: "codex");
        events.Add(spike);
        links.Add(new UsageSessionLink(
            spike.EventKey,
            DeriveKey(OpaqueKeyDomains.CodexSession, "spike"),
            parentSessionKey: null,
            consentEpoch: 1));
        await repository.IngestAsync(events);
        await repository.ReplaceSessionLinksAsync(links);
        UsageDistributionEligibility distribution = await repository.ReadDistributionEligibilityAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            new AgentId("codex"));
        Assert.Equal(UsageStatisticAvailability.Unavailable, distribution.Availability);
        IReadOnlyList<UsageSessionContribution> rows = await repository.ReadSessionContributionsAsync(
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            1,
            new AgentId("codex"));
        UsageSessionOutlier outlier = UsageSessionOutlier.Evaluate(rows
            .Where(row => !row.IsUnassigned && row.SessionKey is not null)
            .Select(row => (row.SessionKey!.Value, row.SelectedTokens.Total))
            .ToArray());
        Assert.Equal(UsageStatisticAvailability.Measured, outlier.Availability);
        Assert.Equal(DeriveKey(OpaqueKeyDomains.CodexSession, "spike"), new OpaqueAttributionKey(outlier.SessionKey!));
        Assert.Equal(10_000, outlier.SessionTokens);
    }

    private static OpaqueAttributionKey DeriveKey(string domain, string identifier) =>
        new HmacOpaqueKeyDeriver(Enumerable.Repeat((byte)0x5A, 32).ToArray())
            .Derive(domain, "codex", identifier);

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
