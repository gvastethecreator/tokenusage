using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;

namespace WOpenUsage.Core.Tests.Usage;

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

        command.CommandText = "SELECT COUNT(*) FROM schema_migration WHERE version IN (1, 2);";
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);

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
                VALUES (3, '2026-07-22T12:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        UsageSchemaTooNewException error = await Assert.ThrowsAsync<UsageSchemaTooNewException>(
            () => UsageRepository.OpenAsync(folder.DatabasePath));

        Assert.Equal(3, error.ActualVersion);
        await using var verify = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await verify.OpenAsync();
        await using SqliteCommand verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'usage_event';";
        Assert.Equal(0L, (long)(await verifyCommand.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task VersionOneDatabaseMigratesIncrementallyToVersionTwo()
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
                VALUES (1, '2026-07-22T12:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await UsageRepository.OpenAsync(folder.DatabasePath);

        await using var verify = new SqliteConnection(
            $"Data Source={folder.DatabasePath};Pooling=False");
        await verify.OpenAsync();
        await using SqliteCommand verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText =
            "SELECT COUNT(*) FROM schema_migration WHERE version = 2;";
        Assert.Equal(1L, (long)(await verifyCommand.ExecuteScalarAsync())!);
        verifyCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'usage_event_tombstone';";
        Assert.Equal(1L, (long)(await verifyCommand.ExecuteScalarAsync())!);
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
        TokenBreakdown? tokens = null) =>
        new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(localIdentity))).ToLowerInvariant()),
            new AgentId("grok"),
            new ModelProviderId("xai"),
            new ModelId("grok-4.5"),
            occurredAtUtc ?? new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            "Argentina Standard Time",
            tokens ?? new TokenBreakdown(100, 25, 5, 20, 0),
            CostObservation.ProviderReported(0.25m),
            "fixture/1",
            CoverageKind.Complete);

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "wopenusage-tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryFolder() => Directory.CreateDirectory(_path);

        public string DatabasePath => Path.Combine(_path, "usage.v1.db");

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
