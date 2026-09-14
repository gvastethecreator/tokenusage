using System.Globalization;
using Microsoft.Data.Sqlite;

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
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = consentEpoch is null
            ? "SELECT COUNT(*) FROM operation_fact WHERE capability = $capability;"
            : "SELECT COUNT(*) FROM operation_fact WHERE capability = $capability AND consent_epoch = $epoch;";
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
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consentEpoch);
        if (consentEpoch == 0)
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
            GROUP BY kind, tool, server
            ORDER BY SUM(quantity) DESC, kind, tool, server;
            """;
        command.Parameters.AddWithValue("$capability", capability.Value);
        command.Parameters.AddWithValue("$epoch", consentEpoch);
        command.Parameters.AddWithValue("$from", fromInclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", toExclusiveUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$session", sessionKey is null ? DBNull.Value : sessionKey.Value);
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
                linkedSession));
            index++;
        }

        return rows;
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
                started_at_utc, ended_at_utc, session_key, quantity)
            VALUES (
                $key, $capability, $epoch, $kind, $tool, $server, $outcome,
                $started, $ended, $session, $quantity)
            ON CONFLICT(operation_key) DO UPDATE SET
                consent_epoch = excluded.consent_epoch,
                kind = excluded.kind,
                tool = excluded.tool,
                server = excluded.server,
                outcome = excluded.outcome,
                started_at_utc = excluded.started_at_utc,
                ended_at_utc = excluded.ended_at_utc,
                session_key = excluded.session_key,
                quantity = excluded.quantity;
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
        foreach (UsageOperationFact fact in facts)
        {
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
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }
}
