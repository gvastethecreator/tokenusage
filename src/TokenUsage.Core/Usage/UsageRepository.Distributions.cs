using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public sealed partial class UsageRepository
{
    public async Task<UsageDistributionEligibility> ReadDistributionEligibilityAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId? agentId = null,
        ModelProviderId? modelProviderId = null,
        ModelId? modelId = null,
        UsageDetailSelection? detail = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromInclusive, toInclusive);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var inputs = new List<long>();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT input_tokens FROM usage_event e
            WHERE e.civil_date BETWEEN $from AND $to
              AND e.record_kind = $kind
              AND e.input_availability = $measured
            """;
        BindFinalEventFilter(command, fromInclusive, toInclusive, agentId, modelProviderId, modelId, detail);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            inputs.Add(reader.GetInt64(0));
        }

        return UsageDistributionEligibility.FromMeasuredInputs(inputs);
    }

    public async Task<(int SessionCount, bool SessionsAvailable)> CountAttributedFinalSessionsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId? agentId = null,
        ModelProviderId? modelProviderId = null,
        ModelId? modelId = null,
        UsageDetailSelection? detail = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromInclusive, toInclusive);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand available = connection.CreateCommand();
        available.CommandText = """
            SELECT COUNT(*) FROM session_attribution a
            INNER JOIN usage_event e ON e.event_key = a.event_key
            WHERE e.civil_date BETWEEN $from AND $to
              AND e.record_kind = $kind
              AND e.input_availability = $measured
            """;
        BindFinalEventFilter(available, fromInclusive, toInclusive, agentId, modelProviderId, modelId, detail);
        long present = (long)(await available.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        await using SqliteCommand distinct = connection.CreateCommand();
        distinct.CommandText = """
            SELECT COUNT(DISTINCT a.session_key) FROM session_attribution a
            INNER JOIN usage_event e ON e.event_key = a.event_key
            WHERE e.civil_date BETWEEN $from AND $to
              AND e.record_kind = $kind
              AND e.input_availability = $measured
            """;
        BindFinalEventFilter(distinct, fromInclusive, toInclusive, agentId, modelProviderId, modelId, detail);
        int count = (int)(long)(await distinct.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        return (count, present > 0);
    }

    public async Task<IReadOnlyList<(string SessionKey, long Tokens)>> ReadFinalSessionTotalsAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId? agentId = null,
        ModelProviderId? modelProviderId = null,
        ModelId? modelId = null,
        UsageDetailSelection? detail = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromInclusive, toInclusive);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.session_key,
                   SUM(e.input_tokens + e.output_tokens + e.reasoning_tokens
                       + e.cache_read_tokens + e.cache_write_tokens)
            FROM session_attribution a
            INNER JOIN usage_event e ON e.event_key = a.event_key
            WHERE e.civil_date BETWEEN $from AND $to
              AND e.record_kind = $kind
              AND e.input_availability = $measured
            """;
        BindFinalEventFilter(command, fromInclusive, toInclusive, agentId, modelProviderId, modelId, detail);
        command.CommandText += " GROUP BY a.session_key";
        var rows = new List<(string SessionKey, long Tokens)>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return rows;
    }

    private static void BindFinalEventFilter(
        SqliteCommand command,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId? agentId,
        ModelProviderId? modelProviderId,
        ModelId? modelId,
        UsageDetailSelection? detail)
    {
        command.Parameters.AddWithValue("$from", FormatDate(fromInclusive));
        command.Parameters.AddWithValue("$to", FormatDate(toInclusive));
        command.Parameters.AddWithValue("$kind", (int)UsageRecordKind.RequestFinal);
        command.Parameters.AddWithValue("$measured", (int)UsageComponentAvailability.Measured);
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
    }
}
