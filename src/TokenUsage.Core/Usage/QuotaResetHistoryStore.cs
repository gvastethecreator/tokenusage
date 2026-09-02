using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Usage;

public enum QuotaResetDetectionKind
{
    Scheduled,
    Early,
    Observed,
}

public sealed record QuotaResetWindowState(
    string ProviderId,
    string MetricId,
    decimal UsedPercent,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset CurrentCycleStartedAtUtc,
    DateTimeOffset? ExpectedResetAtUtc,
    decimal? WindowDurationMinutes);

public sealed record QuotaResetRecord(
    string ProviderId,
    string MetricId,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset PreviousCycleStartedAtUtc,
    DateTimeOffset PreviousObservedAtUtc,
    decimal PreviousUsedPercent,
    decimal CurrentUsedPercent,
    DateTimeOffset? PreviousExpectedResetAtUtc,
    DateTimeOffset? CurrentExpectedResetAtUtc,
    decimal? WindowDurationMinutes,
    QuotaResetDetectionKind DetectionKind);

public sealed record QuotaResetHistory(
    IReadOnlyList<QuotaResetWindowState> Windows,
    IReadOnlyList<QuotaResetRecord> Resets)
{
    public static QuotaResetHistory Empty { get; } = new([], []);
}

public sealed record QuotaResetCycle(
    string ProviderId,
    string MetricId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    bool IsCurrent,
    decimal? WindowDurationMinutes,
    decimal UsedPercent,
    QuotaResetDetectionKind? EndingResetKind);

public static class QuotaResetCycleQuery
{
    public static IReadOnlyList<QuotaResetCycle> Build(
        QuotaResetHistory history,
        string providerId,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        UtcTimestamp.Require(nowUtc, nameof(nowUtc));

        var cycles = new List<QuotaResetCycle>();
        foreach (QuotaResetWindowState window in history.Windows
                     .Where(item => string.Equals(
                         item.ProviderId,
                         providerId,
                         StringComparison.Ordinal))
                     .OrderBy(item => item.MetricId, StringComparer.Ordinal))
        {
            DateTimeOffset currentEnd = nowUtc < window.CurrentCycleStartedAtUtc
                ? window.CurrentCycleStartedAtUtc
                : nowUtc;
            cycles.Add(new QuotaResetCycle(
                window.ProviderId,
                window.MetricId,
                window.CurrentCycleStartedAtUtc,
                currentEnd,
                IsCurrent: true,
                window.WindowDurationMinutes,
                window.UsedPercent,
                EndingResetKind: null));
        }

        cycles.AddRange(history.Resets
            .Where(item => string.Equals(item.ProviderId, providerId, StringComparison.Ordinal)
                && item.PreviousCycleStartedAtUtc < item.OccurredAtUtc)
            .OrderBy(item => item.MetricId, StringComparer.Ordinal)
            .ThenByDescending(item => item.OccurredAtUtc)
            .Select(item => new QuotaResetCycle(
                item.ProviderId,
                item.MetricId,
                item.PreviousCycleStartedAtUtc,
                item.OccurredAtUtc,
                IsCurrent: false,
                ResolveWindowDuration(item),
                item.PreviousUsedPercent,
                item.DetectionKind)));

        return cycles;
    }

    private static decimal? ResolveWindowDuration(QuotaResetRecord reset)
    {
        if (reset.WindowDurationMinutes is > 0m)
        {
            return reset.WindowDurationMinutes;
        }

        if (reset.CurrentExpectedResetAtUtc is not { } expectedReset
            || expectedReset <= reset.DetectedAtUtc)
        {
            return null;
        }

        decimal remainingMinutes = (decimal)(expectedReset - reset.DetectedAtUtc).TotalMinutes;
        if (remainingMinutes is >= 180m and <= 420m)
        {
            return 300m;
        }

        if (remainingMinutes is >= 4_320m and <= 11_520m)
        {
            return 10_080m;
        }

        return decimal.Round(remainingMinutes, 2, MidpointRounding.AwayFromZero);
    }

