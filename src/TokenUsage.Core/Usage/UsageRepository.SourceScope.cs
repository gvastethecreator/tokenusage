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
        UsageObservationMode observationMode = UsageObservationMode.Snapshot,
        UsageSourceProgress? progress = null, CancellationToken token = default)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(sourceInstance);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, to);
        if (!Enum.IsDefined(observationMode)) throw new ArgumentOutOfRangeException(nameof(observationMode));
        UsageEvent[] batch = ValidateAgentBatch(agentId, observations, "Source admission");
        if (batch.Any(row => row.DetailMetadata.SourceInstance != sourceInstance
            || row.ParserVersion != parserVersion || UsageRollupAggregator.CivilDate(row) < from
            || UsageRollupAggregator.CivilDate(row) > to))
            throw new ArgumentException("Source admission requires one source, parser and civil-date window.", nameof(observations));

        await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        if (observationMode == UsageObservationMode.Unchanged && batch.Length != 0)
            throw new ArgumentException("An unchanged read cannot contain observations.", nameof(observations));
        if (batch.Length == 0 && observationMode != UsageObservationMode.Snapshot)
        {
            await WriteSourceProgressOnAsync(connection, transaction, progress, token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return new(0, 0, false);
        }
        await AssociateLegacyObservationsOnAsync(connection, transaction, batch, token).ConfigureAwait(false);
        Dictionary<string, UsageEvent> previous = (await LoadRecoveryEventsOnAsync(connection, transaction, batch, token)
            .ConfigureAwait(false)).ToDictionary(row => row.EventKey.Value, StringComparer.Ordinal);
        string dateFilter = observationMode == UsageObservationMode.Snapshot ? string.Empty
            : " AND civil_date IN (" + string.Join(",", batch.Select(UsageRollupAggregator.CivilDate)
                .Concat(previous.Values.Select(UsageRollupAggregator.CivilDate)).Distinct()
                .Select(date => "'" + FormatDate(date) + "'")) + ")";

        var ambiguousDays = new HashSet<string>(StringComparer.Ordinal);
        var retiredDays = new HashSet<string>(StringComparer.Ordinal);
        var ownedKeys = new HashSet<string>(StringComparer.Ordinal);
        var ownedDates = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        var conflictingKeys = new HashSet<string>(StringComparer.Ordinal);
        bool differentParser = false;
        await using (SqliteCommand history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = """
                SELECT event_key, source_instance_id, civil_date, parser_version FROM usage_event
                WHERE agent_id = $agent AND civil_date BETWEEN $from AND $to
                """ + dateFilter + ";";
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
                    ownedDates.Add(reader.GetString(0), DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture));
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
                      AND COALESCE(e.model_provider_id, '') = r.model_provider_id AND e.model_id = r.model_id)
                """ + dateFilter.Replace("civil_date", "r.civil_date", StringComparison.Ordinal) + ";";
            history.Parameters.AddWithValue("$agent", agentId.Value);
            history.Parameters.AddWithValue("$from", FormatDate(from));
            history.Parameters.AddWithValue("$to", FormatDate(to));
            await using SqliteDataReader reader = await history.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false)) retiredDays.Add(reader.GetString(0));
        }
        HashSet<string> tombstones = await LoadTombstonedKeysAsync(connection, transaction, batch, token).ConfigureAwait(false);
        UsageEvent[] admitted = batch.Where(row => tombstones.Contains(row.EventKey.Value)
            || (!conflictingKeys.Contains(row.EventKey.Value)
                && !retiredDays.Contains(FormatDate(UsageRollupAggregator.CivilDate(row)))
                && (ownedKeys.Contains(row.EventKey.Value)
                    || !ambiguousDays.Contains(FormatDate(UsageRollupAggregator.CivilDate(row)))))).ToArray();
        bool unresolved = ambiguousDays.Count > 0 || retiredDays.Count > 0 || admitted.Length != batch.Length;
        bool authoritative = observationMode == UsageObservationMode.Snapshot && complete && !unresolved;
        if (!authoritative && differentParser)
            admitted = admitted.Where(row => tombstones.Contains(row.EventKey.Value)).ToArray();
        int withheld = batch.Length - admitted.Length;
        unresolved |= withheld > 0;
        // Replay keeps the price already recorded for the same measured counters.
        admitted = admitted.Select(row => previous.TryGetValue(row.EventKey.Value, out UsageEvent? old)
            && old.Tokens == row.Tokens && old.ModelId == row.ModelId && old.ModelProviderId == row.ModelProviderId
            && old.ServiceTier == row.ServiceTier && old.Cost.Kind != CostKind.Unavailable
                ? new UsageEvent(row.EventKey, row.AgentId, row.ModelProviderId, row.ModelId, row.OccurredAtUtc,
                    row.GroupingTimeZoneId, row.Tokens, old.Cost, row.ParserVersion, old.Coverage,
                    row.TimePrecision, row.IntervalStartedAtUtc, row.ObservedModelId, row.ReasoningEffort,
                    row.ServiceTier, row.DetailMetadata) : row).ToArray();
        // Also reject cross-window/key collisions before any deletion or numeric write.
        await VerifyEventOwnershipAsync(connection, transaction, admitted, token).ConfigureAwait(false);
        foreach (DateOnly date in admitted.Where(row => previous.ContainsKey(row.EventKey.Value))
                     .Select(row => UsageRollupAggregator.CivilDate(previous[row.EventKey.Value])).Distinct())
            await VerifyRetainedRollupsCanRebuildAsync(connection, transaction, agentId, date, date, token).ConfigureAwait(false);
        var changedDates = new HashSet<DateOnly>();
        if (authoritative)
        {
            HashSet<string> current = admitted.Select(row => row.EventKey.Value).ToHashSet(StringComparer.Ordinal);
            foreach (string[] keys in ownedKeys.Where(key => !current.Contains(key)).Chunk(500))
            {
                await using SqliteCommand delete = connection.CreateCommand();
                delete.Transaction = transaction;
                string[] parameters = keys.Select((_, index) => "$key" + index.ToString(CultureInfo.InvariantCulture)).ToArray();
                delete.CommandText = "DELETE FROM usage_event WHERE event_key IN (" + string.Join(",", parameters) + ");";
                for (int index = 0; index < keys.Length; index++)
                {
                    delete.Parameters.AddWithValue(parameters[index], keys[index]);
                    changedDates.Add(ownedDates[keys[index]]);
                }
                await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
        UsageEvent[] written = await WriteEventsAsync(connection, transaction, admitted, EventWriteKind.Upsert,
            token).ConfigureAwait(false);
        foreach (UsageEvent row in written)
        {
            changedDates.Add(UsageRollupAggregator.CivilDate(row));
            if (previous.TryGetValue(row.EventKey.Value, out UsageEvent? old))
                changedDates.Add(UsageRollupAggregator.CivilDate(old));
        }
        await RebuildAgentRollupsForDatesAsync(connection, transaction, agentId, changedDates.ToArray(), token).ConfigureAwait(false);
        if (withheld == 0 && !unresolved)
            await WriteSourceProgressOnAsync(connection, transaction, progress, token).ConfigureAwait(false);
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
            BindUsageEventParameters(command, observation, UsageRollupAggregator.CivilDate(observation));
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
