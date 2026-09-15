using System.Globalization;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public sealed partial class UsageRepository
{
    private static async Task ApplyOperationFactSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken token)
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE operation_fact (
                operation_key TEXT NOT NULL PRIMARY KEY
                    CHECK(length(operation_key) = 64 AND operation_key NOT GLOB '*[^0-9a-f]*'),
                capability TEXT NOT NULL
                    CHECK(length(capability) BETWEEN 1 AND 64 AND capability NOT GLOB '*[^a-z0-9-]*'),
                consent_epoch INTEGER NOT NULL CHECK(typeof(consent_epoch) = 'integer' AND consent_epoch > 0),
                kind TEXT NOT NULL CHECK(kind IN ('tool', 'mcp', 'skill', 'spawn', 'command', 'file')),
                tool TEXT NOT NULL
                    CHECK(length(tool) BETWEEN 1 AND 64 AND tool NOT GLOB '*[^0-9A-Za-z._-]*'),
                server TEXT
                    CHECK(server IS NULL
                        OR (length(server) BETWEEN 1 AND 64 AND server NOT GLOB '*[^0-9A-Za-z._-]*')),
                outcome TEXT NOT NULL CHECK(outcome IN ('unknown', 'success', 'error')),
                started_at_utc TEXT NOT NULL,
                ended_at_utc TEXT,
                session_key TEXT
                    CHECK(session_key IS NULL
                        OR (length(session_key) = 64 AND session_key NOT GLOB '*[^0-9a-f]*')),
                quantity INTEGER NOT NULL DEFAULT 1 CHECK(typeof(quantity) = 'integer' AND quantity > 0));
            CREATE INDEX ix_operation_fact_capability
                ON operation_fact(capability, consent_epoch, started_at_utc);
            CREATE INDEX ix_operation_fact_kind ON operation_fact(kind, tool, server);
            INSERT INTO schema_migration(version, applied_at_utc)
                VALUES (11, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """, token).ConfigureAwait(false);

        foreach (string operation in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            await ExecuteAsync(connection, transaction, $"""
                CREATE TRIGGER guard_operation_fact_{operation} BEFORE {operation} ON operation_fact
                BEGIN
                    SELECT CASE WHEN tokenusage_writer_schema() < (SELECT MAX(version) FROM schema_migration)
                        THEN RAISE(ABORT, 'Usage schema changed; reopen with the current application.') END;
                END;
                """, token).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, $"""
                CREATE TRIGGER revision_operation_fact_{operation} AFTER {operation} ON operation_fact
                BEGIN
                    UPDATE usage_data_revision SET sequence = sequence + 1 WHERE singleton = 1;
                END;
                """, token).ConfigureAwait(false);
        }
    }

    private static async Task ApplyOperationSourceInstanceSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken token)
    {
        await ExecuteAsync(connection, transaction, """
            ALTER TABLE operation_fact ADD COLUMN source_instance TEXT
                CHECK(source_instance IS NULL
                    OR (length(source_instance) = 64 AND source_instance NOT GLOB '*[^0-9a-f]*'));
            INSERT INTO schema_migration(version, applied_at_utc)
                VALUES (12, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """, token).ConfigureAwait(false);
    }

    public async Task UpsertOperationFactsAsync(
        IReadOnlyList<UsageOperationFact> facts,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(facts);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await UpsertOperationFactsOnAsync(connection, transaction, facts, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> PurgeOperationFactsAsync(
        AttributionCapability capability,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM operation_fact WHERE capability = $capability;";
        command.Parameters.AddWithValue("$capability", capability.Value);
        int deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    public async Task<int> CountOperationFactsAsync(
        AttributionCapability capability,
        long? consentEpoch = null,
        bool? sourceInstancePresent = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM operation_fact WHERE capability = $capability";
        if (consentEpoch is not null)
        {
            command.CommandText += " AND consent_epoch = $epoch";
        }

        if (sourceInstancePresent is true)
        {
            command.CommandText += " AND source_instance IS NOT NULL";
        }
        else if (sourceInstancePresent is false)
        {
            command.CommandText += " AND source_instance IS NULL";
        }

        command.CommandText += ";";
        command.Parameters.AddWithValue("$capability", capability.Value);
        if (consentEpoch is { } epoch)
        {
            command.Parameters.AddWithValue("$epoch", epoch);
        }

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<UsageOperationRankedRow>> ReadOperationRankingAsync(
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        AttributionCapability capability,
        long consentEpoch,
        OpaqueAttributionKey? sessionKey = null,
        IReadOnlyCollection<string>? sessionKeys = null,
        bool restrictToSessionKeys = false,
        bool unlinkedOnly = false,
        bool? sourceInstancePresent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consentEpoch);
        if (!unlinkedOnly && restrictToSessionKeys && (sessionKeys is null || sessionKeys.Count == 0))
        {
            return [];
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT kind, tool, server,
                   SUM(quantity),
                   SUM(CASE WHEN outcome = 'success' THEN quantity ELSE 0 END),
                   SUM(CASE WHEN outcome = 'error' THEN quantity ELSE 0 END),
                   SUM(CASE WHEN outcome = 'unknown' THEN quantity ELSE 0 END),
                   MIN(session_key),
                   COUNT(DISTINCT session_key),
                   SUM(CASE WHEN session_key IS NULL THEN 1 ELSE 0 END)
            FROM operation_fact
            WHERE capability = $capability
              AND consent_epoch = $epoch
              AND started_at_utc >= $from
              AND started_at_utc < $to
              AND ($session IS NULL OR session_key = $session)
            """;
        AppendOperationPopulationFilters(
            command,
            restrictToSessionKeys,
            sessionKeys,
            unlinkedOnly,
            sourceInstancePresent);
        command.CommandText += " GROUP BY kind, tool, server ORDER BY SUM(quantity) DESC, kind, tool, server;";
        command.Parameters.AddWithValue("$capability", capability.Value);
        command.Parameters.AddWithValue("$epoch", consentEpoch);
        command.Parameters.AddWithValue("$from", fromInclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", toExclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$session", sessionKey is null ? DBNull.Value : sessionKey.Value);
        if (sessionKeys is { Count: > 0 })
        {
            int keyIndex = 0;
            foreach (string key in sessionKeys)
            {
                command.Parameters.AddWithValue("$sk" + keyIndex.ToString(CultureInfo.InvariantCulture), key);
                keyIndex++;
            }
        }
        var rows = new List<UsageOperationRankedRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        int index = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!UsageOperationKindCodec.TryParse(reader.GetString(0), out UsageOperationKind kind))
            {
                continue;
            }

            int invocations = Convert.ToInt32(reader.GetInt64(3), CultureInfo.InvariantCulture);
            int success = Convert.ToInt32(reader.GetInt64(4), CultureInfo.InvariantCulture);
            int error = Convert.ToInt32(reader.GetInt64(5), CultureInfo.InvariantCulture);
            int unknown = Convert.ToInt32(reader.GetInt64(6), CultureInfo.InvariantCulture);
            string tool = reader.GetString(1);
            string? server = reader.IsDBNull(2) ? null : reader.GetString(2);
            int distinctSessions = Convert.ToInt32(reader.GetInt64(8), CultureInfo.InvariantCulture);
            int missingSessions = Convert.ToInt32(reader.GetInt64(9), CultureInfo.InvariantCulture);
            OpaqueAttributionKey? linkedSession = !reader.IsDBNull(7)
                && distinctSessions == 1
                && missingSessions == 0
                && OpaqueAttributionKey.IsHexSha256(reader.GetString(7))
                ? new OpaqueAttributionKey(reader.GetString(7))
                : null;
            rows.Add(new UsageOperationRankedRow(
                $"{UsageOperationKindCodec.ToWire(kind)}:{server}:{tool}:{index}",
                kind,
                tool,
                server,
                invocations,
                success,
                error,
                unknown,
                OutcomesAvailable: unknown != invocations,
                linkedSession,
                capability,
                consentEpoch));
            index++;
        }

        return rows;
    }

    public async Task<(int Named, int LegacyUnnamed)> CountOperationSourcePopulationsAsync(
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        AttributionCapability capability,
        long consentEpoch,
        IReadOnlyCollection<string>? sessionKeys = null,
        bool restrictToSessionKeys = false,
        bool unlinkedOnly = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consentEpoch);
        if (!unlinkedOnly && restrictToSessionKeys && (sessionKeys is null || sessionKeys.Count == 0))
        {
            return (0, 0);
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN source_instance IS NOT NULL THEN quantity ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN source_instance IS NULL THEN quantity ELSE 0 END), 0)
            FROM operation_fact
            WHERE capability = $capability
              AND consent_epoch = $epoch
              AND started_at_utc >= $from
              AND started_at_utc < $to
            """;
        AppendOperationPopulationFilters(
            command,
            restrictToSessionKeys,
            sessionKeys,
            unlinkedOnly,
            sourceInstancePresent: null);
        command.Parameters.AddWithValue("$capability", capability.Value);
        command.Parameters.AddWithValue("$epoch", consentEpoch);
        command.Parameters.AddWithValue("$from", fromInclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", toExclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        if (sessionKeys is { Count: > 0 })
        {
            int keyIndex = 0;
            foreach (string key in sessionKeys)
            {
                command.Parameters.AddWithValue("$sk" + keyIndex.ToString(CultureInfo.InvariantCulture), key);
                keyIndex++;
            }
        }

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return (0, 0);
        }

        return (
            Convert.ToInt32(reader.GetInt64(0), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<UsageOperationTimelineRow>> ReadOperationTimelineAsync(
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        AttributionCapability capability,
        long consentEpoch,
        IReadOnlyCollection<string>? sessionKeys = null,
        bool restrictToSessionKeys = false,
        bool unlinkedOnly = false,
        bool? sourceInstancePresent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consentEpoch);
        if (!unlinkedOnly && restrictToSessionKeys && (sessionKeys is null || sessionKeys.Count == 0))
        {
            return [];
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation_key, kind, tool, server, started_at_utc, ended_at_utc, quantity, session_key, outcome
            FROM operation_fact
            WHERE capability = $capability
              AND consent_epoch = $epoch
              AND started_at_utc >= $from
              AND started_at_utc < $to
            """;
        AppendOperationPopulationFilters(
            command,
            restrictToSessionKeys,
            sessionKeys,
            unlinkedOnly,
            sourceInstancePresent);

        command.CommandText += " ORDER BY started_at_utc, kind, tool, server;";
        command.Parameters.AddWithValue("$capability", capability.Value);
        command.Parameters.AddWithValue("$epoch", consentEpoch);
        command.Parameters.AddWithValue("$from", fromInclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", toExclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        if (sessionKeys is { Count: > 0 })
        {
            int keyIndex = 0;
            foreach (string key in sessionKeys)
            {
                command.Parameters.AddWithValue("$sk" + keyIndex.ToString(CultureInfo.InvariantCulture), key);
                keyIndex++;
            }
        }

        var rows = new List<UsageOperationTimelineRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!UsageOperationKindCodec.TryParse(reader.GetString(1), out UsageOperationKind kind)
                || !UsageOperationOutcomeCodec.TryParse(reader.GetString(8), out UsageOperationOutcome outcome))
            {
                continue;
            }

            string? session = reader.IsDBNull(7) ? null : reader.GetString(7);
            rows.Add(new UsageOperationTimelineRow(
                kind,
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(5)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Convert.ToInt32(reader.GetInt64(6), CultureInfo.InvariantCulture),
                session is not null && OpaqueAttributionKey.IsHexSha256(session)
                    ? new OpaqueAttributionKey(session)
                    : null,
                outcome,
                reader.GetString(0)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<string>> ReadHomogeneousSessionKeysAsync(
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        AgentId agentId,
        AttributionCapability sessionCapability,
        long sessionEpoch,
        ModelId modelId,
        ModelProviderId? modelProviderId = null,
        UsageDetailSelection? detail = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentOutOfRangeException.ThrowIfNegative(sessionEpoch);
        if (sessionEpoch == 0)
        {
            return [];
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.session_key
            FROM session_attribution a
            JOIN usage_event e ON e.event_key = a.event_key
            WHERE a.consent_epoch = $epoch
              AND a.capability = $capability
              AND e.agent_id = $agent
              AND e.occurred_at_utc >= $from
              AND e.occurred_at_utc < $to
            GROUP BY a.session_key
            HAVING COUNT(*) > 0
               AND COUNT(*) = SUM(CASE WHEN e.model_id = $model
        """;
        if (modelProviderId is not null)
        {
            command.CommandText += " AND e.model_provider_id = $provider";
        }

        command.CommandText += " THEN 1 ELSE 0 END)";
        if (detail is { HasFilters: true })
        {
            command.CommandText += """
                 AND COUNT(*) = SUM(CASE WHEN 1=1
                """;
            if (detail.ObservedModels.Count > 0)
            {
                command.CommandText += " AND (" + OptionalInOrNull("e.observed_model_id", "hom", detail.ObservedModels.Select(item => item?.Value).ToArray()) + ")";
            }

            if (detail.ReasoningEfforts.Count > 0)
            {
                command.CommandText += " AND (" + OptionalInOrNull("e.reasoning_effort", "hef", detail.ReasoningEfforts) + ")";
            }

            if (detail.ServiceTiers.Count > 0)
            {
                command.CommandText += " AND (" + OptionalInOrNull("e.service_tier", "hti", detail.ServiceTiers) + ")";
            }

            command.CommandText += " THEN 1 ELSE 0 END)";
        }

        command.Parameters.AddWithValue("$epoch", sessionEpoch);
        command.Parameters.AddWithValue("$capability", sessionCapability.Value);
        command.Parameters.AddWithValue("$agent", agentId.Value);
        command.Parameters.AddWithValue("$from", fromInclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", toExclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$model", modelId.Value);
        if (modelProviderId is not null)
        {
            command.Parameters.AddWithValue("$provider", modelProviderId.Value);
        }

        BindHomogeneousLists(command, detail);
        var keys = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string key = reader.GetString(0);
            if (OpaqueAttributionKey.IsHexSha256(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    public async Task<IReadOnlyList<string>> ReadSessionKeysForProjectAsync(
        OpaqueAttributionKey projectKey,
        long projectEpoch,
        long sessionEpoch,
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectEpoch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionEpoch);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT a.session_key
            FROM session_attribution a
            JOIN project_attribution p ON p.event_key = a.event_key
            JOIN usage_event e ON e.event_key = a.event_key
            WHERE p.project_key = $project
              AND p.consent_epoch = $projectEpoch
              AND a.consent_epoch = $sessionEpoch
              AND a.session_key IS NOT NULL
              AND e.occurred_at_utc >= $from
              AND e.occurred_at_utc < $to;
            """;
        command.Parameters.AddWithValue("$project", projectKey.Value);
        command.Parameters.AddWithValue("$projectEpoch", projectEpoch);
        command.Parameters.AddWithValue("$sessionEpoch", sessionEpoch);
        command.Parameters.AddWithValue("$from", fromInclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", toExclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        return await ReadOpaqueSessionKeysAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ReadHomogeneousSessionKeysForProjectAsync(
        OpaqueAttributionKey projectKey,
        long projectEpoch,
        long sessionEpoch,
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectEpoch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionEpoch);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.session_key
            FROM session_attribution a
            JOIN usage_event e ON e.event_key = a.event_key
            LEFT JOIN project_attribution p
              ON p.event_key = a.event_key
             AND p.consent_epoch = $projectEpoch
            WHERE a.consent_epoch = $sessionEpoch
              AND a.session_key IS NOT NULL
              AND e.occurred_at_utc >= $from
              AND e.occurred_at_utc < $to
            GROUP BY a.session_key
            HAVING COUNT(*) > 0
               AND COUNT(*) = SUM(CASE WHEN p.project_key = $project THEN 1 ELSE 0 END);
            """;
        command.Parameters.AddWithValue("$project", projectKey.Value);
        command.Parameters.AddWithValue("$projectEpoch", projectEpoch);
        command.Parameters.AddWithValue("$sessionEpoch", sessionEpoch);
        command.Parameters.AddWithValue("$from", fromInclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", toExclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        return await ReadOpaqueSessionKeysAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ReadOpaqueSessionKeysAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string key = reader.GetString(0);
            if (OpaqueAttributionKey.IsHexSha256(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static void AppendOperationPopulationFilters(
        SqliteCommand command,
        bool restrictToSessionKeys,
        IReadOnlyCollection<string>? sessionKeys,
        bool unlinkedOnly,
        bool? sourceInstancePresent)
    {
        if (unlinkedOnly)
        {
            command.CommandText += " AND session_key IS NULL";
        }
        else if (restrictToSessionKeys)
        {
            command.CommandText += " AND " + SessionKeyInList("session_key", "sk", sessionKeys!.Count, negate: false);
        }
        else if (sessionKeys is { Count: > 0 })
        {
            command.CommandText += " AND (session_key IS NULL OR "
                + SessionKeyInList("session_key", "sk", sessionKeys.Count, negate: true)
                + ")";
        }

        if (sourceInstancePresent is true)
        {
            command.CommandText += " AND source_instance IS NOT NULL";
        }
        else if (sourceInstancePresent is false)
        {
            command.CommandText += " AND source_instance IS NULL";
        }
    }

    private static string SessionKeyInList(string column, string prefix, int count, bool negate)
    {
        string[] placeholders = Enumerable.Range(0, count)
            .Select(index => "$" + prefix + index.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        return column + (negate ? " NOT IN (" : " IN (") + string.Join(",", placeholders) + ")";
    }

    private static string OptionalInOrNull(string column, string prefix, IReadOnlyList<string?> values)
    {
        bool hasNull = values.Any(item => item is null);
        string[] present = values.OfType<string>().ToArray();
        var clauses = new List<string>();
        if (present.Length > 0)
        {
            string[] placeholders = present
                .Select((_, index) => "$" + prefix + index.ToString(CultureInfo.InvariantCulture))
                .ToArray();
            clauses.Add(column + " IN (" + string.Join(",", placeholders) + ")");
        }

        if (hasNull)
        {
            clauses.Add(column + " IS NULL");
        }

        return clauses.Count == 0 ? "1=1" : string.Join(" OR ", clauses);
    }

    private static void BindHomogeneousLists(SqliteCommand command, UsageDetailSelection? detail)
    {
        if (detail is null || !detail.HasFilters)
        {
            return;
        }

        BindPrefixList(command, "hom", detail.ObservedModels.Select(item => item?.Value).OfType<string>().ToArray());
        BindPrefixList(command, "hef", detail.ReasoningEfforts.OfType<string>().ToArray());
        BindPrefixList(command, "hti", detail.ServiceTiers.OfType<string>().ToArray());
    }

    private static void BindPrefixList(SqliteCommand command, string prefix, string[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            command.Parameters.AddWithValue("$" + prefix + index.ToString(CultureInfo.InvariantCulture), values[index]);
        }
    }

    private static async Task UpsertOperationFactsOnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<UsageOperationFact> facts,
        CancellationToken token)
    {
        if (facts.Count == 0)
        {
            return;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO operation_fact (
                operation_key, capability, consent_epoch, kind, tool, server, outcome,
                started_at_utc, ended_at_utc, session_key, quantity, source_instance)
            VALUES (
                $key, $capability, $epoch, $kind, $tool, $server, $outcome,
                $started, $ended, $session, $quantity, $source)
            ON CONFLICT(operation_key) DO UPDATE SET
                consent_epoch = excluded.consent_epoch,
                kind = excluded.kind,
                tool = excluded.tool,
                server = excluded.server,
                outcome = excluded.outcome,
                started_at_utc = excluded.started_at_utc,
                ended_at_utc = excluded.ended_at_utc,
                session_key = excluded.session_key,
                quantity = excluded.quantity,
                source_instance = excluded.source_instance;
            """;
        var key = command.Parameters.Add("$key", SqliteType.Text);
        var capability = command.Parameters.Add("$capability", SqliteType.Text);
        var epoch = command.Parameters.Add("$epoch", SqliteType.Integer);
        var kind = command.Parameters.Add("$kind", SqliteType.Text);
        var tool = command.Parameters.Add("$tool", SqliteType.Text);
        var server = command.Parameters.Add("$server", SqliteType.Text);
        var outcome = command.Parameters.Add("$outcome", SqliteType.Text);
        var started = command.Parameters.Add("$started", SqliteType.Text);
        var ended = command.Parameters.Add("$ended", SqliteType.Text);
        var session = command.Parameters.Add("$session", SqliteType.Text);
        var quantity = command.Parameters.Add("$quantity", SqliteType.Integer);
        var source = command.Parameters.Add("$source", SqliteType.Text);
        await using SqliteCommand retireLegacy = connection.CreateCommand();
        retireLegacy.Transaction = transaction;
        retireLegacy.CommandText = """
            DELETE FROM operation_fact
            WHERE operation_key = $legacy
              AND capability = $capability
              AND source_instance IS NULL;
            """;
        var legacyKey = retireLegacy.Parameters.Add("$legacy", SqliteType.Text);
        var legacyCapability = retireLegacy.Parameters.Add("$capability", SqliteType.Text);
        foreach (UsageOperationFact fact in facts)
        {
            if (fact.SourceInstance is not null
                && fact.LegacyOperationKey is { } legacy
                && legacy.Value != fact.OperationKey.Value)
            {
                legacyKey.Value = legacy.Value;
                legacyCapability.Value = fact.Capability.Value;
                await retireLegacy.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            key.Value = fact.OperationKey.Value;
            capability.Value = fact.Capability.Value;
            epoch.Value = fact.ConsentEpoch;
            kind.Value = UsageOperationKindCodec.ToWire(fact.Kind);
            tool.Value = fact.Tool;
            server.Value = fact.Server is null ? DBNull.Value : fact.Server;
            outcome.Value = UsageOperationOutcomeCodec.ToWire(fact.Outcome);
            started.Value = fact.StartedAtUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
            ended.Value = fact.EndedAtUtc is { } endedAt
                ? endedAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)
                : DBNull.Value;
            session.Value = fact.SessionKey is null ? DBNull.Value : fact.SessionKey.Value;
            quantity.Value = fact.Quantity;
            source.Value = fact.SourceInstance is null ? DBNull.Value : fact.SourceInstance.Value;
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }
}
