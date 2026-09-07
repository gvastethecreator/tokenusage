using System.Globalization;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

// Provider dates have no implied local/UTC boundary until the source supplies it.
public sealed record AccountUsageAggregate(AgentId AgentId, DateOnly ProviderDate,
    long Tokens, DateTimeOffset ObservedAtUtc);

public sealed record UsageCollectionState(string AgentId, DateTimeOffset AttemptedAtUtc,
    UsageSourceReadStatus Status, UsageSourceIssueKind Issue);

public sealed partial class UsageRepository
{
    private static Task ApplyMeasurementSchemaAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, transaction, """
            ALTER TABLE usage_event ADD COLUMN time_precision INTEGER NOT NULL DEFAULT 0 CHECK(time_precision BETWEEN 0 AND 3);
            ALTER TABLE usage_event ADD COLUMN interval_started_at_utc TEXT NULL;
            ALTER TABLE usage_event ADD COLUMN observed_model_id TEXT NULL;
            ALTER TABLE usage_event ADD COLUMN reasoning_effort TEXT NULL;
            ALTER TABLE usage_event ADD COLUMN service_tier TEXT NULL;
            CREATE INDEX ix_usage_event_model_time ON usage_event(agent_id, model_id, occurred_at_utc);
            CREATE TABLE account_usage_daily (
                agent_id TEXT NOT NULL, provider_date TEXT NOT NULL,
                tokens INTEGER NOT NULL CHECK(tokens >= 0), observed_at_utc TEXT NOT NULL,
                PRIMARY KEY(agent_id, provider_date));
            CREATE TABLE usage_collection_state (
                agent_id TEXT PRIMARY KEY, attempted_at_utc TEXT NOT NULL, status INTEGER NOT NULL, issue INTEGER NOT NULL);
            CREATE TABLE saved_usage_comparison (
                revision_id TEXT NOT NULL PRIMARY KEY, created_at_utc TEXT NOT NULL,
                definition_json TEXT NOT NULL, result_json TEXT NOT NULL);
            INSERT INTO schema_migration(version, applied_at_utc) VALUES (5, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """, cancellationToken);

    public async Task<(string[] Pricing, string[] Parsers)> ReadReportVersionsAsync(DateOnly from, DateOnly to,
        AgentId? agentId, CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT catalog_version, parser_version FROM usage_event
            WHERE civil_date BETWEEN $from AND $to AND ($agent IS NULL OR agent_id = $agent);
            """;
        command.Parameters.AddWithValue("$from", FormatDate(from));
        command.Parameters.AddWithValue("$to", FormatDate(to));
        command.Parameters.AddWithValue("$agent", (object?)agentId?.Value ?? DBNull.Value);
        var pricing = new HashSet<string>(StringComparer.Ordinal);
        var parsers = new HashSet<string>(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0)) pricing.Add(reader.GetString(0));
            parsers.Add(reader.GetString(1));
        }
        return (pricing.Order().ToArray(), parsers.Order().ToArray());
    }

    public async Task RecordCollectionAsync(UsageCollectionState state, CancellationToken token = default)
    {
        EnsureWritable();
        UtcTimestamp.Require(state.AttemptedAtUtc, nameof(state));
        await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO usage_collection_state VALUES ($agent, $time, $status, $issue)
            ON CONFLICT(agent_id) DO UPDATE SET attempted_at_utc = excluded.attempted_at_utc,
                status = excluded.status, issue = excluded.issue
            WHERE excluded.attempted_at_utc > usage_collection_state.attempted_at_utc;
            """;
        command.Parameters.AddWithValue("$agent", state.AgentId);
        command.Parameters.AddWithValue("$time", state.AttemptedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$status", (int)state.Status);
        command.Parameters.AddWithValue("$issue", (int)state.Issue);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UsageCollectionState>> ReadCollectionStateAsync(CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT agent_id, attempted_at_utc, status, issue FROM usage_collection_state ORDER BY agent_id;";
        var result = new List<UsageCollectionState>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                (UsageSourceReadStatus)reader.GetInt32(2), (UsageSourceIssueKind)reader.GetInt32(3)));
        return result;
    }

    public async Task<bool> HasDifferentParserInRangeAsync(AgentId agentId, string parserVersion,
        DateOnly from, DateOnly to, CancellationToken token = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM usage_event WHERE agent_id = $agent
                AND civil_date BETWEEN $from AND $to AND parser_version <> $parser);
            """;
        command.Parameters.AddWithValue("$agent", agentId.Value);
        command.Parameters.AddWithValue("$parser", parserVersion);
        command.Parameters.AddWithValue("$from", FormatDate(from));
        command.Parameters.AddWithValue("$to", FormatDate(to));
        return Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0;
    }

    public async Task UpsertAccountUsageAsync(IReadOnlyList<AccountUsageAggregate> aggregates,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        if (aggregates.Count == 0) return;
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO account_usage_daily(agent_id, provider_date, tokens, observed_at_utc)
            VALUES ($agent, $date, $tokens, $observed)
            ON CONFLICT(agent_id, provider_date) DO UPDATE SET tokens = excluded.tokens,
                observed_at_utc = excluded.observed_at_utc
            WHERE excluded.observed_at_utc > account_usage_daily.observed_at_utc;
            """;
        foreach (AccountUsageAggregate aggregate in aggregates)
        {
            ArgumentNullException.ThrowIfNull(aggregate.AgentId);
            ArgumentOutOfRangeException.ThrowIfNegative(aggregate.Tokens);
            UtcTimestamp.Require(aggregate.ObservedAtUtc, nameof(aggregates));
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$agent", aggregate.AgentId.Value);
            command.Parameters.AddWithValue("$date", FormatDate(aggregate.ProviderDate));
            command.Parameters.AddWithValue("$tokens", aggregate.Tokens);
            command.Parameters.AddWithValue("$observed", aggregate.ObservedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountUsageAggregate>> ReadAccountUsageAsync(DateOnly from,
        DateOnly to, AgentId? agentId = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(to, from);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT agent_id, provider_date, tokens, observed_at_utc FROM account_usage_daily
            WHERE provider_date BETWEEN $from AND $to AND ($agent IS NULL OR agent_id = $agent)
            ORDER BY provider_date, agent_id;
            """;
        command.Parameters.AddWithValue("$from", FormatDate(from));
        command.Parameters.AddWithValue("$to", FormatDate(to));
        command.Parameters.AddWithValue("$agent", (object?)agentId?.Value ?? DBNull.Value);
        var result = new List<AccountUsageAggregate>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(new AgentId(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt64(2), DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        return result;
    }
}
