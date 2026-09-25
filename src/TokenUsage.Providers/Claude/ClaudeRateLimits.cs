using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.Claude;

/// <summary>
/// One subscription window Claude Code reports to its status line command.
/// <see cref="Key"/> is the documented field name (<c>five_hour</c>, <c>seven_day</c>,
/// or <c>spend_limit</c>).
/// </summary>
public sealed record ClaudeRateLimitWindow(string Key, decimal UsedPercent, DateTimeOffset ResetsAtUtc);

public sealed record ClaudeRateLimitSnapshot(
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<ClaudeRateLimitWindow> Windows);

/// <summary>
/// Reads the documented <c>rate_limits</c> object from the JSON Claude Code pipes into a
/// status line command (Claude Code 2.1.251 or later). Every other field of that payload,
/// including session, path, model, and transcript data, is ignored and never returned.
/// See https://code.claude.com/docs/en/statusline#rate-limit-usage.
/// </summary>
public static class ClaudeStatusLineRateLimits
{
    public const int MaximumPayloadBytes = 1024 * 1024;
    public const string FiveHourKey = "five_hour";
    public const string SevenDayKey = "seven_day";
    public const string SpendLimitKey = "spend_limit";

    private const decimal MaximumUsedPercent = 10_000m;
    private static readonly string[] WindowKeys = [FiveHourKey, SevenDayKey, SpendLimitKey];

    /// <summary>
    /// Returns false when the payload is not JSON or carries no <c>rate_limits</c> object:
    /// Claude Code omits it for API-key accounts and before the first response of a session,
    /// so absence is not evidence that a stored reading became wrong.
    /// </summary>
    public static bool TryParse(
        ReadOnlySpan<byte> utf8,
        DateTimeOffset observedAtUtc,
        out ClaudeRateLimitSnapshot? snapshot)
    {
        snapshot = null;
        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The observation time must use the UTC offset.", nameof(observedAtUtc));
        }

