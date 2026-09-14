using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TokenUsage.Core.Usage;

public sealed record UsageSourceStoreResult(int WrittenCount, int WithheldCount, bool HasUnresolvedHistory);

public sealed partial class UsageRepository
{
    /// <summary>Store a profile-proven read. Exact legacy matches acquire only the source ID;
    /// unresolved history prevents authoritative deletion and overlapping new identities.</summary>
    public async Task<UsageSourceStoreResult> StoreSourceObservationsAsync(AgentId agentId,
        UsageSourceInstanceId sourceInstance, string parserVersion, DateOnly from, DateOnly to,
        IEnumerable<UsageEvent> observations, bool complete,
        IReadOnlyList<UsageSessionLink>? sessionLinks = null,
        IReadOnlyList<UsageProjectLink>? projectLinks = null,
        IReadOnlyList<UsageOperationFact>? operations = null,
        CancellationToken token = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(sourceInstance);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, to);
        UsageEvent[] batch = ValidateAgentBatch(agentId, observations, "Source admission");
        if (batch.Any(row => row.DetailMetadata.SourceInstance != sourceInstance
            || row.ParserVersion != parserVersion || AssertSingleRollup(row).Date < from
            || AssertSingleRollup(row).Date > to))
            throw new ArgumentException("Source admission requires one source, parser and civil-date window.", nameof(observations));

        await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await AssociateLegacyObservationsOnAsync(connection, transaction, batch, token).ConfigureAwait(false);

