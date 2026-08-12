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
    public const string ParserVersion = "cursor-composer-state/1";
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
        long maximumDatabaseBytes = 512L * 1024 * 1024,
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

        // Cursor labels these values as estimated context tokens. They are useful
        // local activity evidence, but not cumulative billing usage or quota data.
        return new UsageSourceReadResult(
            ordered,
            UsageSourceReadStatus.Partial,
            UsageSourceIssueKind.PartialScan);
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
        ExecuteControl(connection, "PRAGMA busy_timeout=250", cancellationToken);
        ExecuteControl(connection, "PRAGMA query_only=ON", cancellationToken);

        HashSet<string> columns = GetColumns(connection, "cursorDiskKV", cancellationToken);
        if (!columns.Contains("key") || !columns.Contains("value"))
        {
            state.UnsupportedSchema = true;
            state.MarkPartial();
            return;
        }

        MarkSkippedRows(connection, state, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              key,
              CASE WHEN json_valid(CAST(value AS TEXT)) THEN
                json_extract(CAST(value AS TEXT), '$.conversationCheckpointLastUpdatedAt') END,
              CASE WHEN json_valid(CAST(value AS TEXT)) THEN
                json_extract(CAST(value AS TEXT), '$.lastUpdatedAt') END,
              CASE WHEN json_valid(CAST(value AS TEXT)) THEN
                json_extract(CAST(value AS TEXT), '$.createdAt') END,
              CASE WHEN json_valid(CAST(value AS TEXT)) THEN
                json_extract(CAST(value AS TEXT), '$.modelConfig.modelName') END,
              CASE WHEN json_valid(CAST(value AS TEXT)) THEN
                json_extract(CAST(value AS TEXT), '$.promptTokenBreakdown.totalUsedTokens') END,
              CASE WHEN json_valid(CAST(value AS TEXT)) THEN
                json_extract(CAST(value AS TEXT), '$.contextTokensUsed') END,
              length(value)
            FROM cursorDiskKV
            WHERE key GLOB 'composerData:*'
              AND length(value) <= $value_limit
              AND json_valid(CAST(value AS TEXT))
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

            var usageEvent = new UsageEvent(
                new UsageEventKey(Hash($"cursor\0composer-state-v1\0{candidate.Identity}")),
                AgentId,
                ResolveModelProvider(candidate.Model),
                CreateModelId(candidate.Model),
                candidate.Timestamp,
                _groupingTimeZoneId,
                new TokenBreakdown(candidate.EstimatedContextTokens, 0, 0, 0, 0),
                CostObservation.Unavailable(),
                ParserVersion,
                CoverageKind.Unpriced);
            output[usageEvent.EventKey.Value] = usageEvent;
        }
    }

    private void MarkSkippedRows(
        SqliteConnection connection,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              COUNT(*),
              COALESCE(SUM(CASE
                WHEN length(value) > $value_limit
                  OR NOT json_valid(CAST(value AS TEXT))
                THEN 1 ELSE 0 END), 0)
            FROM cursorDiskKV
            WHERE key GLOB 'composerData:*'
            """;
        command.Parameters.AddWithValue("$value_limit", _maximumValueBytes);
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        if (reader.Read()
            && (reader.GetInt64(0) > _maximumRows || reader.GetInt64(1) > 0))
        {
            state.MarkPartial();
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
}
