using System.Globalization;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public sealed record UsageDetailSelection(
    IReadOnlyList<ModelId?> ObservedModels,
    IReadOnlyList<string?> ReasoningEfforts,
    IReadOnlyList<string?> ServiceTiers)
{
    public static UsageDetailSelection Empty { get; } = new([], [], []);

    public bool HasFilters =>
        ObservedModels.Count > 0 || ReasoningEfforts.Count > 0 || ServiceTiers.Count > 0;
}

public sealed record UsageSessionContribution(
    OpaqueAttributionKey? SessionKey,
    OpaqueAttributionKey? ParentSessionKey,
    TokenBreakdown SelectedTokens,
    TokenBreakdown SessionTokens,
    int SelectedEventCount,
    int SessionEventCount,
    DateTimeOffset? FirstOccurredAtUtc,
    DateTimeOffset? LastOccurredAtUtc,
    bool IsUnassigned);

public sealed partial class UsageRepository
{
    private static async Task ApplyAttributionSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken token)
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE session_attribution (
                event_key TEXT NOT NULL PRIMARY KEY
                    CHECK(length(event_key) = 64 AND event_key NOT GLOB '*[^0-9a-f]*'),
                session_key TEXT NOT NULL
                    CHECK(length(session_key) = 64 AND session_key NOT GLOB '*[^0-9a-f]*'),
                parent_session_key TEXT
                    CHECK(parent_session_key IS NULL
                        OR (length(parent_session_key) = 64 AND parent_session_key NOT GLOB '*[^0-9a-f]*')),
                consent_epoch INTEGER NOT NULL CHECK(typeof(consent_epoch) = 'integer' AND consent_epoch > 0),
                FOREIGN KEY(event_key) REFERENCES usage_event(event_key) ON DELETE CASCADE);
            CREATE INDEX ix_session_attribution_session ON session_attribution(session_key, consent_epoch);
            INSERT INTO schema_migration(version, applied_at_utc)
                VALUES (9, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """, token).ConfigureAwait(false);

        foreach (string operation in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            await ExecuteAsync(connection, transaction, $"""
                CREATE TRIGGER guard_session_attribution_{operation} BEFORE {operation} ON session_attribution
                BEGIN
                    SELECT CASE WHEN tokenusage_writer_schema() < (SELECT MAX(version) FROM schema_migration)
                        THEN RAISE(ABORT, 'Usage schema changed; reopen with the current application.') END;
                END;
                """, token).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, $"""
                CREATE TRIGGER revision_session_attribution_{operation} AFTER {operation} ON session_attribution
                BEGIN
                    UPDATE usage_data_revision SET sequence = sequence + 1 WHERE singleton = 1;
                END;
                """, token).ConfigureAwait(false);
        }
    }

    public async Task ReplaceSessionLinksAsync(
        IReadOnlyList<UsageSessionLink> links,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(links);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await ReplaceSessionLinksOnAsync(connection, transaction, links, restrictToEventKeys: null, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceAttributionLinksForEventsAsync(
        IReadOnlyList<UsageSessionLink> sessionLinks,
        IReadOnlyList<UsageProjectLink> projectLinks,
        IReadOnlyCollection<string> eventKeys,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(sessionLinks);
        ArgumentNullException.ThrowIfNull(projectLinks);
        ArgumentNullException.ThrowIfNull(eventKeys);
        var keys = eventKeys.ToHashSet(StringComparer.Ordinal);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await ReplaceSessionLinksOnAsync(connection, transaction, sessionLinks, keys, cancellationToken)
            .ConfigureAwait(false);
        await ReplaceProjectLinksOnAsync(
            connection,
            transaction,
            projectLinks,
            keys,
            preserveUserMapped: true,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> PurgeSessionLinksAsync(
        AttributionCapability? capability = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = capability is null
            ? "DELETE FROM session_attribution;"
            : "DELETE FROM session_attribution WHERE capability = $capability;";
        if (capability is { } filter)
        {
            command.Parameters.AddWithValue("$capability", filter.Value);
        }

        int deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    public async Task<int> CountSessionLinksAsync(
        long? consentEpoch = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = consentEpoch is null
            ? "SELECT COUNT(*) FROM session_attribution;"
            : "SELECT COUNT(*) FROM session_attribution WHERE consent_epoch = $epoch;";
        if (consentEpoch is { } epoch)
        {
            command.Parameters.AddWithValue("$epoch", epoch);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<UsageSessionContribution>> ReadSessionContributionsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        long consentEpoch,
        AgentId? agentId = null,
        ModelProviderId? modelProviderId = null,
        ModelId? modelId = null,
        AttributionCapability? capability = null,
        OpaqueAttributionKey? projectKey = null,
        bool unassignedProject = false,
        long? projectEpoch = null,
        UsageDetailSelection? detail = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromInclusive, toInclusive);
        ArgumentOutOfRangeException.ThrowIfNegative(consentEpoch);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var selected = new Dictionary<string, (TokenBreakdown Tokens, int Count, DateTimeOffset First, DateTimeOffset Last, string? Parent)>(
            StringComparer.Ordinal);
        TokenBreakdown unassigned = new(0, 0, 0, 0, 0);
        int unassignedCount = 0;
        DateTimeOffset? unassignedFirst = null;
        DateTimeOffset? unassignedLast = null;
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT e.event_key, a.session_key, a.parent_session_key,
                       e.input_tokens, e.output_tokens, e.reasoning_tokens, e.cache_read_tokens, e.cache_write_tokens,
                       e.occurred_at_utc
                FROM usage_event e
                LEFT JOIN session_attribution a
                    ON a.event_key = e.event_key
                    AND $epoch > 0
                    AND a.consent_epoch = $epoch
                    AND a.capability = $capability
                WHERE e.civil_date BETWEEN $from AND $to
                """;
            command.Parameters.AddWithValue("$epoch", consentEpoch);
            command.Parameters.AddWithValue("$capability", (capability ?? AttributionCapability.CodexSession).Value);
            command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
            command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
            BindDetailSelection(command, detail);
            if (projectKey is not null || unassignedProject)
            {
                long epoch = projectEpoch ?? consentEpoch;
                if (unassignedProject)
                {
                    command.CommandText += """
                         AND NOT EXISTS (
                            SELECT 1 FROM project_attribution p
                            WHERE p.event_key = e.event_key AND p.consent_epoch = $projectEpoch)
                        """;
                }
                else
                {
                    command.CommandText += """
                         AND EXISTS (
                            SELECT 1 FROM project_attribution p
                            WHERE p.event_key = e.event_key
                              AND p.consent_epoch = $projectEpoch
                              AND p.project_key = $projectKey)
                        """;
                    command.Parameters.AddWithValue("$projectKey", projectKey!.Value);
                }

                command.Parameters.AddWithValue("$projectEpoch", epoch);
            }
            if (agentId is not null)
            {
                command.CommandText += " AND e.agent_id = $agent";
                command.Parameters.AddWithValue("$agent", agentId.Value);
            }

            if (modelProviderId is not null)
            {
                command.CommandText += " AND e.model_provider_id = $provider";
                command.Parameters.AddWithValue("$provider", modelProviderId.Value);
            }
            else if (modelId is not null)
            {
                command.CommandText += " AND e.model_provider_id IS NULL";
            }

            if (modelId is not null)
            {
                command.CommandText += " AND e.model_id = $model";
                command.Parameters.AddWithValue("$model", modelId.Value);
            }

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var tokens = new TokenBreakdown(
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7));
                DateTimeOffset occurred = DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                if (reader.IsDBNull(1))
                {
                    unassigned = AddTokens(unassigned, tokens);
                    unassignedCount++;
                    unassignedFirst = unassignedFirst is { } first && first < occurred ? first : occurred;
                    unassignedLast = unassignedLast is { } last && last > occurred ? last : occurred;
                    continue;
                }

                string sessionKey = reader.GetString(1);
                string? parent = reader.IsDBNull(2) ? null : reader.GetString(2);
                if (selected.TryGetValue(sessionKey, out var existing))
                {
                    selected[sessionKey] = (
                        AddTokens(existing.Tokens, tokens),
                        existing.Count + 1,
                        existing.First < occurred ? existing.First : occurred,
                        existing.Last > occurred ? existing.Last : occurred,
                        existing.Parent);
                }
                else
                {
                    selected[sessionKey] = (tokens, 1, occurred, occurred, parent);
                }
            }
        }

        var sessionTotals = new Dictionary<string, (TokenBreakdown Tokens, int Count)>(StringComparer.Ordinal);
        if (selected.Count > 0)
        {
            await using SqliteCommand totals = connection.CreateCommand();
            string[] keys = selected.Keys.ToArray();
            string[] parameters = keys.Select((_, index) => "$s" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
            totals.CommandText = $"""
                SELECT a.session_key,
                       SUM(e.input_tokens), SUM(e.output_tokens), SUM(e.reasoning_tokens),
                       SUM(e.cache_read_tokens), SUM(e.cache_write_tokens), COUNT(*)
                FROM session_attribution a
                JOIN usage_event e ON e.event_key = a.event_key
                WHERE a.consent_epoch = $epoch AND a.session_key IN ({string.Join(",", parameters)})
                GROUP BY a.session_key;
                """;
            totals.Parameters.AddWithValue("$epoch", consentEpoch);
            for (int index = 0; index < keys.Length; index++)
            {
                totals.Parameters.AddWithValue(parameters[index], keys[index]);
            }

            await using SqliteDataReader reader = await totals.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                sessionTotals[reader.GetString(0)] = (
                    new TokenBreakdown(
                        reader.GetInt64(1),
                        reader.GetInt64(2),
                        reader.GetInt64(3),
                        reader.GetInt64(4),
                        reader.GetInt64(5)),
                    Convert.ToInt32(reader.GetInt64(6), CultureInfo.InvariantCulture));
            }
        }

        var rows = new List<UsageSessionContribution>(selected.Count + 1);
        foreach ((string key, var selectedRow) in selected.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            (TokenBreakdown sessionTokens, int sessionCount) = sessionTotals.TryGetValue(key, out var total)
                ? total
                : (selectedRow.Tokens, selectedRow.Count);
            rows.Add(new UsageSessionContribution(
                new OpaqueAttributionKey(key),
                selectedRow.Parent is null ? null : new OpaqueAttributionKey(selectedRow.Parent),
                selectedRow.Tokens,
                sessionTokens,
                selectedRow.Count,
                sessionCount,
                selectedRow.First,
                selectedRow.Last,
                IsUnassigned: false));
        }

        if (unassignedCount > 0)
        {
            rows.Add(new UsageSessionContribution(
                SessionKey: null,
                ParentSessionKey: null,
                unassigned,
                unassigned,
                unassignedCount,
                unassignedCount,
                unassignedFirst,
                unassignedLast,
                IsUnassigned: true));
        }

        return rows;
    }

    private static async Task ReplaceSessionLinksOnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<UsageSessionLink> links,
        HashSet<string>? restrictToEventKeys,
        CancellationToken token)
    {
        UsageSessionLink[] admitted = links
            .Where(link => restrictToEventKeys is null || restrictToEventKeys.Contains(link.EventKey.Value))
            .DistinctBy(link => link.EventKey.Value)
            .ToArray();
        var rewriteKeys = new HashSet<string>(restrictToEventKeys ?? admitted.Select(link => link.EventKey.Value), StringComparer.Ordinal);
        if (rewriteKeys.Count > 0)
        {
            foreach (string[] chunk in rewriteKeys.Chunk(SqliteVariableChunkSize))
            {
                await using SqliteCommand delete = connection.CreateCommand();
                delete.Transaction = transaction;
                string[] parameters = chunk.Select((_, index) => "$k" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
                delete.CommandText = $"DELETE FROM session_attribution WHERE event_key IN ({string.Join(",", parameters)});";
                for (int index = 0; index < chunk.Length; index++)
                {
                    delete.Parameters.AddWithValue(parameters[index], chunk[index]);
                }

                await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        if (admitted.Length == 0)
        {
            return;
        }

        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO session_attribution(event_key, session_key, parent_session_key, consent_epoch, capability)
            SELECT $eventKey, $sessionKey, $parent, $epoch, $capability
            WHERE EXISTS (SELECT 1 FROM usage_event WHERE event_key = $eventKey);
            """;
        foreach (UsageSessionLink link in admitted)
        {
            token.ThrowIfCancellationRequested();
            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("$eventKey", link.EventKey.Value);
            insert.Parameters.AddWithValue("$sessionKey", link.SessionKey.Value);
            insert.Parameters.AddWithValue("$parent", (object?)link.ParentSessionKey?.Value ?? DBNull.Value);
            insert.Parameters.AddWithValue("$epoch", link.ConsentEpoch);
            insert.Parameters.AddWithValue("$capability", link.Capability.Value);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    public async Task<int> BackfillSessionLinksAsync(
        IReadOnlyList<UsageSessionLink> links,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        long consentEpoch,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(links);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromInclusive, toInclusive);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consentEpoch);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var existingKeys = new HashSet<string>(StringComparer.Ordinal);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT event_key FROM usage_event
                WHERE civil_date BETWEEN $from AND $to;
                """;
            command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
            command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                existingKeys.Add(reader.GetString(0));
            }
        }

        UsageSessionLink[] admitted = links
            .Where(link => link.ConsentEpoch == consentEpoch && existingKeys.Contains(link.EventKey.Value))
            .ToArray();
        var admittedKeys = admitted
            .Select(link => link.EventKey.Value)
            .ToHashSet(StringComparer.Ordinal);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await ReplaceSessionLinksOnAsync(connection, transaction, admitted, admittedKeys, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return admitted.Length;
    }

    internal static void BindDetailSelection(SqliteCommand command, UsageDetailSelection? detail)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (detail is null || !detail.HasFilters)
        {
            return;
        }

        BindOptionalTextList(
            command,
            "e.observed_model_id",
            "om",
            detail.ObservedModels.Select(model => model?.Value).ToArray());
        BindOptionalTextList(command, "e.reasoning_effort", "ef", detail.ReasoningEfforts);
        BindOptionalTextList(command, "e.service_tier", "ti", detail.ServiceTiers);
    }

    private static void BindOptionalTextList(
        SqliteCommand command,
        string column,
        string prefix,
        IReadOnlyList<string?> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        var clauses = new List<string>();
        string[] present = values.OfType<string>().ToArray();
        if (present.Length > 0)
        {
            string[] placeholders = present
                .Select((_, index) => "$" + prefix + index.ToString(CultureInfo.InvariantCulture))
                .ToArray();
            clauses.Add($"{column} IN ({string.Join(",", placeholders)})");
            for (int index = 0; index < present.Length; index++)
            {
                command.Parameters.AddWithValue(placeholders[index], present[index]);
            }
        }

        if (values.Any(value => value is null))
        {
            clauses.Add($"{column} IS NULL");
        }

        command.CommandText += " AND (" + string.Join(" OR ", clauses) + ")";
    }

    private static TokenBreakdown AddTokens(TokenBreakdown left, TokenBreakdown right) =>
        new(
            checked(left.Input + right.Input),
            checked(left.Output + right.Output),
            checked(left.Reasoning + right.Reasoning),
            checked(left.CacheRead + right.CacheRead),
            checked(left.CacheWrite + right.CacheWrite));
}
