using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;

namespace WOpenUsage.Providers.Claude;

public sealed class ClaudeUsageEventSource : IUsageEventSource
{
    public const string ParserVersion = "claude-jsonl/1";
    private readonly string _homeDirectory;
    private readonly string? _configDirectoryOverride;
    private readonly string _groupingTimeZoneId;
    private readonly int _maximumFiles;
    private readonly long _maximumFileBytes;
    private readonly int _maximumLineBytes;

    public ClaudeUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? configDirectoryOverride = null,
        int maximumFiles = 10_000,
        long maximumFileBytes = 64 * 1024 * 1024,
        int maximumLineCharacters = 8 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLineCharacters, 1);

        _groupingTimeZoneId = groupingTimeZoneId;
        _homeDirectory = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _configDirectoryOverride = configDirectoryOverride
            ?? Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        _maximumFiles = maximumFiles;
        _maximumFileBytes = maximumFileBytes;
        _maximumLineBytes = maximumLineCharacters;
    }

    public SourceKind SourceKind => SourceKind.LocalLog;

    public AgentId AgentId { get; } = new("claude");

    public bool IsRootAvailable => ClaudeConfigLocator.FindProjectDirectories(
        _homeDirectory,
        _configDirectoryOverride).Count > 0;

    public async Task<UsageSourceReadResult> ReadAsync(
        CancellationToken cancellationToken = default) =>
        await Task.Run(() => ReadCore(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

    private UsageSourceReadResult ReadCore(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> projectDirectories = ClaudeConfigLocator.FindProjectDirectories(
            _homeDirectory,
            _configDirectoryOverride);
        if (projectDirectories.Count == 0)
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.RootUnavailable);
        }

        var candidates = new List<Candidate>();
        var scanState = new ScanState();
        int fileCount = 0;
        foreach (string projectDirectory in projectDirectories)
        {
            foreach (string file in EnumerateJsonlFiles(
                         projectDirectory,
                         scanState,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++fileCount > _maximumFiles)
                {
                    scanState.IsPartial = true;
                    return CreateResult(candidates, scanState);
                }

                if (!ReadFile(file, candidates, cancellationToken))
                {
                    scanState.IsPartial = true;
                }
            }
        }

        return CreateResult(candidates, scanState);
    }

    private bool ReadFile(
        string path,
        List<Candidate> candidates,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > _maximumFileBytes)
            {
                return false;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            byte[] readBuffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            var lineBuffer = new ArrayBufferWriter<byte>(64 * 1024);
            int lineNumber = 0;
            bool lineTooLong = false;
            bool complete = true;
            try
            {
                int bytesRead;
                while ((bytesRead = stream.Read(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReadOnlySpan<byte> remaining = readBuffer.AsSpan(0, bytesRead);
                    while (!remaining.IsEmpty)
                    {
                        int newlineIndex = remaining.IndexOf((byte)'\n');
                        ReadOnlySpan<byte> segment = newlineIndex >= 0
                            ? remaining[..newlineIndex]
                            : remaining;
                        if (!lineTooLong
                            && lineBuffer.WrittenCount + segment.Length <= _maximumLineBytes)
                        {
                            lineBuffer.Write(segment);
                        }
                        else if (!segment.IsEmpty)
                        {
                            lineTooLong = true;
                        }

                        if (newlineIndex < 0)
                        {
                            break;
                        }

                        lineNumber++;
                        complete &= ProcessLine(
                            path,
                            lineNumber,
                            lineBuffer.WrittenMemory,
                            lineTooLong,
                            candidates);
                        lineBuffer.Clear();
                        lineTooLong = false;
                        remaining = remaining[(newlineIndex + 1)..];
                    }
                }

                if (lineBuffer.WrittenCount > 0 || lineTooLong)
                {
                    lineNumber++;
                    complete &= ProcessLine(
                        path,
                        lineNumber,
                        lineBuffer.WrittenMemory,
                        lineTooLong,
                        candidates);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(readBuffer);
            }

            return complete;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            // Another process may rotate or lock a session while it is being scanned.
            return false;
        }
    }

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

        if (TryParse(utf8, CreateSourceOrdinal(path, lineNumber), out Candidate? candidate)
            && candidate is not null)
        {
            candidates.Add(candidate);
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateJsonlFiles(
        string root,
        ScanState scanState,
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

    private UsageSourceReadResult CreateResult(
        List<Candidate> candidates,
        ScanState scanState)
    {
        List<UsageEvent> events = CreateEvents(Deduplicate(candidates));
        UsageSourceReadStatus status = scanState.IsPartial
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

    private List<UsageEvent> CreateEvents(IEnumerable<Candidate> candidates)
    {
        var events = new List<UsageEvent>();
        foreach (Candidate candidate in candidates)
        {
            string normalizedModel = candidate.Model.ToLowerInvariant();
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
                CreateModelId(normalizedModel),
                candidate.OccurredAtUtc,
                _groupingTimeZoneId,
                candidate.Tokens,
                cost,
                ParserVersion,
                coverage));
        }

        return events;
    }

    private static Candidate[] Deduplicate(IEnumerable<Candidate> input)
    {
        var exact = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        var withoutExactKey = new List<Candidate>();
        foreach (Candidate candidate in input)
        {
            if (candidate.MessageId is null || candidate.RequestId is null)
            {
                withoutExactKey.Add(candidate);
                continue;
            }

            string key = $"{candidate.MessageId}\0{candidate.RequestId}";
            if (!exact.TryGetValue(key, out Candidate? current)
                || IsPreferred(candidate, current))
            {
                exact[key] = candidate;
            }
        }

        List<Candidate> firstPass = [.. exact.Values, .. withoutExactKey];
        var sidechainIds = firstPass
            .Where(candidate => candidate.IsSidechain && candidate.MessageId is not null)
            .Select(candidate => candidate.MessageId!)
            .ToHashSet(StringComparer.Ordinal);
        var result = new List<Candidate>();
        foreach (IGrouping<string?, Candidate> group in firstPass.GroupBy(
                     candidate => sidechainIds.Contains(candidate.MessageId ?? string.Empty)
                         ? candidate.MessageId
                         : null,
                     StringComparer.Ordinal))
        {
            if (group.Key is null)
            {
                result.AddRange(group);
            }
            else
            {
                result.Add(group.Aggregate((best, next) => IsPreferred(next, best) ? next : best));
            }
        }

        return result.OrderBy(candidate => candidate.OccurredAtUtc)
            .ThenBy(candidate => candidate.SourceOrdinal, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsPreferred(Candidate candidate, Candidate current)
    {
        if (candidate.IsSidechain != current.IsSidechain)
        {
            return !candidate.IsSidechain;
        }

        if (candidate.Tokens.Total != current.Tokens.Total)
        {
            return candidate.Tokens.Total > current.Tokens.Total;
        }

        return candidate.ReportedCostUsd is not null && current.ReportedCostUsd is null;
    }

    private static bool TryParse(
        ReadOnlyMemory<byte> utf8,
        string sourceOrdinal,
        out Candidate? candidate)
    {
        candidate = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8);
            JsonElement root = document.RootElement;
            if (!TryGetString(root, "type", out string? type)
                || !string.Equals(type, "assistant", StringComparison.Ordinal)
                || !TryGetString(root, "timestamp", out string? timestampText)
                || !DateTimeOffset.TryParse(
                    timestampText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset timestamp)
                || timestamp.Offset != TimeSpan.Zero
                || !root.TryGetProperty("message", out JsonElement message)
                || message.ValueKind != JsonValueKind.Object
                || !TryGetString(message, "model", out string? model)
                || string.IsNullOrWhiteSpace(model)
                || !message.TryGetProperty("usage", out JsonElement usage)
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
            long cacheWrite5Minutes = legacyWrite;
            long cacheWrite1Hour = 0;
            if (usage.TryGetProperty("cache_creation", out JsonElement cacheCreation)
                && cacheCreation.ValueKind == JsonValueKind.Object)
            {
                cacheWrite5Minutes = GetNonNegativeInt64OrZero(
                    cacheCreation,
                    "ephemeral_5m_input_tokens");
                cacheWrite1Hour = GetNonNegativeInt64OrZero(
                    cacheCreation,
                    "ephemeral_1h_input_tokens");
                long splitTotal = checked(cacheWrite5Minutes + cacheWrite1Hour);
                if (legacyWrite > splitTotal)
                {
                    cacheWrite5Minutes = checked(cacheWrite5Minutes + legacyWrite - splitTotal);
                }
            }

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
            candidate = new Candidate(
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
                TryGetString(message, "id", out string? messageId) ? messageId : null,
                TryGetString(root, "requestId", out string? requestId) ? requestId : null,
                root.TryGetProperty("isSidechain", out JsonElement sidechain)
                    && sidechain.ValueKind is JsonValueKind.True,
                reportedCost,
                isFast,
                sourceOrdinal);
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
        string identity = candidate.MessageId is not null && candidate.RequestId is not null
            ? $"claude\0{candidate.MessageId}\0{candidate.RequestId}"
            : $"claude\0{candidate.SourceOrdinal}";
        return new UsageEventKey(Hash(identity));
    }

    private static ModelId CreateModelId(string model)
    {
        try
        {
            return new ModelId(model.ToLowerInvariant());
        }
        catch (ArgumentException)
        {
            return new ModelId($"unknown-{Hash(model)[..16]}");
        }
    }

    private static string CreateSourceOrdinal(string path, int lineNumber) =>
        Hash($"{Path.GetFullPath(path)}\0{lineNumber}");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record Candidate(
        DateTimeOffset OccurredAtUtc,
        string Model,
        TokenBreakdown Tokens,
        long CacheWrite5Minutes,
        long CacheWrite1Hour,
        string? MessageId,
        string? RequestId,
        bool IsSidechain,
        decimal? ReportedCostUsd,
        bool IsFast,
        string SourceOrdinal);

    private sealed class ScanState
    {
        public bool IsPartial { get; set; }
    }
}
