using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;

namespace TokenUsage.Providers.Cursor;

/// <summary>
/// Reads Cursor's locally stored, estimated composer context totals.
/// The SQL projection only selects allowlisted metadata and never materializes
/// prompts, responses, workspace paths, account data, or authentication values.
/// </summary>
public sealed class CursorUsageEventSource :
    IWindowedSnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "cursor-local-token-metrics/3";
    private const int LookbackDays = 35;
    private const int DefaultMaximumValueBytes = 2 * 1024 * 1024;
    private const long MaximumPlausibleContextTokens = 16 * 1024 * 1024;
    private readonly string _cursorHome;
    private readonly string _databasePath;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;
    private readonly int _maximumRows;
    private readonly int _maximumValueBytes;

    public CursorUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? roamingAppDataDirectory = null,
        string? databasePathOverride = null,
        long maximumDatabaseBytes = 2L * 1024 * 1024 * 1024,
        int maximumRows = 100_000,
        int maximumValueBytes = DefaultMaximumValueBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDatabaseBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumValueBytes, 1);

        _cursorHome = CursorUsagePaths.ResolveCursorHome(homeDirectory);
        _databasePath = databasePathOverride is null
            ? CursorUsagePaths.ResolveStateDatabasePath(roamingAppDataDirectory)
            : Path.GetFullPath(databasePathOverride);
        _groupingTimeZoneId = groupingTimeZoneId;
        _budget = new LocalScanBudget(1, maximumDatabaseBytes);
        _maximumRows = maximumRows;
        _maximumValueBytes = maximumValueBytes;
    }

    public AgentId AgentId { get; } = new("cursor");

    public SourceKind SourceKind => SourceKind.LocalDatabase;

    public string EventParserVersion => ParserVersion;

    public int ReconciliationWindowDays => LookbackDays;

    public bool IsRootAvailable => Directory.Exists(_cursorHome) || File.Exists(_databasePath);

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

        // Event coverage stays Partial or Unpriced. Scan status is Complete when
        // this read finished inside the row and size limits, so refresh can replace
        // stored Cursor events instead of leaving stale composer snapshots in place.
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

        HashSet<string> columns = GetColumns(connection, "cursorDiskKV", cancellationToken);
        if (!columns.Contains("key") || !columns.Contains("value"))
        {
            state.UnsupportedSchema = true;
            state.MarkPartial();
            return;
        }

        HashSet<string> composersWithTurnTokens = ReadBubbleTokenRows(
            connection,
            output,
            state,
            cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              key,
              json_extract(value, '$.conversationCheckpointLastUpdatedAt'),
              json_extract(value, '$.lastUpdatedAt'),
              json_extract(value, '$.createdAt'),
              json_extract(value, '$.modelConfig.modelName'),
              json_extract(value, '$.promptTokenBreakdown.totalUsedTokens'),
              json_extract(value, '$.contextTokensUsed'),
              length(value)
            FROM cursorDiskKV
            WHERE key GLOB 'composerData:*'
              AND length(value) <= $value_limit
              AND json_extract(value, '$.modelConfig.modelName') IS NOT NULL
            ORDER BY key
            LIMIT $row_limit
            """;
        command.Parameters.AddWithValue("$value_limit", _maximumValueBytes);
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

            if (!TryReadCandidate(reader, out CursorComposerCandidate? candidate)
                || candidate is null)
            {
                continue;
            }

            if (composersWithTurnTokens.Contains(candidate.Identity["composerData:".Length..]))
            {
                continue;
            }

            var tokens = new TokenBreakdown(candidate.EstimatedContextTokens, 0, 0, 0, 0);
            (CostObservation cost, CoverageKind coverage) = PriceCursorTokens(
                candidate.Model,
                candidate.Timestamp,
                tokens);
            var usageEvent = new UsageEvent(
                new UsageEventKey(Hash($"cursor\0composer-state-v1\0{candidate.Identity}")),
                AgentId,
                ResolveModelProvider(candidate.Model),
                CreateModelId(candidate.Model),
                candidate.Timestamp,
                _groupingTimeZoneId,
                tokens,
                cost,
                ParserVersion,
                coverage);
            output[usageEvent.EventKey.Value] = usageEvent;
        }

        MarkSkippedComposerRows(
            connection,
            composersWithTurnTokens,
            state,
            cancellationToken);
    }

    /// <summary>
    /// Reads the per-turn counters Cursor writes next to a conversation turn. A build that keeps
    /// the field but leaves it at zero has no turn counters to read, and those rows are left in
    /// the database instead of being pulled out and rejected one by one. The newest rows come
    /// first, so a row cap gives up the oldest turns rather than today's.
    /// </summary>
    private HashSet<string> ReadBubbleTokenRows(
        SqliteConnection connection,
        Dictionary<string, UsageEvent> output,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        var composers = new HashSet<string>(StringComparer.Ordinal);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              key,
              json_extract(value, '$.createdAt'),
              json_extract(value, '$.modelInfo.modelName'),
              json_extract(value, '$.tokenCount.inputTokens'),
              json_extract(value, '$.tokenCount.outputTokens')
            FROM cursorDiskKV
            WHERE key GLOB 'bubbleId:*'
              AND length(value) <= $value_limit
              AND COALESCE(json_extract(value, '$.tokenCount.inputTokens'), 0)
                + COALESCE(json_extract(value, '$.tokenCount.outputTokens'), 0) > 0
            ORDER BY ROWID DESC
            LIMIT $row_limit
            """;
        command.Parameters.AddWithValue("$value_limit", _maximumValueBytes);
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

            if (!TryReadBubbleCandidate(reader, out CursorBubbleCandidate? candidate)
                || candidate is null)
            {
                state.MarkPartial();
                continue;
            }

            composers.Add(candidate.ComposerId);
            var tokens = new TokenBreakdown(
                candidate.InputTokens,
                candidate.OutputTokens,
                reasoning: 0,
                cacheRead: 0,
                cacheWrite: 0);
            (CostObservation cost, CoverageKind coverage) = PriceCursorTokens(
                candidate.Model,
                candidate.Timestamp,
                tokens);
            var usageEvent = new UsageEvent(
                new UsageEventKey(Hash($"cursor\0bubble-token-v2\0{candidate.Identity}")),
                AgentId,
                ResolveModelProvider(candidate.Model),
                CreateModelId(candidate.Model),
                candidate.Timestamp,
                _groupingTimeZoneId,
                tokens,
                cost,
                ParserVersion,
                coverage);
            output[usageEvent.EventKey.Value] = usageEvent;
        }

        return composers;
    }

    private static bool TryReadBubbleCandidate(
        SqliteDataReader reader,
        out CursorBubbleCandidate? candidate)
    {
        candidate = null;
        if (reader.GetValue(0) is not string identity
            || !TryGetBubbleComposerId(identity, out string? composerId)
            || string.IsNullOrWhiteSpace(composerId)
            || !TryGetTimestamp(reader, 1, out DateTimeOffset timestamp))
        {
            return false;
        }

        string model = reader.IsDBNull(2) ? "cursor-auto" : reader.GetString(2).Trim();
        if (string.IsNullOrWhiteSpace(model) || model.Length > 200)
        {
            model = "cursor-auto";
        }

        long input = GetNonNegativeOrZero(reader, 3);
        long output = GetNonNegativeOrZero(reader, 4);
        if (input == 0 && output == 0
            || input > MaximumPlausibleContextTokens
            || output > MaximumPlausibleContextTokens)
        {
            return false;
        }

        candidate = new CursorBubbleCandidate(
            identity,
            composerId,
            timestamp,
            model,
            input,
            output);
        return true;
    }

    private static bool TryGetBubbleComposerId(string key, out string? composerId)
    {
        composerId = null;
        const string prefix = "bubbleId:";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        int separator = key.IndexOf(':', prefix.Length);
        composerId = separator < 0 ? key[prefix.Length..] : key[prefix.Length..separator];
        return !string.IsNullOrWhiteSpace(composerId)
            && composerId.Length <= 200
            && composerId.All(character => !char.IsControl(character));
    }

    /// <summary>
    /// A skipped composer blob is only a truncated scan when that conversation has no
    /// turn counters. A blob we cannot read for a conversation that already has bubbles
    /// does not keep refresh from replacing stored events.
    /// </summary>
    private void MarkSkippedComposerRows(
        SqliteConnection connection,
        HashSet<string> composersWithTurnTokens,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT key
            FROM cursorDiskKV
            WHERE key GLOB 'composerData:*'
              AND (length(value) > $value_limit
                OR (length(value) <= 65536
                    AND NOT json_valid(CAST(value AS TEXT))))
            LIMIT $row_limit
            """;
        command.Parameters.AddWithValue("$value_limit", _maximumValueBytes);
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
                return;
            }

            if (reader.GetValue(0) is not string key
                || !key.StartsWith("composerData:", StringComparison.Ordinal)
                || key.Length <= "composerData:".Length)
            {
                state.MarkPartial();
                return;
            }

            if (!composersWithTurnTokens.Contains(key["composerData:".Length..]))
            {
                state.MarkPartial();
                return;
            }
        }
    }

    private static bool TryReadCandidate(
        SqliteDataReader reader,
        out CursorComposerCandidate? candidate)
    {
        candidate = null;
        if (reader.GetValue(0) is not string identity
            || reader.GetValue(4) is not string rawModel
            || !TryGetInt64(reader, 7, out long valueBytes)
            || valueBytes <= 0)
        {
            return false;
        }

        string model = rawModel.Trim();
        if (!identity.StartsWith("composerData:", StringComparison.Ordinal)
            || identity.Length <= "composerData:".Length
            || string.IsNullOrWhiteSpace(model)
            || model.Length > 200)
        {
            return false;
        }

        long tokens;
        bool hasBreakdown = TryGetInt64(reader, 5, out long breakdownTokens);
        bool hasContext = TryGetInt64(reader, 6, out long contextTokens);
        if (hasBreakdown)
        {
            tokens = breakdownTokens;
        }
        else if (hasContext)
        {
            tokens = contextTokens;
        }
        else
        {
            return false;
        }

        if (tokens <= 0 || tokens > MaximumPlausibleContextTokens
            || !TryGetTimestamp(reader, out DateTimeOffset timestamp))
        {
            return false;
        }

        candidate = new CursorComposerCandidate(identity, timestamp, model, tokens);
        return true;
    }

    private static bool TryGetTimestamp(
        SqliteDataReader reader,
        out DateTimeOffset timestamp)
    {
        foreach (int ordinal in new[] { 1, 2, 3 })
        {
            if (TryGetInt64(reader, ordinal, out long milliseconds))
            {
                try
                {
                    timestamp = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
                    if (timestamp.Year is >= 2000 and <= 2100)
                    {
                        return true;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }
        }

        timestamp = default;
        return false;
    }

    private static bool TryGetTimestamp(
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
        if (TryConvertTimestamp(raw, out timestamp))
        {
            return timestamp.Year is >= 2000 and <= 2100;
        }

        return false;
    }

    private static bool TryConvertTimestamp(object raw, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (raw is string text
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed))
        {
            timestamp = parsed.ToUniversalTime();
            return true;
        }

        if (raw is long milliseconds)
        {
            try
            {
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        return false;
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

    private static (CostObservation Cost, CoverageKind Coverage) PriceCursorTokens(
        string model,
        DateTimeOffset timestamp,
        TokenBreakdown tokens)
    {
        CostObservation cost = CursorPricingCatalog.Resolve(model, timestamp, tokens);
        CoverageKind coverage = cost.Kind == CostKind.CatalogEstimated
            ? CoverageKind.Partial
            : CoverageKind.Unpriced;
        return (cost, coverage);
    }

    private static ModelId CreateModelId(string model)
    {
        string normalized = new string(model.Trim().ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '.' or '_'
                    ? character
                    : '-')
            .ToArray()).Trim('-');
        return new ModelId(string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized);
    }

    private static ModelProviderId? ResolveModelProvider(string model)
    {
        string normalized = model.Trim().ToLowerInvariant();
        return normalized switch
        {
            _ when normalized.Contains("claude", StringComparison.Ordinal) =>
                new ModelProviderId("anthropic"),
            _ when normalized.Contains("gemini", StringComparison.Ordinal) =>
                new ModelProviderId("google"),
            _ when normalized.Contains("grok", StringComparison.Ordinal) =>
                new ModelProviderId("xai"),
            _ when normalized.StartsWith("gpt-", StringComparison.Ordinal)
                || normalized.StartsWith('o')
                || normalized.Contains("openai", StringComparison.Ordinal) =>
                new ModelProviderId("openai"),
            _ => null,
        };
    }

    private static string Hash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private sealed record CursorComposerCandidate(
        string Identity,
        DateTimeOffset Timestamp,
        string Model,
        long EstimatedContextTokens);

    private sealed record CursorBubbleCandidate(
        string Identity,
        string ComposerId,
        DateTimeOffset Timestamp,
        string Model,
        long InputTokens,
        long OutputTokens);
}
