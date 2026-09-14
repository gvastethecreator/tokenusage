using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public sealed partial class UsageRepository
{
    public async Task<IReadOnlyList<UsageOverviewObservation>> ReadOverviewObservationsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        long codexSessionEpoch,
        long cursorSessionEpoch,
        long projectEpoch,
        UsageDetailSelection? detail = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromInclusive, toInclusive);
        ArgumentOutOfRangeException.ThrowIfNegative(codexSessionEpoch);
        ArgumentOutOfRangeException.ThrowIfNegative(cursorSessionEpoch);
        ArgumentOutOfRangeException.ThrowIfNegative(projectEpoch);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.event_key, e.agent_id, e.model_provider_id, e.model_id,
                   e.input_tokens, e.output_tokens, e.reasoning_tokens, e.cache_read_tokens, e.cache_write_tokens,
                   e.cost_kind, e.reported_cost_micros, e.estimated_cost_micros,
                   e.observed_model_id, e.reasoning_effort, e.service_tier,
                   a.session_key, a.parent_session_key, p.project_key, p.mapping_kind
            FROM usage_event e
            LEFT JOIN session_attribution a
                ON a.event_key = e.event_key
                AND (
                    (e.agent_id = 'codex' AND $codexEpoch > 0
                        AND a.consent_epoch = $codexEpoch
                        AND a.capability = $codexCapability)
                    OR (e.agent_id = 'cursor' AND $cursorEpoch > 0
                        AND a.consent_epoch = $cursorEpoch
                        AND a.capability = $cursorCapability)
                )
            LEFT JOIN project_attribution p
                ON p.event_key = e.event_key
                AND $projectEpoch > 0
                AND p.consent_epoch = $projectEpoch
            WHERE e.civil_date BETWEEN $from AND $to
            """;
        command.Parameters.AddWithValue("$codexEpoch", codexSessionEpoch);
        command.Parameters.AddWithValue("$cursorEpoch", cursorSessionEpoch);
        command.Parameters.AddWithValue("$projectEpoch", projectEpoch);
        command.Parameters.AddWithValue("$codexCapability", AttributionCapability.CodexSession.Value);
        command.Parameters.AddWithValue("$cursorCapability", AttributionCapability.CursorSession.Value);
        command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
        command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
        BindDetailSelection(command, detail);
        var rows = new List<UsageOverviewObservation>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            CostKind costKind = (CostKind)reader.GetInt32(9);
            rows.Add(new UsageOverviewObservation(
                new UsageEventKey(reader.GetString(0)),
                new AgentId(reader.GetString(1)),
                reader.IsDBNull(2) ? null : new ModelProviderId(reader.GetString(2)),
                new ModelId(reader.GetString(3)),
                new TokenBreakdown(
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    reader.GetInt64(8)),
                costKind,
                costKind == CostKind.ProviderReported && !reader.IsDBNull(10)
                    ? FromMicros(reader.GetInt64(10))
                    : null,
                costKind == CostKind.CatalogEstimated && !reader.IsDBNull(11)
                    ? FromMicros(reader.GetInt64(11))
                    : null,
                reader.IsDBNull(15) ? null : new OpaqueAttributionKey(reader.GetString(15)),
                reader.IsDBNull(16) ? null : new OpaqueAttributionKey(reader.GetString(16)),
                reader.IsDBNull(17) ? null : new OpaqueAttributionKey(reader.GetString(17)),
                reader.IsDBNull(18)
                    ? null
                    : ProjectMappingKindCodec.TryParse(reader.GetString(18), out ProjectMappingKind mapping)
                        ? mapping
                        : null,
                reader.IsDBNull(12) ? null : new ModelId(reader.GetString(12)),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        return rows;
    }
}
