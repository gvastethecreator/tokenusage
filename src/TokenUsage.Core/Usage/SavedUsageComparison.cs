using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Automation;

namespace TokenUsage.Core.Usage;

public sealed record UsageComparisonDefinition(string Axis, string Preset,
    DateOnly BaselineStart, DateOnly BaselineEnd, DateOnly CurrentStart, DateOnly CurrentEnd,
    string TimeZoneId, DateTimeOffset? PriceReferenceUtc, string? BaselineModel, string? CurrentModel,
    string Evidence, string DataRevision)
{
    public string BaselineLabel { get; init; } = string.Empty;
    public string CurrentLabel { get; init; } = string.Empty;
    public UsageReportCycleComparison? CycleComparison { get; init; }
}

public sealed record SavedUsageComparisonInfo(string RevisionId, DateTimeOffset CreatedAtUtc, string Axis);

public sealed record SavedUsageComparison(string RevisionId, DateTimeOffset CreatedAtUtc,
    UsageComparisonDefinition Definition, UsageReport Baseline, UsageReport Current)
{
    public IReadOnlyList<UsageCycleComparisonEntry> Cycles { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SavedUsageComparison))]
[JsonSerializable(typeof(UsageComparisonDefinition))]
[JsonSerializable(typeof(UsageReport))]
internal sealed partial class UsageComparisonJsonContext : JsonSerializerContext;

public sealed partial class UsageRepository
{
    public async Task SaveComparisonAsync(SavedUsageComparison snapshot, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        string json = JsonSerializer.Serialize(snapshot, UsageComparisonJsonContext.Default.SavedUsageComparison);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024)
            throw new InvalidDataException("This comparison is too large to save (4 MiB maximum).");
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM saved_usage_comparison;";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) >= 100)
            throw new InvalidOperationException("The saved comparison limit of 100 has been reached.");
        command.CommandText = """
            INSERT INTO saved_usage_comparison(revision_id, created_at_utc, definition_json, result_json)
            VALUES ($id, $created, $definition, $result);
            """;
        command.Parameters.AddWithValue("$id", snapshot.RevisionId);
        command.Parameters.AddWithValue("$created", snapshot.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$definition", JsonSerializer.Serialize(snapshot.Definition, UsageComparisonJsonContext.Default.UsageComparisonDefinition));
        command.Parameters.AddWithValue("$result", json);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();
    }

    public async Task<IReadOnlyList<SavedUsageComparisonInfo>> ReadComparisonsAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT revision_id, created_at_utc, json_extract(definition_json, '$.axis') FROM saved_usage_comparison ORDER BY created_at_utc DESC LIMIT 100;";
        var result = new List<SavedUsageComparisonInfo>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture), reader.GetString(2)));
        }
        return result;
    }

    public async Task<SavedUsageComparison> ReadComparisonAsync(string revisionId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT result_json FROM saved_usage_comparison WHERE revision_id = $id;";
        command.Parameters.AddWithValue("$id", revisionId);
        string? json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (json is null || json.Length > 4 * 1024 * 1024) throw new InvalidDataException("Saved comparison is unavailable or exceeds its size limit.");
        return JsonSerializer.Deserialize(json, UsageComparisonJsonContext.Default.SavedUsageComparison)
            ?? throw new InvalidDataException("Saved comparison is invalid.");
    }

    public static string ComparisonDataRevision(params UsageReport[] reports)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (UsageReport report in reports)
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(report, UsageComparisonJsonContext.Default.UsageReport));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
