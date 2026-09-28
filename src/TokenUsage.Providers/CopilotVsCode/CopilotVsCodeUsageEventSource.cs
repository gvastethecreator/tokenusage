using System.Security.Cryptography;
using System.Text;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.CopilotVsCode;

/// <summary>
/// Reads GitHub Copilot Chat usage that VS Code stores next to each chat session.
/// Only the allowlist in <c>docs/source-gates/COPILOT-VSCODE.md</c> is projected:
/// request id and times, model ids, token counters, and Copilot credits. Messages,
/// responses, tool data, workspace paths, logs, and credentials are never read.
/// </summary>
public sealed class CopilotVsCodeUsageEventSource :
    IWindowedSnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "copilot-vscode-chat-requests/1";
    public const long DefaultMaximumFileBytes = 16 * 1024 * 1024;
    public const int DefaultMaximumFiles = 5_000;

    /// <summary>GitHub bills one AI credit as one US cent.</summary>
    private const decimal UsdPerCredit = 0.01m;
    private const double MaximumPlausibleCredits = 1_000_000;
    private const long MaximumPlausibleTokens = 1L << 36;
    private const string PickerVendorPrefix = "copilot/";

    private readonly IReadOnlyList<string> _userDirectories;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;
    private readonly TimeProvider _clock;

    public CopilotVsCodeUsageEventSource(
        string groupingTimeZoneId,
        string? roamingAppDataDirectory = null,
        IReadOnlyList<string>? userDirectoriesOverride = null,
        long maximumFileBytes = DefaultMaximumFileBytes,
        int maximumFiles = DefaultMaximumFiles,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        _userDirectories = userDirectoriesOverride is null
            ? CopilotVsCodeUsagePaths.ResolveUserDirectories(roamingAppDataDirectory)
            : Array.AsReadOnly(userDirectoriesOverride.Select(Path.GetFullPath).ToArray());
        _groupingTimeZoneId = groupingTimeZoneId;
        _budget = new LocalScanBudget(
            maximumFiles,
            maximumFileBytes,
            (int)Math.Min(maximumFileBytes, int.MaxValue));
        _clock = clock ?? TimeProvider.System;
    }

    public AgentId AgentId { get; } = new("copilot");

    public SourceKind SourceKind => SourceKind.LocalLog;

    public string EventParserVersion => ParserVersion;

    public int ReconciliationWindowDays => UsagePeriodPolicy.ReconciliationDays;

    public bool IsRootAvailable => _userDirectories.Any(Directory.Exists);

    public Task<UsageSourceReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadCore(cancellationToken), cancellationToken);

    private UsageSourceReadResult ReadCore(CancellationToken cancellationToken)
    {
        if (!IsRootAvailable)
        {
            return new UsageSourceReadResult([], UsageSourceReadStatus.NoData, UsageSourceIssueKind.RootUnavailable);
        }

        var state = new LocalScanState(_budget);
        var events = new Dictionary<string, UsageEvent>(StringComparer.Ordinal);
        DateTime oldestWriteUtc = _clock.GetUtcNow().UtcDateTime.AddDays(-ReconciliationWindowDays);
        foreach (FileInfo file in ListSessionFiles(state))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.LastWriteTimeUtc < oldestWriteUtc)
            {
                continue;
            }

            if ((file.Attributes & FileAttributes.ReparsePoint) != 0
                || state.IsFileTooLarge(file.Length))
            {
                state.MarkPartial();
                continue;
            }

            if (!state.TryConsumeFile())
            {
                break;
            }

            ReadFile(file, events, state);
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
                    : state.IsPartial
                        ? UsageSourceIssueKind.AccessBlocked
                        : UsageSourceIssueKind.Empty);
        }

        return state.IsPartial || state.UnsupportedSchema
            ? new UsageSourceReadResult(ordered, UsageSourceReadStatus.Partial, UsageSourceIssueKind.PartialScan)
            : new UsageSourceReadResult(ordered, UsageSourceReadStatus.Complete);
    }

    /// <summary>
    /// Oldest first, so when one session appears under several roots the most
    /// recently written copy decides the event.
    /// </summary>
    private List<FileInfo> ListSessionFiles(LocalScanState state)
    {
        var files = new List<FileInfo>();
        foreach (string userDirectory in _userDirectories)
        {
            try
            {
                files.AddRange(CopilotVsCodeUsagePaths.EnumerateSessionFiles(userDirectory)
                    .Select(path => new FileInfo(path)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                state.MarkPartial();
            }
        }

        return files
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ReadFile(FileInfo file, Dictionary<string, UsageEvent> events, LocalScanState state)
    {
        byte[] content;
        try
        {
            using var stream = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            if (state.IsFileTooLarge(stream.Length))
            {
                return;
            }

            content = new byte[stream.Length];
            stream.ReadExactly(content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            state.MarkPartial();
            return;
        }

        CopilotChatSessionReadResult result = CopilotChatSessionReader.Read(
            content,
            CopilotVsCodeUsagePaths.IsOperationLog(file.FullName),
            state.MaximumLineBytes);
        switch (result.Status)
        {
            case CopilotChatSessionReadStatus.UnsupportedSchema:
                state.UnsupportedSchema = true;
                return;
            case CopilotChatSessionReadStatus.Partial:
                state.MarkPartial();
                break;
        }

        if (result.Session is not { } session)
        {
            return;
        }

        string? sessionId = IsIdentifier(session.SessionId)
            ? session.SessionId
            : Path.GetFileNameWithoutExtension(file.Name);
        foreach (CopilotChatRequest? request in session.Requests)
        {
            if (request is not null
                && CreateEvent(sessionId!, request) is { } usageEvent)
            {
                events[usageEvent.EventKey.Value] = usageEvent;
            }
        }
    }

    private UsageEvent? CreateEvent(string sessionId, CopilotChatRequest request)
    {
        // ResponseModelState: 0 Pending, 1 Complete, 2 Cancelled, 3 Failed, 4 NeedsInput.
        int modelState = request.ModelState ?? (request.HasResult ? 1 : 0);
        if (modelState is not (1 or 2 or 3)
            || !IsIdentifier(request.RequestId)
            || !TryGetTimes(request, out DateTimeOffset occurredAtUtc, out DateTimeOffset? startedAtUtc)
            || !TryGetTokens(request, out TokenBreakdown? tokens, out UsageDetailMetadata? detail))
        {
            return null;
        }

        double? credits = request.CopilotCredits is { } value && value <= MaximumPlausibleCredits
            ? value
            : null;
        if (tokens!.Total == 0 && credits is null or 0)
        {
            return null;
        }

        string? picker = StripVendorPrefix(request.PickerModel);
        string model = request.ResolvedModel
            ?? (request.ModelTotals is [var single] ? single.Model : null)
            ?? picker
            ?? ModelIdentity.Unknown;
        ModelId modelId = ModelIdentity.ToModelId(model);
        ModelId? observedModelId = picker is null ? null : ModelIdentity.ToModelId(picker);

        // Copilot credits are the backend's billed amount for the turn, before any
        // allowance included in the plan. They are not the net charge.
        (CostObservation cost, CoverageKind coverage) = credits is { } billed
            ? (CostObservation.ProviderReported(decimal.Round((decimal)billed * UsdPerCredit, 6)), CoverageKind.Partial)
            : (CostObservation.Unavailable(), CoverageKind.Unpriced);
        return new UsageEvent(
            new UsageEventKey(Hash($"copilot-vscode\0chat-request-v1\0{sessionId}\0{request.RequestId}")),
            AgentId,
            ResolveModelProvider(model),
            modelId,
            occurredAtUtc,
            _groupingTimeZoneId,
            tokens,
            cost,
            ParserVersion,
            coverage,
            startedAtUtc is null ? UsageTimePrecision.Timestamp : UsageTimePrecision.Interval,
            startedAtUtc,
            observedModelId == modelId ? null : observedModelId,
            detailMetadata: detail);
    }

    private static bool TryGetTimes(
        CopilotChatRequest request,
        out DateTimeOffset occurredAtUtc,
        out DateTimeOffset? startedAtUtc)
    {
        startedAtUtc = null;
        bool hasStart = TryConvert(request.TimestampMilliseconds, out DateTimeOffset started);
        if (TryConvert(request.CompletedAtMilliseconds, out occurredAtUtc))
        {
            if (hasStart && started <= occurredAtUtc)
            {
                startedAtUtc = started;
            }

            return true;
        }

        occurredAtUtc = started;
        return hasStart;
    }

    private static bool TryConvert(long? milliseconds, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (milliseconds is not { } value)
        {
            return false;
        }

        try
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return timestamp.Year is >= 2000 and <= 2100;
    }

    private static bool TryGetTokens(
        CopilotChatRequest request,
        out TokenBreakdown? tokens,
        out UsageDetailMetadata? detail)
    {
        tokens = null;
        detail = null;
        if (request.ModelTotals is { Count: > 0 } totals)
        {
            // Whole-turn totals per model. Cached tokens are a subset of inputTokens:
            // VS Code reports its cache hit rate as cachedTokens / inputTokens and clamps
            // cachedTokens to inputTokens. See
            // https://github.com/microsoft/vscode/blob/1f5367975c88d43d6878f0e6e3f7cb1395e35aef/src/vs/workbench/contrib/chat/browser/chatDebug/chatDebugCacheInsights.ts#L721-L765
            long uncached = 0;
            long cached = 0;
            long output = 0;
            foreach (CopilotModelTotal total in totals)
            {
                if (total.InputTokens > MaximumPlausibleTokens
                    || total.OutputTokens > MaximumPlausibleTokens)
                {
                    return false;
                }

                long cachedPart = Math.Min(total.CachedTokens, total.InputTokens);
                uncached = checked(uncached + total.InputTokens - cachedPart);
                cached = checked(cached + cachedPart);
                output = checked(output + total.OutputTokens);
            }

            tokens = new TokenBreakdown(uncached, output, reasoning: 0, cacheRead: cached, cacheWrite: 0);
            detail = new UsageDetailMetadata(
                recordKind: UsageRecordKind.RequestFinal,
                input: UsageComponentAvailability.Measured,
                output: UsageComponentAvailability.Measured,
                reasoning: UsageComponentAvailability.Unavailable,
                cacheRead: UsageComponentAvailability.Measured,
                cacheWrite: UsageComponentAvailability.Unavailable);
            return true;
        }

        // promptTokens describes only the last model call of the turn, so it is a lower
        // bound on input. completionTokens is summed across the turn's calls.
        long input = request.PromptTokens ?? request.MetadataPromptTokens ?? 0;
        long completion = request.CompletionTokens ?? request.MetadataOutputTokens ?? 0;
        if (input > MaximumPlausibleTokens || completion > MaximumPlausibleTokens)
        {
            return false;
        }

        tokens = new TokenBreakdown(input, completion, reasoning: 0, cacheRead: 0, cacheWrite: 0);
        detail = new UsageDetailMetadata(
            recordKind: UsageRecordKind.RequestFinal,
            input: UsageComponentAvailability.Unknown,
            output: request.CompletionTokens is null
                ? UsageComponentAvailability.Unknown
                : UsageComponentAvailability.Measured,
            reasoning: UsageComponentAvailability.Unavailable,
            cacheRead: UsageComponentAvailability.Unavailable,
            cacheWrite: UsageComponentAvailability.Unavailable);
        return true;
    }

    private static string? StripVendorPrefix(string? picker) =>
        picker is not null && picker.StartsWith(PickerVendorPrefix, StringComparison.OrdinalIgnoreCase)
            ? picker[PickerVendorPrefix.Length..] is { Length: > 0 } stripped ? stripped : null
            : picker;

    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 200;

    private static ModelProviderId? ResolveModelProvider(string model)
    {
        string normalized = model.Trim().ToLowerInvariant();
        return normalized switch
        {
            _ when normalized.Contains("claude", StringComparison.Ordinal) => new ModelProviderId("anthropic"),
            _ when normalized.Contains("gemini", StringComparison.Ordinal) => new ModelProviderId("google"),
            _ when normalized.Contains("grok", StringComparison.Ordinal) => new ModelProviderId("xai"),
            _ when normalized.StartsWith("gpt-", StringComparison.Ordinal)
                || normalized.StartsWith("o1", StringComparison.Ordinal)
                || normalized.StartsWith("o3", StringComparison.Ordinal)
                || normalized.StartsWith("o4", StringComparison.Ordinal) => new ModelProviderId("openai"),
            _ => null,
        };
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
