using System.Globalization;
using Microsoft.Data.Sqlite;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Usage;

public sealed record UsageIngestResult(int InsertedCount, int DuplicateCount);

public sealed class UsageSchemaTooNewException(int actualVersion, int supportedVersion)
    : InvalidOperationException(
        $"Usage database schema {actualVersion} is newer than supported schema {supportedVersion}.")
{
    public int ActualVersion { get; } = actualVersion;

    public int SupportedVersion { get; } = supportedVersion;
}

public sealed class UsageSchemaTooOldException(int actualVersion, int supportedVersion)
    : InvalidOperationException(
        $"Usage database schema {actualVersion} is older than supported schema {supportedVersion}.")
{
    public int ActualVersion { get; } = actualVersion;

    public int SupportedVersion { get; } = supportedVersion;
}

public sealed class UsageRepository
{
    public const int CurrentSchemaVersion = 3;
    private const decimal MicrosPerUsd = 1_000_000m;
    private readonly string _connectionString;
    private readonly bool _isReadOnly;

    private UsageRepository(string databasePath, SqliteOpenMode openMode)
    {
        DatabasePath = databasePath;
        _isReadOnly = openMode == SqliteOpenMode.ReadOnly;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = openMode,
            Cache = _isReadOnly ? SqliteCacheMode.Private : SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();
    }

    public string DatabasePath { get; }

