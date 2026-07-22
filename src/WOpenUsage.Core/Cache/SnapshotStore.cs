using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WOpenUsage.Core.Cache.Internal;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Cache;

public sealed class SnapshotStore
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultFileName = "snapshots.v1.json";

    private const int MaximumDocumentBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TimeProvider _clock;
    private readonly string _mutexName;

    public SnapshotStore(string documentPath)
        : this(documentPath, TimeProvider.System)
    {
    }

    public SnapshotStore(string documentPath, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        DocumentPath = Path.GetFullPath(documentPath);
        if (string.IsNullOrWhiteSpace(Path.GetFileName(DocumentPath)))
        {
            throw new ArgumentException("The cache path must include a file name.", nameof(documentPath));
        }

        _mutexName = CreateMutexName(DocumentPath);
    }

    public string DocumentPath { get; }

    public Task<SnapshotCacheReadResult> LoadAsync(CancellationToken cancellationToken = default) =>
        RunLocked(LoadCore, cancellationToken);

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

        return RunLocked(() => SaveCore(snapshotArray), cancellationToken);
    }

    private SnapshotCacheReadResult LoadCore()
    {
        if (!File.Exists(DocumentPath))
        {
            return new SnapshotCacheReadResult.Empty();
        }

        try
        {
            var file = new FileInfo(DocumentPath);
            if (file.Length is <= 0 or > MaximumDocumentBytes)
            {
                return QuarantineCorrupt();
            }

            byte[] bytes = File.ReadAllBytes(DocumentPath);
            if (bytes.Length is <= 0 or > MaximumDocumentBytes)
            {
                return QuarantineCorrupt();
            }

            ReadOnlyMemory<byte> jsonBytes = HasUtf8Preamble(bytes)
                ? bytes.AsMemory(Encoding.UTF8.Preamble.Length)
                : bytes;

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
        SnapshotCacheDocumentV1 document = SnapshotCacheMapper.ToDocument(
            ordered,
            _clock.GetUtcNow().ToUniversalTime());
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        if (bytes.Length > MaximumDocumentBytes)
        {
            throw new InvalidOperationException(
                $"The cache document exceeds the {MaximumDocumentBytes}-byte limit.");
        }

        WriteAtomically(bytes);
        return new SnapshotCacheSaveResult.Saved(ordered);
    }

    private SnapshotCacheReadResult.Corrupt QuarantineCorrupt()
    {
        string directory = GetDocumentDirectory();
        string fileName = Path.GetFileName(DocumentPath);
        string stamp = _clock.GetUtcNow()
            .ToUniversalTime()
            .ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
        string quarantineFileName = $"{fileName}.corrupt-{stamp}-{Guid.NewGuid():N}";
        string quarantinePath = Path.Combine(directory, quarantineFileName);

        File.Move(DocumentPath, quarantinePath);
        return new SnapshotCacheReadResult.Corrupt(quarantineFileName);
    }

    private void WriteAtomically(byte[] bytes)
    {
        string directory = GetDocumentDirectory();
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $"{Path.GetFileName(DocumentPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = 4096,
                    Options = FileOptions.WriteThrough,
                }))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, DocumentPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private Task<TResult> RunLocked<TResult>(
        Func<TResult> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var mutex = new Mutex(initiallyOwned: false, _mutexName);
            bool ownsMutex = false;

            try
            {
                try
                {
                    if (cancellationToken.CanBeCanceled)
                    {
                        int signaled = WaitHandle.WaitAny(
                            [mutex, cancellationToken.WaitHandle],
                            MutexTimeout);
                        if (signaled == 1)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        if (signaled == WaitHandle.WaitTimeout)
                        {
                            throw new TimeoutException("Timed out while waiting for the snapshot cache lock.");
                        }

                        ownsMutex = true;
                    }
                    else
                    {
                        ownsMutex = mutex.WaitOne(MutexTimeout);
                        if (!ownsMutex)
                        {
                            throw new TimeoutException("Timed out while waiting for the snapshot cache lock.");
                        }
                    }
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                cancellationToken.ThrowIfCancellationRequested();
                return operation();
            }
            finally
            {
                if (ownsMutex)
                {
                    mutex.ReleaseMutex();
                }
            }
        });
    }

    private string GetDocumentDirectory() =>
        Path.GetDirectoryName(DocumentPath)
        ?? throw new InvalidOperationException("The cache path has no parent directory.");

    private static string CreateMutexName(string documentPath)
    {
        string normalizedPath = Path.GetFullPath(documentPath).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return $"Local\\WOpenUsage.SnapshotStore.{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }

    private static bool HasUtf8Preamble(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(Encoding.UTF8.Preamble);

    private static bool IsInvalidDocument(Exception exception) =>
        exception is JsonException
            or SnapshotCacheFormatException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or OverflowException;
}
