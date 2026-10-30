using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Claude;

public sealed class ClaudeUsageEventSource :
    IHistoryPreservingUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "claude-jsonl/4";
    private readonly string _homeDirectory;
    private readonly string? _configDirectoryOverride;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;
    private readonly long _maximumReadBytes;
    private readonly bool _includeHistoricalFiles;
    private readonly string? _checkpointPath;
    private readonly ClaudeUsageCheckpointStore _checkpoints;
    private readonly TimeProvider _clock;

    public ClaudeUsageScanProgress LastScanProgress { get; private set; } = new(0, 0, 0, 0, 0, false);

    public ClaudeUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? configDirectoryOverride = null,
        int maximumFiles = 10_000,
        long maximumFileBytes = 64 * 1024 * 1024,
        int maximumLineCharacters = 8 * 1024 * 1024,
        string? checkpointPath = null,
        long maximumReadBytes = 256 * 1024 * 1024,
        bool includeHistoricalFiles = false,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);

        _groupingTimeZoneId = groupingTimeZoneId;
        _homeDirectory = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _configDirectoryOverride = configDirectoryOverride
            ?? Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        _budget = new LocalScanBudget(maximumFiles, maximumFileBytes, maximumLineCharacters);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumReadBytes, 1);
        _maximumReadBytes = maximumReadBytes;
        _includeHistoricalFiles = includeHistoricalFiles;
        _clock = clock ?? TimeProvider.System;
        _checkpointPath = checkpointPath;
        _checkpoints = new ClaudeUsageCheckpointStore(checkpointPath, groupingTimeZoneId);
    }

    public SourceKind SourceKind => SourceKind.LocalLog;

    public AgentId AgentId { get; } = new("claude");

    public string EventParserVersion => ParserVersion;

    public int ReconciliationWindowDays => UsagePeriodPolicy.ReconciliationDays;

    public bool IsRootAvailable => ClaudeConfigLocator.FindProjectDirectories(
        _homeDirectory,
        _configDirectoryOverride).Count > 0;

    public ClaudeUsageEventSource CreateHistorySource(bool dryRun = false, TimeProvider? clock = null) => new(
        _groupingTimeZoneId, _homeDirectory, _configDirectoryOverride,
        _budget.MaximumFiles, _budget.MaximumFileBytes, _budget.MaximumLineBytes,
        dryRun ? null : _checkpointPath, _maximumReadBytes, includeHistoricalFiles: true, clock: clock ?? _clock);

    public async Task<UsageSourceReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        var prepared = await _checkpoints.PrepareAsync(
            state => ReadCore(state, cancellationToken), cancellationToken).ConfigureAwait(false);
        return prepared.Result with { Checkpoint = prepared.Checkpoint };
    }

    private UsageSourceReadResult ReadCore(CheckpointState state, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> roots = ClaudeConfigLocator.FindProjectDirectories(
            _homeDirectory, _configDirectoryOverride);
        if (roots.Count == 0)
        {
            LastScanProgress = new(0, 0, 0, 0, 0, false);
            return new([], UsageSourceReadStatus.NoData, UsageSourceIssueKind.RootUnavailable);
        }

        var scan = new LocalScanState(_budget);
        string[] files = roots.SelectMany(root => EnumerateJsonlFiles(root, scan, cancellationToken))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        int start = Array.FindIndex(files, file => Hash(Path.GetFullPath(file)) == state.NextFileHash);
        if (start < 0) start = 0;
        var changed = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        long bytesRead = 0;
        long advanced = 0;
        int excluded = 0;
        int pending = 0;
        int visited = 0;
        bool readErrors = scan.IsPartial;
        for (; visited < files.Length; visited++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited >= _budget.MaximumFiles || bytesRead >= _maximumReadBytes) break;
            string path = files[(start + visited) % files.Length];
            FileReadProgress result = ReadFile(path, state, changed,
                Math.Min(_budget.MaximumFileBytes, _maximumReadBytes - bytesRead), cancellationToken);
            bytesRead += result.BytesRead;
            advanced += result.BytesAdvanced;
            excluded += result.ExcludedRecords;
            readErrors |= result.ReadError;
        }
        foreach (string path in files)
        {
            try
            {
                var info = new FileInfo(path);
                if (!_includeHistoricalFiles && info.LastWriteTimeUtc < _clock.GetUtcNow().ToUniversalTime().AddDays(-ReconciliationWindowDays))
                    continue;
                if (!state.Files.TryGetValue(Hash(Path.GetFullPath(path)), out FileCheckpoint? file)
                    || !file.ReadToEnd || file.ObservedLength != info.Length
                    || file.CreationTicks != info.CreationTimeUtc.Ticks || file.Offset > info.Length) pending++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or System.Security.SecurityException) { pending++; readErrors = true; }
        }
        state.NextFileHash = files.Length == 0 ? null : Hash(Path.GetFullPath(files[(start + visited) % files.Length]));
        foreach (string key in state.Messages.Where(pair => pair.Value.OccurredAtUtc < _clock.GetUtcNow().ToUniversalTime().AddDays(-400))
                     .Select(pair => pair.Key).ToArray()) state.Messages.Remove(key);
        foreach (var pair in state.Messages.Where(pair => pair.Value.AwaitingClock
                     && pair.Value.OccurredAtUtc <= _clock.GetUtcNow()).ToArray())
        {
            Candidate eligible = pair.Value with { AwaitingClock = false };
            state.Messages[pair.Key] = eligible;
            changed[pair.Key] = eligible;
        }
        int unreadableRecords = state.Files.Values.Sum(file => file.UnreadableRecords);
        int futureMessages = state.Messages.Values.Count(candidate => candidate.AwaitingClock);
        readErrors |= unreadableRecords > 0;
        LastScanProgress = new(bytesRead, advanced, visited, pending, excluded, readErrors)
        { RetainedUnreadableRecords = unreadableRecords, DeferredFutureMessages = futureMessages };
        List<UsageEvent> events = CreateEvents(changed.Values.OrderBy(candidate => candidate.OccurredAtUtc)
            .ThenBy(candidate => candidate.SourceOrdinal, StringComparer.Ordinal));
        bool partial = pending > 0 || readErrors;
        return new(events, partial ? UsageSourceReadStatus.Partial
            : events.Count == 0 && state.Messages.Count == 0
                ? UsageSourceReadStatus.NoData : UsageSourceReadStatus.Complete);
    }

    private FileReadProgress ReadFile(string path, CheckpointState state,
        Dictionary<string, Candidate> changed, long limit, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new(0, 0, 0, true);
            if (!_includeHistoricalFiles && info.LastWriteTimeUtc < _clock.GetUtcNow().ToUniversalTime().AddDays(-ReconciliationWindowDays))
                return new(0, 0, 0, false);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            string key = Hash(Path.GetFullPath(path));
            state.Files.TryGetValue(key, out FileCheckpoint? file);
            if (file is null || file.CreationTicks != info.CreationTimeUtc.Ticks || file.Offset > stream.Length
                || (file.Offset > 0 && file.BoundaryHash != BoundaryHash(stream, file.Offset))
                || (file.PrefixHash is not null && (stream.Length < file.PrefixLength
                    || file.PrefixHash != PrefixHash(stream, file.PrefixLength))))
            {
                file = new FileCheckpoint { CreationTicks = info.CreationTimeUtc.Ticks };
                state.Files[key] = file;
            }
            if (file.PrefixHash is null || file.PrefixLength == 0 && stream.Length > 0)
            {
                file.PrefixLength = (int)Math.Min(64, stream.Length);
                file.PrefixHash = PrefixHash(stream, file.PrefixLength);
            }
            long initial = file.Offset;
            stream.Position = initial;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            var line = new ArrayBufferWriter<byte>();
            long read = 0;
            long position = initial;
            bool skipping = file.SkippingLongLine;
            int excluded = 0;
            bool readError = false;
            try
            {
                while (read < limit)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, limit - read));
                    if (count == 0) break;
                    read += count;
                    ReadOnlySpan<byte> remaining = buffer.AsSpan(0, count);
                    while (!remaining.IsEmpty)
                    {
                        int newline = remaining.IndexOf((byte)'\n');
                        ReadOnlySpan<byte> segment = newline >= 0 ? remaining[..newline] : remaining;
                        if (!skipping && line.WrittenCount + segment.Length <= _budget.MaximumLineBytes)
                            line.Write(segment);
                        else if (!skipping)
                        {
                            skipping = true;
                            line.Clear();
                            excluded++;
                            file.UnreadableRecords++;
                            readError = true;
                        }
                        position += segment.Length + (newline >= 0 ? 1 : 0);
                        if (newline >= 0)
                        {
                            file.LineNumber++;
                            if (!skipping)
                            {
                                var parsed = new List<Candidate>();
                                bool valid = ProcessLine(path, file.LineNumber, line.WrittenMemory, false, parsed);
                                if (!valid) { excluded++; file.UnreadableRecords++; readError = true; }
                                MergeCandidates(parsed, state, changed);
                            }
                            line.Clear();
                            skipping = false;
                            file.Offset = position;
                            file.SkippingLongLine = false;
                        }
                        else if (skipping)
                        {
                            file.Offset = position;
                            file.SkippingLongLine = true;
                        }
                        if (newline < 0) break;
                        remaining = remaining[(newline + 1)..];
                    }
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            if (!skipping && position == stream.Length && line.WrittenCount > 0)
            {
                var parsed = new List<Candidate>();
                if (ProcessLine(path, file.LineNumber + 1, line.WrittenMemory, false, parsed))
                    MergeCandidates(parsed, state, changed);
            }
            file.BoundaryHash = BoundaryHash(stream, file.Offset);
            // An unfinished final record stays behind the cursor until its newline arrives.
            file.ObservedLength = stream.Length;
            file.ReadToEnd = position >= file.ObservedLength;
            return new(read, Math.Max(0, file.Offset - initial), excluded, readError);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or System.Security.SecurityException)
        {
            return new(0, 0, 0, true);
        }
    }

    private void MergeCandidates(IEnumerable<Candidate> candidates, CheckpointState state,
        Dictionary<string, Candidate> changed)
    {
        foreach (Candidate candidate in candidates)
        {
            string identity = CreateEventKey(candidate).Value;
            Candidate numeric = candidate with
            { MessageId = null, EventIdentity = identity, AwaitingClock = candidate.OccurredAtUtc > _clock.GetUtcNow() };
            Candidate merged = state.Messages.TryGetValue(identity, out Candidate? previous)
                ? MergeStreaming(previous, numeric) : numeric;
            if (previous != merged) changed[identity] = merged;
            state.Messages[identity] = merged;
        }
    }

    private static string PrefixHash(FileStream stream, int length)
    {
        stream.Position = 0;
        Span<byte> bytes = stackalloc byte[length];
        stream.ReadExactly(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string BoundaryHash(FileStream stream, long offset)
    {
        stream.Position = Math.Max(0, offset - 64);
        Span<byte> bytes = stackalloc byte[(int)Math.Min(64, offset)];
        stream.ReadExactly(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    internal sealed class CheckpointState
    {
        public CheckpointState() { }
        public int SchemaVersion { get; set; } = 1;
        public string Parser { get; set; } = ParserVersion;
        public string TimeZone { get; set; } = string.Empty;
        public string? NextFileHash { get; set; }
        public Dictionary<string, FileCheckpoint> Files { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, Candidate> Messages { get; set; } = new(StringComparer.Ordinal);
    }

    internal sealed class FileCheckpoint
    {
        public FileCheckpoint() { }
        public long CreationTicks { get; set; }
        public long Offset { get; set; }
        public long ObservedLength { get; set; }
        public bool ReadToEnd { get; set; }
        public int PrefixLength { get; set; }
        public string? PrefixHash { get; set; }
        public int UnreadableRecords { get; set; }
        public int LineNumber { get; set; }
        public bool SkippingLongLine { get; set; }
        public string? BoundaryHash { get; set; }
    }

    private sealed record FileReadProgress(long BytesRead, long BytesAdvanced,
        int ExcludedRecords, bool ReadError);

    private static bool ProcessLine(
        string path,
        int lineNumber,
        ReadOnlyMemory<byte> utf8,
        bool lineTooLong,
        List<Candidate> candidates)
    {
        ReadOnlySpan<byte> bytes = utf8.Span;
        if (lineTooLong)
        {
            return false;
        }

        if (lineNumber == 1
            && bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF)
        {
            utf8 = utf8[3..];
            bytes = utf8.Span;
        }

        if (!bytes.IsEmpty && bytes[^1] == (byte)'\r')
        {
            utf8 = utf8[..^1];
            bytes = utf8.Span;
        }

        if (bytes.IndexOf("\"usage\""u8) < 0)
        {
            return true;
        }

        if (TryParse(
                utf8,
                CreateSourceOrdinal(path, lineNumber),
                out IReadOnlyList<Candidate>? parsedCandidates)
            && parsedCandidates is not null)
        {
            candidates.AddRange(parsedCandidates);
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateJsonlFiles(
        string root,
        LocalScanState scanState,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> files;
            IEnumerable<string> directories;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                directories = Directory.EnumerateDirectories(directory)
                    .OrderDescending(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Security.SecurityException)
            {
                scanState.IsPartial = true;
                continue;
            }

            foreach (string file in files)
            {
                bool include = false;
                try
                {
                    include = (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0;
                    scanState.IsPartial |= !include;
                }
                catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or System.Security.SecurityException)
                {
                    scanState.IsPartial = true;
                }

                if (include)
                {
                    yield return file;
                }
            }

            foreach (string child in directories)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
                catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or System.Security.SecurityException)
                {
                    // Skip a directory that changed during enumeration.
                    scanState.IsPartial = true;
                }
            }
        }
    }

    private List<UsageEvent> CreateEvents(IEnumerable<Candidate> candidates)
    {
        var events = new List<UsageEvent>();
        foreach (Candidate candidate in candidates)
        {
            string normalizedModel = ModelIdentity.ForStorage(candidate.Model);
            CostObservation cost = ClaudePricingCatalog.Resolve(
                normalizedModel,
                candidate.OccurredAtUtc,
                candidate.Tokens,
                candidate.CacheWrite5Minutes,
                candidate.CacheWrite1Hour,
                candidate.ReportedCostUsd,
                candidate.IsFast);
            CoverageKind coverage = cost.Kind switch
            {
                CostKind.ProviderReported => CoverageKind.Complete,
                CostKind.CatalogEstimated => CoverageKind.Partial,
                _ => CoverageKind.Unpriced,
            };
            events.Add(new UsageEvent(
                CreateEventKey(candidate),
                new AgentId("claude"),
                new ModelProviderId("anthropic"),
                ModelIdentity.ToModelId(normalizedModel),
                candidate.OccurredAtUtc,
                _groupingTimeZoneId,
                candidate.Tokens,
                cost,
                ParserVersion,
                coverage,
                // Each assistant message carries its own timestamp, so reset-cycle totals can place it.
                timePrecision: UsageTimePrecision.Timestamp,
                detailMetadata: candidate.DetailMetadata));
        }

        return events;
    }

    private static Candidate MergeStreaming(Candidate current, Candidate candidate)
    {
        if (candidate.IsSidechain != current.IsSidechain)
        {
            Candidate preferred = candidate.IsSidechain ? current : candidate;
            return preferred with
            {
                OccurredAtUtc = current.OccurredAtUtc <= candidate.OccurredAtUtc
                    ? current.OccurredAtUtc
                    : candidate.OccurredAtUtc,
            };
        }

        return current with
        {
            OccurredAtUtc = current.OccurredAtUtc <= candidate.OccurredAtUtc
                ? current.OccurredAtUtc
                : candidate.OccurredAtUtc,
            Tokens = new TokenBreakdown(
                Math.Max(current.Tokens.Input, candidate.Tokens.Input),
                Math.Max(current.Tokens.Output, candidate.Tokens.Output),
                Math.Max(current.Tokens.Reasoning, candidate.Tokens.Reasoning),
                Math.Max(current.Tokens.CacheRead, candidate.Tokens.CacheRead),
                Math.Max(current.Tokens.CacheWrite, candidate.Tokens.CacheWrite)),
            CacheWrite5Minutes = Math.Max(current.CacheWrite5Minutes, candidate.CacheWrite5Minutes),
            CacheWrite1Hour = Math.Max(current.CacheWrite1Hour, candidate.CacheWrite1Hour),
            ReportedCostUsd = MaxCost(current.ReportedCostUsd, candidate.ReportedCostUsd),
            IsFast = current.IsFast || candidate.IsFast,
        };
    }

    private static decimal? MaxCost(decimal? left, decimal? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }

    private static bool TryParse(
        ReadOnlyMemory<byte> utf8,
        string sourceOrdinal,
        out IReadOnlyList<Candidate>? candidates)
    {
        candidates = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!TryGetString(root, "type", out string? type)
                || !string.Equals(type, "assistant", StringComparison.Ordinal))
            {
                candidates = [];
                return true;
            }
            if (!TryGetString(root, "timestamp", out string? timestampText)
                || !DateTimeOffset.TryParse(
                    timestampText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset timestamp)
                || timestamp.Offset != TimeSpan.Zero
                || !root.TryGetProperty("message", out JsonElement message)
                || message.ValueKind != JsonValueKind.Object
                || !TryGetString(message, "model", out string? model)
                || string.IsNullOrWhiteSpace(model))
            {
                return false;
            }
            if (string.Equals(model, "<synthetic>", StringComparison.OrdinalIgnoreCase))
            {
                candidates = [];
                return true;
            }
            if (!message.TryGetProperty("usage", out JsonElement usage)
                || usage.ValueKind != JsonValueKind.Object
                || !TryGetNonNegativeInt64(usage, "input_tokens", out long input)
                || !TryGetNonNegativeInt64(usage, "output_tokens", out long output))
            {
                return false;
            }

            long cacheRead = GetNonNegativeInt64OrZero(usage, "cache_read_input_tokens");
            long legacyWrite = GetNonNegativeInt64OrZero(
                usage,
                "cache_creation_input_tokens");
            (long cacheWrite5Minutes, long cacheWrite1Hour) = ReadCacheCreation(usage, legacyWrite);

            decimal? reportedCost = null;
            if (root.TryGetProperty("costUSD", out JsonElement costElement)
                && costElement.ValueKind == JsonValueKind.Number
                && costElement.TryGetDecimal(out decimal parsedCost)
                && parsedCost >= 0
                && parsedCost <= long.MaxValue / 1_000_000m)
            {
                reportedCost = parsedCost;
            }

            bool isFast = false;
            if (usage.TryGetProperty("speed", out JsonElement speedElement))
            {
                if (speedElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                string? speed = speedElement.GetString();
                if (!string.Equals(speed, "standard", StringComparison.Ordinal)
                    && !string.Equals(speed, "fast", StringComparison.Ordinal))
                {
                    return false;
                }

                isFast = string.Equals(speed, "fast", StringComparison.Ordinal);
            }
            string? messageId = TryGetString(message, "id", out string? parsedMessageId)
                ? parsedMessageId
                : null;
            var mainCandidate = new Candidate(
                timestamp.ToUniversalTime(),
                model,
                new TokenBreakdown(
                    input,
                    output,
                    reasoning: 0,
                    cacheRead,
                    checked(cacheWrite5Minutes + cacheWrite1Hour)),
                cacheWrite5Minutes,
                cacheWrite1Hour,
                messageId,
                root.TryGetProperty("isSidechain", out JsonElement sidechain)
                    && sidechain.ValueKind is JsonValueKind.True,
                reportedCost,
                isFast,
                sourceOrdinal,
                ReadComponentAvailability(usage));
            var parsed = new List<Candidate> { mainCandidate };
            AppendAdvisorCandidates(
                parsed,
                usage,
                timestamp.ToUniversalTime(),
                model,
                messageId,
                isFast,
                sourceOrdinal);
            candidates = parsed;
            return true;
        }
        catch (Exception exception) when (exception is JsonException
                                           or ArgumentException
                                           or InvalidOperationException
                                           or OverflowException)
        {
            return false;
        }
    }

    private static void AppendAdvisorCandidates(
        List<Candidate> candidates,
        JsonElement usage,
        DateTimeOffset occurredAtUtc,
        string fallbackModel,
        string? parentMessageId,
        bool parentIsFast,
        string sourceOrdinal)
    {
        if (!usage.TryGetProperty("iterations", out JsonElement iterations)
            || iterations.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        int advisorOrdinal = 0;
        foreach (JsonElement iteration in iterations.EnumerateArray())
        {
            if (iteration.ValueKind != JsonValueKind.Object
                || !TryGetString(iteration, "type", out string? type)
                || !string.Equals(type, "advisor_message", StringComparison.Ordinal))
            {
                continue;
            }

            int ordinal = advisorOrdinal++;
            string model = TryGetString(iteration, "model", out string? advisorModel)
                ? advisorModel!
                : fallbackModel;
            long input = GetNonNegativeInt64OrZero(iteration, "input_tokens");
            long output = GetNonNegativeInt64OrZero(iteration, "output_tokens");
            long cacheRead = GetNonNegativeInt64OrZero(
                iteration,
                "cache_read_input_tokens");
            long legacyWrite = GetNonNegativeInt64OrZero(
                iteration,
                "cache_creation_input_tokens");
            (long cacheWrite5Minutes, long cacheWrite1Hour) = ReadCacheCreation(
                iteration,
                legacyWrite);
            bool isFast = ReadSpeed(iteration, parentIsFast);
            string? messageId = parentMessageId is null
                ? null
                : $"{parentMessageId}:advisor:{ordinal}";
            candidates.Add(new Candidate(
                occurredAtUtc,
                model,
                new TokenBreakdown(
                    input,
                    output,
                    reasoning: 0,
                    cacheRead,
                    checked(cacheWrite5Minutes + cacheWrite1Hour)),
                cacheWrite5Minutes,
                cacheWrite1Hour,
                messageId,
                IsSidechain: false,
                ReportedCostUsd: null,
                isFast,
                CreateAdvisorSourceOrdinal(sourceOrdinal, ordinal)));
        }
    }

    private static (long FiveMinutes, long OneHour) ReadCacheCreation(
        JsonElement usage,
        long legacyWrite)
    {
        long fiveMinutes = legacyWrite;
        long oneHour = 0;
        if (usage.TryGetProperty("cache_creation", out JsonElement cacheCreation)
            && cacheCreation.ValueKind == JsonValueKind.Object)
        {
            fiveMinutes = GetNonNegativeInt64OrZero(
                cacheCreation,
                "ephemeral_5m_input_tokens");
            oneHour = GetNonNegativeInt64OrZero(
                cacheCreation,
                "ephemeral_1h_input_tokens");
            long splitTotal = checked(fiveMinutes + oneHour);
            if (legacyWrite > splitTotal)
            {
                fiveMinutes = checked(fiveMinutes + legacyWrite - splitTotal);
            }
        }

        return (fiveMinutes, oneHour);
    }

    private static bool ReadSpeed(JsonElement usage, bool fallback)
    {
        if (!usage.TryGetProperty("speed", out JsonElement speedElement)
            || speedElement.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        return string.Equals(speedElement.GetString(), "fast", StringComparison.Ordinal);
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;
        return element.TryGetProperty(propertyName, out JsonElement property)
               && property.ValueKind == JsonValueKind.String
               && !string.IsNullOrWhiteSpace(value = property.GetString());
    }

    private static bool TryGetNonNegativeInt64(
        JsonElement element,
        string propertyName,
        out long value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out JsonElement property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt64(out value)
               && value >= 0;
    }

    private static long GetNonNegativeInt64OrZero(
        JsonElement element,
        string propertyName) =>
        TryGetNonNegativeInt64(element, propertyName, out long value) ? value : 0;

    private static UsageEventKey CreateEventKey(Candidate candidate)
    {
        if (candidate.EventIdentity is { } key) return new UsageEventKey(key);
        string identity = candidate.MessageId is not null
            ? $"claude\0{candidate.MessageId}"
            : $"claude\0{candidate.SourceOrdinal}";
        return new UsageEventKey(Hash(identity));
    }

    private static string CreateSourceOrdinal(string path, int lineNumber) =>
        Hash($"{Path.GetFullPath(path)}\0{lineNumber}");

    private static string CreateAdvisorSourceOrdinal(string sourceOrdinal, int ordinal) =>
        Hash($"{sourceOrdinal}\0advisor\0{ordinal}");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    internal sealed record Candidate(
        DateTimeOffset OccurredAtUtc,
        string Model,
        TokenBreakdown Tokens,
        long CacheWrite5Minutes,
        long CacheWrite1Hour,
        string? MessageId,
        bool IsSidechain,
        decimal? ReportedCostUsd,
        bool IsFast,
        string SourceOrdinal,
        UsageDetailMetadata? DetailMetadata = null)
    {
        public string? EventIdentity { get; init; }
        public bool AwaitingClock { get; init; }
    }

    private static UsageDetailMetadata ReadComponentAvailability(JsonElement usage)
    {
        bool legacyMeasured = TryGetNonNegativeInt64(usage, "cache_creation_input_tokens", out _);
        bool cacheWriteMeasured = usage.TryGetProperty("cache_creation", out JsonElement split)
            && split.ValueKind == JsonValueKind.Object
            ? TryGetNonNegativeInt64(split, "ephemeral_5m_input_tokens", out _)
                && TryGetNonNegativeInt64(split, "ephemeral_1h_input_tokens", out _)
                && (legacyMeasured || !usage.TryGetProperty("cache_creation_input_tokens", out _))
            : legacyMeasured;
        return new UsageDetailMetadata(
            input: UsageComponentAvailability.Measured,
            output: UsageComponentAvailability.Measured,
            reasoning: UsageComponentAvailability.Unavailable,
            cacheRead: TryGetNonNegativeInt64(usage, "cache_read_input_tokens", out _)
                ? UsageComponentAvailability.Measured : UsageComponentAvailability.Unknown,
            cacheWrite: cacheWriteMeasured ? UsageComponentAvailability.Measured : UsageComponentAvailability.Unknown);
    }

}