    public static async Task<UsageRepository> OpenAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        string fullPath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null)
        {
            throw new ArgumentException("The database path needs a parent directory.", nameof(databasePath));
        }

        Directory.CreateDirectory(directory);
        var repository = new UsageRepository(fullPath, SqliteOpenMode.ReadWriteCreate);
        await repository.EnsureMigratedAsync(cancellationToken).ConfigureAwait(false);
        return repository;
    }

    public static async Task<UsageRepository> OpenReadOnlyAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        string fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The usage database does not exist.", fullPath);
        }

        var repository = new UsageRepository(fullPath, SqliteOpenMode.ReadOnly);
        await repository.EnsureReadOnlySchemaAsync(cancellationToken).ConfigureAwait(false);
        return repository;
    }

    public async Task<UsageIngestResult> IngestAsync(
        IEnumerable<UsageEvent> events,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(events);
        UsageEvent[] batch = events.ToArray();
        if (batch.Any(usageEvent => usageEvent is null))
        {
            throw new ArgumentException("Usage event batches cannot contain null entries.", nameof(events));
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

        int insertedCount = 0;
        foreach (UsageEvent usageEvent in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await InsertEventAsync(connection, transaction, usageEvent, cancellationToken)
                    .ConfigureAwait(false))
            {
                DailyUsageRollup delta = AssertSingleRollup(usageEvent);
                await ApplyRollupDeltaAsync(connection, transaction, delta, cancellationToken)
                    .ConfigureAwait(false);
                insertedCount++;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new UsageIngestResult(insertedCount, batch.Length - insertedCount);
    }

    public async Task<UsageIngestResult> ReplaceAgentEventsAsync(
        AgentId agentId,
        IEnumerable<UsageEvent> events,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(events);
        UsageEvent[] batch = events.ToArray();
        if (batch.Any(usageEvent => usageEvent is null
                                    || usageEvent.AgentId != agentId))
        {
            throw new ArgumentException(
                "Replacement batches must contain only the selected agent.",
                nameof(events));
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        DateOnly? replaceFrom = batch.Length == 0
            ? null
            : batch.Select(usageEvent => AssertSingleRollup(usageEvent).Date).Min();
        await using (SqliteCommand minimum = connection.CreateCommand())
        {
            minimum.Transaction = transaction;
            minimum.CommandText =
                "SELECT MIN(civil_date) FROM usage_event WHERE agent_id = $agentId;";
            minimum.Parameters.AddWithValue("$agentId", agentId.Value);
            object? value = await minimum.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is string dateText)
            {
                DateOnly existingFrom = DateOnly.ParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);
                replaceFrom = replaceFrom is null || existingFrom < replaceFrom
                    ? existingFrom
                    : replaceFrom;
            }
        }

        if (replaceFrom is not null)
        {
            await using SqliteCommand deleteRollups = connection.CreateCommand();
            deleteRollups.Transaction = transaction;
            deleteRollups.CommandText =
                "DELETE FROM daily_usage_rollup WHERE agent_id = $agentId AND civil_date >= $from;";
            deleteRollups.Parameters.AddWithValue("$agentId", agentId.Value);
            deleteRollups.Parameters.AddWithValue("$from", FormatDate(replaceFrom.Value));
            await deleteRollups.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM usage_event WHERE agent_id = $agentId;";
            delete.Parameters.AddWithValue("$agentId", agentId.Value);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        int insertedCount = 0;
        foreach (UsageEvent usageEvent in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (SqliteCommand clearTombstone = connection.CreateCommand())
            {
                clearTombstone.Transaction = transaction;
                clearTombstone.CommandText =
                    "DELETE FROM usage_event_tombstone WHERE event_key = $eventKey;";
                clearTombstone.Parameters.AddWithValue("$eventKey", usageEvent.EventKey.Value);
                await clearTombstone.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (await InsertEventAsync(connection, transaction, usageEvent, cancellationToken)
                    .ConfigureAwait(false))
            {
                await ApplyRollupDeltaAsync(
                    connection,
                    transaction,
                    AssertSingleRollup(usageEvent),
                    cancellationToken).ConfigureAwait(false);
                insertedCount++;
            }
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new UsageIngestResult(insertedCount, batch.Length - insertedCount);
    }

    public async Task<IReadOnlyList<DailyUsageRollup>> QueryDailyRollupsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default)
        => await QueryDailyRollupsCoreAsync(
            fromInclusive,
            toInclusive,
            agentId: null,
            cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<DailyUsageRollup>> QueryDailyRollupsByAgentAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        return await QueryDailyRollupsCoreAsync(
            fromInclusive,
            toInclusive,
            agentId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasUsageForAgentAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS(SELECT 1 FROM daily_usage_rollup WHERE agent_id = $agentId LIMIT 1);";
        command.Parameters.AddWithValue("$agentId", agentId.Value);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private async Task<IReadOnlyList<DailyUsageRollup>> QueryDailyRollupsCoreAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId? agentId,
        CancellationToken cancellationToken)
    {
        if (toInclusive < fromInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toInclusive),
                "The end date cannot precede the start date.");
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT civil_date, grouping_time_zone_id, agent_id, model_provider_id, model_id,
                   input_tokens, output_tokens, reasoning_tokens, cache_read_tokens,
                   cache_write_tokens, reported_cost_micros, estimated_cost_micros,
                   unpriced_tokens, unavailable_cost_event_count, event_count, coverage_kind
            FROM daily_usage_rollup
            WHERE civil_date >= $from AND civil_date <= $to
              AND ($agentId IS NULL OR agent_id = $agentId)
            ORDER BY civil_date, agent_id, model_id;
            """;
        command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
        command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
        command.Parameters.AddWithValue("$agentId", (object?)agentId?.Value ?? DBNull.Value);

        var rollups = new List<DailyUsageRollup>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rollups.Add(ReadRollup(reader));
        }

        return rollups;
    }

    public async Task<int> ApplyRetentionAsync(
        DateTimeOffset nowUtc,
        int retentionDays = 400,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retentionDays, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, 10_000);
        UtcTimestamp.Require(nowUtc, nameof(nowUtc));

        string cutoff = nowUtc.AddDays(-retentionDays)
            .ToString("O", CultureInfo.InvariantCulture);
        string retiredAt = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        int totalDeleted = 0;
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT OR IGNORE INTO usage_event_tombstone(event_key, retired_at_utc)
                SELECT event_key, $retiredAt
                FROM usage_event
                WHERE occurred_at_utc < $cutoff
                ORDER BY occurred_at_utc, event_key
                LIMIT $batchSize;
                """;
            command.Parameters.AddWithValue("$cutoff", cutoff);
            command.Parameters.AddWithValue("$retiredAt", retiredAt);
            command.Parameters.AddWithValue("$batchSize", batchSize);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            command.CommandText =
                """
                DELETE FROM usage_event
                WHERE event_key IN (
                    SELECT event_key
                    FROM usage_event
                    WHERE occurred_at_utc < $cutoff
                    ORDER BY occurred_at_utc, event_key
                    LIMIT $batchSize
                );
                """;
            int deleted = await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            totalDeleted = checked(totalDeleted + deleted);
            if (deleted < batchSize)
            {
                return totalDeleted;
            }
        }
    }

    public async Task DeleteAllUsageDataAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            """
            DELETE FROM usage_event;
            DELETE FROM usage_event_tombstone;
            DELETE FROM daily_usage_rollup;
            DELETE FROM source_cursor;
            DELETE FROM pricing_catalog;
            """,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureMigratedAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS schema_migration (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);

        int currentVersion = await ReadSchemaVersionAsync(
            connection,
            transaction,
            cancellationToken).ConfigureAwait(false);
        if (currentVersion > CurrentSchemaVersion)
        {
            throw new UsageSchemaTooNewException(currentVersion, CurrentSchemaVersion);
        }

        if (currentVersion == 0)
        {
            await ApplyVersionOneAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            currentVersion = 1;
        }

        if (currentVersion == 1)
        {
            await ApplyVersionTwoAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            currentVersion = 2;
        }

        if (currentVersion == 2)
        {
            await ApplyVersionThreeAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureReadOnlySchemaAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migration;";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        int currentVersion = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        if (currentVersion > CurrentSchemaVersion)
        {
            throw new UsageSchemaTooNewException(currentVersion, CurrentSchemaVersion);
        }

        if (currentVersion < CurrentSchemaVersion)
        {
            throw new UsageSchemaTooOldException(currentVersion, CurrentSchemaVersion);
        }
    }

    private void EnsureWritable()
    {
        if (_isReadOnly)
        {
            throw new InvalidOperationException("The usage repository is read-only.");
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, null, "PRAGMA busy_timeout = 5000;", cancellationToken)
                .ConfigureAwait(false);
            if (_isReadOnly)
            {
                await ExecuteAsync(connection, null, "PRAGMA query_only = ON;", cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await ExecuteAsync(connection, null, "PRAGMA foreign_keys = ON;", cancellationToken)
                    .ConfigureAwait(false);
                await ExecuteAsync(connection, null, "PRAGMA journal_mode = WAL;", cancellationToken)
                    .ConfigureAwait(false);
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ApplyVersionOneAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS usage_event (
                event_key TEXT NOT NULL PRIMARY KEY,
                agent_id TEXT NOT NULL,
                model_provider_id TEXT NULL,
                model_id TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                grouping_time_zone_id TEXT NOT NULL,
                civil_date TEXT NOT NULL,
                input_tokens INTEGER NOT NULL CHECK(input_tokens >= 0),
                output_tokens INTEGER NOT NULL CHECK(output_tokens >= 0),
                reasoning_tokens INTEGER NOT NULL CHECK(reasoning_tokens >= 0),
                cache_read_tokens INTEGER NOT NULL CHECK(cache_read_tokens >= 0),
                cache_write_tokens INTEGER NOT NULL CHECK(cache_write_tokens >= 0),
                cost_kind INTEGER NOT NULL,
                reported_cost_micros INTEGER NULL CHECK(reported_cost_micros >= 0),
                estimated_cost_micros INTEGER NULL CHECK(estimated_cost_micros >= 0),
                catalog_version TEXT NULL,
                exact_price_match TEXT NULL,
                parser_version TEXT NOT NULL,
                coverage_kind INTEGER NOT NULL,
                CHECK (
                    (cost_kind = 0 AND reported_cost_micros IS NOT NULL AND estimated_cost_micros IS NULL)
                    OR (cost_kind = 1 AND reported_cost_micros IS NULL AND estimated_cost_micros IS NOT NULL)
                    OR (cost_kind = 2 AND reported_cost_micros IS NULL AND estimated_cost_micros IS NULL)
                )
            );
            CREATE INDEX IF NOT EXISTS ix_usage_event_civil_date
                ON usage_event(civil_date);

            CREATE TABLE IF NOT EXISTS daily_usage_rollup (
                civil_date TEXT NOT NULL,
                grouping_time_zone_id TEXT NOT NULL,
                agent_id TEXT NOT NULL,
                model_provider_id TEXT NOT NULL,
                model_id TEXT NOT NULL,
                input_tokens INTEGER NOT NULL CHECK(input_tokens >= 0),
                output_tokens INTEGER NOT NULL CHECK(output_tokens >= 0),
                reasoning_tokens INTEGER NOT NULL CHECK(reasoning_tokens >= 0),
                cache_read_tokens INTEGER NOT NULL CHECK(cache_read_tokens >= 0),
                cache_write_tokens INTEGER NOT NULL CHECK(cache_write_tokens >= 0),
                reported_cost_micros INTEGER NULL CHECK(reported_cost_micros >= 0),
                estimated_cost_micros INTEGER NULL CHECK(estimated_cost_micros >= 0),
                unpriced_tokens INTEGER NOT NULL CHECK(unpriced_tokens >= 0),
                unavailable_cost_event_count INTEGER NOT NULL CHECK(unavailable_cost_event_count >= 0),
                event_count INTEGER NOT NULL CHECK(event_count > 0),
                coverage_kind INTEGER NOT NULL,
                PRIMARY KEY(civil_date, grouping_time_zone_id, agent_id, model_provider_id, model_id)
            );

            CREATE TABLE IF NOT EXISTS source_cursor (
                source_id TEXT NOT NULL PRIMARY KEY,
                cursor_value TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS pricing_catalog (
                catalog_key TEXT NOT NULL PRIMARY KEY,
                catalog_version TEXT NOT NULL,
                model_provider_id TEXT NOT NULL,
                model_id TEXT NOT NULL,
                exact_price_match TEXT NOT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);

        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText =
            "INSERT INTO schema_migration(version, applied_at_utc) VALUES (1, $appliedAt);";
        versionCommand.Parameters.AddWithValue(
            "$appliedAt",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migration;";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task ApplyVersionTwoAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS usage_event_tombstone (
                event_key TEXT NOT NULL PRIMARY KEY,
                retired_at_utc TEXT NOT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);

        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText =
            "INSERT INTO schema_migration(version, applied_at_utc) VALUES (2, $appliedAt);";
        versionCommand.Parameters.AddWithValue(
            "$appliedAt",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyVersionThreeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            DELETE FROM usage_event WHERE parser_version = 'fixture/1';
            """,
            cancellationToken).ConfigureAwait(false);
        await RebuildRollupsAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText =
            "INSERT INTO schema_migration(version, applied_at_utc) VALUES (3, $appliedAt);";
        versionCommand.Parameters.AddWithValue(
            "$appliedAt",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RebuildRollupsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            connection,
            transaction,
            """
            DELETE FROM daily_usage_rollup;
            INSERT INTO daily_usage_rollup (
                civil_date, grouping_time_zone_id, agent_id, model_provider_id, model_id,
                input_tokens, output_tokens, reasoning_tokens, cache_read_tokens,
                cache_write_tokens, reported_cost_micros, estimated_cost_micros,
                unpriced_tokens, unavailable_cost_event_count, event_count, coverage_kind)
            SELECT
                civil_date,
                grouping_time_zone_id,
                agent_id,
                COALESCE(model_provider_id, ''),
                model_id,
                SUM(input_tokens),
                SUM(output_tokens),
                SUM(reasoning_tokens),
                SUM(cache_read_tokens),
                SUM(cache_write_tokens),
                CASE WHEN SUM(CASE WHEN cost_kind = 0 THEN 1 ELSE 0 END) > 0
                     THEN SUM(CASE WHEN cost_kind = 0 THEN reported_cost_micros ELSE 0 END)
                     ELSE NULL END,
                CASE WHEN SUM(CASE WHEN cost_kind = 1 THEN 1 ELSE 0 END) > 0
                     THEN SUM(CASE WHEN cost_kind = 1 THEN estimated_cost_micros ELSE 0 END)
                     ELSE NULL END,
                SUM(CASE WHEN cost_kind = 2
                         THEN input_tokens + output_tokens + reasoning_tokens
                              + cache_read_tokens + cache_write_tokens
                         ELSE 0 END),
                SUM(CASE WHEN cost_kind = 2 THEN 1 ELSE 0 END),
                COUNT(*),
                MAX(coverage_kind)
            FROM usage_event
            GROUP BY civil_date, grouping_time_zone_id, agent_id,
                     COALESCE(model_provider_id, ''), model_id;
            """,
            cancellationToken).ConfigureAwait(false);

    private static async Task<bool> InsertEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UsageEvent usageEvent,
        CancellationToken cancellationToken)
    {
        if (await IsTombstonedAsync(
                connection,
                transaction,
                usageEvent.EventKey,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        DailyUsageRollup rollup = AssertSingleRollup(usageEvent);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO usage_event (
                event_key, agent_id, model_provider_id, model_id, occurred_at_utc,
                grouping_time_zone_id, civil_date, input_tokens, output_tokens,
                reasoning_tokens, cache_read_tokens, cache_write_tokens, cost_kind,
                reported_cost_micros, estimated_cost_micros, catalog_version,
                exact_price_match, parser_version, coverage_kind)
            VALUES (
                $eventKey, $agentId, $modelProviderId, $modelId, $occurredAt,
                $timeZone, $civilDate, $input, $output, $reasoning, $cacheRead,
                $cacheWrite, $costKind, $reported, $estimated, $catalogVersion,
                $priceMatch, $parserVersion, $coverage)
            ON CONFLICT(event_key) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$eventKey", usageEvent.EventKey.Value);
        command.Parameters.AddWithValue("$agentId", usageEvent.AgentId.Value);
        command.Parameters.AddWithValue(
            "$modelProviderId",
            (object?)usageEvent.ModelProviderId?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelId", usageEvent.ModelId.Value);
        command.Parameters.AddWithValue(
            "$occurredAt",
            usageEvent.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$timeZone", usageEvent.GroupingTimeZoneId);
        command.Parameters.AddWithValue("$civilDate", FormatDate(rollup.Date));
        command.Parameters.AddWithValue("$input", usageEvent.Tokens.Input);
        command.Parameters.AddWithValue("$output", usageEvent.Tokens.Output);
        command.Parameters.AddWithValue("$reasoning", usageEvent.Tokens.Reasoning);
        command.Parameters.AddWithValue("$cacheRead", usageEvent.Tokens.CacheRead);
        command.Parameters.AddWithValue("$cacheWrite", usageEvent.Tokens.CacheWrite);
        command.Parameters.AddWithValue("$costKind", (int)usageEvent.Cost.Kind);
        command.Parameters.AddWithValue(
            "$reported",
            ToDatabaseValue(usageEvent.Cost.ReportedCostUsd));
        command.Parameters.AddWithValue(
            "$estimated",
            ToDatabaseValue(usageEvent.Cost.EstimatedCostUsd));
        command.Parameters.AddWithValue(
            "$catalogVersion",
            (object?)usageEvent.Cost.CatalogVersion ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$priceMatch",
            (object?)usageEvent.Cost.ExactPriceMatch ?? DBNull.Value);
        command.Parameters.AddWithValue("$parserVersion", usageEvent.ParserVersion);
        command.Parameters.AddWithValue("$coverage", (int)usageEvent.Coverage);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static async Task<bool> IsTombstonedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UsageEventKey eventKey,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT 1 FROM usage_event_tombstone WHERE event_key = $eventKey LIMIT 1;";
        command.Parameters.AddWithValue("$eventKey", eventKey.Value);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task ApplyRollupDeltaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DailyUsageRollup delta,
        CancellationToken cancellationToken)
    {
        DailyUsageRollup? current = await ReadRollupAsync(
            connection,
            transaction,
            delta,
            cancellationToken).ConfigureAwait(false);
        DailyUsageRollup next = current is null ? delta : Add(current, delta);

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO daily_usage_rollup (
                civil_date, grouping_time_zone_id, agent_id, model_provider_id, model_id,
                input_tokens, output_tokens, reasoning_tokens, cache_read_tokens,
                cache_write_tokens, reported_cost_micros, estimated_cost_micros,
                unpriced_tokens, unavailable_cost_event_count, event_count, coverage_kind)
            VALUES (
                $date, $timeZone, $agentId, $modelProviderId, $modelId, $input,
                $output, $reasoning, $cacheRead, $cacheWrite, $reported, $estimated,
                $unpricedTokens, $unavailable, $eventCount, $coverage)
            ON CONFLICT(civil_date, grouping_time_zone_id, agent_id, model_provider_id, model_id)
            DO UPDATE SET
                input_tokens = excluded.input_tokens,
                output_tokens = excluded.output_tokens,
                reasoning_tokens = excluded.reasoning_tokens,
                cache_read_tokens = excluded.cache_read_tokens,
                cache_write_tokens = excluded.cache_write_tokens,
                reported_cost_micros = excluded.reported_cost_micros,
                estimated_cost_micros = excluded.estimated_cost_micros,
                unpriced_tokens = excluded.unpriced_tokens,
                unavailable_cost_event_count = excluded.unavailable_cost_event_count,
                event_count = excluded.event_count,
                coverage_kind = excluded.coverage_kind;
            """;
        AddRollupParameters(command, next);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DailyUsageRollup?> ReadRollupAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DailyUsageRollup key,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT civil_date, grouping_time_zone_id, agent_id, model_provider_id, model_id,
                   input_tokens, output_tokens, reasoning_tokens, cache_read_tokens,
                   cache_write_tokens, reported_cost_micros, estimated_cost_micros,
                   unpriced_tokens, unavailable_cost_event_count, event_count, coverage_kind
            FROM daily_usage_rollup
            WHERE civil_date = $date AND grouping_time_zone_id = $timeZone
              AND agent_id = $agentId AND model_provider_id = $modelProviderId
              AND model_id = $modelId;
            """;
        AddKeyParameters(command, key);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadRollup(reader)
            : null;
    }

    private static DailyUsageRollup Add(DailyUsageRollup left, DailyUsageRollup right)
    {
        checked
        {
            return new DailyUsageRollup(
                left.Date,
                left.GroupingTimeZoneId,
                left.AgentId,
                left.ModelProviderId,
                left.ModelId,
                new TokenBreakdown(
                    left.Tokens.Input + right.Tokens.Input,
                    left.Tokens.Output + right.Tokens.Output,
                    left.Tokens.Reasoning + right.Tokens.Reasoning,
                    left.Tokens.CacheRead + right.Tokens.CacheRead,
                    left.Tokens.CacheWrite + right.Tokens.CacheWrite),
                AddNullable(left.ReportedCostUsd, right.ReportedCostUsd),
                AddNullable(left.EstimatedCostUsd, right.EstimatedCostUsd),
                left.UnpricedTokens + right.UnpricedTokens,
                left.UnavailableCostEventCount + right.UnavailableCostEventCount,
                left.EventCount + right.EventCount,
                WorstCoverage(left.Coverage, right.Coverage));
        }
    }

    private static decimal? AddNullable(decimal? left, decimal? right) =>
        left is null ? right : right is null ? left : checked(left.Value + right.Value);

    private static CoverageKind WorstCoverage(CoverageKind left, CoverageKind right) =>
        CoverageRank(left) >= CoverageRank(right) ? left : right;

    private static int CoverageRank(CoverageKind coverage) => coverage switch
    {
        CoverageKind.Complete => 0,
        CoverageKind.Partial => 1,
        CoverageKind.SummaryOnly => 2,
        CoverageKind.Unpriced => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(coverage)),
    };

    private static DailyUsageRollup AssertSingleRollup(UsageEvent usageEvent) =>
        AssertSingle(UsageRollupAggregator.Aggregate([usageEvent]));

    private static T AssertSingle<T>(IReadOnlyList<T> values) => values.Count == 1
        ? values[0]
        : throw new InvalidOperationException("A single event must produce one daily rollup.");

    private static void AddRollupParameters(SqliteCommand command, DailyUsageRollup rollup)
    {
        AddKeyParameters(command, rollup);
        command.Parameters.AddWithValue("$input", rollup.Tokens.Input);
        command.Parameters.AddWithValue("$output", rollup.Tokens.Output);
        command.Parameters.AddWithValue("$reasoning", rollup.Tokens.Reasoning);
        command.Parameters.AddWithValue("$cacheRead", rollup.Tokens.CacheRead);
        command.Parameters.AddWithValue("$cacheWrite", rollup.Tokens.CacheWrite);
        command.Parameters.AddWithValue("$reported", ToDatabaseValue(rollup.ReportedCostUsd));
        command.Parameters.AddWithValue("$estimated", ToDatabaseValue(rollup.EstimatedCostUsd));
        command.Parameters.AddWithValue("$unpricedTokens", rollup.UnpricedTokens);
        command.Parameters.AddWithValue("$unavailable", rollup.UnavailableCostEventCount);
        command.Parameters.AddWithValue("$eventCount", rollup.EventCount);
        command.Parameters.AddWithValue("$coverage", (int)rollup.Coverage);
    }

    private static void AddKeyParameters(SqliteCommand command, DailyUsageRollup rollup)
    {
        command.Parameters.AddWithValue("$date", FormatDate(rollup.Date));
        command.Parameters.AddWithValue("$timeZone", rollup.GroupingTimeZoneId);
        command.Parameters.AddWithValue("$agentId", rollup.AgentId.Value);
        command.Parameters.AddWithValue(
            "$modelProviderId",
            rollup.ModelProviderId?.Value ?? string.Empty);
        command.Parameters.AddWithValue("$modelId", rollup.ModelId.Value);
    }

    private static DailyUsageRollup ReadRollup(SqliteDataReader reader) =>
        new(
            DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetString(1),
            new AgentId(reader.GetString(2)),
            string.IsNullOrEmpty(reader.GetString(3))
                ? null
                : new ModelProviderId(reader.GetString(3)),
            new ModelId(reader.GetString(4)),
            new TokenBreakdown(
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9)),
            reader.IsDBNull(10) ? null : FromMicros(reader.GetInt64(10)),
            reader.IsDBNull(11) ? null : FromMicros(reader.GetInt64(11)),
            reader.GetInt64(12),
            reader.GetInt32(13),
            reader.GetInt32(14),
            (CoverageKind)reader.GetInt32(15));

    private static object ToDatabaseValue(decimal? amountUsd) =>
        amountUsd is null ? DBNull.Value : ToMicros(amountUsd.Value);

    private static long ToMicros(decimal amountUsd) =>
        decimal.ToInt64(checked(amountUsd * MicrosPerUsd));

    private static decimal FromMicros(long micros) => micros / MicrosPerUsd;

    private static string FormatDate(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
