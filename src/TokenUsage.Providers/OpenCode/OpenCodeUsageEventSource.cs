using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;

namespace TokenUsage.Providers.OpenCode;

public sealed class OpenCodeUsageEventSource :
    IWindowedSnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "opencode-local/1";
    private const int LookbackDays = 35;
    private readonly string _dataRoot;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;
    private readonly int _maximumRows;

    public OpenCodeUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? dataDirectoryOverride = null,
        string? xdgDataHomeOverride = null,
        int maximumFiles = 10_000,
        long maximumFileBytes = 16 * 1024 * 1024,
        int maximumRows = 1_000_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRows, 1);

        string home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? configured = dataDirectoryOverride ?? Environment.GetEnvironmentVariable("OPENCODE_DATA_DIR");
        string? xdg = xdgDataHomeOverride ?? Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        _dataRoot = ResolveRoot(configured, xdg, home);
        _groupingTimeZoneId = groupingTimeZoneId;
        _budget = new LocalScanBudget(maximumFiles, maximumFileBytes);
        _maximumRows = maximumRows;
    }

    public SourceKind SourceKind => SourceKind.LocalDatabase;
    public AgentId AgentId { get; } = new("opencode");

    public string EventParserVersion => ParserVersion;

    public int ReconciliationWindowDays => LookbackDays;

    public bool IsRootAvailable => Directory.Exists(_dataRoot);

    public async Task<UsageSourceReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        await Task.Run(() => ReadCore(cancellationToken), cancellationToken).ConfigureAwait(false);

    private UsageSourceReadResult ReadCore(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_dataRoot))
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.RootUnavailable);
        }

        var state = new LocalScanState(_budget);
        var databaseEvents = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        var aggregateEvents = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        var aggregateSessions = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in EnumerateDatabaseFiles(state, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CountFile(state))
            {
                break;
            }

            ReadDatabase(
                path,
                databaseEvents,
                aggregateEvents,
                aggregateSessions,
                state,
                _maximumRows,
                cancellationToken);
        }

        HashSet<string> sessionsWithMessages = databaseEvents.Values
            .Select(candidate => candidate.SessionId)
            .ToHashSet(StringComparer.Ordinal);
        foreach ((string sessionId, Candidate aggregate) in aggregateEvents)
        {
            if (!sessionsWithMessages.Contains(sessionId))
            {
                databaseEvents.TryAdd(Key(sessionId, aggregate.MessageId), aggregate);
            }
        }

        ReadLegacyJson(aggregateSessions, databaseEvents, state, cancellationToken);

        List<UsageEvent> events = databaseEvents.Values
            .OrderBy(candidate => candidate.SessionId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.MessageId, StringComparer.Ordinal)
            .Select(CreateEvent)
            .ToList();
        UsageSourceReadStatus status = state.IsPartial
            ? UsageSourceReadStatus.Partial
            : events.Count == 0 ? UsageSourceReadStatus.NoData : UsageSourceReadStatus.Complete;
        return new UsageSourceReadResult(
            events,
            status,
            state.UnsupportedSchema && events.Count == 0
                ? UsageSourceIssueKind.UnsupportedSchema
                : status == UsageSourceReadStatus.NoData
                    ? UsageSourceIssueKind.Empty
                    : null);
    }

    private string[] EnumerateDatabaseFiles(LocalScanState state, CancellationToken cancellationToken)
    {
        try
        {
            return Directory.EnumerateFiles(_dataRoot, "opencode*.db", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
                               && !path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            state.IsPartial = true;
            return [];
        }
    }

    private static bool ReadDatabase(
        string path,
        Dictionary<string, Candidate> messages,
        Dictionary<string, Candidate> aggregates,
        HashSet<string> aggregateSessions,
        LocalScanState state,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                state.IsPartial = true;
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
            ExecuteControl(connection, "BEGIN", cancellationToken);

            HashSet<string> messageColumns = GetColumns(connection, "message", cancellationToken);
            HashSet<string> sessionColumns = GetColumns(connection, "session", cancellationToken);
            if (HasColumns(sessionColumns, "id", "time_updated", "model", "cost", "tokens_input", "tokens_output", "tokens_reasoning", "tokens_cache_read", "tokens_cache_write"))
            {
                ReadAggregateRows(
                    connection,
                    aggregates,
                    aggregateSessions,
                    state,
                    maximumRows,
                    cancellationToken);
            }
            else if (HasColumns(messageColumns, "id", "session_id", "time_created", "data"))
            {
                HashSet<string> partColumns = GetColumns(connection, "part", cancellationToken);
                ReadMessageRows(
                    connection,
                    messages,
                    state,
                    maximumRows,
                    HasColumns(partColumns, "id", "message_id", "time_created", "data"),
                    cancellationToken);
            }
            else
            {
                state.IsPartial = true;
                state.UnsupportedSchema = true;
                return false;
            }

            ExecuteControl(connection, "COMMIT", cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            state.IsPartial = true;
            return false;
        }
    }

    private static void ExecuteControl(SqliteConnection connection, string text, CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = text;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        command.ExecuteNonQuery();
    }

    private static HashSet<string> GetColumns(SqliteConnection connection, string table, CancellationToken cancellationToken)
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

    private static bool HasColumns(HashSet<string> columns, params string[] expected) =>
        expected.All(columns.Contains);

    private static void ReadMessageRows(
        SqliteConnection connection,
        Dictionary<string, Candidate> output,
        LocalScanState state,
        int maximumRows,
        bool hasPartFallback,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        string partJoin = hasPartFallback
            ? """
              LEFT JOIN part p ON p.id = (
                SELECT p2.id FROM part p2
                WHERE p2.message_id = m.id
                  AND json_valid(p2.data)
                  AND json_extract(p2.data, '$.type') = 'step-finish'
                ORDER BY p2.time_created DESC, p2.id DESC
                LIMIT 1)
              """
            : string.Empty;
        string PartValue(string path) => hasPartFallback
            ? $"json_extract(p.data, '$.{path}')"
            : "NULL";
        string partCostType = hasPartFallback ? "json_type(p.data, '$.cost')" : "NULL";
        command.CommandText = $"""
            SELECT m.id, m.session_id, m.time_created,
              json_extract(m.data, '$.role'),
              COALESCE(json_extract(m.data, '$.modelID'), json_extract(m.data, '$.model.modelID'), json_extract(m.data, '$.model.id'), json_extract(m.data, '$.model')),
              COALESCE(json_extract(m.data, '$.providerID'), json_extract(m.data, '$.model.providerID')),
              COALESCE(json_extract(m.data, '$.cost'), {PartValue("cost")}),
              CASE WHEN json_type(m.data, '$.cost') IN ('real','integer') THEN json_type(m.data, '$.cost') ELSE {partCostType} END,
              COALESCE(json_extract(m.data, '$.tokens.input'), json_extract(m.data, '$.usage.input_tokens'), {PartValue("tokens.input")}),
              COALESCE(json_extract(m.data, '$.tokens.output'), json_extract(m.data, '$.usage.output_tokens'), {PartValue("tokens.output")}),
              COALESCE(json_extract(m.data, '$.tokens.reasoning'), {PartValue("tokens.reasoning")}),
              json_extract(m.data, '$.tokens.cache.read'),
              COALESCE(json_extract(m.data, '$.tokens.cache.write'), json_extract(m.data, '$.usage.cache_creation_input_tokens'), {PartValue("tokens.cache.write")}),
              COALESCE(json_extract(m.data, '$.tokens.cache.read'), json_extract(m.data, '$.usage.cache_read_input_tokens'), {PartValue("tokens.cache.read")})
            FROM message m
            {partJoin}
            WHERE m.time_created >= $cutoff AND json_valid(m.data)
            LIMIT $limit
            """;
        command.Parameters.AddWithValue(
            "$cutoff",
            DateTimeOffset.UtcNow.AddDays(-LookbackDays).ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$limit", checked(maximumRows + 1L));
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        int rowsRead = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++rowsRead > maximumRows) { state.IsPartial = true; break; }
            string sessionId = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (!TryReadDbCandidate(reader, sessionId, out Candidate? candidate))
            {
                if (HasPossibleUsage(reader)) state.IsPartial = true;
                continue;
            }
            output.TryAdd(Key(candidate.SessionId, candidate.MessageId), candidate);
        }
    }

    private static bool TryReadDbCandidate(SqliteDataReader reader, string sessionId, out Candidate candidate)
    {
        candidate = default!;
        if (string.IsNullOrWhiteSpace(sessionId) || reader.IsDBNull(0) || reader.IsDBNull(2)
            || reader.IsDBNull(3)
            || (!string.Equals(reader.GetString(3), "assistant", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reader.GetString(3), "model", StringComparison.OrdinalIgnoreCase))
            || reader.IsDBNull(4))
        {
            return false;
        }

        string messageId = reader.GetString(0);
        string model = reader.GetString(4);
        if (!TryTimestamp(reader.GetValue(2), out DateTimeOffset timestamp)
            || !TryNonNegative(reader, 8, out long input)
            || !TryNonNegative(reader, 9, out long output))
        {
            return false;
        }
        long reasoning = GetNonNegativeOrZero(reader, 10);
        long cacheRead = GetNonNegativeOrZero(reader, 13);
        long cacheWrite = GetNonNegativeOrZero(reader, 12);
        decimal? cost = null;
        bool hasCost = !reader.IsDBNull(7) && string.Equals(reader.GetString(7), "real", StringComparison.OrdinalIgnoreCase)
                       || !reader.IsDBNull(7) && string.Equals(reader.GetString(7), "integer", StringComparison.OrdinalIgnoreCase);
        decimal parsedCost = 0;
        if (hasCost && !TryCost(reader, 6, out parsedCost)) return false;
        if (hasCost) cost = parsedCost;
        string? provider = reader.IsDBNull(5) ? null : reader.GetString(5);
        candidate = new Candidate(sessionId, messageId, timestamp, model, new TokenBreakdown(input, output, reasoning, cacheRead, cacheWrite), cost, provider);
        return true;
    }

    private static bool HasPossibleUsage(SqliteDataReader reader) =>
        !reader.IsDBNull(8) || !reader.IsDBNull(9) || !reader.IsDBNull(6);

    private static void ReadAggregateRows(
        SqliteConnection connection,
        Dictionary<string, Candidate> output,
        HashSet<string> aggregateSessions,
        LocalScanState state,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,time_updated,model,cost,tokens_input,tokens_output,tokens_reasoning,tokens_cache_read,tokens_cache_write FROM session WHERE time_updated >= $cutoff LIMIT $limit";
        command.Parameters.AddWithValue(
            "$cutoff",
            DateTimeOffset.UtcNow.AddDays(-LookbackDays).ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$limit", checked(maximumRows + 1L));
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        int rowsRead = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++rowsRead > maximumRows) { state.IsPartial = true; break; }
            string sessionId = reader.IsDBNull(0) ? "" : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(sessionId) || reader.IsDBNull(1) || reader.IsDBNull(2)
                || !TryTimestamp(reader.GetValue(1), out DateTimeOffset timestamp)
                || !TryNonNegative(reader, 4, out long input) || !TryNonNegative(reader, 5, out long outputTokens))
            {
                state.IsPartial = true;
                continue;
            }
            long reasoning = GetNonNegativeOrZero(reader, 6);
            long cacheRead = GetNonNegativeOrZero(reader, 7);
            long cacheWrite = GetNonNegativeOrZero(reader, 8);
            decimal? cost = null;
            if (!reader.IsDBNull(3))
            {
                if (!TryCost(reader, 3, out decimal parsed)) { state.IsPartial = true; continue; }
                cost = parsed;
            }
            aggregateSessions.Add(sessionId);
            output.TryAdd(sessionId, new Candidate(sessionId, "aggregate", timestamp, reader.GetString(2), new TokenBreakdown(input, outputTokens, reasoning, cacheRead, cacheWrite), cost));
        }
    }

    private void ReadLegacyJson(
        HashSet<string> databaseSessions,
        Dictionary<string, Candidate> output,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        string sessionRoot = Path.Combine(_dataRoot, "storage", "session");
        string messageRoot = Path.Combine(_dataRoot, "storage", "message");
        string partRoot = Path.Combine(_dataRoot, "storage", "part");
        if (!Directory.Exists(sessionRoot) || !Directory.Exists(messageRoot)) return;
        foreach (string sessionPath in EnumerateJson(sessionRoot, SearchOption.AllDirectories, state))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CountFile(state)) break;
            if (!TryReadAllowedJson(sessionPath, out JsonFields metadata, state, cancellationToken)
                || string.IsNullOrWhiteSpace(metadata.Id))
            {
                state.IsPartial = true;
                continue;
            }
            string sessionId = metadata.Id;
            if (databaseSessions.Contains(sessionId)) continue;
            string directory = Path.Combine(messageRoot, sessionId);
            if (!Directory.Exists(directory)) continue;
            foreach (string messagePath in EnumerateJson(directory, SearchOption.TopDirectoryOnly, state))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CountFile(state)) return;
                if (!TryReadAllowedJson(messagePath, out JsonFields fields, state, cancellationToken)) continue;
                if (!TryJsonCandidate(sessionId, fields, out Candidate? candidate)
                    && !TryApplyStepFinish(
                        sessionId,
                        partRoot,
                        fields,
                        state,
                        cancellationToken,
                        out candidate))
                {
                    if (fields.HasUsage) state.IsPartial = true;
                    continue;
                }
                output.TryAdd(Key(sessionId, candidate.MessageId), candidate);
            }
        }
    }

    private bool TryApplyStepFinish(
        string sessionId,
        string partRoot,
        JsonFields message,
        LocalScanState state,
        CancellationToken cancellationToken,
        out Candidate candidate)
    {
        candidate = default!;
        if (string.IsNullOrWhiteSpace(message.Id)
            || (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(message.Role, "model", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string directory = Path.Combine(partRoot, message.Id);
        if (!Directory.Exists(directory))
        {
            return false;
        }

        JsonFields? latestStepFinish = null;
        foreach (string path in EnumerateJson(directory, SearchOption.TopDirectoryOnly, state))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CountFile(state))
            {
                return false;
            }

            if (!TryReadAllowedJson(path, out JsonFields part, state, cancellationToken)
                || !string.Equals(part.Type, "step-finish", StringComparison.OrdinalIgnoreCase)
                || !part.HasUsage)
            {
                continue;
            }

            latestStepFinish = part;
        }

        if (latestStepFinish is null) return false;
        message.ApplyUsageFrom(latestStepFinish);
        return TryJsonCandidate(sessionId, message, out candidate);
    }

    private static string[] EnumerateJson(string root, SearchOption option, LocalScanState state)
    {
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = option == SearchOption.AllDirectories,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
            };
            return Directory.EnumerateFiles(root, "*.json", options)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (IsFileFailure(exception)) { state.IsPartial = true; return []; }
    }

    private bool TryReadAllowedJson(string path, out JsonFields fields, LocalScanState state, CancellationToken cancellationToken)
    {
        fields = new JsonFields();
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > _budget.MaximumFileBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                state.IsPartial = true;
                return false;
            }
            if (info.Length > int.MaxValue) { state.IsPartial = true; return false; }
            byte[] bytes = new byte[checked((int)info.Length)];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan))
            {
                int offset = 0;
                while (offset < bytes.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
                    if (count == 0) throw new IOException("The JSON file changed while it was read.");
                    offset += count;
                }
                if (stream.ReadByte() != -1) { state.IsPartial = true; return false; }
            }
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 });
            ParseAllowedFields(ref reader, fields, "");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException) { state.IsPartial = true; return false; }
    }

    private static void ParseAllowedFields(ref Utf8JsonReader reader, JsonFields fields, string prefix)
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            string name = reader.GetString() ?? "";
            if (!reader.Read()) throw new JsonException();
            string path = string.IsNullOrEmpty(prefix) ? name : prefix + "." + name;
            if (reader.TokenType == JsonTokenType.StartObject
                && path is "time" or "tokens" or "tokens.cache" or "model" or "usage")
            {
                ParseAllowedFields(ref reader, fields, path);
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }
            else
            {
                fields.Set(path, ref reader);
            }
        }
    }

    private static bool TryJsonCandidate(string sessionId, JsonFields fields, out Candidate candidate)
    {
        candidate = default!;
        if ((!string.Equals(fields.Role, "assistant", StringComparison.OrdinalIgnoreCase)
             && !string.Equals(fields.Role, "model", StringComparison.OrdinalIgnoreCase))
            || string.IsNullOrWhiteSpace(fields.Id) || string.IsNullOrWhiteSpace(fields.Model)
            || fields.Timestamp is null || fields.Input is null || fields.Output is null) return false;
        candidate = new Candidate(sessionId, fields.Id, fields.Timestamp.Value, fields.Model,
            new TokenBreakdown(fields.Input.Value, fields.Output.Value, fields.Reasoning ?? 0, fields.CacheRead ?? 0, fields.CacheWrite ?? 0), fields.Cost, fields.Provider);
        return true;
    }

    private UsageEvent CreateEvent(Candidate candidate)
    {
        CostObservation cost = candidate.Cost is decimal reported ? CostObservation.ProviderReported(decimal.Round(reported, 6)) : CostObservation.Unavailable();
        (ModelProviderId? providerId, ModelId modelId) = CreateModelIdentity(candidate.Provider, candidate.Model);
        return new UsageEvent(new UsageEventKey(Hash($"opencode\0{candidate.SessionId}\0{candidate.MessageId}")), AgentId, providerId,
            modelId, candidate.Timestamp, _groupingTimeZoneId, candidate.Tokens, cost, ParserVersion,
            candidate.Cost is null ? CoverageKind.Unpriced : CoverageKind.Complete);
    }

    private static (ModelProviderId? Provider, ModelId Model) CreateModelIdentity(string? provider, string model)
    {
        string modelValue = model.Trim();
        int separator = modelValue.IndexOf('/');
        if (separator > 0)
        {
            string prefix = modelValue[..separator];
            if (string.IsNullOrWhiteSpace(provider))
            {
                provider = prefix;
                modelValue = modelValue[(separator + 1)..];
            }
            else if (string.Equals(provider.Trim(), prefix, StringComparison.OrdinalIgnoreCase))
            {
                modelValue = modelValue[(separator + 1)..];
            }
        }
        ModelProviderId? providerId = null;
        try { if (!string.IsNullOrWhiteSpace(provider)) providerId = new ModelProviderId(provider.Trim().ToLowerInvariant()); }
        catch (ArgumentException) { providerId = null; }
        try { return (providerId, new ModelId(modelValue.ToLowerInvariant())); }
        catch (ArgumentException) { return (providerId, new ModelId("unknown-" + Hash(model)[..16])); }
    }

    private static bool TryTimestamp(object value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value is long number)
        {
            try { timestamp = DateTimeOffset.FromUnixTimeMilliseconds(number); return true; }
            catch (ArgumentOutOfRangeException) { return false; }
        }
        return value is string text && DateTimeOffset.TryParse(text, out timestamp) && (timestamp = timestamp.ToUniversalTime()).Offset == TimeSpan.Zero;
    }

    private static bool TryNonNegative(SqliteDataReader reader, int ordinal, out long value)
    {
        value = 0;
        if (reader.IsDBNull(ordinal)) return false;
        try { value = reader.GetInt64(ordinal); return value >= 0; }
        catch (Exception exception) when (exception is InvalidCastException or OverflowException) { return false; }
    }

    private static long GetNonNegativeOrZero(SqliteDataReader reader, int ordinal) => TryNonNegative(reader, ordinal, out long value) ? value : 0;

    private static bool TryCost(SqliteDataReader reader, int ordinal, out decimal value)
    {
        value = 0;
        if (reader.IsDBNull(ordinal)) return false;
        try { value = Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture); return value >= 0 && value <= long.MaxValue / 1_000_000m; }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException) { return false; }
    }

    private bool CountFile(LocalScanState state)
    {
        if (++state.FilesRead <= _budget.MaximumFiles) return true;
        state.IsPartial = true;
        return false;
    }

    private static string ResolveRoot(string? configured, string? xdg, string home)
    {
        string fallback = Path.Combine(home, ".local", "share", "opencode");
        try
        {
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
            if (!string.IsNullOrWhiteSpace(xdg)) return Path.GetFullPath(Path.Combine(Environment.ExpandEnvironmentVariables(xdg), "opencode"));
            return Path.GetFullPath(fallback);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return fallback; }
    }

    private static string Key(string sessionId, string messageId) => sessionId + "\0" + messageId;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool IsFileFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private sealed record Candidate(string SessionId, string MessageId, DateTimeOffset Timestamp, string Model, TokenBreakdown Tokens, decimal? Cost, string? Provider = null);
    private sealed class JsonFields
    {
        public string? Id { get; private set; }
        public string? Role { get; private set; }
        public string? Type { get; private set; }
        public string? Model { get; private set; }
        public string? Provider { get; private set; }
        public DateTimeOffset? Timestamp { get; private set; }
        public decimal? Cost { get; private set; }
        public long? Input { get; private set; }
        public long? Output { get; private set; }
        public long? Reasoning { get; private set; }
        public long? CacheRead { get; private set; }
        public long? CacheWrite { get; private set; }
        public bool HasUsage => Cost is not null || Input is not null || Output is not null;

        public void Set(string path, ref Utf8JsonReader reader)
        {
            if (path == "id" && reader.TokenType == JsonTokenType.String) Id = reader.GetString();
            else if (path == "role" && reader.TokenType == JsonTokenType.String) Role = reader.GetString();
            else if (path == "type" && reader.TokenType == JsonTokenType.String) Type = reader.GetString();
            else if (path is "model" or "modelID" or "model.id" && reader.TokenType == JsonTokenType.String) Model = reader.GetString();
            else if (path == "providerID" && reader.TokenType == JsonTokenType.String) Provider = reader.GetString();
            else if (path is "time.created" or "time_created" && TryReaderTimestamp(ref reader, out DateTimeOffset time)) Timestamp = time;
            else if (path == "cost" && reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out decimal cost) && cost >= 0) Cost = cost;
            else if (path == "tokens.input") Input = ReadNonNegative(ref reader);
            else if (path == "tokens.output") Output = ReadNonNegative(ref reader);
            else if (path == "tokens.reasoning") Reasoning = ReadNonNegative(ref reader);
            else if (path == "tokens.cache.read") CacheRead = ReadNonNegative(ref reader);
            else if (path == "tokens.cache.write") CacheWrite = ReadNonNegative(ref reader);
            else if (path == "usage.input_tokens") Input = ReadNonNegative(ref reader);
            else if (path == "usage.output_tokens") Output = ReadNonNegative(ref reader);
            else if (path == "usage.cache_read_input_tokens") CacheRead = ReadNonNegative(ref reader);
            else if (path == "usage.cache_creation_input_tokens") CacheWrite = ReadNonNegative(ref reader);
        }

        public void ApplyUsageFrom(JsonFields source)
        {
            Cost ??= source.Cost;
            Input ??= source.Input;
            Output ??= source.Output;
            Reasoning ??= source.Reasoning;
            CacheRead ??= source.CacheRead;
            CacheWrite ??= source.CacheWrite;
            Timestamp ??= source.Timestamp;
            Model ??= source.Model;
            Provider ??= source.Provider;
        }

        private static long? ReadNonNegative(ref Utf8JsonReader reader) => reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long value) && value >= 0 ? value : null;
        private static bool TryReaderTimestamp(ref Utf8JsonReader reader, out DateTimeOffset timestamp)
        {
            timestamp = default;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long number)) return TryTimestamp(number, out timestamp);
            return reader.TokenType == JsonTokenType.String && TryTimestamp(reader.GetString()!, out timestamp);
        }
    }
}