    internal static bool SameWindowDuration(decimal? left, decimal? right) =>
        left is null && right is null
        || left is > 0m
            && right is > 0m
            && Math.Abs(left.Value - right.Value) <= 0.01m;
}

public static class QuotaResetCountQuery
{
    public static int Count(
        QuotaResetHistory history,
        string providerId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        string? metricId = null) => Summarize(
            history,
            providerId,
            fromUtc,
            toUtcExclusive,
            metricId).Total;

    public static QuotaResetCountSummary Summarize(
        QuotaResetHistory history,
        string providerId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        string? metricId = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        UtcTimestamp.Require(fromUtc, nameof(fromUtc));
        UtcTimestamp.Require(toUtcExclusive, nameof(toUtcExclusive));
        if (toUtcExclusive < fromUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toUtcExclusive),
                "The reset count range cannot end before it starts.");
        }

        if (metricId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
        }

        Dictionary<string, QuotaResetWindowState> activeWindows = history.Windows
            .Where(item => string.Equals(item.ProviderId, providerId, StringComparison.Ordinal)
                && (metricId is null
                    || string.Equals(item.MetricId, metricId, StringComparison.Ordinal)))
            .ToDictionary(item => item.MetricId, StringComparer.Ordinal);

        QuotaResetRecord[] matchingResets = history.Resets
            .Where(item => string.Equals(item.ProviderId, providerId, StringComparison.Ordinal)
                && activeWindows.TryGetValue(item.MetricId, out QuotaResetWindowState? window)
                && QuotaResetCycleQuery.SameWindowDuration(
                    item.WindowDurationMinutes,
                    window.WindowDurationMinutes)
                && item.OccurredAtUtc >= fromUtc
                && item.OccurredAtUtc < toUtcExclusive)
            .ToArray();

        return new QuotaResetCountSummary(
            matchingResets.Length,
            matchingResets.Count(item => item.DetectionKind == QuotaResetDetectionKind.Scheduled),
            matchingResets.Count(item => item.DetectionKind == QuotaResetDetectionKind.Early),
            matchingResets.Count(item => item.DetectionKind == QuotaResetDetectionKind.Observed));
    }
}

public sealed record QuotaResetCountSummary(
    int Total,
    int Scheduled,
    int Early,
    int Observed);

public sealed class QuotaResetHistoryVersionException(int actualVersion, int supportedVersion)
    : InvalidOperationException(
        $"Quota reset history schema {actualVersion} is newer than supported schema {supportedVersion}.")
{
    public int ActualVersion { get; } = actualVersion;

    public int SupportedVersion { get; } = supportedVersion;
}

