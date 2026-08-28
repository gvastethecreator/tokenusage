using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Storage;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Grok;

/// <summary>
/// Persists the events parsed from each Grok session file so a refresh can skip
/// files whose length and last-write time did not change. Only the hashed file
/// path is stored; no raw local paths, session content, or identities.
/// </summary>
internal sealed class GrokUsageCheckpointStore
{
    private const int SchemaVersion = 1;
    private const int MaximumDocumentBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = 16,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly VersionedDocumentFile _document;
    private readonly string _parserVersion;
    private readonly string _groupingTimeZoneId;

    public GrokUsageCheckpointStore(
        string path,
        TimeProvider clock,
        string parserVersion,
        string groupingTimeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parserVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _document = new VersionedDocumentFile(
            path,
            "TokenUsage.GrokUsageCheckpoint",
            clock,
            "Timed out while waiting for the Grok usage checkpoint lock.");
        _parserVersion = parserVersion;
        _groupingTimeZoneId = groupingTimeZoneId;
    }

    public Task<TResult> UpdateAsync<TResult>(
        Func<GrokUsageCheckpointState, TResult> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return _document.RunLockedAsync(() =>
        {
            GrokUsageCheckpointState state = Load();
            TResult result = update(state);
            Write(state);
            return result;
        }, cancellationToken);
    }

