using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public enum UsageRecoveryExclusionKind
{
    OutsideRetention,
    RetiredDetails,
    Tombstone,
    ConflictingIdentity,
    ConflictingPriceEvidence,
    Future,
}

public sealed record UsageRecoveryExclusionSummary(int OutsideRetention, int RetiredDetails,
    int Tombstones, int ConflictingIdentity, int Future, int UnreadableRecords)
{
    public static UsageRecoveryExclusionSummary Empty { get; } = new(0, 0, 0, 0, 0, 0);
    public int ConflictingPriceEvidence { get; init; }
    public int Total => OutsideRetention + RetiredDetails + Tombstones + ConflictingIdentity
        + ConflictingPriceEvidence + Future + UnreadableRecords;

    public static UsageRecoveryExclusionSummary FromReasons(IEnumerable<UsageRecoveryExclusionKind> reasons,
        int unreadableRecords = 0)
    {
        int retention = 0, retired = 0, tombstones = 0, conflict = 0, priceConflict = 0, future = 0;
        foreach (UsageRecoveryExclusionKind reason in reasons)
            switch (reason)
            {
                case UsageRecoveryExclusionKind.OutsideRetention: retention++; break;
                case UsageRecoveryExclusionKind.RetiredDetails: retired++; break;
                case UsageRecoveryExclusionKind.Tombstone: tombstones++; break;
                case UsageRecoveryExclusionKind.ConflictingIdentity: conflict++; break;
                case UsageRecoveryExclusionKind.ConflictingPriceEvidence: priceConflict++; break;
                case UsageRecoveryExclusionKind.Future: future++; break;
            }
        return new(retention, retired, tombstones, conflict, future, unreadableRecords)
        { ConflictingPriceEvidence = priceConflict };
    }
}

public sealed record UsageRecoveryResult(int Added, int Existing, int Excluded)
{
    public int UnpricedGrowth { get; init; }
    public UsageRecoveryExclusionSummary Exclusions { get; init; } = UsageRecoveryExclusionSummary.Empty;
    public IReadOnlyDictionary<string, UsageRecoveryExclusionKind> ExcludedReasons { get; init; }
        = new Dictionary<string, UsageRecoveryExclusionKind>(StringComparer.Ordinal);
    public IReadOnlyList<UsageEventKey> AddedKeys { get; init; } = [];
    public IReadOnlyList<UsageEventKey> ExistingKeys { get; init; } = [];
    public IReadOnlyList<UsageEventKey> ExcludedKeys { get; init; } = [];
}

