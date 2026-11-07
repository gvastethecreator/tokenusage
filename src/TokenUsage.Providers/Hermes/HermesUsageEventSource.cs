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
    public const string ParserVersion = "hermes-state/4";
    private const int DefaultMaximumDatabases = 100;
    private const int DefaultMaximumRows = 100_000;
    private const long DefaultMaximumDatabaseBytes = 1024L * 1024 * 1024;
    private static readonly string[] TokenColumnNames =
        ["input_tokens", "output_tokens", "reasoning_tokens", "cache_read_tokens", "cache_write_tokens"];
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
            LocalUsageSqlite.ExecuteControl(connection, "PRAGMA busy_timeout=250", cancellationToken);
            LocalUsageSqlite.ExecuteControl(connection, "PRAGMA query_only=ON", cancellationToken);
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
        HashSet<string> columns = LocalUsageSqlite.GetColumns(connection, "sessions", cancellationToken);
        if (!HasColumns(columns, "id", "input_tokens", "output_tokens"))
        {
            return false;
        }

        string NumberColumn(string name) => columns.Contains(name) ? name : "0";
        string NullableColumn(string name) => columns.Contains(name) ? name : "NULL";
        HashSet<string> modelColumns = LocalUsageSqlite.GetColumns(connection, "session_model_usage", cancellationToken);
        bool hasModelUsage = modelColumns.Count > 0;
        if (hasModelUsage && !HasColumns(modelColumns,
                "session_id", "model", "billing_provider", "billing_base_url", "billing_mode", "task",
                "input_tokens", "output_tokens", "reasoning_tokens", "cache_read_tokens", "cache_write_tokens",
                "actual_cost_usd", "first_seen", "last_seen"))
        {
            return false;
        }

        using SqliteCommand command = connection.CreateCommand();
        string sessionQuery = $"""
            SELECT
              id,
              {NullableColumn("model")} AS model,
              {NullableColumn("billing_provider")} AS billing_provider,
              {NumberColumn("input_tokens")} AS input_tokens,
              {NumberColumn("output_tokens")} AS output_tokens,
              {NumberColumn("reasoning_tokens")} AS reasoning_tokens,
              {NumberColumn("cache_read_tokens")} AS cache_read_tokens,
              {NumberColumn("cache_write_tokens")} AS cache_write_tokens,
              {NullableColumn("actual_cost_usd")} AS actual_cost_usd,
              {NullableColumn("ended_at")} AS ended_at,
              {NullableColumn("started_at")} AS started_at,
              NULL AS billing_base_url, NULL AS billing_mode, NULL AS task,
              0 AS is_route, 0 AS has_routes, 1 AS valid_counters
            FROM sessions
            """;
        // Hermes records incremental routes separately; absolute cumulative updates
        // remain only in sessions. Reconcile that residual rather than adding both totals.
        // The writer includes main-loop and voice_chat usage in sessions; auxiliary
        // tasks such as title_generation update only session_model_usage.
        string validSessionCounters = string.Join(" AND ",
            TokenColumnNames
                .Select(name => $"(s.{name} IS NULL OR (typeof(s.{name}) = 'integer' AND s.{name} >= 0))"));
        command.CommandText = hasModelUsage ? $"""
            WITH session_totals AS ({sessionQuery}),
            route_totals AS (
              SELECT session_id,
                SUM(CASE WHEN task IN ('', 'voice_chat') THEN input_tokens ELSE 0 END) AS input_tokens,
                SUM(CASE WHEN task IN ('', 'voice_chat') THEN output_tokens ELSE 0 END) AS output_tokens,
                SUM(CASE WHEN task IN ('', 'voice_chat') THEN reasoning_tokens ELSE 0 END) AS reasoning_tokens,
                SUM(CASE WHEN task IN ('', 'voice_chat') THEN cache_read_tokens ELSE 0 END) AS cache_read_tokens,
                SUM(CASE WHEN task IN ('', 'voice_chat') THEN cache_write_tokens ELSE 0 END) AS cache_write_tokens,
                SUM(CASE WHEN task IN ('', 'voice_chat') THEN actual_cost_usd ELSE 0 END) AS actual_cost_usd,
                COUNT(*) AS route_count
              FROM session_model_usage
              GROUP BY session_id
            ), observations AS (
              SELECT s.id, u.model, u.billing_provider, u.input_tokens, u.output_tokens, u.reasoning_tokens,
                u.cache_read_tokens, u.cache_write_tokens, u.actual_cost_usd,
                COALESCE(u.last_seen, s.ended_at), COALESCE(u.first_seen, s.started_at),
                u.billing_base_url, u.billing_mode, u.task, 1, 0, 1
              FROM session_model_usage u JOIN session_totals s ON s.id = u.session_id
              UNION ALL
              SELECT s.id, s.model, s.billing_provider,
                MAX(0, COALESCE(s.input_tokens, 0) - COALESCE(r.input_tokens, 0)),
                MAX(0, COALESCE(s.output_tokens, 0) - COALESCE(r.output_tokens, 0)),
                MAX(0, COALESCE(s.reasoning_tokens, 0) - COALESCE(r.reasoning_tokens, 0)),
                MAX(0, COALESCE(s.cache_read_tokens, 0) - COALESCE(r.cache_read_tokens, 0)),
                MAX(0, COALESCE(s.cache_write_tokens, 0) - COALESCE(r.cache_write_tokens, 0)),
                MAX(0, COALESCE(s.actual_cost_usd, 0) - COALESCE(r.actual_cost_usd, 0)),
                s.ended_at, s.started_at, NULL, NULL, NULL, 0, COALESCE(r.route_count, 0),
                CASE WHEN {validSessionCounters} THEN 1 ELSE 0 END
              FROM session_totals s LEFT JOIN route_totals r ON r.session_id = s.id
            )
            SELECT * FROM observations ORDER BY 1, 15, 2, 3, 12, 13, 14 LIMIT $limit
            """ : $"{sessionQuery} ORDER BY id LIMIT $limit";
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

            if (TryCreateEvent(reader, databasePath, out UsageEvent? usageEvent))
            {
                if (usageEvent is not null)
                {
                    output[usageEvent.EventKey.Value] = usageEvent;
                }
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

        if (reader.GetInt64(16) == 0
            || !TryNonNegativeTokens(reader, 3, out long input)
            || !TryNonNegativeTokens(reader, 4, out long output)
            || !TryNonNegativeTokens(reader, 5, out long recordedReasoning)
            || !TryNonNegativeTokens(reader, 6, out long cacheRead)
            || !TryNonNegativeTokens(reader, 7, out long cacheWrite))
        {
            return false;
        }

        long reasoning = Math.Min(output, recordedReasoning);
        var tokens = new TokenBreakdown(
            input,
            output - reasoning,
            reasoning,
            cacheRead,
            cacheWrite);
        // Keep the original session identity as its residual, even at zero, so
        // re-import replaces older aggregate observations and future totals can grow.
        bool isResidual = reader.GetInt64(15) > 0;
        bool hasReportedCost = TryNonNegativeDecimal(reader, 8, out decimal reportedCost);
        if (tokens.Total == 0 && !isResidual && !(hasReportedCost && reportedCost > 0m))
        {
            return reader.IsDBNull(8) || (hasReportedCost && reportedCost == 0m);
        }

        if (!TryTimestamp(reader, 9, out DateTimeOffset timestamp)
            && !TryTimestamp(reader, 10, out timestamp))
        {
            return false;
        }

        CostObservation cost = hasReportedCost && reportedCost > 0m
            ? CostObservation.ProviderReported(decimal.Round(
                reportedCost,
                6,
                MidpointRounding.AwayFromZero))
            : string.Equals(model, "unknown", StringComparison.OrdinalIgnoreCase)
                ? CostObservation.Unavailable()
                : KnownModelPricingCatalog.Resolve(model, timestamp, tokens, perRequestTokenCounters: false);
        string provider = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        string identity = reader.GetInt64(14) == 0
            ? $"hermes\0{databasePath}\0{sessionId}"
            : $"hermes-model\0{databasePath}\0{sessionId}\0{model}\0{provider}\0{reader.GetString(11)}\0{reader.GetString(12)}\0{reader.GetString(13)}";
        usageEvent = new UsageEvent(
            new UsageEventKey(Hash(identity)),
            AgentId,
            ModelIdentity.TryProviderId(provider),
            ModelIdentity.ToModelId(model),
            timestamp,
            _groupingTimeZoneId,
            tokens,
            cost,
            ParserVersion,
            isResidual ? CoverageKind.Partial : cost.Kind switch
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

    private static bool TryNonNegativeTokens(SqliteDataReader reader, int ordinal, out long value)
    {
        value = 0;
        if (reader.IsDBNull(ordinal))
        {
            return true;
        }

        if (reader.GetValue(ordinal) is long count && count >= 0)
        {
            value = count;
            return true;
        }

        return false;
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

    private static bool HasColumns(HashSet<string> columns, params string[] expected) =>
        expected.All(columns.Contains);

    private static string Hash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();
}
