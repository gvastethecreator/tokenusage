using System.Globalization;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public sealed partial class UsageRepository
{
    private static async Task ApplyProjectAttributionSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken token)
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE project_attribution (
                event_key TEXT NOT NULL PRIMARY KEY
                    CHECK(length(event_key) = 64 AND event_key NOT GLOB '*[^0-9a-f]*'),
                project_key TEXT
                    CHECK(project_key IS NULL
                        OR (length(project_key) = 64 AND project_key NOT GLOB '*[^0-9a-f]*')),
                consent_epoch INTEGER NOT NULL CHECK(typeof(consent_epoch) = 'integer' AND consent_epoch > 0),
                mapping_kind TEXT NOT NULL CHECK(mapping_kind IN ('observed', 'user-mapped', 'ambiguous')),
                CHECK(
                    (mapping_kind = 'ambiguous')
                    OR (mapping_kind IN ('observed', 'user-mapped') AND project_key IS NOT NULL)
                ),
                FOREIGN KEY(event_key) REFERENCES usage_event(event_key) ON DELETE CASCADE);
            CREATE INDEX ix_project_attribution_project ON project_attribution(project_key, consent_epoch);
            ALTER TABLE session_attribution ADD COLUMN capability TEXT NOT NULL DEFAULT 'codex-session';
            INSERT INTO schema_migration(version, applied_at_utc)
                VALUES (10, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """, token).ConfigureAwait(false);

        foreach (string operation in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            await ExecuteAsync(connection, transaction, $"""
                CREATE TRIGGER guard_project_attribution_{operation} BEFORE {operation} ON project_attribution
                BEGIN
                    SELECT CASE WHEN tokenusage_writer_schema() < (SELECT MAX(version) FROM schema_migration)
                        THEN RAISE(ABORT, 'Usage schema changed; reopen with the current application.') END;
                END;
                """, token).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, $"""
                CREATE TRIGGER revision_project_attribution_{operation} AFTER {operation} ON project_attribution
                BEGIN
                    UPDATE usage_data_revision SET sequence = sequence + 1 WHERE singleton = 1;
                END;
                """, token).ConfigureAwait(false);
        }
    }

    public async Task ReplaceProjectLinksAsync(
        IReadOnlyList<UsageProjectLink> links,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(links);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await ReplaceProjectLinksOnAsync(
            connection,
            transaction,
            links,
            restrictToEventKeys: null,
            preserveUserMapped: false,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MapEventsToProjectAsync(
        IReadOnlyList<UsageEventKey> eventKeys,
        OpaqueAttributionKey projectKey,
        long consentEpoch,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(eventKeys);
        ArgumentNullException.ThrowIfNull(projectKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consentEpoch);
        UsageProjectLink[] links = eventKeys
            .Select(key => new UsageProjectLink(key, projectKey, consentEpoch, ProjectMappingKind.UserMapped))
            .ToArray();
        await ReplaceProjectLinksAsync(links, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> BackfillProjectLinksAsync(
        IReadOnlyList<UsageProjectLink> links,
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

        UsageProjectLink[] admitted = links
            .Where(link => link.ConsentEpoch == consentEpoch && existingKeys.Contains(link.EventKey.Value))
            .ToArray();
        var admittedKeys = admitted
            .Select(link => link.EventKey.Value)
            .ToHashSet(StringComparer.Ordinal);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await ReplaceProjectLinksOnAsync(
            connection,
            transaction,
            admitted,
            admittedKeys,
            preserveUserMapped: true,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return admitted.Length;
    }

    public async Task<int> PurgeProjectLinksAsync(CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM project_attribution;";
        int deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    public async Task<int> CountProjectLinksAsync(
        long? consentEpoch = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = consentEpoch is null
            ? "SELECT COUNT(*) FROM project_attribution;"
            : "SELECT COUNT(*) FROM project_attribution WHERE consent_epoch = $epoch;";
        if (consentEpoch is { } epoch)
        {
            command.Parameters.AddWithValue("$epoch", epoch);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<UsageEventKey>> ReadUnassignedProjectEventKeysAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        long consentEpoch,
        AgentId? agentId = null,
        ModelProviderId? modelProviderId = null,
        ModelId? modelId = null,
        UsageDetailSelection? detail = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromInclusive, toInclusive);
        ArgumentOutOfRangeException.ThrowIfNegative(consentEpoch);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.event_key FROM usage_event e
            WHERE e.civil_date BETWEEN $from AND $to
              AND NOT EXISTS (
                SELECT 1 FROM project_attribution p
                WHERE p.event_key = e.event_key AND $epoch > 0 AND p.consent_epoch = $epoch)
            """;
        command.Parameters.AddWithValue("$epoch", consentEpoch);
        command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
        command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
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

        BindDetailSelection(command, detail);
        var keys = new List<UsageEventKey>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(new UsageEventKey(reader.GetString(0)));
        }

        return keys;
    }

    public async Task<IReadOnlyList<UsageProjectContribution>> ReadProjectContributionsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        long consentEpoch,
        AgentId? agentId = null,
        ModelProviderId? modelProviderId = null,
        ModelId? modelId = null,
        UsageDetailSelection? detail = null,
        string? modelSearch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromInclusive, toInclusive);
        ArgumentOutOfRangeException.ThrowIfNegative(consentEpoch);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var selected = new Dictionary<string, (TokenBreakdown Tokens, int Count, DateTimeOffset First, DateTimeOffset Last, string Kind)>(
            StringComparer.Ordinal);
        TokenBreakdown unassigned = new(0, 0, 0, 0, 0);
        int unassignedCount = 0;
        DateTimeOffset? unassignedFirst = null;
        DateTimeOffset? unassignedLast = null;
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT e.event_key, a.project_key, a.mapping_kind,
                       e.input_tokens, e.output_tokens, e.reasoning_tokens, e.cache_read_tokens, e.cache_write_tokens,
                       e.occurred_at_utc
                FROM usage_event e
                LEFT JOIN project_attribution a
                    ON a.event_key = e.event_key AND $epoch > 0 AND a.consent_epoch = $epoch
                WHERE e.civil_date BETWEEN $from AND $to
                """;
            command.Parameters.AddWithValue("$epoch", consentEpoch);
            command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
            command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
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

            BindDetailSelection(command, detail);
            AppendModelSearchFilter(command, modelSearch);

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var tokens = new TokenBreakdown(
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7));
                DateTimeOffset occurred = DateTimeOffset.Parse(
                    reader.GetString(8),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
                if (reader.IsDBNull(1) && (reader.IsDBNull(2) || reader.GetString(2) != "ambiguous"))
                {
                    unassigned = AddTokens(unassigned, tokens);
                    unassignedCount++;
                    unassignedFirst = unassignedFirst is { } first && first < occurred ? first : occurred;
                    unassignedLast = unassignedLast is { } last && last > occurred ? last : occurred;
                    continue;
                }

                string bucket = reader.IsDBNull(1)
                    ? "ambiguous"
                    : reader.GetString(1) + "\u001f" + reader.GetString(2);
                string kind = reader.IsDBNull(2) ? "ambiguous" : reader.GetString(2);
                if (selected.TryGetValue(bucket, out var existing))
                {
                    selected[bucket] = (
                        AddTokens(existing.Tokens, tokens),
                        existing.Count + 1,
                        existing.First < occurred ? existing.First : occurred,
                        existing.Last > occurred ? existing.Last : occurred,
                        existing.Kind);
                }
                else
                {
                    selected[bucket] = (tokens, 1, occurred, occurred, kind);
                }
            }
        }

        var projectTotals = new Dictionary<string, (TokenBreakdown Tokens, int Count)>(StringComparer.Ordinal);
        string[] keyed = selected.Keys.Where(key => key != "ambiguous" && !key.EndsWith("\u001fambiguous", StringComparison.Ordinal)).ToArray();
        if (keyed.Length > 0)
        {
            string[] projectKeys = keyed
                .Select(key => key.Split('\u001f')[0])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            await using SqliteCommand totals = connection.CreateCommand();
            string[] parameters = projectKeys.Select((_, index) => "$p" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
            totals.CommandText = $"""
                SELECT a.project_key,
                       SUM(e.input_tokens), SUM(e.output_tokens), SUM(e.reasoning_tokens),
                       SUM(e.cache_read_tokens), SUM(e.cache_write_tokens), COUNT(*)
                FROM project_attribution a
                JOIN usage_event e ON e.event_key = a.event_key
                WHERE a.consent_epoch = $epoch AND a.project_key IN ({string.Join(",", parameters)})
                GROUP BY a.project_key;
                """;
            totals.Parameters.AddWithValue("$epoch", consentEpoch);
            for (int index = 0; index < projectKeys.Length; index++)
            {
                totals.Parameters.AddWithValue(parameters[index], projectKeys[index]);
            }

            await using SqliteDataReader reader = await totals.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                projectTotals[reader.GetString(0)] = (
                    new TokenBreakdown(
                        reader.GetInt64(1),
                        reader.GetInt64(2),
                        reader.GetInt64(3),
                        reader.GetInt64(4),
                        reader.GetInt64(5)),
                    Convert.ToInt32(reader.GetInt64(6), CultureInfo.InvariantCulture));
            }
        }

        var rows = new List<UsageProjectContribution>(selected.Count + 1);
        foreach ((string bucket, var selectedRow) in selected.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            bool ambiguous = selectedRow.Kind == "ambiguous";
            OpaqueAttributionKey? projectKey = ambiguous || bucket == "ambiguous"
                ? null
                : new OpaqueAttributionKey(bucket.Split('\u001f')[0]);
            ProjectMappingKind mapping = ProjectMappingKind.Ambiguous;
            _ = ProjectMappingKindCodec.TryParse(selectedRow.Kind, out mapping);
            (TokenBreakdown projectTokens, int projectCount) = projectKey is { } key
                && projectTotals.TryGetValue(key.Value, out var total)
                ? total
                : (selectedRow.Tokens, selectedRow.Count);
            rows.Add(new UsageProjectContribution(
                projectKey,
                mapping,
                selectedRow.Tokens,
                projectTokens,
                selectedRow.Count,
                projectCount,
                selectedRow.First,
                selectedRow.Last,
                IsUnassigned: false,
                IsAmbiguous: ambiguous,
                AttributionCapability.CodexProject,
                consentEpoch));
        }

        if (unassignedCount > 0)
        {
            rows.Add(new UsageProjectContribution(
                ProjectKey: null,
                MappingKind: null,
                unassigned,
                unassigned,
                unassignedCount,
                unassignedCount,
                unassignedFirst,
                unassignedLast,
                IsUnassigned: true,
                IsAmbiguous: false,
                AttributionCapability.CodexProject,
                consentEpoch));
        }

        return rows;
    }

    private static async Task ReplaceProjectLinksOnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<UsageProjectLink> links,
        HashSet<string>? restrictToEventKeys,
        bool preserveUserMapped,
        CancellationToken token)
    {
        UsageProjectLink[] admitted = links
            .Where(link => restrictToEventKeys is null || restrictToEventKeys.Contains(link.EventKey.Value))
            .DistinctBy(link => link.EventKey.Value)
            .ToArray();
        var rewriteKeys = new HashSet<string>(
            restrictToEventKeys ?? admitted.Select(link => link.EventKey.Value),
            StringComparer.Ordinal);
        if (preserveUserMapped && rewriteKeys.Count > 0)
        {
            foreach (string[] chunk in rewriteKeys.Chunk(SqliteVariableChunkSize))
            {
                await using SqliteCommand keep = connection.CreateCommand();
                keep.Transaction = transaction;
                string[] parameters = chunk.Select((_, index) => "$k" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
                keep.CommandText = $"""
                    SELECT event_key FROM project_attribution
                    WHERE mapping_kind = 'user-mapped' AND event_key IN ({string.Join(",", parameters)});
                    """;
                for (int index = 0; index < chunk.Length; index++)
                {
                    keep.Parameters.AddWithValue(parameters[index], chunk[index]);
                }

                await using SqliteDataReader reader = await keep.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    rewriteKeys.Remove(reader.GetString(0));
                }
            }

            admitted = admitted.Where(link => rewriteKeys.Contains(link.EventKey.Value)
                || link.MappingKind == ProjectMappingKind.UserMapped).ToArray();
            foreach (UsageProjectLink link in admitted.Where(link => link.MappingKind == ProjectMappingKind.UserMapped))
            {
                rewriteKeys.Add(link.EventKey.Value);
            }
        }

        if (rewriteKeys.Count > 0)
        {
            foreach (string[] chunk in rewriteKeys.Chunk(SqliteVariableChunkSize))
            {
                await using SqliteCommand delete = connection.CreateCommand();
                delete.Transaction = transaction;
                string[] parameters = chunk.Select((_, index) => "$k" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
                delete.CommandText = $"DELETE FROM project_attribution WHERE event_key IN ({string.Join(",", parameters)});";
                for (int index = 0; index < chunk.Length; index++)
                {
                    delete.Parameters.AddWithValue(parameters[index], chunk[index]);
                }

                await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        UsageProjectLink[] write = admitted.Where(link => rewriteKeys.Contains(link.EventKey.Value)).ToArray();
        if (write.Length == 0)
        {
            return;
        }

        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO project_attribution(event_key, project_key, consent_epoch, mapping_kind)
            SELECT $eventKey, $projectKey, $epoch, $kind
            WHERE EXISTS (SELECT 1 FROM usage_event WHERE event_key = $eventKey);
            """;
        foreach (UsageProjectLink link in write)
        {
            token.ThrowIfCancellationRequested();
            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("$eventKey", link.EventKey.Value);
            insert.Parameters.AddWithValue("$projectKey", (object?)link.ProjectKey?.Value ?? DBNull.Value);
            insert.Parameters.AddWithValue("$epoch", link.ConsentEpoch);
            insert.Parameters.AddWithValue("$kind", ProjectMappingKindCodec.ToWire(link.MappingKind));
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }
}
