using Microsoft.Data.Sqlite;

namespace TokenUsage.Core.Usage;

public sealed partial class UsageRepository
{
    public static async Task<UsageRepository> RecoverCopyAsync(string backupPath, string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string sourcePath = Path.GetFullPath(backupPath);
        string destinationPath = Path.GetFullPath(outputPath);
        if (File.Exists(destinationPath) || File.Exists(destinationPath + "-wal")
            || File.Exists(destinationPath + "-shm") || File.Exists(destinationPath + "-journal"))
            throw new IOException("Recovery requires a new output database with no existing sidecars.");
        await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 5,
        }.ToString()))
        {
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction snapshot = source.BeginTransaction(deferred: true);
            int version = await ReadSchemaVersionAsync(source, snapshot, cancellationToken).ConfigureAwait(false);
            if (version < 1 || version > CurrentSchemaVersion)
                throw new InvalidDataException("The recovery copy has an unsupported usage schema.");
            await using SqliteCommand check = source.CreateCommand();
            check.Transaction = snapshot;
            check.CommandText = "PRAGMA quick_check;";
            if (!string.Equals((string?)await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), "ok", StringComparison.Ordinal))
                throw new InvalidDataException("The recovery copy failed its integrity check.");
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('usage_event', 'daily_usage_rollup', 'source_cursor', 'pricing_catalog');";
            if ((long)(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! != 4)
                throw new InvalidDataException("The recovery copy is not a supported TokenUsage store.");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using (var reservation = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await reservation.FlushAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath, Mode = SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 5,
            }.ToString());
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
            source.BackupDatabase(destination);
        }
        cancellationToken.ThrowIfCancellationRequested();
        UsageRepository recovered = await OpenAsync(destinationPath, cancellationToken).ConfigureAwait(false);
        await using SqliteConnection writer = await recovered.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // A recovered copy is a distinct database. Old page cursors must not match it.
        await ExecuteAsync(writer, null,
            "UPDATE usage_data_revision SET database_id = lower(hex(randomblob(16))), sequence = sequence + 1 WHERE singleton = 1;",
            cancellationToken).ConfigureAwait(false);
        return recovered;
    }

    // Called while the migration transaction owns the writer lock. A separate
    // private reader sees the committed pre-migration state, including WAL pages.
    private async Task CreateMigrationBackupAsync(int schemaVersion, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string path = DatabasePath + $".pre-v{CurrentSchemaVersion}-{Guid.NewGuid():N}.db";
        string pending = path + ".pending";
        await using (var reservation = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await reservation.FlushAsync(token).ConfigureAwait(false);
        await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 5,
        }.ToString()))
        await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = pending, Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 5,
        }.ToString()))
        {
            await source.OpenAsync(token).ConfigureAwait(false);
            await destination.OpenAsync(token).ConfigureAwait(false);
            source.BackupDatabase(destination);
            token.ThrowIfCancellationRequested();
            await using SqliteCommand check = destination.CreateCommand();
            check.CommandText = "PRAGMA quick_check;";
            if (!string.Equals((string?)await check.ExecuteScalarAsync(token).ConfigureAwait(false), "ok", StringComparison.Ordinal))
                throw new InvalidDataException("The pre-migration usage backup failed its integrity check.");
            if (await ReadSchemaVersionAsync(destination, null, token).ConfigureAwait(false) != schemaVersion)
                throw new InvalidDataException("The pre-migration usage backup has an unexpected schema.");
        }
        token.ThrowIfCancellationRequested();
        File.Move(pending, path); // Never replace an earlier recovery copy.
    }
}