public sealed partial class UsageRepository
{
    public async Task<UsageRecoveryResult> RecoverAgentEventsAsync(AgentId agentId,
        IEnumerable<UsageEvent> events, DateTimeOffset nowUtc, bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        UtcTimestamp.Require(nowUtc, nameof(nowUtc));
        if (!dryRun) EnsureWritable();
        UsageEvent[] batch = ValidateAgentBatchContent(agentId, events, "History recovery");
        if (batch.Length == 0) return new(0, 0, 0);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: dryRun);
        HashSet<string> tombstones = await LoadTombstonedKeysAsync(connection, transaction, batch, cancellationToken)
            .ConfigureAwait(false);
        var retiredDays = new HashSet<string>(StringComparer.Ordinal);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT DISTINCT r.civil_date FROM daily_usage_rollup r
                WHERE r.agent_id = $agent AND r.civil_date BETWEEN $from AND $to
                  AND r.event_count > (SELECT COUNT(*) FROM usage_event e
                    WHERE e.agent_id = r.agent_id AND e.civil_date = r.civil_date
                      AND e.grouping_time_zone_id = r.grouping_time_zone_id
                      AND COALESCE(e.model_provider_id, '') = r.model_provider_id AND e.model_id = r.model_id);
                """;
            command.Parameters.AddWithValue("$agent", agentId.Value);
            command.Parameters.AddWithValue("$from", FormatDate(batch.Min(UsageRollupAggregator.CivilDate)));
            command.Parameters.AddWithValue("$to", FormatDate(batch.Max(UsageRollupAggregator.CivilDate)));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) retiredDays.Add(reader.GetString(0));
        }
        IReadOnlyList<UsageEvent> saved = await LoadRecoveryEventsOnAsync(connection, transaction,
            batch, cancellationToken).ConfigureAwait(false);
        var byKey = saved.ToDictionary(row => row.EventKey.Value, StringComparer.Ordinal);
        var admitted = new List<UsageEvent>();
        int unpricedGrowth = 0;
        var added = new List<UsageEventKey>();
        var existing = new List<UsageEventKey>();
        var excluded = new List<UsageEventKey>();
        var reasons = new Dictionary<string, UsageRecoveryExclusionKind>(StringComparer.Ordinal);
        foreach (UsageEvent row in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UsageRecoveryExclusionKind? reason = row.OccurredAtUtc < nowUtc.AddDays(-400)
                ? UsageRecoveryExclusionKind.OutsideRetention
                : row.OccurredAtUtc > nowUtc ? UsageRecoveryExclusionKind.Future
                : tombstones.Contains(row.EventKey.Value) ? UsageRecoveryExclusionKind.Tombstone
                : retiredDays.Contains(FormatDate(UsageRollupAggregator.CivilDate(row)))
                    ? UsageRecoveryExclusionKind.RetiredDetails : null;
            if (reason is { } excludedReason)
            {
                excluded.Add(row.EventKey);
                reasons[row.EventKey.Value] = excludedReason;
                continue;
            }
            if (byKey.TryGetValue(row.EventKey.Value, out UsageEvent? previous))
            {
                existing.Add(row.EventKey);
                if (previous.AgentId != agentId || previous.DetailMetadata.SourceInstance != row.DetailMetadata.SourceInstance)
                {
                    excluded.Add(row.EventKey);
                    existing.Remove(row.EventKey);
                    reasons[row.EventKey.Value] = UsageRecoveryExclusionKind.ConflictingIdentity;
                    continue;
                }
                var tokens = new TokenBreakdown(Math.Max(previous.Tokens.Input, row.Tokens.Input),
                    Math.Max(previous.Tokens.Output, row.Tokens.Output),
                    Math.Max(previous.Tokens.Reasoning, row.Tokens.Reasoning),
                    Math.Max(previous.Tokens.CacheRead, row.Tokens.CacheRead),
                    Math.Max(previous.Tokens.CacheWrite, row.Tokens.CacheWrite));
                CostObservation cost = previous.Cost;
                if (tokens != previous.Tokens && previous.Cost.Kind == CostKind.ProviderReported
                    && row.Cost.Kind == CostKind.ProviderReported)
                    cost = CostObservation.ProviderReported(Math.Max(previous.Cost.ReportedCostUsd!.Value,
                        row.Cost.ReportedCostUsd!.Value));
                else if (tokens != previous.Tokens && previous.Cost.Kind == CostKind.CatalogEstimated)
                {
                    if (row.Cost.Kind == CostKind.CatalogEstimated
                        && previous.Cost.CatalogVersion == row.Cost.CatalogVersion
                        && previous.Cost.ExactPriceMatch == row.Cost.ExactPriceMatch
                        && previous.ModelId == row.ModelId && tokens == row.Tokens)
                        cost = CostObservation.CatalogEstimated(Math.Max(previous.Cost.EstimatedCostUsd!.Value,
                            row.Cost.EstimatedCostUsd!.Value), previous.Cost.CatalogVersion!, previous.Cost.ExactPriceMatch!);
                    else
                    {
                        unpricedGrowth++;
                        existing.Remove(row.EventKey);
                        excluded.Add(row.EventKey);
                        reasons[row.EventKey.Value] = UsageRecoveryExclusionKind.ConflictingPriceEvidence;
                        continue;
                    }
                }
                UsageDetailMetadata detail = previous.DetailMetadata == UsageDetailMetadata.Unknown
                    && previous.Tokens == row.Tokens && row.DetailMetadata.Input == UsageComponentAvailability.Measured
                    ? row.DetailMetadata : previous.DetailMetadata;
                // Equal counters retain their recorded price even when the catalog changes.
                var merged = new UsageEvent(previous.EventKey, previous.AgentId, previous.ModelProviderId,
                    previous.ModelId, previous.OccurredAtUtc <= row.OccurredAtUtc ? previous.OccurredAtUtc : row.OccurredAtUtc,
                    previous.GroupingTimeZoneId, tokens, cost, previous.ParserVersion, previous.Coverage,
                    previous.TimePrecision, previous.IntervalStartedAtUtc, previous.ObservedModelId,
                    previous.ReasoningEffort, previous.ServiceTier, detail);
                if (merged != previous) admitted.Add(merged);
            }
            else
            {
                added.Add(row.EventKey);
                admitted.Add(row);
            }
        }
        if (dryRun) return new(added.Count, existing.Count, excluded.Count)
        { UnpricedGrowth = unpricedGrowth, AddedKeys = added, ExistingKeys = existing, ExcludedKeys = excluded,
            ExcludedReasons = reasons, Exclusions = UsageRecoveryExclusionSummary.FromReasons(reasons.Values) };
        DateOnly[] previousDates = await LoadExistingEventDatesAsync(connection, transaction, agentId,
            admitted.ToArray(), cancellationToken).ConfigureAwait(false);
        foreach (DateOnly date in previousDates.Concat(admitted.Select(UsageRollupAggregator.CivilDate)).Distinct())
            await VerifyRetainedRollupsCanRebuildAsync(connection, transaction, agentId, date, date,
                cancellationToken).ConfigureAwait(false);
        UsageEvent[] written = await WriteEventsAsync(connection, transaction, admitted.ToArray(),
            EventWriteKind.Upsert, cancellationToken).ConfigureAwait(false);
        await RebuildAgentRollupsForDatesAsync(connection, transaction, agentId,
            previousDates.Concat(written.Select(UsageRollupAggregator.CivilDate)).Distinct().ToArray(),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(added.Count, existing.Count, excluded.Count)
        { UnpricedGrowth = unpricedGrowth, AddedKeys = added, ExistingKeys = existing, ExcludedKeys = excluded,
            ExcludedReasons = reasons, Exclusions = UsageRecoveryExclusionSummary.FromReasons(reasons.Values) };
    }
    private static async Task<IReadOnlyList<UsageEvent>> LoadRecoveryEventsOnAsync(
        SqliteConnection connection, SqliteTransaction transaction, UsageEvent[] batch, CancellationToken token)
    {
        var rows = new List<UsageEvent>();
        foreach (UsageEvent[] chunk in batch.Chunk(SqliteVariableChunkSize))
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            string keys = string.Join(",", Enumerable.Range(0, chunk.Length).Select(index => "$key" + index));
            command.CommandText = $"""
                SELECT event_key, agent_id, model_provider_id, model_id, occurred_at_utc,
                       grouping_time_zone_id, input_tokens, output_tokens, reasoning_tokens,
                       cache_read_tokens, cache_write_tokens, cost_kind, reported_cost_micros,
                       estimated_cost_micros, catalog_version, exact_price_match, parser_version,
                       coverage_kind, time_precision, interval_started_at_utc, observed_model_id,
                       reasoning_effort, service_tier, source_instance_id, record_kind, representation_revision,
                       input_availability, output_availability, reasoning_availability,
                       cache_read_availability, cache_write_availability
                FROM usage_event WHERE event_key IN ({keys});
                """;
            for (int index = 0; index < chunk.Length; index++)
                command.Parameters.AddWithValue("$key" + index, chunk[index].EventKey.Value);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(ReadUsageEvent(reader));
        }
        return rows;
    }

}
