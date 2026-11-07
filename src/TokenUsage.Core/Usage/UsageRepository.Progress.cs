using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TokenUsage.Core.Usage;

public sealed partial class UsageRepository
{
    private static Task ApplyIncrementalCheckpointSchemaAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken token) => ExecuteAsync(connection, transaction, """
            CREATE INDEX IF NOT EXISTS ix_usage_event_source_civil_date
                ON usage_event(agent_id, source_instance_id, civil_date);
            INSERT INTO schema_migration(version, applied_at_utc)
                VALUES (14, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """, token);

    public async Task<UsageSourceReadResult> ResolveSourceReadAsync(UsageSourceReadResult read,
        CancellationToken token = default)
    {
        if (read.Progress is not { } progress || read.ObservationMode == UsageObservationMode.Snapshot
            || await HasSourceProgressAsync(progress, token).ConfigureAwait(false)) return read;
        return new UsageSourceReadResult(progress.SnapshotEvents(), read.Status, read.Issue)
        {
            ObservationMode = UsageObservationMode.Snapshot, Progress = progress, Checkpoint = read.Checkpoint,
            GroupingTimeZoneId = read.GroupingTimeZoneId, SourceInstance = read.SourceInstance,
            AccountAggregates = read.AccountAggregates, SupersededEventKeys = read.SupersededEventKeys,
        };
    }

    public async Task<bool> HasSourceProgressAsync(UsageSourceProgress progress, CancellationToken token = default)
    {
        if (progress.PreviousCursor is null) return false;
        await using SqliteConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT cursor_value FROM source_cursor WHERE source_id = $source;";
        command.Parameters.AddWithValue("$source", "observations/" + progress.CursorId);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string == progress.PreviousCursor;
    }

    private static async Task WriteSourceProgressOnAsync(SqliteConnection connection,
        SqliteTransaction transaction, UsageSourceProgress? progress, CancellationToken token)
    {
        if (progress is null) return;
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO source_cursor(source_id, cursor_value, updated_at_utc) VALUES ($source, $cursor, $time)
            ON CONFLICT(source_id) DO UPDATE SET cursor_value = excluded.cursor_value,
                updated_at_utc = excluded.updated_at_utc WHERE source_cursor.cursor_value <> excluded.cursor_value;
            """;
        command.Parameters.AddWithValue("$source", "observations/" + progress.CursorId);
        command.Parameters.AddWithValue("$cursor", progress.Cursor);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
