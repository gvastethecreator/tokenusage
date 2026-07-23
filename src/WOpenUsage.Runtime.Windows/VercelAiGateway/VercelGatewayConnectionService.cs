using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Runtime.Windows.VercelAiGateway;

public enum VercelGatewayCacheCleanupStatus
{
    Removed,
    Missing,
    Quarantined,
    RefusedUnsupportedVersion,
    IoFailure,
    AccessDenied,
    LockTimedOut,
    Rejected,
}

public sealed record VercelGatewayDisconnectResult
{
    public VercelGatewayDisconnectResult(
        bool credentialRemoved,
        VercelGatewayCacheCleanupStatus cacheStatus,
        string? quarantineFileName = null,
        int? unsupportedSchemaVersion = null)
    {
        if (!Enum.IsDefined(cacheStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(cacheStatus));
        }

        if (quarantineFileName is not null
            && !string.Equals(
                quarantineFileName,
                Path.GetFileName(quarantineFileName),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The quarantine value must contain a file name only.",
                nameof(quarantineFileName));
        }

        if (cacheStatus == VercelGatewayCacheCleanupStatus.Quarantined
            != (quarantineFileName is not null))
        {
            throw new ArgumentException(
                "Quarantined cache cleanup requires a quarantine file name only.",
                nameof(quarantineFileName));
        }

        if (cacheStatus == VercelGatewayCacheCleanupStatus.RefusedUnsupportedVersion
            != unsupportedSchemaVersion.HasValue)
        {
            throw new ArgumentException(
                "Unsupported cache cleanup requires its schema version.",
                nameof(unsupportedSchemaVersion));
        }

        if (unsupportedSchemaVersion <= SnapshotStore.CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(unsupportedSchemaVersion));
        }

        CredentialRemoved = credentialRemoved;
        CacheStatus = cacheStatus;
        QuarantineFileName = quarantineFileName;
        UnsupportedSchemaVersion = unsupportedSchemaVersion;
    }

    public bool CredentialRemoved { get; }

    public VercelGatewayCacheCleanupStatus CacheStatus { get; }

    public string? QuarantineFileName { get; }

    public int? UnsupportedSchemaVersion { get; }

    public bool IsComplete => CacheStatus is VercelGatewayCacheCleanupStatus.Removed
        or VercelGatewayCacheCleanupStatus.Missing;
}

public sealed class VercelGatewayConnectionService
{
    private static readonly ProviderId ProviderId = new("vercel-ai-gateway");

    private readonly IVercelGatewayCredentialStore _credentialStore;
    private readonly SnapshotStore _snapshotStore;
    private readonly ProviderOperationGate _operationGate;

    public VercelGatewayConnectionService(
        IVercelGatewayCredentialStore credentialStore,
        SnapshotStore snapshotStore,
        ProviderOperationGate operationGate)
    {
        _credentialStore = credentialStore
            ?? throw new ArgumentNullException(nameof(credentialStore));
        _snapshotStore = snapshotStore
            ?? throw new ArgumentNullException(nameof(snapshotStore));
        _operationGate = operationGate
            ?? throw new ArgumentNullException(nameof(operationGate));
    }

    public async Task<VercelGatewayDisconnectResult> DisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        await using IAsyncDisposable lease = await _operationGate
            .EnterAsync(cancellationToken)
            .ConfigureAwait(false);

        bool credentialRemoved = await _credentialStore
            .DeleteAsync(CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            SnapshotCacheRemoveResult cacheResult = await _snapshotStore
                .RemoveProviderAsync(ProviderId, CancellationToken.None)
                .ConfigureAwait(false);
            return MapResult(credentialRemoved, cacheResult);
        }
        catch (IOException)
        {
            return Failure(credentialRemoved, VercelGatewayCacheCleanupStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(credentialRemoved, VercelGatewayCacheCleanupStatus.AccessDenied);
        }
        catch (TimeoutException)
        {
            return Failure(credentialRemoved, VercelGatewayCacheCleanupStatus.LockTimedOut);
        }
        catch (InvalidOperationException)
        {
            return Failure(credentialRemoved, VercelGatewayCacheCleanupStatus.Rejected);
        }
    }

    private static VercelGatewayDisconnectResult MapResult(
        bool credentialRemoved,
        SnapshotCacheRemoveResult cacheResult) => cacheResult switch
        {
            SnapshotCacheRemoveResult.Removed =>
                new(credentialRemoved, VercelGatewayCacheCleanupStatus.Removed),
            SnapshotCacheRemoveResult.Missing =>
                new(credentialRemoved, VercelGatewayCacheCleanupStatus.Missing),
            SnapshotCacheRemoveResult.Unreadable unreadable =>
                new(
                    credentialRemoved,
                    VercelGatewayCacheCleanupStatus.Quarantined,
                    quarantineFileName: unreadable.QuarantineFileName),
            SnapshotCacheRemoveResult.RefusedUnsupportedVersion unsupported =>
                new(
                    credentialRemoved,
                    VercelGatewayCacheCleanupStatus.RefusedUnsupportedVersion,
                    unsupportedSchemaVersion: unsupported.SchemaVersion),
            _ => Failure(credentialRemoved, VercelGatewayCacheCleanupStatus.Rejected),
        };

    private static VercelGatewayDisconnectResult Failure(
        bool credentialRemoved,
        VercelGatewayCacheCleanupStatus status) => new(credentialRemoved, status);
}