        if (utf8.IsEmpty || utf8.Length > MaximumPayloadBytes)
        {
            return false;
        }

        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 64 });
            if (!JsonDocument.TryParseValue(ref reader, out JsonDocument? document))
            {
                return false;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("rate_limits", out JsonElement limits)
                    || limits.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var windows = new List<ClaudeRateLimitWindow>(WindowKeys.Length);
                foreach (string key in WindowKeys)
                {
                    if (TryReadWindow(limits, key, out ClaudeRateLimitWindow? window))
                    {
                        windows.Add(window!);
                    }
                }

                snapshot = new ClaudeRateLimitSnapshot(observedAtUtc, windows.AsReadOnly());
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadWindow(
        JsonElement limits,
        string key,
        out ClaudeRateLimitWindow? window)
    {
        window = null;
        if (!limits.TryGetProperty(key, out JsonElement element)
            || element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("used_percentage", out JsonElement used)
            || used.ValueKind != JsonValueKind.Number
            || !used.TryGetDecimal(out decimal usedPercent)
            || usedPercent < 0m
            || usedPercent > MaximumUsedPercent
            || !element.TryGetProperty("resets_at", out JsonElement resets)
            || resets.ValueKind != JsonValueKind.Number
            || !resets.TryGetDouble(out double resetsAtSeconds)
            || !double.IsFinite(resetsAtSeconds)
            || resetsAtSeconds <= 0
            || resetsAtSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        window = new ClaudeRateLimitWindow(
            key,
            usedPercent,
            DateTimeOffset.FromUnixTimeSeconds((long)Math.Floor(resetsAtSeconds)));
        return true;
    }
}

/// <summary>
/// App-owned numeric sidecar holding the last Claude Code subscription reading. It stores the
/// observation time and, per window, the used percentage and reset time. Nothing else from the
/// status line payload is written.
/// </summary>
public sealed class ClaudeRateLimitStore
{
    public const string FileName = "rate-limits.v1.json";
    private const int SchemaVersion = 1;
    private const int MaximumDocumentBytes = 64 * 1024;
    private static readonly TimeSpan UnchangedRewriteInterval = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 8,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ClaudeRateLimitStore(string documentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        DocumentPath = Path.GetFullPath(documentPath);
    }

    public string DocumentPath { get; }

    public static string DefaultPath(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        return Path.Combine(dataDirectory, "cache", "providers", "claude", FileName);
    }

    public ClaudeRateLimitSnapshot? Load()
    {
        try
        {
            var info = new FileInfo(DocumentPath);
            if (!info.Exists || info.Length > MaximumDocumentBytes)
            {
                return null;
            }

            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(DocumentPath, Encoding.UTF8),
                SerializerOptions);
            if (document is null
                || document.SchemaVersion != SchemaVersion
                || document.Windows is null
                || document.ObservedAtUtc.Offset != TimeSpan.Zero)
            {
                return null;
            }

            ClaudeRateLimitWindow[] windows = document.Windows
                .Where(window => window is not null
                    && !string.IsNullOrWhiteSpace(window.Key)
                    && window.UsedPercent >= 0m
                    && window.ResetsAtUtc.Offset == TimeSpan.Zero)
                .Select(window => new ClaudeRateLimitWindow(
                    window.Key!,
                    window.UsedPercent,
                    window.ResetsAtUtc))
                .ToArray();
            return new ClaudeRateLimitSnapshot(document.ObservedAtUtc, windows);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a reading. Claude Code reruns its status line many times a minute, so an
    /// unchanged reading only refreshes the observation time once a minute.
    /// </summary>
    public bool Save(ClaudeRateLimitSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ClaudeRateLimitSnapshot? current = Load();
        if (current is not null
            && current.Windows.SequenceEqual(snapshot.Windows)
            && snapshot.ObservedAtUtc - current.ObservedAtUtc < UnchangedRewriteInterval
            && snapshot.ObservedAtUtc >= current.ObservedAtUtc)
        {
            return false;
        }

        var document = new Document(
            SchemaVersion,
            snapshot.ObservedAtUtc,
            snapshot.Windows
                .Select(window => new WindowDocument(window.Key, window.UsedPercent, window.ResetsAtUtc))
                .ToArray());
        string directory = Path.GetDirectoryName(DocumentPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = DocumentPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, SerializerOptions),
                new UTF8Encoding(false));
            File.Move(temporaryPath, DocumentPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return true;
    }

    public void Delete()
    {
        if (File.Exists(DocumentPath))
        {
            File.Delete(DocumentPath);
        }
    }

    private sealed record Document(
        int SchemaVersion,
        DateTimeOffset ObservedAtUtc,
        WindowDocument[]? Windows);

    private sealed record WindowDocument(
        string? Key,
        decimal UsedPercent,
        DateTimeOffset ResetsAtUtc);
}

/// <summary>
/// Maps a stored Claude Code reading onto the shared quota snapshot contract, so the reset
/// history, the quota journal, and every quota surface treat Claude like Codex.
/// </summary>
public static class ClaudeRateLimitSnapshotMapper
{
    public const string AdapterVersion = "claude-statusline/1";
    public const string FiveHourMetricId = "quota.five-hour";
    public const string SevenDayMetricId = "quota.seven-day";
    public const string SpendLimitMetricId = "quota.spend-limit";
    private const int AdapterContractVersion = 1;
    private static readonly ProviderId ClaudeProviderId = new("claude");

    /// <summary>
    /// Returns null when no window is still open. Claude Code drops a window from its own
    /// payload once <c>resets_at</c> passes, and a passed window says nothing about the new one.
    /// </summary>
    public static ProviderSnapshot? Map(
        ClaudeRateLimitSnapshot reading,
        DateTimeOffset nowUtc,
        string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        nowUtc = nowUtc.ToUniversalTime();
        var provenance = new DataProvenance(
            SourceKind.OfficialLocalApi,
            MeasurementKind.ProviderReported,
            AdapterVersion);
        var metrics = new List<MetricSnapshot>();
        foreach (ClaudeRateLimitWindow window in reading.Windows
                     .Where(window => window.ResetsAtUtc > nowUtc)
                     .DistinctBy(window => window.Key, StringComparer.Ordinal))
        {
            (string? metricId, decimal? minutes) = window.Key switch
            {
                ClaudeStatusLineRateLimits.FiveHourKey => (FiveHourMetricId, 5m * 60m),
                ClaudeStatusLineRateLimits.SevenDayKey => (SevenDayMetricId, 7m * 24m * 60m),
                ClaudeStatusLineRateLimits.SpendLimitKey => (SpendLimitMetricId, (decimal?)null),
                _ => ((string?)null, (decimal?)null),
            };
            if (metricId is null)
            {
                continue;
            }

            metrics.Add(new ProgressMetricSnapshot(
                new MetricId(metricId),
                window.UsedPercent,
                100m,
                window.ResetsAtUtc,
                provenance,
                unit: "percent",
                labelEvidence: new MetricLabelEvidence(
                    window.Key,
                    providerMetricId: null,
                    providerMetricName: null,
                    MetricLabelSource.Provider,
                    MetricLabelConfidence.Exact)));
            if (minutes is decimal windowMinutes)
            {
                metrics.Add(new ScalarMetricSnapshot(
                    new MetricId(metricId + ".window-minutes"),
                    windowMinutes,
                    "minutes",
                    provenance));
            }
        }

        if (metrics.Count == 0)
        {
            return null;
        }

        DateTimeOffset observed = reading.ObservedAtUtc;
        return new ProviderSnapshot(
            ClaudeProviderId,
            "Claude",
            planLabel: null,
            fetchedAtUtc: observed > nowUtc ? observed : nowUtc,
            sourceObservedAtUtc: observed,
            timeZoneId,
            metrics,
            CoverageKind.Complete,
            AdapterContractVersion);
    }
}