    public static string HashPath(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))
            .ToLowerInvariant();

    private GrokUsageCheckpointState Load()
    {
        if (!_document.Exists)
        {
            return new GrokUsageCheckpointState();
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaximumDocumentBytes);
            DocumentV1? document = JsonSerializer.Deserialize<DocumentV1>(
                VersionedDocumentFile.RemoveUtf8Preamble(bytes).Span,
                SerializerOptions);
            if (document is null
                || document.SchemaVersion != SchemaVersion
                || document.ParserVersion != _parserVersion
                || document.GroupingTimeZoneId != _groupingTimeZoneId
                || document.Files is null)
            {
                return new GrokUsageCheckpointState();
            }

            var state = new GrokUsageCheckpointState();
            foreach (FileV1 file in document.Files)
            {
                if (string.IsNullOrWhiteSpace(file.PathHash)
                    || file.Length < 0
                    || file.Events is null
                    || file.Events.Count == 0)
                {
                    continue;
                }

                DateTimeOffset? lastWrite = ParseUtc(file.LastWriteUtc);
                if (lastWrite is null)
                {
                    continue;
                }

                var events = new List<UsageEvent>(file.Events.Count);
                foreach (EventV1 cached in file.Events)
                {
                    if (!TryRehydrate(cached, out UsageEvent? rehydrated))
                    {
                        events = null!;
                        break;
                    }

                    events.Add(rehydrated!);
                }

                if (events is null)
                {
                    continue;
                }

                state.Files[file.PathHash] = new GrokCachedFile(
                    file.Length,
                    lastWrite.Value,
                    events);
            }

            return state;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or PathTooLongException
                                           or ArgumentException
                                           or System.Security.SecurityException
                                           or JsonException
                                           or InvalidOperationException)
        {
            return new GrokUsageCheckpointState();
        }
    }

    private void Write(GrokUsageCheckpointState state)
    {
        var files = new List<FileV1>(state.Files.Count);
        foreach ((string pathHash, GrokCachedFile file) in state.Files)
        {
            var events = new List<EventV1>(file.Events.Count);
            foreach (UsageEvent usageEvent in file.Events)
            {
                CostObservation cost = usageEvent.Cost;
                long? costMicros = cost.Kind switch
                {
                    CostKind.ProviderReported when cost.ReportedCostUsd is decimal reported
                        => decimal.ToInt64(
                            decimal.Round(reported * 1_000_000m, 0, MidpointRounding.AwayFromZero)),
                    CostKind.CatalogEstimated when cost.EstimatedCostUsd is decimal estimated
                        => decimal.ToInt64(
                            decimal.Round(estimated * 1_000_000m, 0, MidpointRounding.AwayFromZero)),
                    _ => null,
                };
                events.Add(new EventV1(
                    usageEvent.EventKey.Value,
                    usageEvent.ModelId.Value,
                    usageEvent.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    usageEvent.Tokens.Input,
                    usageEvent.Tokens.Output,
                    usageEvent.Tokens.Reasoning,
                    usageEvent.Tokens.CacheRead,
                    usageEvent.Tokens.CacheWrite,
                    (int)cost.Kind,
                    costMicros,
                    cost.CatalogVersion,
                    cost.ExactPriceMatch,
                    (int)usageEvent.Coverage));
            }

            files.Add(new FileV1(
                pathHash,
                file.Length,
                file.LastWriteUtc.ToString("O", CultureInfo.InvariantCulture),
                events));
        }

        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(
            new DocumentV1(
                SchemaVersion,
                _parserVersion,
                _groupingTimeZoneId,
                files),
            SerializerOptions);
        try
        {
            _document.WriteAtomically(serialized, MaximumDocumentBytes);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or PathTooLongException
                                           or ArgumentException
                                           or System.Security.SecurityException)
        {
        }
    }

    private bool TryRehydrate(EventV1 cached, out UsageEvent? rehydrated)
    {
        rehydrated = null;
        if (string.IsNullOrWhiteSpace(cached.Key)
            || string.IsNullOrWhiteSpace(cached.ModelId)
            || cached.Input < 0
            || cached.Output < 0
            || cached.Reasoning < 0
            || cached.CacheRead < 0
            || cached.CacheWrite < 0)
        {
            return false;
        }

        DateTimeOffset? occurredAt = ParseUtc(cached.OccurredAtUtc);
        if (occurredAt is null
            || !Enum.IsDefined((CostKind)cached.CostKind)
            || !Enum.IsDefined((CoverageKind)cached.CoverageKind))
        {
            return false;
        }

        CostObservation cost = (CostKind)cached.CostKind switch
        {
            CostKind.ProviderReported => CostObservation.ProviderReported(
                (cached.CostMicros ?? 0) / 1_000_000m),
            CostKind.CatalogEstimated when cached.CatalogVersion is not null
                                           && cached.ExactPriceMatch is not null
                => CostObservation.CatalogEstimated(
                    (cached.CostMicros ?? 0) / 1_000_000m,
                    cached.CatalogVersion,
                    cached.ExactPriceMatch),
            _ => CostObservation.Unavailable(),
        };

        var tokens = new TokenBreakdown(
            cached.Input,
            cached.Output,
            cached.Reasoning,
            cached.CacheRead,
            cached.CacheWrite);
        rehydrated = new UsageEvent(
            new UsageEventKey(cached.Key),
            AgentId,
            ProviderId,
            new ModelId(cached.ModelId),
            occurredAt.Value,
            _groupingTimeZoneId,
            tokens,
            cost,
            _parserVersion,
            (CoverageKind)cached.CoverageKind);
        return true;
    }

    private static AgentId AgentId { get; } = new("grok");

    private static ModelProviderId ProviderId { get; } = new("xai");

    private static DateTimeOffset? ParseUtc(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
                   text,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out DateTimeOffset parsed)
               && parsed.Offset == TimeSpan.Zero
            ? parsed
            : null;
    }

    private sealed record DocumentV1(
        int SchemaVersion,
        string ParserVersion,
        string GroupingTimeZoneId,
        List<FileV1> Files);

    private sealed record FileV1(
        string PathHash,
        long Length,
        string LastWriteUtc,
        List<EventV1> Events);

    private sealed record EventV1(
        string Key,
        string ModelId,
        string OccurredAtUtc,
        long Input,
        long Output,
        long Reasoning,
        long CacheRead,
        long CacheWrite,
        int CostKind,
        long? CostMicros,
        string? CatalogVersion,
        string? ExactPriceMatch,
        int CoverageKind);
}

internal sealed class GrokUsageCheckpointState
{
    public Dictionary<string, GrokCachedFile> Files { get; } =
        new(StringComparer.Ordinal);

    public void PruneExcept(HashSet<string> seenPathHashes)
    {
        ArgumentNullException.ThrowIfNull(seenPathHashes);
        foreach (string stale in Files.Keys
                     .Where(key => !seenPathHashes.Contains(key))
                     .ToArray())
        {
            Files.Remove(stale);
        }
    }
}

internal sealed record GrokCachedFile(
    long Length,
    DateTimeOffset LastWriteUtc,
    IReadOnlyList<UsageEvent> Events);
