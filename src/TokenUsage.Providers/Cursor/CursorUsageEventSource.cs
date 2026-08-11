using System.Globalization;
using System.Text;
using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;

namespace TokenUsage.Providers.Cursor;

/// <summary>
/// Reads the numeric-only spool produced by the opt-in Cursor stop hook.
/// The hook does not persist prompts, responses, paths, emails, or raw session IDs.
/// </summary>
public sealed class CursorUsageEventSource :
    IWindowedSnapshotUsageEventSource,
    IRootDetectingUsageEventSource
{
    public const string ParserVersion = "cursor-hook/1";
    private const int LookbackDays = 35;
    private readonly string _cursorHome;
    private readonly string _spoolPath;
    private readonly string _groupingTimeZoneId;
    private readonly LocalScanBudget _budget;

    public CursorUsageEventSource(
        string groupingTimeZoneId,
        string? homeDirectory = null,
        string? localAppDataDirectory = null,
        string? spoolPathOverride = null,
        long maximumFileBytes = 16 * 1024 * 1024,
        int maximumLineBytes = 64 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);

        _cursorHome = CursorUsagePaths.ResolveCursorHome(homeDirectory);
        _spoolPath = spoolPathOverride is null
            ? CursorUsagePaths.ResolveSpoolPath(localAppDataDirectory)
            : Path.GetFullPath(spoolPathOverride);
        _groupingTimeZoneId = groupingTimeZoneId;
        _budget = new LocalScanBudget(2, maximumFileBytes, maximumLineBytes);
    }

    public AgentId AgentId { get; } = new("cursor");

    public SourceKind SourceKind => SourceKind.LocalLog;

    public string EventParserVersion => ParserVersion;

    public int ReconciliationWindowDays => LookbackDays;

    public bool IsRootAvailable => Directory.Exists(_cursorHome) || File.Exists(_spoolPath);

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

        string[] paths = [_spoolPath + ".1", _spoolPath];
        if (!paths.Any(File.Exists))
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.Empty);
        }

        var state = new LocalScanState(_budget);
        var events = new Dictionary<string, UsageEvent>(StringComparer.Ordinal);
        foreach (string path in paths.Where(File.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.TryConsumeFile())
            {
                break;
            }

            ReadFile(path, events, state, cancellationToken);
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

        // The hook covers local Agent turns after opt-in. It does not cover Tab,
        // cloud agents, reasoning tokens, quota, or provider-reported cost.
        return new UsageSourceReadResult(
            ordered,
            UsageSourceReadStatus.Partial,
            UsageSourceIssueKind.PartialScan);
    }

    private void ReadFile(
        string path,
        Dictionary<string, UsageEvent> events,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists
                || (info.Attributes & FileAttributes.ReparsePoint) != 0
                || state.IsFileTooLarge(info.Length))
            {
                state.MarkPartial();
                return;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 16 * 1024,
                leaveOpen: false);
            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Encoding.UTF8.GetByteCount(line) > state.MaximumLineBytes)
                {
                    state.MarkPartial();
                    continue;
                }

                if (TryParse(line, out UsageEvent? usageEvent) && usageEvent is not null)
                {
                    events[usageEvent.EventKey.Value] = usageEvent;
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    state.UnsupportedSchema = true;
                    state.MarkPartial();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or DecoderFallbackException)
        {
            state.MarkPartial();
        }
    }

    private bool TryParse(string line, out UsageEvent? usageEvent)
    {
        usageEvent = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!TryGetInt64(root, "version", out long version) || version != 1
                || !TryGetString(root, "event_key", out string? eventKey)
                || !TryGetString(root, "occurred_at_utc", out string? timestampText)
                || !DateTimeOffset.TryParse(
                    timestampText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset timestamp)
                || timestamp.Offset != TimeSpan.Zero
                || !TryGetString(root, "model", out string? model)
                || !TryGetInt64(root, "input_tokens", out long input)
                || !TryGetInt64(root, "output_tokens", out long output)
                || !TryGetInt64(root, "cache_read_tokens", out long cacheRead)
                || !TryGetInt64(root, "cache_write_tokens", out long cacheWrite))
            {
                return false;
            }

            usageEvent = new UsageEvent(
                new UsageEventKey(eventKey!),
                AgentId,
                ResolveModelProvider(model!),
                CreateModelId(model!),
                timestamp,
                _groupingTimeZoneId,
                new TokenBreakdown(input, output, 0, cacheRead, cacheWrite),
                CostObservation.Unavailable(),
                ParserVersion,
                CoverageKind.Unpriced);
            return true;
        }
        catch (Exception exception) when (exception is JsonException
                                           or ArgumentException
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
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetInt64(
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
}
