using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Goose;

/// <summary>
/// Reads Goose's aggregate session counters from sessions.db. The SQL query
/// projects IDs, model metadata, timestamps, numeric tokens, and numeric cost.
/// It never selects messages or session content.
/// </summary>
public sealed class GooseUsageEventSource :
    ISnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "goose-sessions/3";
    private const long DefaultMaximumDatabaseBytes = 1024L * 1024 * 1024;
    private readonly string _databasePath;
    private readonly string _groupingTimeZoneId;
    private readonly int _maximumRows;
    private readonly long _maximumDatabaseBytes;

    public GooseUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? roamingAppDataDirectory = null,
        string? databasePathOverride = null,
        int maximumRows = 100_000,
        long maximumDatabaseBytes = DefaultMaximumDatabaseBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDatabaseBytes, 1);
        string home = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _databasePath = databasePathOverride is null
            ? ResolveDatabasePath(home, roamingAppDataDirectory)
            : Path.GetFullPath(databasePathOverride);
        _groupingTimeZoneId = groupingTimeZoneId;
        _maximumRows = maximumRows;
        _maximumDatabaseBytes = maximumDatabaseBytes;
    }

    public SourceKind SourceKind => SourceKind.LocalDatabase;

    public AgentId AgentId { get; } = new("goose");

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The property implements the usage-source contract.")]
    public string EventParserVersion => ParserVersion;

    public bool IsRootAvailable => File.Exists(_databasePath);

    public async Task<UsageSourceReadResult> ReadAsync(
        CancellationToken cancellationToken = default) =>
        await Task.Run(() => ReadCore(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

    private UsageSourceReadResult ReadCore(CancellationToken cancellationToken)
    {
        if (!IsRootAvailable)
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.RootUnavailable);
        }

        try
        {
            var info = new FileInfo(_databasePath);
            if (info.Length <= 0
                || info.Length > _maximumDatabaseBytes
                || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return new UsageSourceReadResult(
                    [],
                    UsageSourceReadStatus.NoData,
                    UsageSourceIssueKind.AccessBlocked);
            }

            return ReadDatabase(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.AccessBlocked);
        }
    }

    private UsageSourceReadResult ReadDatabase(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        ExecuteControl(connection, "PRAGMA busy_timeout=250", cancellationToken);
        ExecuteControl(connection, "PRAGMA query_only=ON", cancellationToken);
        HashSet<string> columns = GetColumns(connection, "sessions", cancellationToken);
        if (!HasColumns(columns, "id", "created_at", "model_config_json"))
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.UnsupportedSchema);
        }

        string Column(string preferred, string fallback) => columns.Contains(preferred)
            ? preferred
            : columns.Contains(fallback)
                ? fallback
                : "NULL";
        string providerColumn = columns.Contains("provider_name") ? "provider_name" : "NULL";
        string costColumn = columns.Contains("accumulated_cost") ? "accumulated_cost" : "NULL";
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
              id,
              created_at,
              COALESCE(
                json_extract(model_config_json, '$.model_name'),
                json_extract(model_config_json, '$.model'),
                json_extract(model_config_json, '$.name')),
              {providerColumn},
              {Column("accumulated_input_tokens", "input_tokens")},
              {Column("accumulated_output_tokens", "output_tokens")},
              {Column("accumulated_total_tokens", "total_tokens")},
              {costColumn}
            FROM sessions
            WHERE model_config_json IS NOT NULL
              AND model_config_json != ''
              AND json_valid(model_config_json)
            ORDER BY created_at
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", checked(_maximumRows + 1L));
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        var events = new List<UsageEvent>();
        bool isPartial = false;
        int rowsRead = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++rowsRead > _maximumRows)
            {
                isPartial = true;
                break;
            }

            if (TryReadEvent(reader, out UsageEvent? usageEvent) && usageEvent is not null)
            {
                events.Add(usageEvent);
            }
            else
            {
                isPartial = true;
            }
        }

        UsageSourceReadStatus status = isPartial
            ? UsageSourceReadStatus.Partial
            : events.Count == 0
                ? UsageSourceReadStatus.NoData
                : UsageSourceReadStatus.Complete;
        return new UsageSourceReadResult(
            events,
            status,
            status == UsageSourceReadStatus.NoData
                ? UsageSourceIssueKind.Empty
                : isPartial
                    ? UsageSourceIssueKind.PartialScan
                    : null);
    }

    private bool TryReadEvent(SqliteDataReader reader, out UsageEvent? usageEvent)
    {
        usageEvent = null;
        if (reader.IsDBNull(0)
            || reader.IsDBNull(1)
            || reader.IsDBNull(2)
            || !TryTimestamp(reader.GetValue(1), out DateTimeOffset timestamp))
        {
            return false;
        }

        string sessionId = reader.GetString(0).Trim();
        string model = reader.GetString(2).Trim();
        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(model)
            || sessionId.Length > 500
            || model.Length > 200)
        {
            return false;
        }

        long input = GetNonNegativeOrZero(reader, 4);
        long output = GetNonNegativeOrZero(reader, 5);
        long total = GetNonNegativeOrZero(reader, 6);
        long reasoning = total > input + output ? total - input - output : 0;
        var tokens = new TokenBreakdown(input, output, reasoning, 0, 0);
        if (tokens.Total == 0)
        {
            return false;
        }

        bool hasCost = TryNonNegativeDecimal(reader, 7, out decimal costUsd);
        CostObservation cost = KnownModelPricingCatalog.ResolveReportedOrCatalog(
            hasCost ? costUsd : null,
            model,
            timestamp,
            tokens);
        string provider = reader.IsDBNull(3)
            ? string.Empty
            : reader.GetString(3);
        usageEvent = new UsageEvent(
            new UsageEventKey(Hash($"goose\0{sessionId}")),
            AgentId,
            ModelIdentity.TryProviderId(provider),
            ModelIdentity.ToModelId(model),
            timestamp,
            _groupingTimeZoneId,
            tokens,
            cost,
            ParserVersion,
            cost.Kind switch
            {
                CostKind.ProviderReported => CoverageKind.Complete,
                CostKind.CatalogEstimated => CoverageKind.Partial,
                _ => CoverageKind.Unpriced,
            });
        return true;
    }

    private static string ResolveDatabasePath(string home, string? roamingOverride)
    {
        string? root = Environment.GetEnvironmentVariable("GOOSE_PATH_ROOT");
        string roaming = roamingOverride
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(roaming))
        {
            roaming = Path.Combine(home, "AppData", "Roaming");
        }

        string[] candidates =
        [
            .. string.IsNullOrWhiteSpace(root)
                ? Array.Empty<string>()
                : [Path.Combine(root, "data", "sessions", "sessions.db")],
            Path.Combine(roaming, "Block", "goose", "sessions", "sessions.db"),
            Path.Combine(roaming, "goose", "sessions", "sessions.db"),
        ];
        return Path.GetFullPath(candidates.FirstOrDefault(File.Exists) ?? candidates[0]);
    }

    private static bool TryTimestamp(object raw, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (raw is string text)
        {
            string[] formats =
            [
                "O", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
            ];
            foreach (string format in formats)
            {
                if (DateTimeOffset.TryParseExact(
                        text,
                        format,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out timestamp))
                {
                    return true;
                }
            }
        }

        if (raw is long value)
        {
            try
            {
                timestamp = value > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                    : DateTimeOffset.FromUnixTimeSeconds(value);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        return false;
    }

    private static long GetNonNegativeOrZero(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return 0;
        }

        try
        {
            long value = Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
            return value >= 0 ? value : 0;
        }
        catch (Exception exception) when (exception is FormatException
                                           or InvalidCastException
                                           or OverflowException)
        {
            return 0;
        }
    }

    private static bool TryNonNegativeDecimal(
        SqliteDataReader reader,
        int ordinal,
        out decimal value)
    {
        value = 0;
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        try
        {
            value = Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
            return value >= 0 && value <= long.MaxValue / 1_000_000m;
        }
        catch (Exception exception) when (exception is FormatException
                                           or InvalidCastException
                                           or OverflowException)
        {
            return false;
        }
    }

    private static HashSet<string> GetColumns(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static bool HasColumns(HashSet<string> columns, params string[] expected) =>
        expected.All(columns.Contains);

    private static void ExecuteControl(
        SqliteConnection connection,
        string text,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = text;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        command.ExecuteNonQuery();
    }

    private static string Hash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();
}
