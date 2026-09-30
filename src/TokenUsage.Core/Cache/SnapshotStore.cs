using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Cache.Internal;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Cache;

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
    private readonly ProviderInstanceKey? _scope;

    public SnapshotStore(string documentPath)
        : this(documentPath, TimeProvider.System)
    {
    }

    public SnapshotStore(string documentPath, TimeProvider clock, ProviderInstanceKey? scope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _scope = scope;
        _document = new VersionedDocumentFile(
            documentPath,
            mutexNamePrefix: "TokenUsage.SnapshotStore",
            clock,
            lockTimeoutMessage: "Timed out while waiting for the snapshot cache lock.");
    }

    public string DocumentPath => _document.DocumentPath;

    public ProviderInstanceKey? Scope => _scope;

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
        if (snapshotArray.Any(snapshot => snapshot is not null
            && (_scope is null ? snapshot.InstanceKey.AccountKey is not null : snapshot.InstanceKey != _scope)))
        {
            throw new InvalidOperationException("The snapshot does not belong to this store.");
        }
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
        if (!(_scope is null ? _document.Exists : _document.ExistsStrict))
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

            return new SnapshotCacheReadResult.Loaded(ReadSnapshots(document));
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

            IReadOnlyList<ProviderSnapshot> snapshots = ReadSnapshots(document);
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
        if (_scope is not null && current is SnapshotCacheReadResult.Corrupt)
        {
            throw new IOException("The account cache is unreadable and was preserved.");
        }
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
        document.ProviderId = _scope?.ProviderId.Value;
        document.AccountKey = _scope?.AccountKey;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        _document.WriteAtomically(bytes, MaximumDocumentBytes);
    }

    private SnapshotCacheReadResult.Corrupt QuarantineCorrupt() =>
        new(_scope is null ? _document.QuarantineCorrupt() : Path.GetFileName(DocumentPath));

    private IReadOnlyList<ProviderSnapshot> ReadSnapshots(SnapshotCacheDocumentV1 document)
    {
        if (document.AccountKey != _scope?.AccountKey || document.ProviderId != _scope?.ProviderId.Value)
        {
            throw new IOException("The cache belongs to a different provider account.");
        }

        IReadOnlyList<ProviderSnapshot> snapshots = SnapshotCacheMapper.FromDocument(document);
        if (_scope is null) return snapshots;
        if (snapshots.Any(snapshot => snapshot.ProviderId != _scope.ProviderId))
            throw new IOException("The account cache contains a different provider.");
        return snapshots.Select(snapshot => snapshot.ForAccount(_scope.AccountKey!)).ToArray();
    }

    private static bool IsInvalidDocument(Exception exception) =>
        exception is JsonException
            or SnapshotCacheFormatException
            or VersionedDocumentFormatException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or OverflowException;
}
