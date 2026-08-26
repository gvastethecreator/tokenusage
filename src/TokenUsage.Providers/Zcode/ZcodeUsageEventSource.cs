using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Zcode;

/// <summary>
/// Reads ZCode's locally stored per-request model usage metrics from the
/// agent's usage database. The SQL projection only selects allowlisted
/// counter columns from <c>model_usage</c> and never materializes prompts,
/// responses, transcripts, credentials, workspace paths, or account data.
/// </summary>
public sealed class ZcodeUsageEventSource :
    IWindowedSnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "zcode-local/1";
    public const long DefaultMaximumDatabaseBytes = 64L * 1024 * 1024 * 1024;
    private const long MaximumPlausibleRequestTokens = 16L * 1024 * 1024;

    /// <summary>
    /// The counter columns this parser understands. A database that drops or
    /// renames any of them is treated as an unsupported schema instead of a
    /// source of made-up numbers.
    /// </summary>
    private static readonly string[] RequiredColumns =
    [
        "id",
        "started_at",
        "model_id",
        "input_tokens",
        "output_tokens",
        "reasoning_tokens",
        "cache_creation_input_tokens",
        "cache_read_input_tokens",
    ];

    private readonly string _zcodeHome;
    private readonly string _databasePath;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;
    private readonly int _maximumRows;
    private readonly TimeProvider _clock;

    public ZcodeUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? zcodeHomeOverride = null,
        string? databasePathOverride = null,
        long maximumDatabaseBytes = DefaultMaximumDatabaseBytes,
        int maximumRows = 100_000,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDatabaseBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRows, 1);

        _zcodeHome = ZcodeUsagePaths.ResolveConfiguredHome(
            homeDirectory,
            zcodeHomeOverride ?? Environment.GetEnvironmentVariable("ZCODE_HOME"));
        _databasePath = databasePathOverride is null
            ? ZcodeUsagePaths.ResolveDatabasePath(_zcodeHome)
            : Path.GetFullPath(databasePathOverride);
        _groupingTimeZoneId = groupingTimeZoneId;
        _budget = new LocalScanBudget(1, maximumDatabaseBytes);
        _maximumRows = maximumRows;
        _clock = clock ?? TimeProvider.System;
    }

    public AgentId AgentId { get; } = new("zcode");

    public SourceKind SourceKind => SourceKind.LocalDatabase;

    public string EventParserVersion => ParserVersion;

    public int ReconciliationWindowDays => UsagePeriodPolicy.ReconciliationDays;

    public bool IsRootAvailable => Directory.Exists(_zcodeHome) || File.Exists(_databasePath);

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

        if (!File.Exists(_databasePath))
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.Empty);
        }

        var state = new LocalScanState(_budget);
        var events = new Dictionary<string, UsageEvent>(StringComparer.Ordinal);
        bool accessBlocked = false;
        try
        {
            var info = new FileInfo(_databasePath);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0
                || state.IsFileTooLarge(info.Length)
                || !state.TryConsumeFile())
            {
                state.MarkPartial();
                accessBlocked = true;
            }
            else
            {
                ReadDatabase(events, state, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            state.MarkPartial();
            accessBlocked = true;
        }

        UsageEvent[] ordered = events.Values
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.EventKey.Value, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                state.UnsupportedSchema
                    ? UsageSourceIssueKind.UnsupportedSchema
                    : accessBlocked || state.IsPartial
                        ? UsageSourceIssueKind.AccessBlocked
                        : UsageSourceIssueKind.Empty);
        }

        return state.IsPartial
            ? new UsageSourceReadResult(
                ordered,
                UsageSourceReadStatus.Partial,
                UsageSourceIssueKind.PartialScan)
            : new UsageSourceReadResult(ordered, UsageSourceReadStatus.Complete);
    }

    private void ReadDatabase(
        Dictionary<string, UsageEvent> output,
        LocalScanState state,
        CancellationToken cancellationToken)
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
        ExecuteControl(connection, "PRAGMA busy_timeout=5000", cancellationToken);
        ExecuteControl(connection, "PRAGMA query_only=ON", cancellationToken);

        HashSet<string> columns = GetColumns(connection, "model_usage", cancellationToken);
        if (!RequiredColumns.All(columns.Contains))
        {
            state.UnsupportedSchema = true;
            state.MarkPartial();
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        // The newest rows come first, so a row cap gives up the oldest
        // requests inside the reconciliation window, not today's usage.
        command.CommandText = """
            SELECT
              id,
              started_at,
              model_id,
              input_tokens,
              output_tokens,
              reasoning_tokens,
              cache_creation_input_tokens,
              cache_read_input_tokens
            FROM model_usage
            WHERE started_at IS NOT NULL
              AND started_at >= $cutoff
              AND COALESCE(input_tokens, 0) + COALESCE(output_tokens, 0)
                + COALESCE(reasoning_tokens, 0) + COALESCE(cache_creation_input_tokens, 0)
                + COALESCE(cache_read_input_tokens, 0) > 0
            ORDER BY started_at DESC, id DESC
            LIMIT $row_limit
            """;
        command.Parameters.AddWithValue("$cutoff", CutoffUnixMilliseconds());
        command.Parameters.AddWithValue("$row_limit", checked(_maximumRows + 1L));
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        int rowsRead = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++rowsRead > _maximumRows)
            {
                state.MarkPartial();
                break;
            }

            if (!TryReadUsage(reader, out ZcodeUsageRow? row) || row is null)
            {
                state.MarkPartial();
                continue;
            }

            UsageEvent usageEvent = CreateEvent(row);
            output[usageEvent.EventKey.Value] = usageEvent;
        }
    }

    private static bool TryReadUsage(SqliteDataReader reader, out ZcodeUsageRow? row)
    {
        row = null;
        if (reader.GetValue(0) is not string id
            || string.IsNullOrWhiteSpace(id)
            || id.Length > 200
            || !TryGetInt64(reader, 1, out long startedAt))
        {
            return false;
        }

        string? rawModel = reader.IsDBNull(2) ? null : reader.GetString(2);
        long input = GetNonNegativeOrZero(reader, 3);
        long output = GetNonNegativeOrZero(reader, 4);
        long reasoning = GetNonNegativeOrZero(reader, 5);
        long cacheWrite = GetNonNegativeOrZero(reader, 6);
        long cacheRead = GetNonNegativeOrZero(reader, 7);
        if (input > MaximumPlausibleRequestTokens
            || output > MaximumPlausibleRequestTokens
            || reasoning > MaximumPlausibleRequestTokens
            || cacheWrite > MaximumPlausibleRequestTokens
            || cacheRead > MaximumPlausibleRequestTokens)
        {
            return false;
        }

        // ZCode's own computed total is input + output, and input is never
        // smaller than the cached counters, so both cached counters sit inside
        // input and reasoning sits inside output. Splitting them keeps the
        // stored breakdown comparable with the other local sources.
        cacheRead = Math.Min(cacheRead, input);
        cacheWrite = Math.Min(cacheWrite, Math.Max(input - cacheRead, 0));
        long uncachedInput = Math.Max(input - cacheRead - cacheWrite, 0);
        long outputTokens = Math.Max(output - Math.Min(reasoning, output), 0);
        row = new ZcodeUsageRow(
            id,
            rawModel,
            startedAt,
            new TokenBreakdown(uncachedInput, outputTokens, reasoning, cacheRead, cacheWrite));
        return true;
    }

    private UsageEvent CreateEvent(ZcodeUsageRow row)
    {
        string model = string.IsNullOrWhiteSpace(row.Model)
            ? "unknown"
            : row.Model.Trim();
        DateTimeOffset occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(row.StartedAtMilliseconds);
        CostObservation cost = KnownModelPricingCatalog.Resolve(model, occurredAt, row.Tokens);
        CoverageKind coverage = cost.Kind switch
        {
            CostKind.ProviderReported => CoverageKind.Complete,
            CostKind.CatalogEstimated => CoverageKind.Partial,
            _ => CoverageKind.Unpriced,
        };
        return new UsageEvent(
            new UsageEventKey(Hash($"zcode\0model-usage\0{row.Id}")),
            AgentId,
            ResolveModelProvider(model),
            ModelIdentity.ToModelId(model),
            occurredAt,
            _groupingTimeZoneId,
            row.Tokens,
            cost,
            ParserVersion,
            coverage);
    }

    private static ModelProviderId? ResolveModelProvider(string model)
    {
        string normalized = model.Trim().ToLowerInvariant();
        return normalized switch
        {
            _ when normalized.Contains("glm", StringComparison.Ordinal) =>
                new ModelProviderId("zai"),
            _ when normalized.Contains("claude", StringComparison.Ordinal) =>
                new ModelProviderId("anthropic"),
            _ when normalized.Contains("gemini", StringComparison.Ordinal) =>
                new ModelProviderId("google"),
            _ when normalized.Contains("grok", StringComparison.Ordinal) =>
                new ModelProviderId("xai"),
            _ when normalized.StartsWith("gpt-", StringComparison.Ordinal)
                || normalized.Contains("openai", StringComparison.Ordinal) =>
                new ModelProviderId("openai"),
            _ when normalized.Contains("kimi", StringComparison.Ordinal) =>
                new ModelProviderId("moonshot"),
            _ when normalized.Contains("deepseek", StringComparison.Ordinal) =>
                new ModelProviderId("deepseek"),
            _ when normalized.Contains("qwen", StringComparison.Ordinal) =>
                new ModelProviderId("alibaba-cloud"),
            _ => null,
        };
    }

    private long CutoffUnixMilliseconds() =>
        _clock.GetUtcNow()
            .AddDays(-UsagePeriodPolicy.ReconciliationDays)
            .ToUnixTimeMilliseconds();

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
            cancellationToken.ThrowIfCancellationRequested();
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static bool TryGetInt64(SqliteDataReader reader, int ordinal, out long value)
    {
        value = 0;
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        object raw = reader.GetValue(ordinal);
        return raw switch
        {
            long number when number >= 0 => Assign(number, out value),
            int number when number >= 0 => Assign(number, out value),
            string text when long.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long number) && number >= 0 => Assign(number, out value),
            _ => false,
        };
    }

    private static bool Assign(long source, out long destination)
    {
        destination = source;
        return true;
    }

    private static long GetNonNegativeOrZero(SqliteDataReader reader, int ordinal) =>
        TryGetInt64(reader, ordinal, out long value) ? value : 0;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record ZcodeUsageRow(
        string Id,
        string? Model,
        long StartedAtMilliseconds,
        TokenBreakdown Tokens);
}
