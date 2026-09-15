using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

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

public sealed record UsageReportReadSnapshot(
    IReadOnlyList<DailyUsageRollup> Rollups,
    IReadOnlyList<UsageEvent> Events,
    string[] PricingVersions,
    string[] ParserVersions,
    IReadOnlyList<AccountUsageAggregate> AccountUsage,
    IReadOnlyList<UsageCollectionState> CollectionState)
{
    public IReadOnlyList<UsageTimeRollup> TimeBuckets { get; init; } = [];
    public UsageDataRevision? DataRevision { get; init; }
}

public sealed partial class UsageRepository
{
    public const int CurrentSchemaVersion = 12;
    public const string RetentionCursorId = "usage-retention/v1";
    private const int SqliteVariableChunkSize = 400;
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

        UsageEvent[] inserted = await WriteEventsAsync(
                connection,
                transaction,
                batch,
                EventWriteKind.Insert,
                respectTombstones: true,
                cancellationToken)
            .ConfigureAwait(false);
        foreach (DailyUsageRollup delta in UsageRollupAggregator.Aggregate(inserted))
        {
            await ApplyRollupDeltaAsync(connection, transaction, delta, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new UsageIngestResult(inserted.Length, batch.Length - inserted.Length);
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
        if (batch.Any(row => row.DetailMetadata.SourceInstance is not null))
            throw new ArgumentException("Attributed events require source-scoped reconciliation.", nameof(events));
        await VerifyEventOwnershipAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
        await using (SqliteCommand scope = connection.CreateCommand())
        {
            scope.Transaction = transaction;
            scope.CommandText = "SELECT EXISTS(SELECT 1 FROM usage_event WHERE agent_id = $agent AND source_instance_id IS NOT NULL);";
            scope.Parameters.AddWithValue("$agent", agentId.Value);
            if ((long)(await scope.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! != 0)
                throw new InvalidDataException("Whole-agent replacement cannot replace attributed source history.");
        }
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
            await VerifyRetainedRollupsCanRebuildAsync(connection, transaction, agentId,
                replaceFrom.Value, DateOnly.MaxValue, cancellationToken).ConfigureAwait(false);
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

        await DeleteTombstonesAsync(connection, transaction, batch, cancellationToken)
            .ConfigureAwait(false);
        UsageEvent[] inserted = await WriteEventsAsync(
                connection,
                transaction,
                batch,
                EventWriteKind.Insert,
                respectTombstones: false,
                cancellationToken)
            .ConfigureAwait(false);
        foreach (DailyUsageRollup delta in UsageRollupAggregator.Aggregate(inserted))
        {
            await ApplyRollupDeltaAsync(connection, transaction, delta, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new UsageIngestResult(inserted.Length, batch.Length - inserted.Length);
    }

    public Task<UsageIngestResult> ReconcileAgentEventRangeAsync(
        AgentId agentId,
        string parserVersion,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IEnumerable<UsageEvent> events,
        CancellationToken cancellationToken = default)
        => ReconcileEventRangeCoreAsync(agentId, null, parserVersion, fromInclusive, toInclusive, events, cancellationToken);

    public Task<UsageIngestResult> ReconcileSourceEventRangeAsync(AgentId agentId,
        UsageSourceInstanceId sourceInstance, string parserVersion, DateOnly fromInclusive,
        DateOnly toInclusive, IEnumerable<UsageEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceInstance);
        return ReconcileEventRangeCoreAsync(agentId, sourceInstance, parserVersion, fromInclusive, toInclusive, events, cancellationToken);
    }

    private async Task<UsageIngestResult> ReconcileEventRangeCoreAsync(AgentId agentId,
        UsageSourceInstanceId? sourceInstance, string parserVersion, DateOnly fromInclusive,
        DateOnly toInclusive, IEnumerable<UsageEvent> events, CancellationToken cancellationToken)
    {
        UsageEvent[] batch = ValidateAgentBatch(agentId, events, "Range replacement");
        if (batch.Any(row => row.DetailMetadata.SourceInstance != sourceInstance))
            throw new ArgumentException("Reconciliation events must belong to the selected source instance.", nameof(events));
        ArgumentException.ThrowIfNullOrWhiteSpace(parserVersion);
        if (fromInclusive > toInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fromInclusive),
                "The start of a reconciliation range cannot follow its end.");
        }

        if (batch.Any(usageEvent => !string.Equals(
                usageEvent.ParserVersion,
                parserVersion,
                StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Reconciliation batches must use one parser version.",
                nameof(events));
        }

        if (batch.Any(usageEvent =>
            {
                DateOnly date = AssertSingleRollup(usageEvent).Date;
                return date < fromInclusive || date > toInclusive;
            }))
        {
            throw new ArgumentException(
                "Reconciliation events must fall inside the selected range.",
                nameof(events));
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        DateOnly[] previousDates = await LoadExistingEventDatesAsync(
                connection,
                transaction,
                agentId,
                batch,
                cancellationToken)
            .ConfigureAwait(false);
        await VerifyEventOwnershipAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
        await VerifyRetainedRollupsCanRebuildAsync(connection, transaction, agentId,
            fromInclusive, toInclusive, cancellationToken).ConfigureAwait(false);
        foreach (DateOnly date in previousDates.Where(date => date < fromInclusive || date > toInclusive))
            await VerifyRetainedRollupsCanRebuildAsync(connection, transaction, agentId,
                date, date, cancellationToken).ConfigureAwait(false);
        await using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                """
                DELETE FROM usage_event
                WHERE agent_id = $agentId
                  AND source_instance_id IS $sourceInstance
                  AND civil_date BETWEEN $from AND $to;
                """;
            delete.Parameters.AddWithValue("$agentId", agentId.Value);
            delete.Parameters.AddWithValue("$sourceInstance", (object?)sourceInstance?.Value ?? DBNull.Value);
            delete.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
            delete.Parameters.AddWithValue("$to", FormatDate(toInclusive));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (sourceInstance is null)
            await DeleteTombstonesAsync(connection, transaction, batch, cancellationToken)
                .ConfigureAwait(false);
        UsageEvent[] written = await WriteEventsAsync(
                connection,
                transaction,
                batch,
                EventWriteKind.Upsert,
                respectTombstones: sourceInstance is not null,
                cancellationToken)
            .ConfigureAwait(false);

        await RebuildAgentRollupsInRangeAsync(
                connection,
                transaction,
                agentId,
                fromInclusive,
                toInclusive,
                cancellationToken)
            .ConfigureAwait(false);
        DateOnly[] movedFromOutsideRange = previousDates
            .Where(date => date < fromInclusive || date > toInclusive)
            .ToArray();
        await RebuildAgentRollupsForDatesAsync(
                connection,
                transaction,
                agentId,
                movedFromOutsideRange,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new UsageIngestResult(written.Length, batch.Length - written.Length);
    }

    public async Task<UsageIngestResult> UpsertAgentEventsAsync(
        AgentId agentId,
        IEnumerable<UsageEvent> events,
        CancellationToken cancellationToken = default)
    {
        UsageEvent[] batch = ValidateAgentBatch(agentId, events, "Upsert");
        if (batch.Length == 0)
        {
            return new UsageIngestResult(0, 0);
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        DateOnly[] previousDates = await LoadExistingEventDatesAsync(
                connection,
                transaction,
                agentId,
                batch,
                cancellationToken)
            .ConfigureAwait(false);
        HashSet<string> retiredKeys = await LoadTombstonedKeysAsync(connection, transaction, batch, cancellationToken)
            .ConfigureAwait(false);
        foreach (DateOnly date in previousDates.Concat(batch
                     .Where(row => !retiredKeys.Contains(row.EventKey.Value))
                     .Select(row => AssertSingleRollup(row).Date)).Distinct())
            await VerifyRetainedRollupsCanRebuildAsync(connection, transaction, agentId,
                date, date, cancellationToken).ConfigureAwait(false);
        UsageEvent[] written = await WriteEventsAsync(
                connection,
                transaction,
                batch,
                EventWriteKind.Upsert,
                respectTombstones: true,
                cancellationToken)
            .ConfigureAwait(false);

        DateOnly[] dates = previousDates
            .Concat(written
            .Select(usageEvent => AssertSingleRollup(usageEvent).Date)
            )
            .Distinct()
            .ToArray();
        await RebuildAgentRollupsForDatesAsync(
                connection,
                transaction,
                agentId,
                dates,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new UsageIngestResult(written.Length, batch.Length - written.Length);
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

    public async Task<(DateOnly From, DateOnly To)?> QueryDailyRollupRangeAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken = default)
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
        command.CommandText = """
            SELECT MIN(civil_date), MAX(civil_date)
            FROM daily_usage_rollup
            WHERE civil_date >= $from AND civil_date <= $to;
            """;
        command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
        command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || reader.IsDBNull(0)
            || reader.IsDBNull(1))
        {
            return null;
        }

        return (
            DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<UsageEvent>> QueryUsageEventsAsync(
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        AgentId? agentId = null,
        bool includeOverlappingIntervals = false,
        CancellationToken cancellationToken = default)
    {
        UtcTimestamp.Require(fromInclusiveUtc, nameof(fromInclusiveUtc));
        UtcTimestamp.Require(toExclusiveUtc, nameof(toExclusiveUtc));
        if (toExclusiveUtc <= fromInclusiveUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toExclusiveUtc),
                "The exclusive end of an event range must follow its start.");
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        return await QueryUsageEventsOnAsync(
            connection,
            transaction: null,
            fromInclusiveUtc,
            toExclusiveUtc,
            agentId,
            includeOverlappingIntervals,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<UsageReportReadSnapshot> ReadReportSnapshotAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DateTimeOffset eventsFromInclusiveUtc,
        DateTimeOffset eventsToExclusiveUtc,
        AgentId? agentId = null,
        bool includeOverlappingIntervals = false,
        bool includeTimeBuckets = false,
        CancellationToken cancellationToken = default)
    {
        if (toInclusive < fromInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toInclusive),
                "The end date cannot precede the start date.");
        }

        UtcTimestamp.Require(eventsFromInclusiveUtc, nameof(eventsFromInclusiveUtc));
        UtcTimestamp.Require(eventsToExclusiveUtc, nameof(eventsToExclusiveUtc));
        if (eventsToExclusiveUtc <= eventsFromInclusiveUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventsToExclusiveUtc),
                "The exclusive end of an event range must follow its start.");
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        UsageReportReadSnapshot snapshot = await ReadDailyReportSnapshotOnAsync(
            connection, transaction, fromInclusive, toInclusive, agentId, includeTimeBuckets, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<UsageEvent> events = await QueryUsageEventsOnAsync(
            connection, transaction, eventsFromInclusiveUtc, eventsToExclusiveUtc, agentId,
            includeOverlappingIntervals, cancellationToken: cancellationToken).ConfigureAwait(false);
        return snapshot with { Events = events };
    }

    public async Task<UsageReportReadSnapshot> ReadDailyReportSnapshotAsync(
        DateOnly fromInclusive, DateOnly toInclusive, AgentId? agentId = null,
        bool includeTimeBuckets = false, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(toInclusive, fromInclusive);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        return await ReadDailyReportSnapshotOnAsync(connection, transaction, fromInclusive,
            toInclusive, agentId, includeTimeBuckets, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<UsageReportReadSnapshot> ReadDailyReportSnapshotOnAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        DateOnly fromInclusive, DateOnly toInclusive, AgentId? agentId,
        bool includeTimeBuckets, CancellationToken cancellationToken)
    {
        IReadOnlyList<DailyUsageRollup> rollups = await QueryDailyRollupsOnAsync(
            connection, transaction, fromInclusive, toInclusive, agentId, cancellationToken).ConfigureAwait(false);
        (string[] pricing, string[] parsers) = await ReadReportVersionsOnAsync(
            connection, transaction, fromInclusive, toInclusive, agentId, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<AccountUsageAggregate> account = await ReadAccountUsageOnAsync(
            connection, transaction, fromInclusive, toInclusive, agentId, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<UsageCollectionState> collection = await ReadCollectionStateOnAsync(
            connection, transaction, cancellationToken).ConfigureAwait(false);
        return new(rollups, [], pricing, parsers, account, collection)
        {
            DataRevision = await ReadDataRevisionOnAsync(connection, transaction, cancellationToken).ConfigureAwait(false),
            TimeBuckets = includeTimeBuckets ? await QueryTwoHourRollupsOnAsync(connection,
                transaction, fromInclusive, toInclusive, agentId, cancellationToken).ConfigureAwait(false) : [],
        };
    }

    public async Task<IReadOnlyList<UsageTimeRollup>> QueryTwoHourRollupsAsync(
        DateOnly fromInclusive, DateOnly toInclusive, AgentId? agentId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(toInclusive, fromInclusive);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await QueryTwoHourRollupsOnAsync(connection, null, fromInclusive, toInclusive,
            agentId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<UsageTimeRollup>> QueryTwoHourRollupsOnAsync(
        SqliteConnection connection, SqliteTransaction? transaction,
        DateOnly fromInclusive, DateOnly toInclusive, AgentId? agentId, CancellationToken cancellationToken)
    {
        var zones = new Dictionary<string, TimeZoneInfo>(StringComparer.Ordinal);
        connection.CreateFunction<string, string, int>("two_hour_bucket", (timestamp, zoneId) =>
        {
            if (!zones.TryGetValue(zoneId, out TimeZoneInfo? zone))
                zones[zoneId] = zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            return TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture), zone).Hour / 2 * 2;
        }, isDeterministic: true);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT civil_date, grouping_time_zone_id, agent_id, COALESCE(model_provider_id, ''), model_id,
                   SUM(input_tokens), SUM(output_tokens), SUM(reasoning_tokens),
                   SUM(cache_read_tokens), SUM(cache_write_tokens),
                   CASE WHEN SUM(CASE WHEN cost_kind = 0 THEN 1 ELSE 0 END) > 0
                        THEN SUM(CASE WHEN cost_kind = 0 THEN reported_cost_micros ELSE 0 END) END,
                   CASE WHEN SUM(CASE WHEN cost_kind = 1 THEN 1 ELSE 0 END) > 0
                        THEN SUM(CASE WHEN cost_kind = 1 THEN estimated_cost_micros ELSE 0 END) END,
                   SUM(CASE WHEN cost_kind = 2 THEN input_tokens + output_tokens + reasoning_tokens
                            + cache_read_tokens + cache_write_tokens ELSE 0 END),
                   SUM(CASE WHEN cost_kind = 2 THEN 1 ELSE 0 END), COUNT(*), MAX(coverage_kind),
                   two_hour_bucket(occurred_at_utc, grouping_time_zone_id) AS bucket_hour
            FROM usage_event
            WHERE civil_date >= $from AND civil_date <= $to
              AND ($agent IS NULL OR agent_id = $agent)
              AND time_precision = 1
              AND record_kind NOT IN (3, 4)
            GROUP BY civil_date, bucket_hour, agent_id, model_id, grouping_time_zone_id, COALESCE(model_provider_id, '')
            ORDER BY civil_date, bucket_hour, agent_id, model_id, grouping_time_zone_id, COALESCE(model_provider_id, '');
            """;
        command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
        command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
        command.Parameters.AddWithValue("$agent", (object?)agentId?.Value ?? DBNull.Value);
        var result = new List<UsageTimeRollup>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new UsageTimeRollup(ReadRollup(reader), reader.GetInt32(16)));
        return result;
    }

    public async Task<long> SumTokensAsync(
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        AgentId agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        UtcTimestamp.Require(fromInclusiveUtc, nameof(fromInclusiveUtc));
        UtcTimestamp.Require(toExclusiveUtc, nameof(toExclusiveUtc));
        if (toExclusiveUtc <= fromInclusiveUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toExclusiveUtc),
                "The exclusive end of an event range must follow its start.");
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        // Sum columns separately so SQLite cannot promote a per-event integer overflow
        // to a floating-point value. The final addition is also checked.
        command.CommandText = """
            SELECT COALESCE(SUM(input_tokens), 0), COALESCE(SUM(output_tokens), 0),
                   COALESCE(SUM(reasoning_tokens), 0), COALESCE(SUM(cache_read_tokens), 0),
                   COALESCE(SUM(cache_write_tokens), 0)
            FROM usage_event
            WHERE agent_id = $agentId
              AND occurred_at_utc >= $from AND occurred_at_utc < $to
              AND record_kind NOT IN (3, 4)
              AND (time_precision = 1 OR (time_precision = 2 AND interval_started_at_utc >= $from));
            """;
        command.Parameters.AddWithValue("$agentId", agentId.Value);
        command.Parameters.AddWithValue(
            "$from", fromInclusiveUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$to", toExclusiveUtc.ToString("O", CultureInfo.InvariantCulture));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return checked(reader.GetInt64(0) + reader.GetInt64(1) + reader.GetInt64(2)
            + reader.GetInt64(3) + reader.GetInt64(4));
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
        return await QueryDailyRollupsOnAsync(
            connection, transaction: null, fromInclusive, toInclusive, agentId, cancellationToken)
            .ConfigureAwait(false);
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

    public async Task<int> ApplyRetentionIfDueAsync(
        DateTimeOffset nowUtc,
        TimeSpan minInterval,
        int retentionDays = 400,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentOutOfRangeException.ThrowIfLessThan(minInterval, TimeSpan.Zero);
        UtcTimestamp.Require(nowUtc, nameof(nowUtc));

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset? lastApplied = await ReadRetentionCursorAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);
        if (lastApplied is not null && nowUtc - lastApplied.Value < minInterval)
        {
            return -1;
        }

        int deleted = await ApplyRetentionAsync(
                nowUtc,
                retentionDays,
                batchSize,
                cancellationToken)
            .ConfigureAwait(false);
        await WriteRetentionCursorAsync(connection, nowUtc, cancellationToken)
            .ConfigureAwait(false);
        return deleted;
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
            DELETE FROM project_attribution;
            DELETE FROM session_attribution;
            DELETE FROM usage_event;
            DELETE FROM usage_event_tombstone;
            DELETE FROM daily_usage_rollup;
            DELETE FROM source_cursor;
            DELETE FROM pricing_catalog;
            DELETE FROM account_usage_daily;
            DELETE FROM saved_usage_comparison;
            DELETE FROM usage_collection_state;
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

        if (currentVersion > 0 && currentVersion < CurrentSchemaVersion)
            await CreateMigrationBackupAsync(currentVersion, cancellationToken).ConfigureAwait(false);

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
            currentVersion = 3;
        }

        if (currentVersion == 3)
        {
            await ApplyVersionFourAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            currentVersion = 4;
        }

        if (currentVersion == 4)
        {
            await ApplyMeasurementSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            currentVersion = 5;
        }

        if (currentVersion == 5)
        {
            await ApplyRevisionSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            currentVersion = 6;
        }
        if (currentVersion == 6)
        {
            await ApplyDetailSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            currentVersion = 7;
        }
        if (currentVersion == 7)
        {
            await ApplyCollectionFreshnessSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            currentVersion = 8;
        }
        if (currentVersion == 8)
        {
            await ApplyAttributionSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            currentVersion = 9;
        }
        if (currentVersion == 9)
        {
            await ApplyProjectAttributionSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            currentVersion = 10;
        }
        if (currentVersion == 10)
        {
            await ApplyOperationFactSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            currentVersion = 11;
        }
        if (currentVersion == 11)
        {
            await ApplyOperationSourceInstanceSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            currentVersion = 12;
        }
        if (await ReadSchemaVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false) != CurrentSchemaVersion)
            throw new InvalidDataException("The usage migration did not reach the required schema version.");

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

    private UsageEvent[] ValidateAgentBatch(
        AgentId agentId,
        IEnumerable<UsageEvent> events,
        string operation)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(events);
        UsageEvent[] batch = events.ToArray();
        if (batch.Any(usageEvent => usageEvent is null || usageEvent.AgentId != agentId))
        {
            throw new ArgumentException(
                $"{operation} batches must contain only the selected agent.",
                nameof(events));
        }

        return batch;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            connection.CreateFunction("tokenusage_writer_schema", () => CurrentSchemaVersion);
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
        SqliteTransaction? transaction,
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

    private static async Task ApplyVersionFourAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS ix_usage_event_agent_civil_date
                ON usage_event(agent_id, civil_date);
            CREATE INDEX IF NOT EXISTS ix_usage_event_occurred_at_utc
                ON usage_event(occurred_at_utc);
            CREATE INDEX IF NOT EXISTS ix_daily_usage_rollup_agent_civil_date
                ON daily_usage_rollup(agent_id, civil_date);
            """,
            cancellationToken).ConfigureAwait(false);

        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText =
            "INSERT INTO schema_migration(version, applied_at_utc) VALUES (4, $appliedAt);";
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

    private static Task RebuildAgentRollupsInRangeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AgentId agentId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken) =>
        RebuildAgentRollupsCoreAsync(
            connection,
            transaction,
            agentId,
            "civil_date BETWEEN $from AND $to",
            [
                ("$from", FormatDate(fromInclusive)),
                ("$to", FormatDate(toInclusive)),
            ],
            cancellationToken);

    private static Task RebuildAgentRollupsForDatesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AgentId agentId,
        DateOnly[] dates,
        CancellationToken cancellationToken)
    {
        if (dates.Length == 0)
        {
            return Task.CompletedTask;
        }

        string[] placeholders = dates
            .Select((_, index) => "$date" + index.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        (string Name, string Value)[] parameters = dates
            .Select((date, index) => (placeholders[index], FormatDate(date)))
            .ToArray();
        return RebuildAgentRollupsCoreAsync(
            connection,
            transaction,
            agentId,
            "civil_date IN (" + string.Join(", ", placeholders) + ")",
            parameters,
            cancellationToken);
    }

    private static async Task<DateOnly[]> LoadExistingEventDatesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AgentId agentId,
        UsageEvent[] batch,
        CancellationToken cancellationToken)
    {
        var dates = new HashSet<DateOnly>();
        string[] eventKeys = batch
            .Select(usageEvent => usageEvent.EventKey.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string[] chunk in eventKeys.Chunk(500))
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            string[] placeholders = chunk
                .Select((_, index) => "$key" + index.ToString(CultureInfo.InvariantCulture))
                .ToArray();
            command.CommandText =
                $"SELECT DISTINCT civil_date FROM usage_event "
                + $"WHERE agent_id = $agentId AND event_key IN ({string.Join(", ", placeholders)});";
            command.Parameters.AddWithValue("$agentId", agentId.Value);
            for (int index = 0; index < chunk.Length; index++)
            {
                command.Parameters.AddWithValue(placeholders[index], chunk[index]);
            }

            await using SqliteDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                dates.Add(DateOnly.ParseExact(
                    reader.GetString(0),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture));
            }
        }

        return dates.ToArray();
    }

    private static async Task RebuildAgentRollupsCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AgentId agentId,
        string datePredicate,
        (string Name, string Value)[] extraParameters,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            DELETE FROM daily_usage_rollup
            WHERE agent_id = $agentId AND {datePredicate};
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
            WHERE agent_id = $agentId AND {datePredicate}
            GROUP BY civil_date, grouping_time_zone_id, agent_id,
                     COALESCE(model_provider_id, ''), model_id;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.Value);
        foreach ((string name, string value) in extraParameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private enum EventWriteKind
    {
        Insert,
        Upsert,
    }

    private const string InsertEventSql =
        """
        INSERT INTO usage_event (
            event_key, agent_id, model_provider_id, model_id, occurred_at_utc,
            grouping_time_zone_id, civil_date, input_tokens, output_tokens,
            reasoning_tokens, cache_read_tokens, cache_write_tokens, cost_kind,
            reported_cost_micros, estimated_cost_micros, catalog_version,
            exact_price_match, parser_version, coverage_kind, time_precision, interval_started_at_utc, observed_model_id, reasoning_effort, service_tier, source_instance_id, record_kind, representation_revision, input_availability, output_availability, reasoning_availability, cache_read_availability, cache_write_availability)
        VALUES (
            $eventKey, $agentId, $modelProviderId, $modelId, $occurredAt,
            $timeZone, $civilDate, $input, $output, $reasoning, $cacheRead,
            $cacheWrite, $costKind, $reported, $estimated, $catalogVersion,
            $priceMatch, $parserVersion, $coverage, $precision, $intervalStart, $observedModel, $effort, $tier, $sourceInstance, $recordKind, $representationRevision, $inputAvailability, $outputAvailability, $reasoningAvailability, $cacheReadAvailability, $cacheWriteAvailability)
        ON CONFLICT(event_key) DO NOTHING;
        """;

    private const string UpsertEventSql =
        """
        INSERT INTO usage_event (
            event_key, agent_id, model_provider_id, model_id, occurred_at_utc,
            grouping_time_zone_id, civil_date, input_tokens, output_tokens,
            reasoning_tokens, cache_read_tokens, cache_write_tokens, cost_kind,
            reported_cost_micros, estimated_cost_micros, catalog_version,
            exact_price_match, parser_version, coverage_kind, time_precision, interval_started_at_utc, observed_model_id, reasoning_effort, service_tier, source_instance_id, record_kind, representation_revision, input_availability, output_availability, reasoning_availability, cache_read_availability, cache_write_availability)
        VALUES (
            $eventKey, $agentId, $modelProviderId, $modelId, $occurredAt,
            $timeZone, $civilDate, $input, $output, $reasoning, $cacheRead,
            $cacheWrite, $costKind, $reported, $estimated, $catalogVersion,
            $priceMatch, $parserVersion, $coverage, $precision, $intervalStart, $observedModel, $effort, $tier, $sourceInstance, $recordKind, $representationRevision, $inputAvailability, $outputAvailability, $reasoningAvailability, $cacheReadAvailability, $cacheWriteAvailability)
        ON CONFLICT(event_key) DO UPDATE SET
            agent_id = excluded.agent_id,
            model_provider_id = excluded.model_provider_id,
            model_id = excluded.model_id,
            occurred_at_utc = excluded.occurred_at_utc,
            grouping_time_zone_id = excluded.grouping_time_zone_id,
            civil_date = excluded.civil_date,
            input_tokens = excluded.input_tokens,
            output_tokens = excluded.output_tokens,
            reasoning_tokens = excluded.reasoning_tokens,
            cache_read_tokens = excluded.cache_read_tokens,
            cache_write_tokens = excluded.cache_write_tokens,
            cost_kind = excluded.cost_kind,
            reported_cost_micros = excluded.reported_cost_micros,
            estimated_cost_micros = excluded.estimated_cost_micros,
            catalog_version = excluded.catalog_version,
            exact_price_match = excluded.exact_price_match,
            parser_version = excluded.parser_version,
            coverage_kind = excluded.coverage_kind,
            time_precision = excluded.time_precision,
            interval_started_at_utc = excluded.interval_started_at_utc,
            observed_model_id = excluded.observed_model_id,
            reasoning_effort = excluded.reasoning_effort,
            service_tier = excluded.service_tier,
            source_instance_id = excluded.source_instance_id,
            record_kind = excluded.record_kind,
            representation_revision = excluded.representation_revision,
            input_availability = excluded.input_availability,
            output_availability = excluded.output_availability,
            reasoning_availability = excluded.reasoning_availability,
            cache_read_availability = excluded.cache_read_availability,
            cache_write_availability = excluded.cache_write_availability;
        """;

    private static async Task<UsageEvent[]> WriteEventsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UsageEvent[] batch,
        EventWriteKind kind,
        bool respectTombstones,
        CancellationToken cancellationToken)
    {
        if (batch.Length == 0)
        {
            return [];
        }

        await VerifyEventOwnershipAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);

        HashSet<string> tombstoned = respectTombstones
            ? await LoadTombstonedKeysAsync(connection, transaction, batch, cancellationToken)
                .ConfigureAwait(false)
            : new HashSet<string>(StringComparer.Ordinal);

        if (kind == EventWriteKind.Insert)
            foreach (var day in batch.Where(row => row.DetailMetadata.SourceInstance is not null
                         && !tombstoned.Contains(row.EventKey.Value))
                         .Select(row => (row.AgentId, AssertSingleRollup(row).Date)).Distinct())
                await VerifyRetainedRollupsCanRebuildAsync(connection, transaction, day.AgentId,
                    day.Date, day.Date, cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = kind == EventWriteKind.Insert ? InsertEventSql : UpsertEventSql;

        var written = new List<UsageEvent>(batch.Length);
        foreach (UsageEvent usageEvent in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tombstoned.Contains(usageEvent.EventKey.Value))
            {
                continue;
            }

            DailyUsageRollup rollup = AssertSingleRollup(usageEvent);
            BindUsageEventParameters(command, usageEvent, rollup.Date);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
            {
                written.Add(usageEvent);
            }
        }

        return written.ToArray();
    }

    private static async Task<HashSet<string>> LoadTombstonedKeysAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UsageEvent[] batch,
        CancellationToken cancellationToken)
    {
        var tombstoned = new HashSet<string>(StringComparer.Ordinal);
        string[] keys = batch
            .Select(usageEvent => usageEvent.EventKey.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await ForEachKeyChunkAsync(
            keys,
            async command =>
            {
                await using SqliteDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    tombstoned.Add(reader.GetString(0));
                }
            },
            connection,
            transaction,
            "SELECT event_key FROM usage_event_tombstone WHERE event_key IN ({0});",
            cancellationToken).ConfigureAwait(false);
        return tombstoned;
    }

