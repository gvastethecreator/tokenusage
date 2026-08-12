using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;

namespace TokenUsage.Providers.Grok;

public sealed class GrokUsageEventSource :
    IWindowedSnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "grok-local/4";

    /// <summary>
    /// Stands in for the model of a turn the log does not name. It matches no catalog rate, so the
    /// cost of that turn stays unavailable instead of being guessed.
    /// </summary>
    public const string UnknownModel = "unknown";

    private const decimal TicksPerUsd = 10_000_000_000m;
    private const int SnapshotTailBytes = 1024 * 1024;
    private const int SnapshotFullFallbackBytes = 4 * 1024 * 1024;
    private readonly string _groupingTimeZoneId;
    private readonly string _grokHome;
    private readonly LocalScanBudget _budget;

    public GrokUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? grokHomeOverride = null,
        int maximumFiles = 10_000,
        long maximumFileBytes = 64 * 1024 * 1024,
        int maximumLineCharacters = 8 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);

        string userHome = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? configured = grokHomeOverride ?? Environment.GetEnvironmentVariable("GROK_HOME");
        _grokHome = ResolveHome(configured, userHome);
        _groupingTimeZoneId = groupingTimeZoneId;
        _budget = new LocalScanBudget(maximumFiles, maximumFileBytes, maximumLineCharacters);
    }

    public SourceKind SourceKind => SourceKind.LocalLog;

    public AgentId AgentId { get; } = new("grok");

    public string EventParserVersion => ParserVersion;

    public int ReconciliationWindowDays => UsagePeriodPolicy.ReconciliationDays;

    public bool IsRootAvailable => Directory.Exists(_grokHome);

    public async Task<UsageSourceReadResult> ReadAsync(
        CancellationToken cancellationToken = default) =>
        await Task.Run(() => ReadCore(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

    private UsageSourceReadResult ReadCore(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_grokHome))
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.RootUnavailable);
        }

        var state = new LocalScanState(_budget);
        string unifiedPath = Path.Combine(_grokHome, "logs", "unified.jsonl");
        if (File.Exists(unifiedPath))
        {
            state.FilesRead++;
            List<UsageEvent> unifiedEvents = ReadUnified(
                unifiedPath,
                DateTimeOffset.UtcNow.AddDays(-ReconciliationWindowDays),
                state,
                cancellationToken);
            // A partial unified log is still the primary lower bound when it
            // yielded events. Session snapshots can overlap those rows, so
            // merging them would risk charging the same inference twice.
            if (unifiedEvents.Count > 0)
            {
                return CreateResult(unifiedEvents, state);
            }
        }

        var sessionEvents = new List<UsageEvent>();
        string sessionsRoot = Path.Combine(_grokHome, "sessions");
        if (Directory.Exists(sessionsRoot))
        {
            foreach (string summaryPath in EnumerateNamedFiles(
                         sessionsRoot,
                         "summary.json",
                         state,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++state.FilesRead > _budget.MaximumFiles)
                {
                    state.IsPartial = true;
                    break;
                }

                string sessionDirectory = Path.GetDirectoryName(summaryPath)!;
                string updatesPath = Path.Combine(sessionDirectory, "updates.jsonl");
                if (!File.Exists(updatesPath))
                {
                    continue;
                }

                if (++state.FilesRead > _budget.MaximumFiles)
                {
                    state.IsPartial = true;
                    break;
                }

                SummaryInfo summary = ReadSummary(summaryPath, state, cancellationToken);
                Snapshot? snapshot = ReadLatestSnapshot(updatesPath, summary, state, cancellationToken);
                if (snapshot is not null)
                {
                    string sessionKey = Path.GetRelativePath(sessionsRoot, sessionDirectory)
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');
                    sessionEvents.AddRange(CreateSessionEvents(
                        sessionKey,
                        snapshot));
                }
            }
        }

        return CreateResult(sessionEvents, state);
    }

    private static UsageSourceReadResult CreateResult(
        List<UsageEvent> events,
        LocalScanState state)
    {
        UsageSourceReadStatus status = state.IsPartial
            ? UsageSourceReadStatus.Partial
            : events.Count == 0
                ? UsageSourceReadStatus.NoData
                : UsageSourceReadStatus.Complete;
        return new UsageSourceReadResult(
            events,
            status,
            status == UsageSourceReadStatus.NoData
                ? UsageSourceIssueKind.Empty
                : null);
    }

    private SummaryInfo ReadSummary(
        string path,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = ReadSmallFile(path, cancellationToken);
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            string? model = TryGetString(root, "current_model_id", out string? value)
                ? value
                : null;
            DateTimeOffset? timestamp = TryGetUtcTimestamp(root, "updated_at", out DateTimeOffset parsed)
                ? parsed
                : TryGetUtcTimestamp(root, "last_active_at", out parsed)
                    ? parsed
                    : null;
            return new SummaryInfo(model, timestamp);
        }
        catch (Exception exception) when (IsDataFailure(exception))
        {
            state.IsPartial = true;
            return new SummaryInfo(null, null);
        }
    }

    private Snapshot? ReadLatestSnapshot(
        string path,
        SummaryInfo summary,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        Snapshot? latest = null;
        bool complete = ReadRecentLines(
            path,
            (line, lineNumber) =>
            {
                if (line.Span.IndexOf("\"usage\""u8) < 0)
                {
                    return true;
                }

                if (TryParseSnapshot(line, summary, out Snapshot? parsed) && parsed is not null)
                {
                    latest = parsed;
                    return true;
                }

                return false;
            },
            cancellationToken,
            out bool readWholeFile);
        if (latest is null && !readWholeFile)
        {
            long fileLength = GetFileLength(path);
            if (fileLength >= 0 && fileLength <= SnapshotFullFallbackBytes)
            {
                complete &= ReadLines(
                    path,
                    (line, lineNumber) =>
                    {
                        if (line.Span.IndexOf("\"usage\""u8) < 0)
                        {
                            return true;
                        }

                        if (TryParseSnapshot(line, summary, out Snapshot? parsed)
                            && parsed is not null)
                        {
                            latest = parsed;
                            return true;
                        }

                        return false;
                    },
                    cancellationToken);
            }
            else
            {
                complete = false;
            }
        }

        state.IsPartial |= !complete;
        state.IsPartial |= latest?.HasInvalidModelCounters == true;
        state.IsPartial |= latest?.HasIncompleteUsage == true;
        return latest;
    }

    private List<UsageEvent> ReadUnified(
        string path,
        DateTimeOffset sinceUtc,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        var events = new List<UsageEvent>();
        var modelsByProcess = new Dictionary<long, string>();
        bool complete = ReadLines(
            path,
            (line, lineNumber) => ProcessUnifiedLine(
                line,
                lineNumber,
                modelsByProcess,
                events,
                sinceUtc),
            cancellationToken);
        state.IsPartial |= !complete;
        return events;
    }

    private bool ProcessUnifiedLine(
        ReadOnlyMemory<byte> utf8,
        int lineNumber,
        Dictionary<long, string> modelsByProcess,
        List<UsageEvent> events,
        DateTimeOffset sinceUtc)
    {
        ReadOnlySpan<byte> bytes = utf8.Span;
        if (bytes.IndexOf("inference_done"u8) < 0 && bytes.IndexOf("model"u8) < 0)
        {
            return true;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8);
            JsonElement root = document.RootElement;
            if (!TryGetString(root, "msg", out string? message))
            {
                return true;
            }

            long? processId = TryGetNonNegativeInt64(root, "pid", out long parsedProcessId)
                ? parsedProcessId
                : null;
            JsonElement context = root.TryGetProperty("ctx", out JsonElement parsedContext)
                                  && parsedContext.ValueKind == JsonValueKind.Object
                ? parsedContext
                : default;
            string? model = GetUnifiedModel(message!, context);
            if (model is not null)
            {
                if (processId is not null)
                {
                    modelsByProcess[processId.Value] = model;
                }

                return true;
            }

            if (!string.Equals(message, "shell.turn.inference_done", StringComparison.Ordinal)
                || processId is null
                || !TryGetUtcTimestamp(root, "ts", out DateTimeOffset timestamp)
                || timestamp < sinceUtc
                || !TryGetNonNegativeInt64(context, "prompt_tokens", out long input))
            {
                return true;
            }

            // A turn whose process announced its model before the retained part of the log has
            // real tokens and no name for them. Dropping it hid about one turn in twenty from the
            // total, so it is counted under an unnamed model, which prices as unavailable.
            if (!modelsByProcess.TryGetValue(processId.Value, out model))
            {
                model = UnknownModel;
            }

            long cacheRead = GetNonNegativeInt64OrZero(context, "cached_prompt_tokens");
            long output = GetNonNegativeInt64OrZero(context, "completion_tokens");
            long reasoning = GetNonNegativeInt64OrZero(context, "reasoning_tokens");
            cacheRead = Math.Min(cacheRead, input);
            TokenBreakdown tokens = new(input - cacheRead, output, reasoning, cacheRead, 0);
            events.Add(CreateEvent(
                $"grok-unified\0{lineNumber}",
                model,
                timestamp,
                tokens,
                GrokPricingCatalog.Resolve(model, tokens)));
            return true;
        }
        catch (Exception exception) when (IsDataFailure(exception))
        {
            return false;
        }
    }

    private IEnumerable<UsageEvent> CreateSessionEvents(string sessionKey, Snapshot snapshot)
    {
        if (snapshot.Models.Count > 0)
        {
            foreach ((string model, Counters counters) in snapshot.Models.OrderBy(
                         pair => pair.Key,
                         StringComparer.Ordinal))
            {
                CostObservation cost = counters.CostUsdTicks is long modelTicks
                    ? CostObservation.ProviderReported(decimal.Round(
                        ToUsd(modelTicks),
                        6,
                        MidpointRounding.AwayFromZero))
                    : GrokPricingCatalog.Resolve(model, counters.Tokens);
                yield return CreateEvent(
                    $"grok-session\0{sessionKey}\0{model.ToLowerInvariant()}",
                    model,
                    snapshot.Timestamp,
                    counters.Tokens,
                    cost);
            }

            yield break;
        }

        CostObservation totalCost = snapshot.Totals.CostUsdTicks is long totalTicks
            ? CostObservation.ProviderReported(decimal.Round(
                ToUsd(totalTicks),
                6,
                MidpointRounding.AwayFromZero))
            : GrokPricingCatalog.Resolve(snapshot.Model, snapshot.Totals.Tokens);
        yield return CreateEvent(
            $"grok-session\0{sessionKey}\0{snapshot.Model.ToLowerInvariant()}",
            snapshot.Model,
            snapshot.Timestamp,
            snapshot.Totals.Tokens,
            totalCost);
    }

    private UsageEvent CreateEvent(
        string identity,
        string model,
        DateTimeOffset timestamp,
        TokenBreakdown tokens,
        CostObservation cost)
    {
        CoverageKind coverage = cost.Kind switch
        {
            CostKind.ProviderReported => CoverageKind.Complete,
            CostKind.CatalogEstimated => CoverageKind.Partial,
            _ => CoverageKind.Unpriced,
        };
        return new UsageEvent(
            new UsageEventKey(Hash(identity)),
            AgentId,
            new ModelProviderId("xai"),
            CreateModelId(model),
            timestamp,
            _groupingTimeZoneId,
            tokens,
            cost,
            ParserVersion,
            coverage);
    }

    private static bool TryParseSnapshot(
        ReadOnlyMemory<byte> utf8,
        SummaryInfo summary,
        out Snapshot? snapshot)
    {
        snapshot = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8);
            JsonElement root = document.RootElement;
            if (!TryGetString(root, "method", out string? method)
                || method is not ("params.update" or "_x.ai/session/update")
                || !root.TryGetProperty("params", out JsonElement parameters)
                || parameters.ValueKind != JsonValueKind.Object
                || !parameters.TryGetProperty("update", out JsonElement update)
                || update.ValueKind != JsonValueKind.Object
                || !update.TryGetProperty("usage", out JsonElement usage)
                || usage.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            DateTimeOffset? timestamp = TryGetUtcTimestamp(
                    update,
                    "timestamp",
                    out DateTimeOffset parsed)
                ? parsed
                : TryGetUtcTimestamp(parameters, "timestamp", out parsed)
                    ? parsed
                    : TryGetUtcTimestamp(root, "timestamp", out parsed)
                        ? parsed
                        : summary.Timestamp;
            if (timestamp is null)
            {
                return false;
            }

            string? model = GetFirstString(
                usage,
                "current_model_id",
                "currentModelId",
                "model") ?? summary.Model;
            Counters? totals = ParseCounters(usage);
            var models = new Dictionary<string, Counters>(StringComparer.OrdinalIgnoreCase);
            bool hasInvalidModelCounters = false;
            if (usage.TryGetProperty("modelUsage", out JsonElement modelUsage)
                && modelUsage.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in modelUsage.EnumerateObject())
                {
                    if (!string.IsNullOrWhiteSpace(property.Name)
                        && property.Value.ValueKind == JsonValueKind.Object
                        && ParseCounters(property.Value) is Counters counters)
                    {
                        models[property.Name] = counters;
                    }
                    else
                    {
                        hasInvalidModelCounters = true;
                    }
                }
            }
            else if (usage.TryGetProperty("modelUsage", out _))
            {
                hasInvalidModelCounters = true;
            }

            bool hasIncompleteUsage = usage.TryGetProperty(
                    "usageIsIncomplete",
                    out JsonElement incomplete)
                && incomplete.ValueKind == JsonValueKind.True;

            if (models.Count == 0 && (totals is null || string.IsNullOrWhiteSpace(model)))
            {
                return false;
            }

            snapshot = new Snapshot(
                timestamp.Value,
                model ?? "unknown",
                totals ?? new Counters(new TokenBreakdown(0, 0, 0, 0, 0), null),
                models,
                hasInvalidModelCounters,
                hasIncompleteUsage);
            return true;
        }
        catch (Exception exception) when (IsDataFailure(exception))
        {
            return false;
        }
    }

    private static Counters? ParseCounters(JsonElement usage)
    {
        if (!TryGetNonNegativeInt64(usage, "inputTokens", out long input)
            || !TryGetNonNegativeInt64(usage, "outputTokens", out long output))
        {
            return null;
        }

        long cacheRead = GetFirstNonNegativeInt64OrZero(
            usage,
            "cachedReadTokens",
            "cacheReadInputTokens",
            "cache_read_input_tokens");
        long reasoning = GetFirstNonNegativeInt64OrZero(
            usage,
            "reasoningTokens",
            "thoughtTokens",
            "reasoning_tokens");
        long cacheWrite = GetFirstNonNegativeInt64OrZero(
            usage,
            "cacheWriteTokens",
            "cacheWriteInputTokens",
            "cache_write_input_tokens");
        long? costTicks = TryGetNonNegativeInt64(usage, "costUsdTicks", out long parsedCost)
            ? parsedCost
            : null;
        return new Counters(
            new TokenBreakdown(
                Math.Max(input - cacheRead, 0),
                output,
                reasoning,
                cacheRead,
                cacheWrite),
            costTicks);
    }

    private bool ReadLines(
        string path,
        Func<ReadOnlyMemory<byte>, int, bool> processLine,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists
                || info.Length > _budget.MaximumFileBytes
                || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.SequentialScan);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            var line = new ArrayBufferWriter<byte>(64 * 1024);
            int lineNumber = 0;
            bool tooLong = false;
            bool complete = true;
            try
            {
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReadOnlySpan<byte> remaining = buffer.AsSpan(0, count);
                    while (!remaining.IsEmpty)
                    {
                        int newline = remaining.IndexOf((byte)'\n');
                        ReadOnlySpan<byte> segment = newline < 0 ? remaining : remaining[..newline];
                        if (!tooLong && line.WrittenCount + segment.Length <= _budget.MaximumLineBytes)
                        {
                            line.Write(segment);
                        }
                        else if (!segment.IsEmpty)
                        {
                            tooLong = true;
                        }

                        if (newline < 0)
                        {
                            break;
                        }

                        lineNumber++;
                        complete &= ProcessBufferedLine(line, tooLong, lineNumber, processLine);
                        line.Clear();
                        tooLong = false;
                        remaining = remaining[(newline + 1)..];
                    }
                }

                if (line.WrittenCount > 0 || tooLong)
                {
                    lineNumber++;
                    complete &= ProcessBufferedLine(line, tooLong, lineNumber, processLine);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return complete;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            return false;
        }
    }

    private bool ReadRecentLines(
        string path,
        Func<ReadOnlyMemory<byte>, int, bool> processLine,
        CancellationToken cancellationToken,
        out bool readWholeFile)
    {
        readWholeFile = false;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists
                || info.Length > _budget.MaximumFileBytes
                || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            int bytesToRead = checked((int)Math.Min(info.Length, SnapshotTailBytes));
            long startOffset = info.Length - bytesToRead;
            readWholeFile = startOffset == 0;
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.RandomAccess);
            stream.Seek(startOffset, SeekOrigin.Begin);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(bytesToRead, 1));
            try
            {
                int bytesRead = 0;
                while (bytesRead < bytesToRead)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = stream.Read(buffer, bytesRead, bytesToRead - bytesRead);
                    if (count == 0)
                    {
                        break;
                    }

                    bytesRead += count;
                }

                int position = 0;
                if (!readWholeFile)
                {
                    int firstNewline = buffer.AsSpan(0, bytesRead).IndexOf((byte)'\n');
                    if (firstNewline < 0)
                    {
                        return false;
                    }

                    position = firstNewline + 1;
                }

                int lineNumber = 0;
                bool complete = true;
                while (position < bytesRead)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int newline = buffer.AsSpan(position, bytesRead - position)
                        .IndexOf((byte)'\n');
                    int lineLength = newline < 0 ? bytesRead - position : newline;
                    if (lineLength > 0 && buffer[position + lineLength - 1] == (byte)'\r')
                    {
                        lineLength--;
                    }

                    int lineStart = position;
                    if (readWholeFile
                        && lineNumber == 0
                        && lineLength >= 3
                        && buffer[lineStart] == 0xEF
                        && buffer[lineStart + 1] == 0xBB
                        && buffer[lineStart + 2] == 0xBF)
                    {
                        lineStart += 3;
                        lineLength -= 3;
                    }

                    lineNumber++;
                    if (lineLength > _budget.MaximumLineBytes)
                    {
                        complete = false;
                    }
                    else
                    {
                        complete &= processLine(
                            buffer.AsMemory(lineStart, lineLength),
                            lineNumber);
                    }

                    if (newline < 0)
                    {
                        break;
                    }

                    position += newline + 1;
                }

                return complete;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static long GetFileLength(string path)
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

    private static bool ProcessBufferedLine(
        ArrayBufferWriter<byte> line,
        bool tooLong,
        int lineNumber,
        Func<ReadOnlyMemory<byte>, int, bool> processLine)
    {
        if (tooLong)
        {
            return false;
        }

        ReadOnlyMemory<byte> value = line.WrittenMemory;
        if (lineNumber == 1
            && value.Length >= 3
            && value.Span[0] == 0xEF
            && value.Span[1] == 0xBB
            && value.Span[2] == 0xBF)
        {
            value = value[3..];
        }

        if (!value.IsEmpty && value.Span[^1] == (byte)'\r')
        {
            value = value[..^1];
        }

        return processLine(value, lineNumber);
    }

    private byte[] ReadSmallFile(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists
            || info.Length > _budget.MaximumFileBytes
            || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The metadata file cannot be read safely.");
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream((int)Math.Min(info.Length, int.MaxValue));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                memory.Write(buffer, 0, count);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return memory.ToArray();
    }

    private static IEnumerable<string> EnumerateNamedFiles(
        string root,
        string fileName,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string candidate = Path.Combine(directory, fileName);
            bool includeCandidate = false;
            string[] children = [];
            try
            {
                if (File.Exists(candidate))
                {
                    if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) == 0)
                    {
                        includeCandidate = true;
                    }
                    else
                    {
                        state.IsPartial = true;
                    }
                }

                children = Directory.EnumerateDirectories(directory)
                    .OrderDescending(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Security.SecurityException)
            {
                state.IsPartial = true;
            }

            if (includeCandidate)
            {
                yield return candidate;
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
                        state.IsPartial = true;
                    }
                }
                catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or System.Security.SecurityException)
                {
                    state.IsPartial = true;
                }
            }
        }
    }

    private static string ResolveHome(string? configured, string userHome)
    {
        string raw = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(userHome, ".grok")
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
            return Path.GetFullPath(Path.Combine(userHome, ".grok"));
        }
    }

    private static string? GetUnifiedModel(string message, JsonElement context) => message switch
    {
        "model changed" => GetFirstString(context, "model"),
        "model catalog: notifying clients" => GetFirstString(context, "current_model_id"),
        "backend_search: model switch" => GetFirstString(
            context,
            "model",
            "current_model_id",
            "model_id"),
        "subagent model resolved" => GetFirstString(context, "model_id", "model"),
        _ => null,
    };

    private static string? GetFirstString(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (TryGetString(element, name, out string? value))
            {
                return value;
            }
        }

        return null;
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
        if (TryGetString(element, propertyName, out string? text))
        {
            return DateTimeOffset.TryParse(
                       text,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.RoundtripKind,
                       out timestamp)
                   && timestamp.Offset == TimeSpan.Zero;
        }

        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt64(out long unixSeconds)
            || unixSeconds is < -62_135_596_800 or > 253_402_300_799)
        {
            return false;
        }

        timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        return true;
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

    private static long GetNonNegativeInt64OrZero(
        JsonElement element,
        string propertyName) =>
        TryGetNonNegativeInt64(element, propertyName, out long value) ? value : 0;

    private static long GetFirstNonNegativeInt64OrZero(
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (TryGetNonNegativeInt64(element, propertyName, out long value))
            {
                return value;
            }
        }

        return 0;
    }

    private static decimal ToUsd(long ticks) => ticks / TicksPerUsd;

    private static ModelId CreateModelId(string model)
    {
        try
        {
            return new ModelId(model.Trim().ToLowerInvariant());
        }
        catch (ArgumentException)
        {
            return new ModelId($"unknown-{Hash(model)[..16]}");
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsDataFailure(Exception exception) => exception is JsonException
        or IOException
        or UnauthorizedAccessException
        or ArgumentException
        or InvalidOperationException
        or OverflowException
        or NotSupportedException
        or System.Security.SecurityException;

    private sealed record SummaryInfo(string? Model, DateTimeOffset? Timestamp);

    private sealed record Counters(TokenBreakdown Tokens, long? CostUsdTicks);

    private sealed record Snapshot(
        DateTimeOffset Timestamp,
        string Model,
        Counters Totals,
        IReadOnlyDictionary<string, Counters> Models,
        bool HasInvalidModelCounters,
        bool HasIncompleteUsage);

}
