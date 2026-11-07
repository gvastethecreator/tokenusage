using Microsoft.Data.Sqlite;

namespace TokenUsage.Core.Usage;

/// <summary>Incremental numeric checkpoint records. Providers own their records and migration.</summary>
public sealed class UsageCheckpointDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _write;
    private readonly SqliteCommand _delete;

    public UsageCheckpointDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connection = new(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false }.ToString());
        _connection.Open();
        Execute("""
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            CREATE TABLE IF NOT EXISTS checkpoint_record (
                kind TEXT NOT NULL, identity TEXT NOT NULL, value TEXT NOT NULL,
                PRIMARY KEY(kind, identity));
            """);
        _write = _connection.CreateCommand();
        _write.CommandText = """
            INSERT INTO checkpoint_record VALUES ($kind, $id, $value)
            ON CONFLICT(kind, identity) DO UPDATE SET value = excluded.value
            WHERE checkpoint_record.value <> excluded.value;
            """;
        _write.Parameters.AddWithValue("$kind", string.Empty);
        _write.Parameters.AddWithValue("$id", string.Empty);
        _write.Parameters.AddWithValue("$value", string.Empty);
        _write.Prepare();
        _delete = _connection.CreateCommand();
        _delete.CommandText = "DELETE FROM checkpoint_record WHERE kind = $kind AND identity = $id;";
        _delete.Parameters.AddWithValue("$kind", string.Empty);
        _delete.Parameters.AddWithValue("$id", string.Empty);
        _delete.Prepare();
    }

    public string? Read(string kind, string identity)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT value FROM checkpoint_record WHERE kind = $kind AND identity = $id;";
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$id", identity);
        return command.ExecuteScalar() as string;
    }

    public IEnumerable<(string Identity, string Value)> ReadRecords(string kind)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT identity, value FROM checkpoint_record WHERE kind = $kind ORDER BY identity;";
        command.Parameters.AddWithValue("$kind", kind);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) yield return (reader.GetString(0), reader.GetString(1));
    }

    public void Write(string kind, string identity, string? value)
    {
        SqliteCommand command = value is null ? _delete : _write;
        command.Parameters["$kind"].Value = kind;
        command.Parameters["$id"].Value = identity;
        if (value is not null) command.Parameters["$value"].Value = value;
        command.ExecuteNonQuery();
    }

    public void Commit(Action write)
    {
        Execute("BEGIN IMMEDIATE;");
        try { write(); Execute("COMMIT;"); }
        catch { Execute("ROLLBACK;"); throw; }
    }

    private void Execute(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _write.Dispose();
        _delete.Dispose();
        _connection.Dispose();
    }
}