    private static async Task DeleteTombstonesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UsageEvent[] batch,
        CancellationToken cancellationToken) =>
        await ForEachKeyChunkAsync(
            batch.Select(usageEvent => usageEvent.EventKey.Value).Distinct(StringComparer.Ordinal).ToArray(),
            async command =>
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            },
            connection,
            transaction,
            "DELETE FROM usage_event_tombstone WHERE event_key IN ({0});",
            cancellationToken).ConfigureAwait(false);

    private static async Task ForEachKeyChunkAsync(
        string[] keys,
        Func<SqliteCommand, Task> executeChunk,
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sqlFormat,
        CancellationToken cancellationToken)
    {
        for (int offset = 0; offset < keys.Length; offset += SqliteVariableChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(SqliteVariableChunkSize, keys.Length - offset);
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            var names = new string[count];
            for (int index = 0; index < count; index++)
            {
                names[index] = "$k" + index.ToString(CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue(names[index], keys[offset + index]);
            }

            command.CommandText = string.Format(
                CultureInfo.InvariantCulture,
                sqlFormat,
                string.Join(",", names));
            await executeChunk(command).ConfigureAwait(false);
        }
    }

    private static void BindUsageEventParameters(
        SqliteCommand command,
        UsageEvent usageEvent,
        DateOnly civilDate)
    {
        if (command.Parameters.Count == 0)
        {
            AddUsageEventParameters(command, usageEvent, civilDate);
            return;
        }

        command.Parameters["$eventKey"].Value = usageEvent.EventKey.Value;
        command.Parameters["$agentId"].Value = usageEvent.AgentId.Value;
        command.Parameters["$modelProviderId"].Value =
            (object?)usageEvent.ModelProviderId?.Value ?? DBNull.Value;
        command.Parameters["$modelId"].Value = usageEvent.ModelId.Value;
        command.Parameters["$occurredAt"].Value =
            usageEvent.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters["$timeZone"].Value = usageEvent.GroupingTimeZoneId;
        command.Parameters["$civilDate"].Value = FormatDate(civilDate);
        command.Parameters["$input"].Value = usageEvent.Tokens.Input;
        command.Parameters["$output"].Value = usageEvent.Tokens.Output;
        command.Parameters["$reasoning"].Value = usageEvent.Tokens.Reasoning;
        command.Parameters["$cacheRead"].Value = usageEvent.Tokens.CacheRead;
        command.Parameters["$cacheWrite"].Value = usageEvent.Tokens.CacheWrite;
        command.Parameters["$costKind"].Value = (int)usageEvent.Cost.Kind;
        command.Parameters["$reported"].Value = ToDatabaseValue(usageEvent.Cost.ReportedCostUsd);
        command.Parameters["$estimated"].Value = ToDatabaseValue(usageEvent.Cost.EstimatedCostUsd);
        command.Parameters["$catalogVersion"].Value =
            (object?)usageEvent.Cost.CatalogVersion ?? DBNull.Value;
        command.Parameters["$priceMatch"].Value =
            (object?)usageEvent.Cost.ExactPriceMatch ?? DBNull.Value;
        command.Parameters["$parserVersion"].Value = usageEvent.ParserVersion;
        command.Parameters["$coverage"].Value = (int)usageEvent.Coverage;
        command.Parameters["$observedModel"].Value = (object?)usageEvent.ObservedModelId?.Value ?? DBNull.Value;
        command.Parameters["$effort"].Value = (object?)usageEvent.ReasoningEffort ?? DBNull.Value;
        command.Parameters["$tier"].Value = (object?)usageEvent.ServiceTier ?? DBNull.Value;
        command.Parameters["$sourceInstance"].Value = (object?)usageEvent.DetailMetadata.SourceInstance?.Value ?? DBNull.Value;
        command.Parameters["$recordKind"].Value = (int)usageEvent.DetailMetadata.RecordKind;
        command.Parameters["$representationRevision"].Value = (object?)usageEvent.DetailMetadata.RepresentationRevision ?? DBNull.Value;
        command.Parameters["$inputAvailability"].Value = (int)usageEvent.DetailMetadata.Input;
        command.Parameters["$outputAvailability"].Value = (int)usageEvent.DetailMetadata.Output;
        command.Parameters["$reasoningAvailability"].Value = (int)usageEvent.DetailMetadata.Reasoning;
        command.Parameters["$cacheReadAvailability"].Value = (int)usageEvent.DetailMetadata.CacheRead;
        command.Parameters["$cacheWriteAvailability"].Value = (int)usageEvent.DetailMetadata.CacheWrite;
        command.Parameters["$precision"].Value = (int)usageEvent.TimePrecision;
        command.Parameters["$intervalStart"].Value = (object?)usageEvent.IntervalStartedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value;
    }

    private static void AddUsageEventParameters(
        SqliteCommand command,
        UsageEvent usageEvent,
        DateOnly civilDate)
    {
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
        command.Parameters.AddWithValue("$civilDate", FormatDate(civilDate));
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
        command.Parameters.AddWithValue("$observedModel", (object?)usageEvent.ObservedModelId?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("$effort", (object?)usageEvent.ReasoningEffort ?? DBNull.Value);
        command.Parameters.AddWithValue("$tier", (object?)usageEvent.ServiceTier ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceInstance", (object?)usageEvent.DetailMetadata.SourceInstance?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("$recordKind", (int)usageEvent.DetailMetadata.RecordKind);
        command.Parameters.AddWithValue("$representationRevision", (object?)usageEvent.DetailMetadata.RepresentationRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$inputAvailability", (int)usageEvent.DetailMetadata.Input);
        command.Parameters.AddWithValue("$outputAvailability", (int)usageEvent.DetailMetadata.Output);
        command.Parameters.AddWithValue("$reasoningAvailability", (int)usageEvent.DetailMetadata.Reasoning);
        command.Parameters.AddWithValue("$cacheReadAvailability", (int)usageEvent.DetailMetadata.CacheRead);
        command.Parameters.AddWithValue("$cacheWriteAvailability", (int)usageEvent.DetailMetadata.CacheWrite);
        command.Parameters.AddWithValue("$precision", (int)usageEvent.TimePrecision);
        command.Parameters.AddWithValue("$intervalStart", (object?)usageEvent.IntervalStartedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
    }

    private static async Task<DateTimeOffset?> ReadRetentionCursorAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT cursor_value FROM source_cursor WHERE source_id = $sourceId LIMIT 1;";
        command.Parameters.AddWithValue("$sourceId", RetentionCursorId);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static async Task WriteRetentionCursorAsync(
        SqliteConnection connection,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO source_cursor(source_id, cursor_value, updated_at_utc)
            VALUES ($sourceId, $cursorValue, $updatedAt)
            ON CONFLICT(source_id) DO UPDATE SET
                cursor_value = excluded.cursor_value,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$sourceId", RetentionCursorId);
        command.Parameters.AddWithValue(
            "$cursorValue",
            appliedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$updatedAt",
            appliedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

    private static async Task<IReadOnlyList<DailyUsageRollup>> QueryDailyRollupsOnAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId? agentId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = agentId is null
            ? """
              SELECT civil_date, grouping_time_zone_id, agent_id, model_provider_id, model_id,
                     input_tokens, output_tokens, reasoning_tokens, cache_read_tokens,
                     cache_write_tokens, reported_cost_micros, estimated_cost_micros,
                     unpriced_tokens, unavailable_cost_event_count, event_count, coverage_kind
              FROM daily_usage_rollup
              WHERE civil_date >= $from AND civil_date <= $to
              ORDER BY civil_date, agent_id, model_id;
              """
            : """
              SELECT civil_date, grouping_time_zone_id, agent_id, model_provider_id, model_id,
                     input_tokens, output_tokens, reasoning_tokens, cache_read_tokens,
                     cache_write_tokens, reported_cost_micros, estimated_cost_micros,
                     unpriced_tokens, unavailable_cost_event_count, event_count, coverage_kind
              FROM daily_usage_rollup
              WHERE agent_id = $agentId AND civil_date >= $from AND civil_date <= $to
              ORDER BY civil_date, agent_id, model_id;
              """;
        command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
        command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
        if (agentId is not null)
        {
            command.Parameters.AddWithValue("$agentId", agentId.Value);
        }

        var rollups = new List<DailyUsageRollup>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rollups.Add(ReadRollup(reader));
        }

        return rollups;
    }

    private static async Task<IReadOnlyList<UsageEvent>> QueryUsageEventsOnAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        AgentId? agentId,
        bool includeOverlappingIntervals,
        int? pageSize = null,
        UsageEventPageCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = agentId is null
            ? """
              SELECT event_key, agent_id, model_provider_id, model_id, occurred_at_utc,
                     grouping_time_zone_id, input_tokens, output_tokens, reasoning_tokens,
                     cache_read_tokens, cache_write_tokens, cost_kind, reported_cost_micros,
                     estimated_cost_micros, catalog_version, exact_price_match, parser_version,
                     coverage_kind, time_precision, interval_started_at_utc, observed_model_id, reasoning_effort, service_tier, source_instance_id, record_kind, representation_revision, input_availability, output_availability, reasoning_availability, cache_read_availability, cache_write_availability
              FROM usage_event
              WHERE occurred_at_utc >= $from AND occurred_at_utc < $to
              ORDER BY occurred_at_utc, agent_id, model_id, event_key;
              """
            : """
              SELECT event_key, agent_id, model_provider_id, model_id, occurred_at_utc,
                     grouping_time_zone_id, input_tokens, output_tokens, reasoning_tokens,
                     cache_read_tokens, cache_write_tokens, cost_kind, reported_cost_micros,
                     estimated_cost_micros, catalog_version, exact_price_match, parser_version,
                     coverage_kind, time_precision, interval_started_at_utc, observed_model_id, reasoning_effort, service_tier, source_instance_id, record_kind, representation_revision, input_availability, output_availability, reasoning_availability, cache_read_availability, cache_write_availability
              FROM usage_event
              WHERE agent_id = $agentId
                AND occurred_at_utc >= $from AND occurred_at_utc < $to
              ORDER BY occurred_at_utc, agent_id, model_id, event_key;
              """;
        if (includeOverlappingIntervals)
            command.CommandText = command.CommandText.Replace(
                "occurred_at_utc >= $from AND occurred_at_utc < $to",
                "((occurred_at_utc >= $from AND occurred_at_utc < $to) OR (time_precision = 2 AND interval_started_at_utc < $to AND occurred_at_utc >= $from))",
                StringComparison.Ordinal);
        if (cursor is not null)
        {
            command.CommandText = command.CommandText.Replace("ORDER BY occurred_at_utc",
                "AND (occurred_at_utc, agent_id, model_id, event_key) > ($afterUtc, $afterAgent, $afterModel, $afterKey) ORDER BY occurred_at_utc",
                StringComparison.Ordinal);
            command.Parameters.AddWithValue("$afterUtc", cursor.AfterUtc.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$afterAgent", cursor.AfterAgent.Value);
            command.Parameters.AddWithValue("$afterModel", cursor.AfterModel.Value);
            command.Parameters.AddWithValue("$afterKey", cursor.AfterKey.Value);
        }
        if (pageSize is { } limit)
        {
            command.CommandText = command.CommandText.TrimEnd().TrimEnd(';') + " LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", limit);
        }
        command.Parameters.AddWithValue(
            "$from",
            fromInclusiveUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$to",
            toExclusiveUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        if (agentId is not null)
        {
            command.Parameters.AddWithValue("$agentId", agentId.Value);
        }

        var events = new List<UsageEvent>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(ReadUsageEvent(reader));
        }

        return events;
    }

    private static async Task<(string[] Pricing, string[] Parsers)> ReadReportVersionsOnAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId? agentId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT DISTINCT catalog_version, parser_version FROM usage_event
            WHERE civil_date BETWEEN $from AND $to AND ($agent IS NULL OR agent_id = $agent);
            """;
        command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
        command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
        command.Parameters.AddWithValue("$agent", (object?)agentId?.Value ?? DBNull.Value);
        var pricing = new HashSet<string>(StringComparer.Ordinal);
        var parsers = new HashSet<string>(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0)) pricing.Add(reader.GetString(0));
            parsers.Add(reader.GetString(1));
        }

        return (pricing.Order().ToArray(), parsers.Order().ToArray());
    }

    private static async Task<IReadOnlyList<AccountUsageAggregate>> ReadAccountUsageOnAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId? agentId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT agent_id, provider_date, tokens, observed_at_utc FROM account_usage_daily
            WHERE provider_date BETWEEN $from AND $to AND ($agent IS NULL OR agent_id = $agent)
            ORDER BY provider_date, agent_id;
            """;
        command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
        command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
        command.Parameters.AddWithValue("$agent", (object?)agentId?.Value ?? DBNull.Value);
        var result = new List<AccountUsageAggregate>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new(
                new AgentId(reader.GetString(0)),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt64(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<UsageCollectionState>> ReadCollectionStateOnAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT agent_id, attempted_at_utc, status, issue, last_successful_at_utc FROM usage_collection_state ORDER BY agent_id;";
        var result = new List<UsageCollectionState>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new(
                reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                (UsageSourceReadStatus)reader.GetInt32(2),
                (UsageSourceIssueKind)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)));
        }

        return result;
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

    private static UsageEvent ReadUsageEvent(SqliteDataReader reader)
    {
        CostKind costKind = (CostKind)reader.GetInt32(11);
        CostObservation cost = costKind switch
        {
            CostKind.ProviderReported when !reader.IsDBNull(12) =>
                CostObservation.ProviderReported(FromMicros(reader.GetInt64(12))),
            CostKind.CatalogEstimated when !reader.IsDBNull(13)
                && !reader.IsDBNull(14)
                && !reader.IsDBNull(15) => CostObservation.CatalogEstimated(
                    FromMicros(reader.GetInt64(13)),
                    reader.GetString(14),
                    reader.GetString(15)),
            CostKind.Unavailable => CostObservation.Unavailable(),
            _ => throw new InvalidDataException("A usage event contains an invalid cost record."),
        };

        return new UsageEvent(
            new UsageEventKey(reader.GetString(0)),
            new AgentId(reader.GetString(1)),
            reader.IsDBNull(2) ? null : new ModelProviderId(reader.GetString(2)),
            new ModelId(reader.GetString(3)),
            DateTimeOffset.ParseExact(
                reader.GetString(4),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            reader.GetString(5),
            new TokenBreakdown(
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetInt64(10)),
            cost,
            reader.GetString(16),
            (CoverageKind)reader.GetInt32(17),
            (UsageTimePrecision)reader.GetInt32(18),
            reader.IsDBNull(19) ? null : DateTimeOffset.Parse(reader.GetString(19), CultureInfo.InvariantCulture),
            reader.IsDBNull(20) ? null : new ModelId(reader.GetString(20)),
            reader.IsDBNull(21) ? null : reader.GetString(21), reader.IsDBNull(22) ? null : reader.GetString(22),
            new UsageDetailMetadata(reader.IsDBNull(23) ? null : new UsageSourceInstanceId(reader.GetString(23)),
                (UsageRecordKind)reader.GetInt32(24), reader.IsDBNull(25) ? null : reader.GetInt64(25),
                (UsageComponentAvailability)reader.GetInt32(26), (UsageComponentAvailability)reader.GetInt32(27),
                (UsageComponentAvailability)reader.GetInt32(28), (UsageComponentAvailability)reader.GetInt32(29),
                (UsageComponentAvailability)reader.GetInt32(30)));
    }

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
