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
    public const string ParserVersion = "codex-jsonl/2";
    private const int DefaultTailBytes = 64 * 1024;
    private readonly string _codexHome;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;
    private readonly ICodexQuotaClientFactory? _clientFactory;

    public CodexUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? codexHomeOverride = null,
        int maximumFiles = 10_000,
        long maximumTailBytes = DefaultTailBytes,
        int maximumLineCharacters = DefaultTailBytes,
        ICodexQuotaClientFactory? clientFactory = null)
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
        ScanResult scan = await Task.Run(
                () => ScanCore(cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        return _clientFactory is null
            ? CreateFallbackResult(scan)
            : await ReadOfficialUsageAsync(scan, cancellationToken).ConfigureAwait(false);
    }

    private ScanResult ScanCore(CancellationToken cancellationToken)
    {
        string[] roots = SessionRoots().Where(Directory.Exists).ToArray();
        if (roots.Length == 0)
        {
            return new ScanResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.RootUnavailable);
        }

        var state = new LocalScanState(_budget);
        SessionFile[] files = FindSessionFiles(roots, state, cancellationToken);
        var sessions = new List<ScannedSession>(files.Length);
        foreach (SessionFile file in files)
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
        }

        return CreateScanResult(sessions, state);
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
            path = Path.GetFullPath(rawPath);
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
            if (hasCumulative && !TryReadTokenBreakdown(cumulativeElement, out cumulative)
                || hasLast && !TryReadTokenBreakdown(lastElement, out last))
            {
                return MarkSchemaFailure(state, markSchemaFailures);
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
            UsageEvent[] events = CreateOfficialEvents(usage, scan.Sessions);
            return events.Length == 0
                ? new UsageSourceReadResult(
                    [],
                    UsageSourceReadStatus.NoData,
                    UsageSourceIssueKind.Empty)
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
        IReadOnlyList<ScannedSession> sessions)
    {
        TimeZoneInfo groupingTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            _groupingTimeZoneId);
        Dictionary<DateOnly, ModelSample[]> samplesByDate = sessions
            .Where(session => session.Candidate.SampleTokens.Total > 0)
            .GroupBy(session => DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(
                    session.Candidate.Timestamp,
                    groupingTimeZone).DateTime))
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(session => session.Candidate.Model, StringComparer.Ordinal)
                    .Select(CreateModelSample)
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
        foreach ((DateOnly date, long totalTokens) in tokensByDate)
        {
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

        return events.ToArray();
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
        IGrouping<string, ScannedSession> modelGroup)
    {
        ScannedSession[] sessions = modelGroup.ToArray();
        TokenBreakdown tokens = SumTokens(
            sessions.Select(value => value.Candidate.SampleTokens));
        CostObservation[] costs = sessions
            .Select(value => CodexPricingCatalog.Resolve(
                modelGroup.Key,
                value.Candidate.SampleTokens))
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
        return new ScanResult(sessions, status, issue ?? UsageSourceIssueKind.None);
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
        UsageSourceReadStatus Status,
        UsageSourceIssueKind Issue);

    private sealed record ScannedSession(string SessionIdentity, Candidate Candidate);

    private sealed record ModelSample(
        string Model,
        TokenBreakdown Tokens,
        CostObservation Cost);

    private sealed record Candidate(
        DateTimeOffset Timestamp,
        string Model,
        TokenBreakdown TotalTokens,
        TokenBreakdown SampleTokens);
}
