using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;

namespace TokenUsage.Providers.Codex;

public sealed class CodexUsageEventSource :
    IWindowedSnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "codex-jsonl/3";
    private const int DefaultTailBytes = 64 * 1024;
    private const int RecentLocalWindowDays = 3;
    private const long MaximumInitialRecentScanBytes = 16L * 1024 * 1024 * 1024;
    private readonly string _codexHome;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;
    private readonly ICodexQuotaClientFactory? _clientFactory;
    private readonly TimeProvider _clock;
    private readonly CodexUsageCheckpointStore? _checkpointStore;

    public CodexUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? codexHomeOverride = null,
        int maximumFiles = 10_000,
        long maximumTailBytes = DefaultTailBytes,
        int maximumLineCharacters = DefaultTailBytes,
        ICodexQuotaClientFactory? clientFactory = null,
        string? checkpointPath = null,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumTailBytes, int.MaxValue);

        string userHome = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? configured = codexHomeOverride
            ?? Environment.GetEnvironmentVariable("CODEX_HOME");
        _codexHome = ResolveHome(configured, userHome);
        _groupingTimeZoneId = groupingTimeZoneId;
        _budget = new LocalScanBudget(maximumFiles, maximumTailBytes, maximumLineCharacters);
        _clientFactory = clientFactory;
        _clock = clock ?? TimeProvider.System;
        _checkpointStore = checkpointPath is null
            ? null
            : new CodexUsageCheckpointStore(checkpointPath, _clock);
    }

    public SourceKind SourceKind => _clientFactory is null
        ? SourceKind.LocalLog
        : SourceKind.OfficialLocalApi;

    public AgentId AgentId { get; } = new("codex");

    public string EventParserVersion => ParserVersion;

    public int ReconciliationWindowDays => 35;

    public bool IsRootAvailable => SessionRoots().Any(Directory.Exists);

    public async Task<UsageSourceReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        ScanResult scan = _checkpointStore is null
            ? await Task.Run(
                    () => ScanCore(checkpoints: null, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false)
            : await _checkpointStore.UpdateAsync(
                    checkpoints => ScanCore(checkpoints, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        return _clientFactory is null
            ? CreateFallbackResult(scan)
            : await ReadOfficialUsageAsync(scan, cancellationToken).ConfigureAwait(false);
    }

    private ScanResult ScanCore(
        CodexUsageCheckpointState? checkpoints,
        CancellationToken cancellationToken)
    {
        string[] roots = SessionRoots().Where(Directory.Exists).ToArray();
        if (roots.Length == 0)
        {
            return new ScanResult(
                [],
                [],
                UsesCheckpoints: checkpoints is not null,
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.RootUnavailable);
        }

        var state = new LocalScanState(_budget);
        SessionFile[] files = FindSessionFiles(roots, state, cancellationToken);
        var sessions = new List<ScannedSession>(files.Length);
        DateOnly recentFrom = RecentFrom();
        long initialBytesRemaining = MaximumInitialRecentScanBytes;
        IEnumerable<SessionFile> orderedFiles = checkpoints is null
            ? files
            : files.OrderByDescending(TryGetLastWriteTimeUtc);
        HashSet<string> activeSessionIdentities = files
            .Select(file => file.SessionIdentity)
            .ToHashSet(StringComparer.Ordinal);
        foreach (SessionFile file in orderedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.TryConsumeFile())
            {
                break;
            }

            bool tailComplete = ReadTail(file, state, cancellationToken, out Candidate? candidate);
            if (!tailComplete)
            {
                state.MarkPartial();
            }

            if (candidate is not null)
            {
                sessions.Add(new ScannedSession(file.SessionIdentity, candidate));
            }

            if (checkpoints is not null)
            {
                ScanRecentUsage(
                    file,
                    checkpoints,
                    recentFrom,
                    ref initialBytesRemaining,
                    state,
                    cancellationToken);
            }
        }

        if (checkpoints is not null)
        {
            foreach (string staleIdentity in checkpoints.Files.Keys
                         .Where(identity => !activeSessionIdentities.Contains(identity))
                         .ToArray())
            {
                checkpoints.Files.Remove(staleIdentity);
            }

            PruneCheckpointDays(checkpoints, recentFrom);
        }

        return CreateScanResult(sessions, checkpoints, state);
    }

    private SessionFile[] FindSessionFiles(
        string[] roots,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        SessionFile[]? indexed = ReadStateIndex(roots, state, cancellationToken);
        if (indexed is { Length: > 0 })
        {
            return indexed;
        }

        var bySession = new Dictionary<string, SessionFile>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            foreach (string path in EnumerateJsonlFiles(root, state, cancellationToken))
            {
                AddSessionFile(bySession, new SessionFile(
                    path,
                    SessionIdentity(path),
                    Model: null));
            }
        }

        return bySession.Values
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private SessionFile[]? ReadStateIndex(
        string[] roots,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        string databasePath = Path.Combine(_codexHome, "state_5.sqlite");
        if (!File.Exists(databasePath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(databasePath);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                state.MarkPartial();
                return null;
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            ExecuteControl(connection, "PRAGMA busy_timeout=250", cancellationToken);
            ExecuteControl(connection, "PRAGMA query_only=ON", cancellationToken);

            HashSet<string> columns = GetColumns(connection, "threads", cancellationToken);
            if (!columns.Contains("rollout_path") || !columns.Contains("model"))
            {
                state.UnsupportedSchema = true;
                state.MarkPartial();
                return null;
            }

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT rollout_path, model FROM threads "
                + "WHERE rollout_path IS NOT NULL AND rollout_path <> '' "
                + "ORDER BY rollout_path LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", checked(_budget.MaximumFiles + 1L));
            using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
            using SqliteDataReader reader = command.ExecuteReader();
            var bySession = new Dictionary<string, SessionFile>(StringComparer.OrdinalIgnoreCase);
            int rowsRead = 0;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++rowsRead > _budget.MaximumFiles)
                {
                    state.MarkPartial();
                    break;
                }

                string rawPath = reader.GetString(0);
                string? path = ResolveIndexedPath(rawPath, roots);
                if (path is null)
                {
                    state.MarkPartial();
                    continue;
                }

                string? model = reader.IsDBNull(1) ? null : reader.GetString(1);
                AddSessionFile(bySession, new SessionFile(
                    path,
                    SessionIdentity(path),
                    NormalizeModel(model)));
            }

            return bySession.Values
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            state.MarkPartial();
            return null;
        }
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
        command.CommandText = $"PRAGMA table_info({table});";
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

    private static string? ResolveIndexedPath(string rawPath, string[] roots)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        string path;
        try
        {
            path = Path.GetFullPath(RemoveExtendedPathPrefix(rawPath));
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return null;
        }

        return string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase)
               && roots.Any(root => IsWithinRoot(path, root))
            ? path
            : null;
    }

    private static string RemoveExtendedPathPrefix(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        return path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)
            ? path[devicePrefix.Length..]
            : path;
    }

    private static bool IsWithinRoot(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
               && !string.Equals(relative, "..", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void AddSessionFile(
        Dictionary<string, SessionFile> files,
        SessionFile candidate)
    {
        if (!files.TryGetValue(candidate.SessionIdentity, out SessionFile? current))
        {
            files.Add(candidate.SessionIdentity, candidate);
            return;
        }

        long currentLength = TryGetLength(current.Path);
        long candidateLength = TryGetLength(candidate.Path);
        if (candidateLength > currentLength
            || (candidateLength == currentLength
                && current.Model is null
                && candidate.Model is not null))
        {
            files[candidate.SessionIdentity] = candidate;
        }
    }

    private static long TryGetLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            return -1;
        }
    }

    private DateOnly RecentFrom()
    {
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_groupingTimeZoneId);
        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), timeZone).DateTime);
        return today.AddDays(-(RecentLocalWindowDays - 1));
    }

    private static DateTime TryGetLastWriteTimeUtc(SessionFile file)
    {
        try
        {
            return File.GetLastWriteTimeUtc(file.Path);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            return DateTime.MinValue;
        }
    }

    private void ScanRecentUsage(
        SessionFile file,
        CodexUsageCheckpointState checkpoints,
        DateOnly recentFrom,
        ref long initialBytesRemaining,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(file.Path);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                state.MarkPartial();
                return;
            }

            string pathHash = Hash(Path.GetFullPath(file.Path).ToUpperInvariant());
            bool hasCheckpoint = checkpoints.Files.TryGetValue(
                file.SessionIdentity,
                out CodexUsageFileCheckpoint? checkpoint);
            if (!hasCheckpoint
                || !string.Equals(checkpoint!.PathHash, pathHash, StringComparison.Ordinal)
                || checkpoint.Offset > info.Length)
            {
                if (info.LastWriteTimeUtc < StartOfDayUtc(recentFrom))
                {
                    checkpoints.Files.Remove(file.SessionIdentity);
                    return;
                }

                if (info.Length > initialBytesRemaining)
                {
                    state.MarkPartial();
                    return;
                }

                initialBytesRemaining -= info.Length;
                checkpoint = new CodexUsageFileCheckpoint(
                    pathHash,
                    offset: 0,
                    NormalizeModel(file.Model) ?? "unknown",
                    previous: null);
                checkpoints.Files[file.SessionIdentity] = checkpoint;
            }

            if (checkpoint.Offset == info.Length)
            {
                return;
            }

            using var stream = new FileStream(
                file.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                1024 * 1024,
                FileOptions.SequentialScan);
            stream.Seek(checkpoint.Offset, SeekOrigin.Begin);
            ScanRecentLines(
                stream,
                checkpoint,
                recentFrom,
                state,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or ArgumentException
                                           or OverflowException
                                           or System.Security.SecurityException)
        {
            state.MarkPartial();
        }
    }

    private void ScanRecentLines(
        FileStream stream,
        CodexUsageFileCheckpoint checkpoint,
        DateOnly recentFrom,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024 * 1024];
        using var line = new MemoryStream(capacity: Math.Min(state.MaximumLineBytes, 64 * 1024));
        bool oversized = false;
        long absoluteOffset = stream.Position;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                break;
            }

            int segmentStart = 0;
            while (segmentStart < bytesRead)
            {
                int newline = Array.IndexOf(buffer, (byte)'\n', segmentStart, bytesRead - segmentStart);
                int segmentEnd = newline >= 0 ? newline : bytesRead;
                AppendRecentLineSegment(
                    line,
                    buffer.AsSpan(segmentStart, segmentEnd - segmentStart),
                    state.MaximumLineBytes,
                    ref oversized);
                if (newline < 0)
                {
                    break;
                }

                if (!oversized)
                {
                    ReadOnlyMemory<byte> utf8 = line.GetBuffer().AsMemory(0, checked((int)line.Length));
                    if (!utf8.IsEmpty && utf8.Span[^1] == (byte)'\r')
                    {
                        utf8 = utf8[..^1];
                    }

                    ProcessRecentLine(utf8, checkpoint, recentFrom, state);
                }
                else if (line.GetBuffer().AsSpan(0, checked((int)line.Length))
                         .IndexOf("token_count"u8) >= 0)
                {
                    state.UnsupportedSchema = true;
                    state.MarkPartial();
                }

                checkpoint.Offset = checked(absoluteOffset + newline + 1L);
                line.SetLength(0);
                oversized = false;
                segmentStart = newline + 1;
            }

            absoluteOffset = checked(absoluteOffset + bytesRead);
        }
    }

    private static void AppendRecentLineSegment(
        MemoryStream line,
        ReadOnlySpan<byte> segment,
        int maximumLineBytes,
        ref bool oversized)
    {
        if (oversized || segment.IsEmpty)
        {
            return;
        }

        int remaining = maximumLineBytes - checked((int)line.Length);
        if (segment.Length <= remaining)
        {
            line.Write(segment);
            return;
        }

        if (remaining > 0)
        {
            line.Write(segment[..remaining]);
        }

        oversized = true;
    }

    private void ProcessRecentLine(
        ReadOnlyMemory<byte> utf8,
        CodexUsageFileCheckpoint checkpoint,
        DateOnly recentFrom,
        LocalScanState state)
    {
        ReadOnlySpan<byte> bytes = utf8.Span;
        bool mightBeContext = bytes.IndexOf("turn_context"u8) >= 0;
        bool mightBeUsage = bytes.IndexOf("token_count"u8) >= 0;
        bool mightBeSessionMeta = bytes.IndexOf("session_meta"u8) >= 0;
        bool mightBeTaskStarted = bytes.IndexOf("task_started"u8) >= 0;
        if (!mightBeContext && !mightBeUsage && !mightBeSessionMeta && !mightBeTaskStarted)
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8);
            JsonElement root = document.RootElement;
            if (!TryGetString(root, "type", out string? recordType)
                || !root.TryGetProperty("payload", out JsonElement payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                state.UnsupportedSchema = true;
                state.MarkPartial();
                return;
            }

            if (string.Equals(recordType, "session_meta", StringComparison.Ordinal))
            {
                ObserveSessionMeta(root, payload, checkpoint);
                return;
            }

            if (string.Equals(recordType, "turn_context", StringComparison.Ordinal))
            {
                if (TryGetString(payload, "model", out string? model))
                {
                    checkpoint.Model = NormalizeModel(model) ?? "unknown";
                }

                return;
            }

            if (!string.Equals(recordType, "event_msg", StringComparison.Ordinal)
                || !TryGetString(payload, "type", out string? eventType))
            {
                return;
            }

            if (string.Equals(eventType, "task_started", StringComparison.Ordinal))
            {
                ObserveTaskStarted(root, payload, checkpoint);
                return;
            }

            if (!string.Equals(eventType, "token_count", StringComparison.Ordinal)
                || !payload.TryGetProperty("info", out JsonElement info)
                || info.ValueKind is JsonValueKind.Null)
            {
                return;
            }

            if (info.ValueKind != JsonValueKind.Object
                || !TryGetUtcTimestamp(root, "timestamp", out DateTimeOffset timestamp))
            {
                state.UnsupportedSchema = true;
                state.MarkPartial();
                return;
            }

            bool hasCumulative = info.TryGetProperty(
                                     "total_token_usage",
                                     out JsonElement cumulativeElement)
                                 && cumulativeElement.ValueKind == JsonValueKind.Object;
            bool hasLast = info.TryGetProperty(
                               "last_token_usage",
                               out JsonElement lastElement)
                           && lastElement.ValueKind == JsonValueKind.Object;
            TokenBreakdown? current = null;
            TokenBreakdown? last = null;
            bool cumulativeIsValid = hasCumulative
                && TryReadTokenBreakdown(cumulativeElement, out current);
            bool lastIsValid = hasLast && TryReadTokenBreakdown(lastElement, out last);
            if (!cumulativeIsValid && !lastIsValid)
            {
                state.UnsupportedSchema = true;
                state.MarkPartial();
                return;
            }

            if (checkpoint.ChildReplayPending)
            {
                if (cumulativeIsValid)
                {
                    checkpoint.Previous = current;
                }
                else
                {
                    state.MarkPartial();
                }

                return;
            }

            TokenBreakdown delta;
            if (cumulativeIsValid)
            {
                delta = Difference(current!, checkpoint.Previous);
                checkpoint.Previous = current;
            }
            else
            {
                delta = last!;
                state.MarkPartial();
            }

            if (delta.Total == 0)
            {
                return;
            }

            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_groupingTimeZoneId);
            DateOnly date = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(timestamp, timeZone).DateTime);
            if (date < recentFrom)
            {
                return;
            }

            var key = (date, checkpoint.Model);
            checkpoint.Daily[key] = checkpoint.Daily.TryGetValue(key, out TokenBreakdown? existing)
                ? AddTokens(existing, delta)
                : delta;
        }
        catch (Exception exception) when (exception is JsonException
                                           or ArgumentException
                                           or InvalidOperationException
                                           or OverflowException)
        {
            state.UnsupportedSchema = true;
            state.MarkPartial();
        }
    }

    private static void ObserveSessionMeta(
        JsonElement root,
        JsonElement payload,
        CodexUsageFileCheckpoint checkpoint)
    {
        if (checkpoint.SawSessionMeta)
        {
            return;
        }

        checkpoint.SawSessionMeta = true;
        if (!IsChildSessionMeta(payload))
        {
            return;
        }

        checkpoint.ChildReplayPending = true;
        checkpoint.ChildCreatedAtUnixSeconds = TryGetUtcTimestamp(
            root,
            "timestamp",
            out DateTimeOffset createdAt)
            ? createdAt.ToUnixTimeSeconds()
            : null;
    }

    private static void ObserveTaskStarted(
        JsonElement root,
        JsonElement payload,
        CodexUsageFileCheckpoint checkpoint)
    {
        if (!checkpoint.ChildReplayPending
            || !TryGetNonNegativeDouble(payload, "started_at", out double startedAt))
        {
            return;
        }

        long? threshold = checkpoint.ChildCreatedAtUnixSeconds;
        if (threshold is null
            && TryGetUtcTimestamp(root, "timestamp", out DateTimeOffset lineTimestamp))
        {
            threshold = lineTimestamp.ToUnixTimeSeconds();
        }

        if (threshold is null || startedAt < threshold.Value)
        {
            return;
        }

        checkpoint.ChildReplayPending = false;
        checkpoint.ChildCreatedAtUnixSeconds = null;
    }

    private static bool IsChildSessionMeta(JsonElement payload)
    {
        if (HasNonNullValue(payload, "forked_from_id")
            || HasNonNullValue(payload, "parent_thread_id"))
        {
            return true;
        }

        if (TryGetString(payload, "thread_source", out string? threadSource)
            && string.Equals(threadSource, "subagent", StringComparison.Ordinal))
        {
            return true;
        }

        return payload.TryGetProperty("source", out JsonElement source)
               && source.ValueKind == JsonValueKind.Object
               && HasNonNullValue(source, "subagent");
    }

    private static bool HasNonNullValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        return value.ValueKind != JsonValueKind.String
               || !string.IsNullOrWhiteSpace(value.GetString());
    }

    private static bool TryGetNonNegativeDouble(
        JsonElement element,
        string propertyName,
        out double value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out JsonElement property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetDouble(out value)
               && double.IsFinite(value)
               && value >= 0;
    }

    private DateTime StartOfDayUtc(DateOnly date)
    {
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_groupingTimeZoneId);
        DateTime local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private static TokenBreakdown Difference(
        TokenBreakdown current,
        TokenBreakdown? previous)
    {
        if (previous is null
            || TotalInput(current) < TotalInput(previous)
            || TotalOutput(current) < TotalOutput(previous))
        {
            return current;
        }

        return new TokenBreakdown(
            Math.Max(0, current.Input - previous.Input),
            Math.Max(0, current.Output - previous.Output),
            Math.Max(0, current.Reasoning - previous.Reasoning),
            Math.Max(0, current.CacheRead - previous.CacheRead),
            Math.Max(0, current.CacheWrite - previous.CacheWrite));
    }

    private static long TotalInput(TokenBreakdown value) => checked(
        value.Input + value.CacheRead + value.CacheWrite);

    private static long TotalOutput(TokenBreakdown value) => checked(
        value.Output + value.Reasoning);

    private static TokenBreakdown AddTokens(TokenBreakdown left, TokenBreakdown right) => new(
        checked(left.Input + right.Input),
        checked(left.Output + right.Output),
        checked(left.Reasoning + right.Reasoning),
        checked(left.CacheRead + right.CacheRead),
        checked(left.CacheWrite + right.CacheWrite));

    private static void PruneCheckpointDays(
        CodexUsageCheckpointState checkpoints,
        DateOnly recentFrom)
    {
        foreach (CodexUsageFileCheckpoint checkpoint in checkpoints.Files.Values)
        {
            foreach ((DateOnly Date, string Model) key in checkpoint.Daily.Keys
                         .Where(key => key.Date < recentFrom)
                         .ToArray())
            {
                checkpoint.Daily.Remove(key);
            }
        }
    }

    private bool ReadTail(
        SessionFile file,
        LocalScanState state,
        CancellationToken cancellationToken,
        out Candidate? candidate)
    {
        candidate = null;
        try
        {
            var info = new FileInfo(file.Path);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            using var stream = new FileStream(
                file.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.RandomAccess);
            int tailLength = checked((int)Math.Min(stream.Length, _budget.MaximumFileBytes));
            if (tailLength == 0)
            {
                return true;
            }

            long startPosition = stream.Length - tailLength;
            stream.Seek(startPosition, SeekOrigin.Begin);
            byte[] bytes = new byte[tailLength];
            int bytesRead = 0;
            while (bytesRead < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = stream.Read(bytes, bytesRead, bytes.Length - bytesRead);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            int offset = 0;
            if (startPosition > 0)
            {
                int firstNewline = Array.IndexOf(bytes, (byte)'\n', 0, bytesRead);
                if (firstNewline < 0)
                {
                    return false;
                }

                offset = firstNewline + 1;
            }

            bool complete = true;
            string? currentModel = file.Model;
            while (offset < bytesRead)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int newline = Array.IndexOf(bytes, (byte)'\n', offset, bytesRead - offset);
                bool hasNewline = newline >= 0;
                int end = hasNewline ? newline : bytesRead;
                int length = end - offset;
                if (length > 0 && bytes[end - 1] == (byte)'\r')
                {
                    length--;
                }

                bool lineIsValid = ProcessLine(
                    bytes.AsMemory(offset, length),
                    ref currentModel,
                    ref candidate,
                    state,
                    markSchemaFailures: hasNewline);
                if (hasNewline)
                {
                    complete &= lineIsValid;
                }

                if (!hasNewline)
                {
                    break;
                }

                offset = newline + 1;
            }

            return complete;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool ProcessLine(
        ReadOnlyMemory<byte> utf8,
        ref string? currentModel,
        ref Candidate? latest,
        LocalScanState state,
        bool markSchemaFailures)
    {
        if (utf8.Length == 0 || utf8.Length > state.MaximumLineBytes)
        {
            return utf8.Length == 0 || MarkSchemaFailure(state, markSchemaFailures);
        }

        ReadOnlySpan<byte> bytes = utf8.Span;
        if (bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF)
        {
            utf8 = utf8[3..];
            bytes = utf8.Span;
        }

        bool mightBeContext = bytes.IndexOf("turn_context"u8) >= 0;
        bool mightBeUsage = bytes.IndexOf("token_count"u8) >= 0;
        if (!mightBeContext && !mightBeUsage)
        {
            return true;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8);
            JsonElement root = document.RootElement;
            if (!TryGetString(root, "type", out string? recordType)
                || !root.TryGetProperty("payload", out JsonElement payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return MarkSchemaFailure(state, markSchemaFailures);
            }

            if (string.Equals(recordType, "turn_context", StringComparison.Ordinal))
            {
                if (!TryGetString(payload, "model", out string? model))
                {
                    return MarkSchemaFailure(state, markSchemaFailures);
                }

                currentModel = NormalizeModel(model);
                return true;
            }

            if (!string.Equals(recordType, "event_msg", StringComparison.Ordinal)
                || !TryGetString(payload, "type", out string? eventType)
                || !string.Equals(eventType, "token_count", StringComparison.Ordinal))
            {
                return true;
            }

            if (!payload.TryGetProperty("info", out JsonElement info)
                || info.ValueKind is JsonValueKind.Null)
            {
                return true;
            }

            if (info.ValueKind != JsonValueKind.Object)
            {
                return MarkSchemaFailure(state, markSchemaFailures);
            }

            bool hasCumulative = info.TryGetProperty(
                                     "total_token_usage",
                                     out JsonElement cumulativeElement)
                                 && cumulativeElement.ValueKind == JsonValueKind.Object;
            bool hasLast = info.TryGetProperty(
                               "last_token_usage",
                               out JsonElement lastElement)
                           && lastElement.ValueKind == JsonValueKind.Object;
            if (!hasCumulative && !hasLast)
            {
                return info.TryGetProperty("total_token_usage", out _)
                       || info.TryGetProperty("last_token_usage", out _)
                    ? MarkSchemaFailure(state, markSchemaFailures)
                    : true;
            }

            if (!TryGetUtcTimestamp(root, "timestamp", out DateTimeOffset timestamp))
            {
                return MarkSchemaFailure(state, markSchemaFailures);
            }

            TokenBreakdown? cumulative = null;
            TokenBreakdown? last = null;
            bool cumulativeIsValid = hasCumulative
                && TryReadTokenBreakdown(cumulativeElement, out cumulative);
            bool lastIsValid = hasLast
                && TryReadTokenBreakdown(lastElement, out last);
            if (!cumulativeIsValid && !lastIsValid)
            {
                return MarkSchemaFailure(state, markSchemaFailures);
            }

            if ((hasCumulative && !cumulativeIsValid)
                || (hasLast && !lastIsValid))
            {
                state.MarkPartial();
            }

            TokenBreakdown total = cumulative ?? last!;
            TokenBreakdown sample = last ?? total;

            latest = new Candidate(
                timestamp,
                NormalizeModel(currentModel) ?? "unknown",
                total,
                sample);
            if (!hasCumulative)
            {
                state.MarkPartial();
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException
                                           or ArgumentException
                                           or InvalidOperationException
                                           or OverflowException)
        {
            return MarkSchemaFailure(state, markSchemaFailures);
        }
    }

    private static bool TryReadTokenBreakdown(
        JsonElement usage,
        out TokenBreakdown? tokens)
    {
        tokens = null;
        if (!TryGetNonNegativeInt64(usage, "input_tokens", out long input)
            || !TryGetNonNegativeInt64(usage, "output_tokens", out long output)
            || !TryGetOptionalNonNegativeInt64(
                usage,
                "cached_input_tokens",
                out long cacheRead)
            || !TryGetOptionalNonNegativeInt64(
                usage,
                "cache_write_input_tokens",
                out long cacheWrite)
            || !TryGetOptionalNonNegativeInt64(
                usage,
                "reasoning_output_tokens",
                out long reasoning)
            || checked(cacheRead + cacheWrite) > input
            || reasoning > output)
        {
            return false;
        }

        if (usage.TryGetProperty("total_tokens", out JsonElement totalElement)
            && (!totalElement.TryGetInt64(out long total)
                || total < 0
                || total != checked(input + output)))
        {
            return false;
        }

        tokens = new TokenBreakdown(
            input - cacheRead - cacheWrite,
            output - reasoning,
            reasoning,
            cacheRead,
            cacheWrite);
        return true;
    }

    private async Task<UsageSourceReadResult> ReadOfficialUsageAsync(
        ScanResult scan,
        CancellationToken cancellationToken)
    {
        try
        {
            CodexClientAvailability availability = await _clientFactory!
                .DetectAsync(cancellationToken)
                .ConfigureAwait(false);
            if (availability != CodexClientAvailability.Available)
            {
                return CreateObservedFallbackResult(scan);
            }

            await using ICodexQuotaClient client = await _clientFactory
                .CreateAsync(cancellationToken)
                .ConfigureAwait(false);
            await client.HandshakeAsync(cancellationToken).ConfigureAwait(false);
            CodexTokenUsageSnapshot usage = await client
                .ReadTokenUsageAsync(cancellationToken)
                .ConfigureAwait(false);
            UsageEvent[] events = CreateOfficialEvents(
                usage,
                scan,
                out bool usesRecentLocalTotals);
            return events.Length == 0
                ? new UsageSourceReadResult(
                    [],
                    UsageSourceReadStatus.NoData,
                    UsageSourceIssueKind.Empty)
                : usesRecentLocalTotals && scan.Status == UsageSourceReadStatus.Partial
                    ? new UsageSourceReadResult(
                        events,
                        UsageSourceReadStatus.Partial,
                        scan.Issue)
                    : new UsageSourceReadResult(events, UsageSourceReadStatus.Complete);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is CodexClientUnavailableException
                                           or CodexProtocolException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or ObjectDisposedException)
        {
            return CreateObservedFallbackResult(scan);
        }
    }

    private UsageEvent[] CreateOfficialEvents(
        CodexTokenUsageSnapshot usage,
        ScanResult scan,
        out bool usesRecentLocalTotals)
    {
        TimeZoneInfo groupingTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            _groupingTimeZoneId);
        Dictionary<DateOnly, ModelSample[]> samplesByDate = scan.Sessions
            .Where(session => session.Candidate.SampleTokens.Total > 0)
            .GroupBy(session => DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(
                    session.Candidate.Timestamp,
                    groupingTimeZone).DateTime))
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(session => session.Candidate.Model, StringComparer.Ordinal)
                    .Select(group => CreateModelSample(group, useCumulativeTokens: false))
                    .Where(sample => sample.Tokens.Total > 0)
                    .OrderBy(sample => sample.Model, StringComparer.Ordinal)
                    .ToArray());
        Dictionary<DateOnly, ModelSample[]> localTotalsByDate = scan.UsesCheckpoints
            ? scan.RecentSamples
                .GroupBy(sample => sample.Date)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(sample => sample.Sample)
                        .Where(sample => sample.Tokens.Total > 0)
                        .OrderBy(sample => sample.Model, StringComparer.Ordinal)
                        .ToArray())
            : scan.Sessions
                .Where(session => session.Candidate.TotalTokens.Total > 0)
                .GroupBy(session => DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(
                        session.Candidate.Timestamp,
                        groupingTimeZone).DateTime))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .GroupBy(session => session.Candidate.Model, StringComparer.Ordinal)
                        .Select(group => CreateModelSample(group, useCumulativeTokens: true))
                        .Where(sample => sample.Tokens.Total > 0)
                        .OrderBy(sample => sample.Model, StringComparer.Ordinal)
                        .ToArray());

        var tokensByDate = new SortedDictionary<DateOnly, long>();
        foreach (CodexUsageDailyBucket bucket in usage.DailyUsageBuckets)
        {
            tokensByDate[bucket.StartDate] = checked(
                tokensByDate.GetValueOrDefault(bucket.StartDate) + bucket.Tokens);
        }

        var events = new List<UsageEvent>();
        usesRecentLocalTotals = false;
        DateOnly? latestOfficialDate = tokensByDate.Count == 0
            ? null
            : tokensByDate.Keys.Max();
        foreach ((DateOnly date, long totalTokens) in tokensByDate)
        {
            if (date == latestOfficialDate
                && localTotalsByDate.TryGetValue(date, out ModelSample[]? latestLocalSamples)
                && SumTokens(latestLocalSamples) > totalTokens)
            {
                AddLocalEvents(events, date, latestLocalSamples, groupingTimeZone);
                usesRecentLocalTotals = true;
                continue;
            }

            if (totalTokens == 0)
            {
                continue;
            }

            if (!samplesByDate.TryGetValue(date, out ModelSample[]? samples)
                || samples.Length == 0)
            {
                events.Add(CreateOfficialEvent(
                    date,
                    "codex-account",
                    new TokenBreakdown(totalTokens, 0, 0, 0, 0),
                    groupingTimeZone,
                    CostObservation.Unavailable()));
                continue;
            }

            long[] modelTotals = Allocate(
                totalTokens,
                samples.Select(sample => sample.Tokens.Total).ToArray());
            for (int index = 0; index < samples.Length; index++)
            {
                if (modelTotals[index] == 0)
                {
                    continue;
                }

                events.Add(CreateOfficialEvent(
                    date,
                    samples[index].Model,
                    ScaleTokens(samples[index].Tokens, modelTotals[index]),
                    groupingTimeZone,
                    ScaleSampleCost(samples[index], modelTotals[index])));
            }
        }

        foreach ((DateOnly date, ModelSample[] samples) in localTotalsByDate
                     .Where(item => latestOfficialDate is null || item.Key > latestOfficialDate)
                     .OrderBy(item => item.Key))
        {
            AddLocalEvents(events, date, samples, groupingTimeZone);
            usesRecentLocalTotals = true;
        }

        return events.ToArray();
    }

    private void AddLocalEvents(
        List<UsageEvent> events,
        DateOnly date,
        IEnumerable<ModelSample> samples,
        TimeZoneInfo groupingTimeZone)
    {
        foreach (ModelSample sample in samples)
        {
            events.Add(CreateOfficialEvent(
                date,
                sample.Model,
                sample.Tokens,
                groupingTimeZone,
                sample.Cost));
        }
    }

    private UsageEvent CreateOfficialEvent(
        DateOnly date,
        string model,
        TokenBreakdown tokens,
        TimeZoneInfo groupingTimeZone,
        CostObservation cost)
    {
        DateTime localNoon = DateTime.SpecifyKind(
            date.ToDateTime(new TimeOnly(12, 0)),
            DateTimeKind.Unspecified);
        DateTimeOffset timestamp = new(
            TimeZoneInfo.ConvertTimeToUtc(localNoon, groupingTimeZone),
            TimeSpan.Zero);
        return new UsageEvent(
            new UsageEventKey(Hash(
                $"codex-account\0{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\0{model}")),
            AgentId,
            new ModelProviderId("openai"),
            CreateModelId(model),
            timestamp,
            _groupingTimeZoneId,
            tokens,
            cost,
            ParserVersion,
            cost.Kind == CostKind.CatalogEstimated
                ? CoverageKind.Partial
                : CoverageKind.Unpriced);
    }

    private static ModelSample CreateModelSample(
        IGrouping<string, ScannedSession> modelGroup,
        bool useCumulativeTokens)
    {
        ScannedSession[] sessions = modelGroup.ToArray();
        TokenBreakdown tokens = SumTokens(
            sessions.Select(value => useCumulativeTokens
                ? value.Candidate.TotalTokens
                : value.Candidate.SampleTokens));
        CostObservation[] costs = sessions
            .Select(value => CodexPricingCatalog.Resolve(
                modelGroup.Key,
                useCumulativeTokens
                    ? value.Candidate.TotalTokens
                    : value.Candidate.SampleTokens))
            .ToArray();
        if (costs.Any(cost => cost.Kind != CostKind.CatalogEstimated))
        {
            return new ModelSample(modelGroup.Key, tokens, CostObservation.Unavailable());
        }

        decimal totalCost = costs.Sum(cost => cost.EstimatedCostUsd ?? 0m);
        return new ModelSample(
            modelGroup.Key,
            tokens,
            CostObservation.CatalogEstimated(
                decimal.Round(totalCost, 6, MidpointRounding.AwayFromZero),
                CodexPricingCatalog.Version,
            costs[0].ExactPriceMatch!));
    }

    private static ModelSample CreateModelSample(string model, TokenBreakdown tokens) =>
        new(model, tokens, CodexPricingCatalog.Resolve(model, tokens));

    private static long SumTokens(IEnumerable<ModelSample> samples)
    {
        long total = 0;
        foreach (ModelSample sample in samples)
        {
            total = checked(total + sample.Tokens.Total);
        }

        return total;
    }

    private static CostObservation ScaleSampleCost(ModelSample sample, long totalTokens)
    {
        if (sample.Cost.Kind != CostKind.CatalogEstimated
            || sample.Cost.EstimatedCostUsd is not decimal sampleCost
            || sample.Tokens.Total == 0)
        {
            return CostObservation.Unavailable();
        }

        decimal scaled = decimal.Round(
            sampleCost * totalTokens / sample.Tokens.Total,
            6,
            MidpointRounding.AwayFromZero);
        return CostObservation.CatalogEstimated(
            scaled,
            sample.Cost.CatalogVersion!,
            sample.Cost.ExactPriceMatch!);
    }

    private static TokenBreakdown ScaleTokens(TokenBreakdown sample, long total)
    {
        long[] values = Allocate(
            total,
            [
                sample.Input,
                sample.Output,
                sample.Reasoning,
                sample.CacheRead,
                sample.CacheWrite,
            ]);
        return new TokenBreakdown(values[0], values[1], values[2], values[3], values[4]);
    }

    private static long[] Allocate(long total, long[] weights)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        if (weights.Length == 0 || weights.Any(weight => weight < 0))
        {
            throw new ArgumentException("Allocation weights must be non-negative.", nameof(weights));
        }

        long weightTotal = 0;
        foreach (long weight in weights)
        {
            weightTotal = checked(weightTotal + weight);
        }
        if (weightTotal == 0)
        {
            var emptyWeights = new long[weights.Length];
            emptyWeights[0] = total;
            return emptyWeights;
        }

        var result = new long[weights.Length];
        var remainders = new decimal[weights.Length];
        long allocated = 0;
        for (int index = 0; index < weights.Length; index++)
        {
            decimal exact = total * ((decimal)weights[index] / weightTotal);
            result[index] = decimal.ToInt64(decimal.Truncate(exact));
            remainders[index] = exact - result[index];
            allocated = checked(allocated + result[index]);
        }

        long remaining = total - allocated;
        foreach (int index in Enumerable.Range(0, weights.Length)
                     .OrderByDescending(index => remainders[index])
                     .ThenBy(index => index)
                     .Take(checked((int)remaining)))
        {
            result[index]++;
        }

        return result;
    }

    private static TokenBreakdown SumTokens(IEnumerable<TokenBreakdown> values)
    {
        long input = 0;
        long output = 0;
        long reasoning = 0;
        long cacheRead = 0;
        long cacheWrite = 0;
        foreach (TokenBreakdown value in values)
        {
            input = checked(input + value.Input);
            output = checked(output + value.Output);
            reasoning = checked(reasoning + value.Reasoning);
            cacheRead = checked(cacheRead + value.CacheRead);
            cacheWrite = checked(cacheWrite + value.CacheWrite);
        }

        return new TokenBreakdown(input, output, reasoning, cacheRead, cacheWrite);
    }

    private UsageSourceReadResult CreateFallbackResult(ScanResult scan) =>
        new(
            scan.Sessions
                .Select(session => CreateEvent(
                    session.SessionIdentity,
                    session.Candidate,
                    session.Candidate.TotalTokens))
                .ToArray(),
            scan.Status,
            scan.Issue);

    private UsageSourceReadResult CreateObservedFallbackResult(ScanResult scan)
    {
        UsageEvent[] events = scan.Sessions
            .Select(session => CreateEvent(
                $"sample\0{session.SessionIdentity}",
                session.Candidate,
                session.Candidate.SampleTokens))
            .ToArray();
        return events.Length == 0
            ? new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                scan.Issue is UsageSourceIssueKind.RootUnavailable
                    ? UsageSourceIssueKind.RootUnavailable
                    : UsageSourceIssueKind.Empty)
            : new UsageSourceReadResult(
                events,
                UsageSourceReadStatus.Partial,
                scan.Issue is UsageSourceIssueKind.UnsupportedSchema
                    ? UsageSourceIssueKind.UnsupportedSchema
                    : UsageSourceIssueKind.PartialScan);
    }

    private UsageEvent CreateEvent(
        string sessionIdentity,
        Candidate candidate,
        TokenBreakdown tokens)
    {
        CostObservation cost = CodexPricingCatalog.Resolve(candidate.Model, tokens);
        return new UsageEvent(
            new UsageEventKey(Hash($"codex\0{sessionIdentity}")),
            AgentId,
            new ModelProviderId("openai"),
            CreateModelId(candidate.Model),
            candidate.Timestamp,
            _groupingTimeZoneId,
            tokens,
            cost,
            ParserVersion,
            cost.Kind == CostKind.CatalogEstimated
                ? CoverageKind.Partial
                : CoverageKind.Unpriced);
    }

    private static bool MarkSchemaFailure(LocalScanState state, bool mark)
    {
        if (mark)
        {
            state.UnsupportedSchema = true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateJsonlFiles(
        string root,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] files;
            string[] children;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                children = Directory.EnumerateDirectories(directory)
                    .OrderDescending(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Security.SecurityException)
            {
                state.MarkPartial();
                continue;
            }

            foreach (string file in files)
            {
                bool include = false;
                try
                {
                    include = (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0;
                }
                catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or System.Security.SecurityException)
                {
                    state.MarkPartial();
                }

                if (include)
                {
                    yield return file;
                }
                else
                {
                    state.MarkPartial();
                }
            }

            foreach (string child in children)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                    else
                    {
                        state.MarkPartial();
                    }
                }
                catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or System.Security.SecurityException)
                {
                    state.MarkPartial();
                }
            }
        }
    }

    private static ScanResult CreateScanResult(
        List<ScannedSession> sessions,
        CodexUsageCheckpointState? checkpoints,
        LocalScanState state)
    {
        UsageSourceReadStatus status = state.IsPartial
            ? UsageSourceReadStatus.Partial
            : sessions.Count == 0
                ? UsageSourceReadStatus.NoData
                : UsageSourceReadStatus.Complete;
        UsageSourceIssueKind? issue = status switch
        {
            UsageSourceReadStatus.Partial when state.UnsupportedSchema =>
                UsageSourceIssueKind.UnsupportedSchema,
            UsageSourceReadStatus.Partial => UsageSourceIssueKind.PartialScan,
            UsageSourceReadStatus.NoData => UsageSourceIssueKind.Empty,
            _ => null,
        };
        DatedModelSample[] recentSamples = checkpoints is null
            ? []
            : checkpoints.Files.Values
                .SelectMany(checkpoint => checkpoint.Daily)
                .GroupBy(item => item.Key)
                .Select(group => new DatedModelSample(
                    group.Key.Date,
                    CreateModelSample(group.Key.Model, SumTokens(group.Select(item => item.Value)))))
                .OrderBy(sample => sample.Date)
                .ThenBy(sample => sample.Sample.Model, StringComparer.Ordinal)
                .ToArray();
        return new ScanResult(
            sessions,
            recentSamples,
            UsesCheckpoints: checkpoints is not null,
            status,
            issue ?? UsageSourceIssueKind.None);
    }

    private string[] SessionRoots() =>
    [
        Path.Combine(_codexHome, "sessions"),
        Path.Combine(_codexHome, "archived_sessions"),
    ];

    private static string ResolveHome(string? configured, string userHome)
    {
        string raw = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(userHome, ".codex")
            : configured.Trim();
        if (raw == "~")
        {
            raw = userHome;
        }
        else if (raw.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                 || raw.StartsWith($"~{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            raw = Path.Combine(userHome, raw[2..]);
        }

        try
        {
            return Path.GetFullPath(raw);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return Path.GetFullPath(Path.Combine(userHome, ".codex"));
        }
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out JsonElement property)
               && property.ValueKind == JsonValueKind.String
               && !string.IsNullOrWhiteSpace(value = property.GetString());
    }

    private static bool TryGetUtcTimestamp(
        JsonElement element,
        string propertyName,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        return TryGetString(element, propertyName, out string? text)
               && DateTimeOffset.TryParse(
                   text,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out timestamp)
               && timestamp.Offset == TimeSpan.Zero;
    }

    private static bool TryGetNonNegativeInt64(
        JsonElement element,
        string propertyName,
        out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out JsonElement property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt64(out value)
               && value >= 0;
    }

    private static bool TryGetOptionalNonNegativeInt64(
        JsonElement element,
        string propertyName,
        out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.Number
               && property.TryGetInt64(out value)
               && value >= 0;
    }

    private static string? NormalizeModel(string? model) =>
        string.IsNullOrWhiteSpace(model) ? null : model.Trim().ToLowerInvariant();

    private static string SessionIdentity(string path) =>
        Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

    private static ModelId CreateModelId(string model)
    {
        try
        {
            return new ModelId(model);
        }
        catch (ArgumentException)
        {
            return new ModelId($"unknown-{Hash(model)[..16]}");
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record SessionFile(string Path, string SessionIdentity, string? Model);

    private sealed record ScanResult(
        IReadOnlyList<ScannedSession> Sessions,
        IReadOnlyList<DatedModelSample> RecentSamples,
        bool UsesCheckpoints,
        UsageSourceReadStatus Status,
        UsageSourceIssueKind Issue);

    private sealed record ScannedSession(string SessionIdentity, Candidate Candidate);

    private sealed record ModelSample(
        string Model,
        TokenBreakdown Tokens,
        CostObservation Cost);

    private sealed record DatedModelSample(DateOnly Date, ModelSample Sample);

    private sealed record Candidate(
        DateTimeOffset Timestamp,
        string Model,
        TokenBreakdown TotalTokens,
        TokenBreakdown SampleTokens);
}
