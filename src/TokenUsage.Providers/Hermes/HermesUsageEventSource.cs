using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Hermes;

/// <summary>
/// Reads only aggregate numeric rows from Hermes state.db files. It does not
/// query the messages table or retain profile and session identifiers.
/// </summary>
public sealed class HermesUsageEventSource :
    ISnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "hermes-state/1";
    private const int DefaultMaximumDatabases = 100;
    private const int DefaultMaximumRows = 100_000;
    private const long DefaultMaximumDatabaseBytes = 1024L * 1024 * 1024;
    private readonly string _homeDirectory;
    private readonly string _groupingTimeZoneId;
    private readonly int _maximumDatabases;
    private readonly int _maximumRows;
    private readonly long _maximumDatabaseBytes;

    public HermesUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? hermesHomeOverride = null,
        int maximumDatabases = DefaultMaximumDatabases,
        int maximumRows = DefaultMaximumRows,
        long maximumDatabaseBytes = DefaultMaximumDatabaseBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDatabases, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDatabaseBytes, 1);
        string home = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _homeDirectory = Path.GetFullPath(hermesHomeOverride
            ?? Environment.GetEnvironmentVariable("HERMES_HOME")
            ?? Path.Combine(home, ".hermes"));
        _groupingTimeZoneId = groupingTimeZoneId;
        _maximumDatabases = maximumDatabases;
        _maximumRows = maximumRows;
        _maximumDatabaseBytes = maximumDatabaseBytes;
    }

    public SourceKind SourceKind => SourceKind.LocalDatabase;

    public AgentId AgentId { get; } = new("hermes");

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The property implements the usage-source contract.")]
    public string EventParserVersion => ParserVersion;

    /// <summary>
    /// Hermes is present when a <c>state.db</c> exists. A leftover
    /// <c>.hermes</c> folder from another tool is not an install.
    /// </summary>
    public bool IsRootAvailable
    {
        get
        {
            try
            {
                return FindDatabasePaths().Count > 0;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

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

        List<string> databasePaths;
        try
        {
            databasePaths = FindDatabasePaths();
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or System.Security.SecurityException)
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.AccessBlocked);
        }

        bool isPartial = databasePaths.Count > _maximumDatabases;
        var events = new Dictionary<string, UsageEvent>(StringComparer.Ordinal);
        foreach (string path in databasePaths.Take(_maximumDatabases))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadDatabase(path, events, cancellationToken))
            {
                isPartial = true;
            }
        }

        UsageEvent[] ordered = events.Values
            .OrderBy(usageEvent => usageEvent.OccurredAtUtc)
            .ThenBy(usageEvent => usageEvent.EventKey.Value, StringComparer.Ordinal)
            .ToArray();
        UsageSourceReadStatus status = isPartial
            ? UsageSourceReadStatus.Partial
            : ordered.Length == 0
                ? UsageSourceReadStatus.NoData
                : UsageSourceReadStatus.Complete;
        return new UsageSourceReadResult(
            ordered,
            status,
            status == UsageSourceReadStatus.NoData
                ? UsageSourceIssueKind.Empty
                : isPartial
                    ? UsageSourceIssueKind.PartialScan
                    : null);
    }

    private List<string> FindDatabasePaths()
    {
        var paths = new List<string>();
        string rootDatabase = Path.Combine(_homeDirectory, "state.db");
        if (File.Exists(rootDatabase))
        {
            paths.Add(rootDatabase);
        }

        string profilesDirectory = Path.Combine(_homeDirectory, "profiles");
        if (Directory.Exists(profilesDirectory))
        {
            foreach (string profile in Directory.EnumerateDirectories(profilesDirectory)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                var info = new DirectoryInfo(profile);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                string path = Path.Combine(profile, "state.db");
                if (File.Exists(path))
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }

    private bool TryReadDatabase(
        string path,
        Dictionary<string, UsageEvent> output,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length <= 0
                || info.Length > _maximumDatabaseBytes
                || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            ExecuteControl(connection, "PRAGMA busy_timeout=250", cancellationToken);
            ExecuteControl(connection, "PRAGMA query_only=ON", cancellationToken);
            return QuerySessions(connection, path, output, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool QuerySessions(
        SqliteConnection connection,
        string databasePath,
        Dictionary<string, UsageEvent> output,
        CancellationToken cancellationToken)
    {
        HashSet<string> columns = GetColumns(connection, "sessions", cancellationToken);
        if (!HasColumns(columns, "id", "input_tokens", "output_tokens"))
        {
            return false;
        }

        string NumberColumn(string name) => columns.Contains(name) ? name : "0";
        string NullableColumn(string name) => columns.Contains(name) ? name : "NULL";
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
              id,
              {NullableColumn("model")},
              {NullableColumn("billing_provider")},
              {NumberColumn("input_tokens")},
              {NumberColumn("output_tokens")},
              {NumberColumn("reasoning_tokens")},
              {NumberColumn("cache_read_tokens")},
              {NumberColumn("cache_write_tokens")},
              {NullableColumn("actual_cost_usd")},
              {NullableColumn("ended_at")},
              {NullableColumn("started_at")}
            FROM sessions
            ORDER BY id
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", checked(_maximumRows + 1L));
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        int rowsRead = 0;
        bool complete = true;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++rowsRead > _maximumRows)
            {
                complete = false;
                break;
            }

            if (TryCreateEvent(reader, databasePath, out UsageEvent? usageEvent)
                && usageEvent is not null)
            {
                output[usageEvent.EventKey.Value] = usageEvent;
            }
            else
            {
                complete = false;
            }
        }

        return complete;
    }

    private bool TryCreateEvent(
        SqliteDataReader reader,
        string databasePath,
        out UsageEvent? usageEvent)
    {
        usageEvent = null;
        if (reader.IsDBNull(0))
        {
            return false;
        }

        string sessionId = Convert.ToString(
            reader.GetValue(0),
            CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        string model = reader.IsDBNull(1) ? "unknown" : reader.GetString(1).Trim();
        if (string.IsNullOrWhiteSpace(sessionId)
            || sessionId.Length > 500
            || model.Length > 200)
        {
            return false;
        }

        if (!TryTimestamp(reader, 9, out DateTimeOffset timestamp)
            && !TryTimestamp(reader, 10, out timestamp))
        {
            return false;
        }

        var tokens = new TokenBreakdown(
            GetNonNegativeOrZero(reader, 3),
            GetNonNegativeOrZero(reader, 4),
            GetNonNegativeOrZero(reader, 5),
            GetNonNegativeOrZero(reader, 6),
            GetNonNegativeOrZero(reader, 7));
        if (tokens.Total == 0)
        {
            return false;
        }

        bool hasReportedCost = TryNonNegativeDecimal(reader, 8, out decimal reportedCost);
        CostObservation cost = hasReportedCost
            ? CostObservation.ProviderReported(decimal.Round(
                reportedCost,
                6,
                MidpointRounding.AwayFromZero))
            : string.Equals(model, "unknown", StringComparison.OrdinalIgnoreCase)
                ? CostObservation.Unavailable()
                : KnownModelPricingCatalog.Resolve(model, timestamp, tokens);
        string provider = reader.IsDBNull(2) ? string.Empty : NormalizeId(reader.GetString(2));
        usageEvent = new UsageEvent(
            new UsageEventKey(Hash($"hermes\0{databasePath}\0{sessionId}")),
            AgentId,
            string.IsNullOrWhiteSpace(provider) ? null : new ModelProviderId(provider),
            new ModelId(NormalizeId(model)),
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

    private static bool TryTimestamp(
        SqliteDataReader reader,
        int ordinal,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        object raw = reader.GetValue(ordinal);
        if (raw is long integer)
        {
            return TryUnixTimestamp(integer, out timestamp);
        }

        if (raw is double floating
            && double.IsFinite(floating)
            && floating >= long.MinValue
            && floating <= long.MaxValue)
        {
            return TryUnixTimestamp((long)floating, out timestamp);
        }

        return raw is string text
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestamp);
    }

    private static bool TryUnixTimestamp(long value, out DateTimeOffset timestamp)
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
            timestamp = default;
            return false;
        }
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
            return value > 0 && value <= long.MaxValue / 1_000_000m;
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

    private static string NormalizeId(string value)
    {
        var output = new StringBuilder(value.Length);
        bool separator = false;
        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                output.Append(character);
                separator = false;
            }
            else if (!separator && output.Length > 0)
            {
                output.Append('-');
                separator = true;
            }
        }

        string normalized = output.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private static string Hash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();
}
