using Microsoft.Data.Sqlite;

namespace TokenUsage.Core.Usage;

public sealed record UsageDataRevision(string DatabaseId, long Sequence);

public sealed partial class UsageRepository
{
    private static async Task ApplyRevisionSchemaAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken token)
    {
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE usage_data_revision (
                singleton INTEGER NOT NULL PRIMARY KEY CHECK(singleton = 1),
                database_id TEXT NOT NULL CHECK(length(database_id) = 32),
                sequence INTEGER NOT NULL CHECK(typeof(sequence) = 'integer' AND sequence >= 0));
            INSERT INTO usage_data_revision VALUES (1, lower(hex(randomblob(16))), 0);
            INSERT INTO schema_migration(version, applied_at_utc)
                VALUES (6, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """, token).ConfigureAwait(false);

        string[] tables = ["schema_migration", "usage_event", "daily_usage_rollup", "source_cursor",
            "pricing_catalog", "usage_event_tombstone", "account_usage_daily", "usage_collection_state",
            "saved_usage_comparison", "usage_data_revision"];
        foreach (string table in tables)
        foreach (string operation in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            // Names come from this fixed schema list. Old processes lack the function and
            // fail before modifying a row, including connections opened before migration.
            await ExecuteAsync(connection, transaction, $"""
                CREATE TRIGGER guard_{table}_{operation} BEFORE {operation} ON {table}
                BEGIN
                    SELECT CASE WHEN tokenusage_writer_schema() < (SELECT MAX(version) FROM schema_migration)
                        THEN RAISE(ABORT, 'Usage schema changed; reopen with the current application.') END;
                END;
                """, token).ConfigureAwait(false);
            if (table is "usage_event" or "daily_usage_rollup" or "account_usage_daily" or "usage_collection_state")
                await ExecuteAsync(connection, transaction, $"""
                    CREATE TRIGGER revision_{table}_{operation} AFTER {operation} ON {table}
                    BEGIN
                        UPDATE usage_data_revision SET sequence = sequence + 1 WHERE singleton = 1;
                    END;
                    """, token).ConfigureAwait(false);
        }
    }

    public async Task<UsageDataRevision> ReadDataRevisionAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadDataRevisionOnAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<UsageDataRevision> ReadDataRevisionOnAsync(SqliteConnection connection,
        SqliteTransaction? transaction, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT database_id, sequence FROM usage_data_revision WHERE singleton = 1;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidDataException("Usage data revision is missing.");
        return new(reader.GetString(0), reader.GetInt64(1));
    }
}
