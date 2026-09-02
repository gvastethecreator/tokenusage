using System.Globalization;
using System.Text.Json;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;

namespace TokenUsage.Providers.Codex;

public sealed partial class CodexUsageEventSource
{
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
                else
                {
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
                if (IsStaleRegression(current!, last, checkpoint.Previous, lastIsValid))
                {
                    return;
                }

                delta = ComputeTurnDelta(current!, last, checkpoint.Previous, lastIsValid);
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

    private static TokenBreakdown ComputeTurnDelta(
        TokenBreakdown current,
        TokenBreakdown? last,
        TokenBreakdown? previous,
        bool lastIsValid)
    {
        if (previous is null)
        {
            // A resumed session carries prior totals. last_token_usage is the
            // current turn; the cumulative snapshot is not a new charge.
            return lastIsValid ? last! : current;
        }

        if (TotalInput(current) < TotalInput(previous)
            || TotalOutput(current) < TotalOutput(previous))
        {
            // A real session reset starts a new cumulative. last_token_usage
            // is that turn. Stale replicas never reach this branch.
            return lastIsValid ? last! : new TokenBreakdown(0, 0, 0, 0, 0);
        }

        return Difference(current, previous);
    }

    private static bool IsStaleRegression(
        TokenBreakdown current,
        TokenBreakdown? last,
        TokenBreakdown? previous,
        bool lastIsValid)
    {
        if (previous is null)
        {
            return false;
        }

        if (TotalInput(current) >= TotalInput(previous)
            && TotalOutput(current) >= TotalOutput(previous))
        {
            return false;
        }

        long previousSum = SumOf(previous);
        long currentSum = SumOf(current);
        long lastSum = lastIsValid ? SumOf(last!) : 0;
        return previousSum > 0
            && (currentSum * 100 >= previousSum * 98
                || currentSum + lastSum * 2 >= previousSum);
    }

    private static long SumOf(TokenBreakdown value) =>
        checked(TotalInput(value) + TotalOutput(value));

    private DateTime StartOfDayUtc(DateOnly date)
    {
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_groupingTimeZoneId);
        DateTime local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private static bool CanSubtract(TokenBreakdown current, TokenBreakdown previous) =>
        TotalInput(current) >= TotalInput(previous)
        && TotalOutput(current) >= TotalOutput(previous);

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
            bool captureResumeCarry = startPosition == 0;
            TokenBreakdown? resumeCarry = null;
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
                    markSchemaFailures: hasNewline,
                    captureResumeCarry,
                    ref resumeCarry);
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
}
