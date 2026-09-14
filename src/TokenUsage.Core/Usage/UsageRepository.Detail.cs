using Microsoft.Data.Sqlite;

namespace TokenUsage.Core.Usage;

public sealed partial class UsageRepository
{
    private static async Task ApplyDetailSchemaAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken token)
    {
        const string statements = """
        ALTER TABLE usage_event ADD COLUMN source_instance_id TEXT
            CHECK(source_instance_id IS NULL OR (length(source_instance_id) = 64 AND source_instance_id NOT GLOB '*[^0-9a-f]*'));
        ALTER TABLE usage_event ADD COLUMN record_kind INTEGER NOT NULL DEFAULT 0
            CHECK(typeof(record_kind) = 'integer' AND record_kind BETWEEN 0 AND 4);
        ALTER TABLE usage_event ADD COLUMN representation_revision INTEGER
            CHECK(representation_revision IS NULL OR (typeof(representation_revision) = 'integer' AND representation_revision > 0));
        ALTER TABLE usage_event ADD COLUMN input_availability INTEGER NOT NULL DEFAULT 0
            CHECK(typeof(input_availability) = 'integer' AND input_availability BETWEEN 0 AND 2);
        ALTER TABLE usage_event ADD COLUMN output_availability INTEGER NOT NULL DEFAULT 0
            CHECK(typeof(output_availability) = 'integer' AND output_availability BETWEEN 0 AND 2);
        ALTER TABLE usage_event ADD COLUMN reasoning_availability INTEGER NOT NULL DEFAULT 0
            CHECK(typeof(reasoning_availability) = 'integer' AND reasoning_availability BETWEEN 0 AND 2);
        ALTER TABLE usage_event ADD COLUMN cache_read_availability INTEGER NOT NULL DEFAULT 0
            CHECK(typeof(cache_read_availability) = 'integer' AND cache_read_availability BETWEEN 0 AND 2);
        ALTER TABLE usage_event ADD COLUMN cache_write_availability INTEGER NOT NULL DEFAULT 0
            CHECK(typeof(cache_write_availability) = 'integer' AND cache_write_availability BETWEEN 0 AND 2);
        UPDATE usage_data_revision SET sequence = sequence + 1 WHERE singleton = 1;
        INSERT INTO schema_migration(version, applied_at_utc)
            VALUES (7, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        """;
        // This fixed migration contains only simple statements, with no trigger bodies.
        // Execute them separately so an intermediate SQLite failure cannot hide behind DDL results.
        foreach (string statement in statements.Split(';', StringSplitOptions.RemoveEmptyEntries))
            await ExecuteAsync(connection, transaction, statement, token).ConfigureAwait(false);
    }
}