        var ambiguousDays = new HashSet<string>(StringComparer.Ordinal);
        var retiredDays = new HashSet<string>(StringComparer.Ordinal);
        var ownedKeys = new HashSet<string>(StringComparer.Ordinal);
        var conflictingKeys = new HashSet<string>(StringComparer.Ordinal);
        bool differentParser = false;
        await using (SqliteCommand history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = """
                SELECT event_key, source_instance_id, civil_date, parser_version FROM usage_event
                WHERE agent_id = $agent AND civil_date BETWEEN $from AND $to;
                """;
            history.Parameters.AddWithValue("$agent", agentId.Value);
            history.Parameters.AddWithValue("$from", FormatDate(from));
            history.Parameters.AddWithValue("$to", FormatDate(to));
            await using SqliteDataReader reader = await history.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (reader.IsDBNull(1)) ambiguousDays.Add(reader.GetString(2));
                if (!reader.IsDBNull(1) && reader.GetString(1) == sourceInstance.Value)
                {
                    ownedKeys.Add(reader.GetString(0));
                    differentParser |= reader.GetString(3) != parserVersion;
                }
                else conflictingKeys.Add(reader.GetString(0));
            }
        }
        await using (SqliteCommand history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = """
                SELECT DISTINCT r.civil_date FROM daily_usage_rollup r
                WHERE r.agent_id = $agent AND r.civil_date BETWEEN $from AND $to
                  AND r.event_count > (SELECT COUNT(*) FROM usage_event e
                    WHERE e.agent_id = r.agent_id AND e.civil_date = r.civil_date
                      AND e.grouping_time_zone_id = r.grouping_time_zone_id
                      AND COALESCE(e.model_provider_id, '') = r.model_provider_id AND e.model_id = r.model_id);
                """;
            history.Parameters.AddWithValue("$agent", agentId.Value);
            history.Parameters.AddWithValue("$from", FormatDate(from));
            history.Parameters.AddWithValue("$to", FormatDate(to));
            await using SqliteDataReader reader = await history.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false)) retiredDays.Add(reader.GetString(0));
        }
        HashSet<string> tombstones = await LoadTombstonedKeysAsync(connection, transaction, batch, token).ConfigureAwait(false);
        UsageEvent[] admitted = batch.Where(row => tombstones.Contains(row.EventKey.Value)
            || (!conflictingKeys.Contains(row.EventKey.Value)
                && !retiredDays.Contains(FormatDate(AssertSingleRollup(row).Date))
                && (ownedKeys.Contains(row.EventKey.Value)
                    || !ambiguousDays.Contains(FormatDate(AssertSingleRollup(row).Date))))).ToArray();
        bool unresolved = ambiguousDays.Count > 0 || retiredDays.Count > 0 || admitted.Length != batch.Length;
        bool authoritative = complete && !unresolved;
        if (!authoritative && differentParser)
            admitted = admitted.Where(row => tombstones.Contains(row.EventKey.Value)).ToArray();
        int withheld = batch.Length - admitted.Length;
        unresolved |= withheld > 0;
        DateOnly[] previousDates = await LoadExistingEventDatesAsync(connection, transaction, agentId, admitted, token)
            .ConfigureAwait(false);
        // Also reject cross-window/key collisions before any deletion or numeric write.
        await VerifyEventOwnershipAsync(connection, transaction, admitted, token).ConfigureAwait(false);
        foreach (DateOnly date in previousDates)
            await VerifyRetainedRollupsCanRebuildAsync(connection, transaction, agentId, date, date, token).ConfigureAwait(false);
        if (authoritative)
        {
            await using SqliteCommand delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM usage_event WHERE agent_id = $agent AND source_instance_id = $source AND civil_date BETWEEN $from AND $to;";
            delete.Parameters.AddWithValue("$agent", agentId.Value);
            delete.Parameters.AddWithValue("$source", sourceInstance.Value);
            delete.Parameters.AddWithValue("$from", FormatDate(from));
            delete.Parameters.AddWithValue("$to", FormatDate(to));
            await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        UsageEvent[] written = await WriteEventsAsync(connection, transaction, admitted, EventWriteKind.Upsert,
            respectTombstones: true, token).ConfigureAwait(false);
        await ReplaceSessionLinksOnAsync(
            connection,
            transaction,
            sessionLinks ?? [],
            written.Select(row => row.EventKey.Value).ToHashSet(StringComparer.Ordinal),
            token).ConfigureAwait(false);
        await ReplaceProjectLinksOnAsync(
            connection,
            transaction,
            projectLinks ?? [],
            written.Select(row => row.EventKey.Value).ToHashSet(StringComparer.Ordinal),
            preserveUserMapped: true,
            token).ConfigureAwait(false);
        await UpsertOperationFactsOnAsync(connection, transaction, operations ?? [], token).ConfigureAwait(false);
        if (authoritative)
            await RebuildAgentRollupsInRangeAsync(connection, transaction, agentId, from, to, token).ConfigureAwait(false);
        await RebuildAgentRollupsForDatesAsync(connection, transaction, agentId,
            previousDates.Concat(written.Select(row => AssertSingleRollup(row).Date))
                .Where(date => !authoritative || date < from || date > to).Distinct().ToArray(), token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return new(written.Length, withheld, unresolved);
    }

    private static async Task VerifyRetainedRollupsCanRebuildAsync(SqliteConnection connection,
        SqliteTransaction transaction, AgentId agentId, DateOnly from, DateOnly to,
        CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM daily_usage_rollup r
                WHERE r.agent_id = $agent AND r.civil_date BETWEEN $from AND $to
                  AND r.event_count > (
                    SELECT COUNT(*) FROM usage_event e
                    WHERE e.agent_id = r.agent_id AND e.civil_date = r.civil_date
                      AND e.grouping_time_zone_id = r.grouping_time_zone_id
                      AND COALESCE(e.model_provider_id, '') = r.model_provider_id
                      AND e.model_id = r.model_id));
            """;
        command.Parameters.AddWithValue("$agent", agentId.Value);
        command.Parameters.AddWithValue("$from", FormatDate(from));
        command.Parameters.AddWithValue("$to", FormatDate(to));
        if (Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0)
            throw new InvalidDataException("Retained aggregate history cannot be replaced without its underlying records.");
    }

    public async Task<int> AssociateLegacyObservationsAsync(AgentId agentId, UsageSourceInstanceId sourceInstance,
        IEnumerable<UsageEvent> observations, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(sourceInstance);
        UsageEvent[] batch = ValidateAgentBatch(agentId, observations, "Legacy association");
        if (batch.Any(row => row.DetailMetadata.SourceInstance != sourceInstance))
            throw new ArgumentException("Association evidence must declare the selected source.", nameof(observations));
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        int associated = await AssociateLegacyObservationsOnAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return associated;
    }

    private static async Task<int> AssociateLegacyObservationsOnAsync(SqliteConnection connection,
        SqliteTransaction transaction, UsageEvent[] batch, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE usage_event SET source_instance_id = $sourceInstance
            WHERE source_instance_id IS NULL AND event_key = $eventKey AND agent_id = $agentId
              AND model_provider_id IS $modelProviderId AND model_id = $modelId
              AND occurred_at_utc = $occurredAt AND grouping_time_zone_id = $timeZone AND civil_date = $civilDate
              AND parser_version = $parserVersion AND coverage_kind = $coverage
              AND input_tokens = $input AND output_tokens = $output AND reasoning_tokens = $reasoning
              AND cache_read_tokens = $cacheRead AND cache_write_tokens = $cacheWrite
              AND cost_kind = $costKind AND reported_cost_micros IS $reported AND estimated_cost_micros IS $estimated
              AND catalog_version IS $catalogVersion AND exact_price_match IS $priceMatch
              AND time_precision = $precision AND interval_started_at_utc IS $intervalStart
              AND observed_model_id IS $observedModel AND reasoning_effort IS $effort AND service_tier IS $tier
              AND record_kind = $recordKind AND representation_revision IS $representationRevision
              AND input_availability = $inputAvailability AND output_availability = $outputAvailability
              AND reasoning_availability = $reasoningAvailability AND cache_read_availability = $cacheReadAvailability
              AND cache_write_availability = $cacheWriteAvailability;
            """;
        int associated = 0;
        foreach (UsageEvent observation in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BindUsageEventParameters(command, observation, AssertSingleRollup(observation).Date);
            associated = checked(associated + await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false));
        }
        return associated;
    }

    private static async Task VerifyEventOwnershipAsync(SqliteConnection connection,
        SqliteTransaction transaction, UsageEvent[] batch, CancellationToken token)
    {
        var owners = new Dictionary<string, (AgentId Agent, UsageSourceInstanceId? Source)>(StringComparer.Ordinal);
        foreach (UsageEvent row in batch)
        {
            var owner = (row.AgentId, row.DetailMetadata.SourceInstance);
            if (owners.TryGetValue(row.EventKey.Value, out var previous) && previous != owner)
                throw new InvalidDataException("An event key cannot belong to multiple source authorities.");
            owners[row.EventKey.Value] = owner;
        }
        foreach (string[] keys in owners.Keys.Chunk(500))
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            string[] parameters = keys.Select((_, index) => "$key" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
            command.CommandText = $"SELECT event_key, agent_id, source_instance_id FROM usage_event WHERE event_key IN ({string.Join(",", parameters)});";
            for (int index = 0; index < keys.Length; index++) command.Parameters.AddWithValue(parameters[index], keys[index]);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var expected = owners[reader.GetString(0)];
                if (reader.GetString(1) != expected.Agent.Value
                    || (reader.IsDBNull(2) ? null : reader.GetString(2)) != expected.Source?.Value)
                    throw new InvalidDataException("An event key already belongs to another or unattributed source. Explicit association is required.");
            }
        }
    }
}
