using System.Globalization;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public sealed record QuotaObservation(string ProviderId, string MetricId,
    DateTimeOffset ObservedAtUtc, DateTimeOffset ReceivedAtUtc, decimal UsedPercent,
    decimal Capacity, decimal? WindowMinutes, DateTimeOffset? ExpectedResetAtUtc)
{
    public QuotaWindowSemantics Semantics { get; init; } = QuotaWindowSemantics.Unknown;
    public decimal? MeterResolutionPoints { get; init; }
}

public sealed record QuotaObservationRange(IReadOnlyList<QuotaObservation> Observations, bool Truncated);

/// <summary>Numeric-only evidence. The provider does not supply request-to-pool attribution.</summary>
public sealed class QuotaObservationJournal(string path)
{
    public const int RetentionDays = 90;
    public const int MaximumRows = 250_000;
    private readonly string _path = Path.GetFullPath(path);

    // Called under the reset history lock, before committing its derived state.
    internal void Append(ProviderSnapshot snapshot, IReadOnlyDictionary<string, decimal> durations)
    {
        using SqliteConnection connection = Open(readOnly: false);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS quota_observation (
                provider_id TEXT NOT NULL, metric_id TEXT NOT NULL,
                observed_at_utc TEXT NOT NULL, received_at_utc TEXT NOT NULL,
                used_percent TEXT NOT NULL, capacity TEXT NOT NULL, window_minutes TEXT NULL,
                expected_reset_at_utc TEXT NULL,
                PRIMARY KEY(provider_id, metric_id, observed_at_utc));
            CREATE INDEX IF NOT EXISTS ix_quota_observation_time ON quota_observation(observed_at_utc);
            CREATE TABLE IF NOT EXISTS journal_maintenance(id INTEGER PRIMARY KEY CHECK(id = 1), pruned_at_utc TEXT NOT NULL);
            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
        foreach (ProgressMetricSnapshot metric in snapshot.Metrics.OfType<ProgressMetricSnapshot>()
                     .Where(item => item.Id.Value.StartsWith("quota.", StringComparison.Ordinal)))
        {
            command.CommandText = """
                INSERT OR IGNORE INTO quota_observation VALUES
                ($provider, $metric, $observed, $received, $used, $capacity, $duration, $reset);
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$provider", snapshot.ProviderId.Value);
            command.Parameters.AddWithValue("$metric", metric.Id.Value);
            command.Parameters.AddWithValue("$observed", Format(snapshot.SourceObservedAtUtc));
            command.Parameters.AddWithValue("$received", Format(snapshot.FetchedAtUtc));
            command.Parameters.AddWithValue("$used", (metric.Used / metric.Limit * 100m).ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$capacity", metric.Limit.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$duration", durations.TryGetValue(metric.Id.Value, out decimal duration)
                ? duration.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
            command.Parameters.AddWithValue("$reset", (object?)metric.ResetsAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        command.Parameters.Clear();
        command.CommandText = "SELECT pruned_at_utc FROM journal_maintenance WHERE id = 1;";
        string? lastPruned = command.ExecuteScalar() as string;
        if (lastPruned is null || snapshot.FetchedAtUtc - DateTimeOffset.Parse(lastPruned, CultureInfo.InvariantCulture) >= TimeSpan.FromDays(1))
        {
            command.CommandText = """
                DELETE FROM quota_observation WHERE observed_at_utc < $cutoff;
                DELETE FROM quota_observation WHERE rowid IN
                    (SELECT rowid FROM quota_observation ORDER BY observed_at_utc DESC LIMIT -1 OFFSET 250000);
                INSERT INTO journal_maintenance VALUES (1, $now)
                    ON CONFLICT(id) DO UPDATE SET pruned_at_utc = excluded.pruned_at_utc;
                """;
            command.Parameters.AddWithValue("$cutoff", Format(snapshot.FetchedAtUtc.AddDays(-RetentionDays)));
            command.Parameters.AddWithValue("$now", Format(snapshot.FetchedAtUtc));
            command.ExecuteNonQuery();
        }
        // Keep a hard row bound between daily age-pruning passes.
        command.Parameters.Clear();
        command.CommandText = """
            DELETE FROM quota_observation WHERE rowid IN
                (SELECT rowid FROM quota_observation ORDER BY observed_at_utc DESC LIMIT -1 OFFSET 250000);
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public Task<QuotaObservationRange> ReadAsync(string providerId, string metricId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            UtcTimestamp.Require(fromUtc, nameof(fromUtc));
            UtcTimestamp.Require(toUtc, nameof(toUtc));
            ArgumentOutOfRangeException.ThrowIfLessThan(toUtc, fromUtc);
            if (!File.Exists(_path)) return new QuotaObservationRange([], false);
            using SqliteConnection connection = Open(readOnly: true);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT observed_at_utc, received_at_utc, used_percent, capacity, window_minutes, expected_reset_at_utc
                FROM quota_observation WHERE provider_id = $provider AND metric_id = $metric
                    AND observed_at_utc >= $from AND observed_at_utc <= $to
                ORDER BY observed_at_utc LIMIT 32769;
                """;
            command.Parameters.AddWithValue("$provider", providerId);
            command.Parameters.AddWithValue("$metric", metricId);
            command.Parameters.AddWithValue("$from", Format(fromUtc));
            command.Parameters.AddWithValue("$to", Format(toUtc));
            var observations = new List<QuotaObservation>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                observations.Add(new(providerId, metricId, ParseTime(reader.GetString(0)), ParseTime(reader.GetString(1)),
                    decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture), decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                    reader.IsDBNull(4) ? null : decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    reader.IsDBNull(5) ? null : ParseTime(reader.GetString(5))));
            }
            return new QuotaObservationRange(observations.Take(32768).ToArray(), observations.Count > 32768);
        }, cancellationToken);

    private SqliteConnection Open(bool readOnly)
    {
        if (!readOnly) Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false, DefaultTimeout = 5,
        }.ToString());
        try
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            long version = (long)command.ExecuteScalar()!;
            if (version > 1) throw new InvalidDataException("Quota journal schema is newer than supported.");
            command.CommandText = readOnly ? "PRAGMA query_only = ON;" : "PRAGMA journal_mode = WAL;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