public sealed class QuotaResetHistoryStore
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultFileName = "quota-resets.v1.json";

    private const int MaximumDocumentBytes = 2 * 1024 * 1024;
    private const int MaximumWindows = 128;
    private const int MaximumResetRecords = 1024;
    private const decimal FullRemainingTolerancePercent = 0.01m;
    private static readonly TimeSpan ScheduleTolerance = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 16,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly VersionedDocumentFile _document;

    public QuotaResetHistoryStore(string documentPath, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        _document = new VersionedDocumentFile(
            documentPath,
            "TokenUsage.QuotaResetHistory",
            clock ?? TimeProvider.System,
            "Timed out while waiting for the quota reset history lock.");
    }

    public string DocumentPath => _document.DocumentPath;

    public Task<QuotaResetHistory> LoadAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(LoadCore, cancellationToken);

    public Task<QuotaResetHistory> ObserveAsync(
        ProviderSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _document.RunLockedAsync(() => ObserveCore(snapshot), cancellationToken);
    }

    private QuotaResetHistory ObserveCore(ProviderSnapshot snapshot)
    {
        QuotaResetHistory history = LoadCore();
        var windows = history.Windows.ToDictionary(
            item => (item.ProviderId, item.MetricId),
            item => item);
        var resets = history.Resets.ToList();
        IReadOnlyDictionary<string, decimal> durations = snapshot.Metrics
            .OfType<ScalarMetricSnapshot>()
            .Where(metric => metric.Id.Value.EndsWith(
                ".window-minutes",
                StringComparison.Ordinal))
            .ToDictionary(
                metric => metric.Id.Value[..^".window-minutes".Length],
                metric => metric.Value,
                StringComparer.Ordinal);
        ProgressMetricSnapshot[] progressMetrics = snapshot.Metrics
            .OfType<ProgressMetricSnapshot>()
            .Where(metric => metric.Id.Value.StartsWith("quota.", StringComparison.Ordinal))
            .ToArray();
        if (snapshot.Coverage == CoverageKind.Complete)
        {
            var currentMetricIds = progressMetrics
                .Select(metric => metric.Id.Value)
                .ToHashSet(StringComparer.Ordinal);
            foreach ((string providerId, string metricId) in windows.Keys
                         .Where(key => string.Equals(
                                 key.ProviderId,
                                 snapshot.ProviderId.Value,
                                 StringComparison.Ordinal)
                             && !currentMetricIds.Contains(key.MetricId))
                         .ToArray())
            {
                windows.Remove((providerId, metricId));
            }
        }

        foreach (ProgressMetricSnapshot metric in progressMetrics)
        {
            string providerId = snapshot.ProviderId.Value;
            string metricId = metric.Id.Value;
            decimal usedPercent = decimal.Round(
                Math.Clamp(metric.Used / metric.Limit * 100m, 0m, 100m),
                4,
                MidpointRounding.AwayFromZero);
            decimal? durationMinutes = durations.GetValueOrDefault(metricId) is > 0m and var duration
                ? duration
                : null;
            var key = (providerId, metricId);

            if (!windows.TryGetValue(key, out QuotaResetWindowState? previous))
            {
                windows[key] = new QuotaResetWindowState(
                    providerId,
                    metricId,
                    usedPercent,
                    snapshot.SourceObservedAtUtc,
                    InferCycleStart(snapshot.SourceObservedAtUtc, metric.ResetsAtUtc, durationMinutes),
                    metric.ResetsAtUtc,
                    durationMinutes);
                continue;
            }

            if (snapshot.SourceObservedAtUtc <= previous.ObservedAtUtc)
            {
                continue;
            }

            bool sameWindowDuration = QuotaResetCycleQuery.SameWindowDuration(
                previous.WindowDurationMinutes,
                durationMinutes ?? previous.WindowDurationMinutes);
            ResetDetection? detection = sameWindowDuration
                ? DetectReset(
                    previous,
                    usedPercent,
                    snapshot.SourceObservedAtUtc,
                    metric.ResetsAtUtc)
                : null;
            DateTimeOffset previousCycleStart = TryInferCycleStart(
                    previous.ObservedAtUtc,
                    previous.ExpectedResetAtUtc,
                    previous.WindowDurationMinutes)
                ?? previous.CurrentCycleStartedAtUtc;
            decimal? currentDuration = durationMinutes ?? previous.WindowDurationMinutes;
            DateTimeOffset cycleStart = TryInferCycleStart(
                    snapshot.SourceObservedAtUtc,
                    metric.ResetsAtUtc,
                    currentDuration)
                ?? previous.CurrentCycleStartedAtUtc;
            if (detection is not null)
            {
                resets.Add(new QuotaResetRecord(
                    providerId,
                    metricId,
                    detection.OccurredAtUtc,
                    snapshot.SourceObservedAtUtc,
                    previousCycleStart,
                    previous.ObservedAtUtc,
                    previous.UsedPercent,
                    usedPercent,
                    previous.ExpectedResetAtUtc,
                    metric.ResetsAtUtc,
                    previous.WindowDurationMinutes,
                    detection.Kind));
            }

            windows[key] = previous with
            {
                UsedPercent = usedPercent,
                ObservedAtUtc = snapshot.SourceObservedAtUtc,
                CurrentCycleStartedAtUtc = cycleStart,
                ExpectedResetAtUtc = metric.ResetsAtUtc,
                WindowDurationMinutes = currentDuration,
            };
        }

        QuotaResetHistory updated = new(
            windows.Values
                .OrderBy(item => item.ProviderId, StringComparer.Ordinal)
                .ThenBy(item => item.MetricId, StringComparer.Ordinal)
                .Take(MaximumWindows)
                .ToArray(),
            resets
                .OrderByDescending(item => item.OccurredAtUtc)
                .Take(MaximumResetRecords)
                .OrderBy(item => item.OccurredAtUtc)
                .ToArray());
        Write(updated);
        return updated;
    }

    private QuotaResetHistory LoadCore()
    {
        if (!_document.Exists)
        {
            return QuotaResetHistory.Empty;
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaximumDocumentBytes);
            ReadOnlyMemory<byte> json = VersionedDocumentFile.RemoveUtf8Preamble(bytes);
            using JsonDocument parsed = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = SerializerOptions.MaxDepth });
            if (!parsed.RootElement.TryGetProperty("schemaVersion", out JsonElement versionElement)
                || !versionElement.TryGetInt32(out int schemaVersion))
            {
                return QuarantineInvalid();
            }

            if (schemaVersion > CurrentSchemaVersion)
            {
                throw new QuotaResetHistoryVersionException(
                    schemaVersion,
                    CurrentSchemaVersion);
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                return QuarantineInvalid();
            }

            DocumentV1? document = JsonSerializer.Deserialize<DocumentV1>(
                json.Span,
                SerializerOptions);
            return document is null ? QuarantineInvalid() : FromDocument(document);
        }
        catch (QuotaResetHistoryVersionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
            or VersionedDocumentFormatException
            or InvalidDataException
            or ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            return QuarantineInvalid();
        }
    }

    private void Write(QuotaResetHistory history)
    {
        var document = new DocumentV1
        {
            SchemaVersion = CurrentSchemaVersion,
            Windows = history.Windows.ToList(),
            Resets = history.Resets.ToList(),
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        _document.WriteAtomically(bytes, MaximumDocumentBytes);
    }

    private QuotaResetHistory QuarantineInvalid()
    {
        _document.QuarantineCorrupt();
        return QuotaResetHistory.Empty;
    }

    private static QuotaResetHistory FromDocument(DocumentV1 document)
    {
        QuotaResetWindowState[] windows = document.Windows?.ToArray() ?? [];
        QuotaResetRecord[] resets = document.Resets?.ToArray() ?? [];
        if (windows.Length > MaximumWindows
            || resets.Length > MaximumResetRecords
            || windows.Any(item => !IsValid(item))
            || resets.Any(item => !IsValid(item))
            || windows.GroupBy(
                    item => (item.ProviderId, item.MetricId),
                    EqualityComparer<(string, string)>.Default)
                .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("Quota reset history contains invalid records.");
        }

        return new QuotaResetHistory(windows, resets);
    }

    private static bool IsValid(QuotaResetWindowState item) =>
        IsValidId(item.ProviderId)
        && IsValidId(item.MetricId)
        && item.UsedPercent is >= 0m and <= 100m
        && IsUtc(item.ObservedAtUtc)
        && IsUtc(item.CurrentCycleStartedAtUtc)
        && item.CurrentCycleStartedAtUtc <= item.ObservedAtUtc
        && (item.ExpectedResetAtUtc is null || IsUtc(item.ExpectedResetAtUtc.Value))
        && (item.WindowDurationMinutes is null || item.WindowDurationMinutes > 0m);

    private static bool IsValid(QuotaResetRecord item) =>
        IsValidId(item.ProviderId)
        && IsValidId(item.MetricId)
        && item.PreviousUsedPercent is >= 0m and <= 100m
        && item.CurrentUsedPercent is >= 0m and <= 100m
        && IsUtc(item.OccurredAtUtc)
        && IsUtc(item.DetectedAtUtc)
        && IsUtc(item.PreviousCycleStartedAtUtc)
        && IsUtc(item.PreviousObservedAtUtc)
        && item.PreviousCycleStartedAtUtc <= item.OccurredAtUtc
        && item.PreviousObservedAtUtc <= item.DetectedAtUtc
        && item.OccurredAtUtc <= item.DetectedAtUtc
        && (item.PreviousExpectedResetAtUtc is null
            || IsUtc(item.PreviousExpectedResetAtUtc.Value))
        && (item.CurrentExpectedResetAtUtc is null
            || IsUtc(item.CurrentExpectedResetAtUtc.Value))
        && (item.WindowDurationMinutes is null || item.WindowDurationMinutes > 0m)
        && Enum.IsDefined(item.DetectionKind);

    private static bool IsValidId(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '.' or '-');

    private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    private static DateTimeOffset InferCycleStart(
        DateTimeOffset observedAtUtc,
        DateTimeOffset? expectedResetAtUtc,
        decimal? durationMinutes) =>
        TryInferCycleStart(observedAtUtc, expectedResetAtUtc, durationMinutes)
        ?? observedAtUtc;

    private static DateTimeOffset? TryInferCycleStart(
        DateTimeOffset observedAtUtc,
        DateTimeOffset? expectedResetAtUtc,
        decimal? durationMinutes)
    {
        if (expectedResetAtUtc is not null && durationMinutes is > 0m)
        {
            DateTimeOffset inferred = expectedResetAtUtc.Value.AddMinutes(-(double)durationMinutes.Value);
            if (inferred <= observedAtUtc)
            {
                return inferred;
            }
        }

        return null;
    }

    private static ResetDetection? DetectReset(
        QuotaResetWindowState previous,
        decimal currentUsedPercent,
        DateTimeOffset observedAtUtc,
        DateTimeOffset? currentExpectedResetAtUtc)
    {
        bool resetTimeAdvanced = previous.ExpectedResetAtUtc is not null
            && currentExpectedResetAtUtc is not null
            && currentExpectedResetAtUtc.Value
                > previous.ExpectedResetAtUtc.Value + ScheduleTolerance;
        bool crossedExpectedReset = previous.ExpectedResetAtUtc is not null
            && previous.ObservedAtUtc < previous.ExpectedResetAtUtc.Value
            && observedAtUtc >= previous.ExpectedResetAtUtc.Value;
        if (resetTimeAdvanced && crossedExpectedReset)
        {
            return new ResetDetection(
                previous.ExpectedResetAtUtc!.Value,
                QuotaResetDetectionKind.Scheduled);
        }

        bool returnedToFullRemaining = previous.UsedPercent > FullRemainingTolerancePercent
            && currentUsedPercent <= FullRemainingTolerancePercent;
        if (!returnedToFullRemaining)
        {
            return null;
        }

        bool happenedBeforeSchedule = previous.ExpectedResetAtUtc is not null
            && observedAtUtc + ScheduleTolerance < previous.ExpectedResetAtUtc.Value;
        return new ResetDetection(
            observedAtUtc,
            happenedBeforeSchedule
                ? QuotaResetDetectionKind.Early
                : QuotaResetDetectionKind.Observed);
    }

    private sealed record ResetDetection(
        DateTimeOffset OccurredAtUtc,
        QuotaResetDetectionKind Kind);

    private sealed class DocumentV1
    {
        public int SchemaVersion { get; set; }

        public List<QuotaResetWindowState>? Windows { get; set; }

        public List<QuotaResetRecord>? Resets { get; set; }
    }
}
