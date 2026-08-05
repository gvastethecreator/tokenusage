using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WOpenUsage.Core.Cache.Internal;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Storage;

namespace WOpenUsage.Core.Cache;

public sealed class SnapshotStore
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultFileName = "snapshots.v1.json";

    private const int MaximumDocumentBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TimeProvider _clock;
    private readonly VersionedDocumentFile _document;

    public SnapshotStore(string documentPath)
        : this(documentPath, TimeProvider.System)
    {
    }

    public SnapshotStore(string documentPath, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _document = new VersionedDocumentFile(
            documentPath,
            mutexNamePrefix: "WOpenUsage.SnapshotStore",
            clock,
            lockTimeoutMessage: "Timed out while waiting for the snapshot cache lock.");
    }

    public string DocumentPath => _document.DocumentPath;

    public Task<SnapshotCacheReadResult> LoadAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(LoadCore, cancellationToken);

    public Task<SnapshotCacheProbeResult> ProbeAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() => ProbeCore(requiredProvider: null), cancellationToken);

    public Task<SnapshotCacheProbeResult> ProbeProviderAsync(
        ProviderId providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        return _document.RunLockedAsync(() => ProbeCore(providerId), cancellationToken);
    }

    public Task<SnapshotCacheSaveResult> UpsertLastGoodAsync(
        ProviderSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return SaveLastGoodAsync([snapshot], cancellationToken);
    }

    public Task<SnapshotCacheSaveResult> SaveLastGoodAsync(
        IEnumerable<ProviderSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ProviderSnapshot[] snapshotArray = snapshots.ToArray();
        if (snapshotArray.Any(snapshot => snapshot is null))
        {
            throw new ArgumentException("Snapshots cannot contain null values.", nameof(snapshots));
        }

        string? duplicateProvider = snapshotArray
            .GroupBy(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateProvider is not null)
        {
            throw new ArgumentException(
                $"Provider '{duplicateProvider}' appears more than once.",
                nameof(snapshots));
        }

        return _document.RunLockedAsync(() => SaveCore(snapshotArray), cancellationToken);
    }

    public Task<SnapshotCacheRemoveResult> RemoveProviderAsync(
        ProviderId providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        return _document.RunLockedAsync(() => RemoveProviderCore(providerId), cancellationToken);
    }

    private SnapshotCacheReadResult LoadCore()
    {
        if (!_document.Exists)
        {
            return new SnapshotCacheReadResult.Empty();
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaximumDocumentBytes);
            ReadOnlyMemory<byte> jsonBytes = VersionedDocumentFile.RemoveUtf8Preamble(bytes);

            using JsonDocument parsed = JsonDocument.Parse(
                jsonBytes,
                new JsonDocumentOptions { MaxDepth = SerializerOptions.MaxDepth });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object
                || !parsed.RootElement.TryGetProperty("schemaVersion", out JsonElement versionElement)
                || !versionElement.TryGetInt32(out int schemaVersion))
            {
                return QuarantineCorrupt();
            }

            if (schemaVersion > CurrentSchemaVersion)
            {
                return new SnapshotCacheReadResult.UnsupportedVersion(schemaVersion);
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                return QuarantineCorrupt();
            }

            SnapshotCacheDocumentV1? document = JsonSerializer.Deserialize<SnapshotCacheDocumentV1>(
                jsonBytes.Span,
                SerializerOptions);
            if (document is null)
            {
                return QuarantineCorrupt();
            }

            return new SnapshotCacheReadResult.Loaded(SnapshotCacheMapper.FromDocument(document));
        }
        catch (Exception exception) when (IsInvalidDocument(exception))
        {
            return QuarantineCorrupt();
        }
    }

    private SnapshotCacheProbeResult ProbeCore(ProviderId? requiredProvider)
    {
        if (!_document.Exists)
        {
            return new SnapshotCacheProbeResult.Missing();
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaximumDocumentBytes);
            ReadOnlyMemory<byte> jsonBytes = VersionedDocumentFile.RemoveUtf8Preamble(bytes);
            using JsonDocument parsed = JsonDocument.Parse(
                jsonBytes,
                new JsonDocumentOptions { MaxDepth = SerializerOptions.MaxDepth });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object
                || !parsed.RootElement.TryGetProperty("schemaVersion", out JsonElement versionElement)
                || !versionElement.TryGetInt32(out int schemaVersion))
            {
                return new SnapshotCacheProbeResult.Unreadable();
            }

            if (schemaVersion > CurrentSchemaVersion)
            {
                return new SnapshotCacheProbeResult.UnsupportedVersion();
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                return new SnapshotCacheProbeResult.Unreadable();
            }

            SnapshotCacheDocumentV1? document = JsonSerializer.Deserialize<SnapshotCacheDocumentV1>(
                jsonBytes.Span,
                SerializerOptions);
            if (document is null)
            {
                return new SnapshotCacheProbeResult.Unreadable();
            }

            IReadOnlyList<ProviderSnapshot> snapshots = SnapshotCacheMapper.FromDocument(document);
            return requiredProvider is null
                   || snapshots.Any(snapshot => snapshot.ProviderId == requiredProvider)
                ? new SnapshotCacheProbeResult.Present()
                : new SnapshotCacheProbeResult.Missing();
        }
        catch (Exception exception) when (IsInvalidDocument(exception)
                                          || exception is IOException
                                          or UnauthorizedAccessException
                                          or System.Security.SecurityException)
        {
            return new SnapshotCacheProbeResult.Unreadable();
        }
    }

    private SnapshotCacheSaveResult SaveCore(IReadOnlyList<ProviderSnapshot> incoming)
    {
        SnapshotCacheReadResult current = LoadCore();
        if (current is SnapshotCacheReadResult.UnsupportedVersion unsupported)
        {
            return new SnapshotCacheSaveResult.RefusedUnsupportedVersion(unsupported.SchemaVersion);
        }

        var merged = current is SnapshotCacheReadResult.Loaded loaded
            ? loaded.Snapshots.ToDictionary(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
            : new Dictionary<string, ProviderSnapshot>(StringComparer.Ordinal);

        foreach (ProviderSnapshot snapshot in incoming)
        {
            merged[snapshot.ProviderId.Value] = snapshot;
        }

        ProviderSnapshot[] ordered = merged.Values
            .OrderBy(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
            .ToArray();
        WriteSnapshotSet(ordered);
        return new SnapshotCacheSaveResult.Saved(ordered);
    }

    private SnapshotCacheRemoveResult RemoveProviderCore(ProviderId providerId)
    {
        SnapshotCacheReadResult current = LoadCore();
        if (current is SnapshotCacheReadResult.UnsupportedVersion unsupported)
        {
            return new SnapshotCacheRemoveResult.RefusedUnsupportedVersion(
                unsupported.SchemaVersion);
        }

        if (current is SnapshotCacheReadResult.Corrupt corrupt)
        {
            return new SnapshotCacheRemoveResult.Unreadable(corrupt.QuarantineFileName);
        }

        if (current is not SnapshotCacheReadResult.Loaded loaded)
        {
            return new SnapshotCacheRemoveResult.Missing();
        }

        ProviderSnapshot[] remaining = loaded.Snapshots
            .Where(snapshot => snapshot.ProviderId != providerId)
            .OrderBy(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
            .ToArray();
        if (remaining.Length == loaded.Snapshots.Count)
        {
            return new SnapshotCacheRemoveResult.Missing();
        }

        WriteSnapshotSet(remaining);
        return new SnapshotCacheRemoveResult.Removed(remaining);
    }

    private void WriteSnapshotSet(IReadOnlyList<ProviderSnapshot> snapshots)
    {
        SnapshotCacheDocumentV1 document = SnapshotCacheMapper.ToDocument(
            snapshots,
            _clock.GetUtcNow().ToUniversalTime());
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        _document.WriteAtomically(bytes, MaximumDocumentBytes);
    }

    private SnapshotCacheReadResult.Corrupt QuarantineCorrupt() =>
        new(_document.QuarantineCorrupt());

    private static bool IsInvalidDocument(Exception exception) =>
        exception is JsonException
            or SnapshotCacheFormatException
            or VersionedDocumentFormatException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or OverflowException;
}
