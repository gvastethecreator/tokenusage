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

public sealed record VercelGatewayConnectResult
{
    public VercelGatewayConnectResult(
        bool credentialSaved,
        VercelGatewayCacheCleanupStatus cacheStatus,
        string? quarantineFileName = null,
        int? unsupportedSchemaVersion = null)
    {
        VercelGatewayCacheCleanupMetadata.Validate(
            cacheStatus,
            quarantineFileName,
            unsupportedSchemaVersion);
        CredentialSaved = credentialSaved;
        CacheStatus = cacheStatus;
        QuarantineFileName = quarantineFileName;
        UnsupportedSchemaVersion = unsupportedSchemaVersion;
    }

    public bool CredentialSaved { get; }

    public VercelGatewayCacheCleanupStatus CacheStatus { get; }

    public string? QuarantineFileName { get; }

    public int? UnsupportedSchemaVersion { get; }

    public bool IsComplete => CredentialSaved
        && VercelGatewayCacheCleanupMetadata.IsClear(CacheStatus);
}

public sealed record VercelGatewayDisconnectResult
{
    public VercelGatewayDisconnectResult(
        bool credentialRemoved,
        VercelGatewayCacheCleanupStatus cacheStatus,
        string? quarantineFileName = null,
        int? unsupportedSchemaVersion = null)
    {
        VercelGatewayCacheCleanupMetadata.Validate(
            cacheStatus,
            quarantineFileName,
            unsupportedSchemaVersion);
        CredentialRemoved = credentialRemoved;
        CacheStatus = cacheStatus;
        QuarantineFileName = quarantineFileName;
        UnsupportedSchemaVersion = unsupportedSchemaVersion;
    }

    public bool CredentialRemoved { get; }

    public VercelGatewayCacheCleanupStatus CacheStatus { get; }

    public string? QuarantineFileName { get; }

    public int? UnsupportedSchemaVersion { get; }

    public bool IsComplete => VercelGatewayCacheCleanupMetadata.IsClear(CacheStatus);
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

        CacheCleanupOutcome cleanup = await RemoveCachedProviderAsync().ConfigureAwait(false);
        return cleanup.ToDisconnectResult(credentialRemoved);
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        await using IAsyncDisposable lease = await _operationGate
            .EnterAsync(cancellationToken)
            .ConfigureAwait(false);
        return await _credentialStore
            .IsConfiguredAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }

    public async Task<VercelGatewayConnectResult> ConnectAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        await using IAsyncDisposable lease = await _operationGate
            .EnterAsync(cancellationToken)
            .ConfigureAwait(false);

        CacheCleanupOutcome cleanup = await RemoveCachedProviderAsync().ConfigureAwait(false);
        if (!VercelGatewayCacheCleanupMetadata.IsClear(cleanup.Status))
        {
            return cleanup.ToConnectResult(credentialSaved: false);
        }

        await _credentialStore
            .SaveAsync(apiKey, CancellationToken.None)
            .ConfigureAwait(false);
        return cleanup.ToConnectResult(credentialSaved: true);
    }

    private async Task<CacheCleanupOutcome> RemoveCachedProviderAsync()
    {
        try
        {
            SnapshotCacheRemoveResult cacheResult = await _snapshotStore
                .RemoveProviderAsync(ProviderId, CancellationToken.None)
                .ConfigureAwait(false);
            return MapResult(cacheResult);
        }
        catch (IOException)
        {
            return new(VercelGatewayCacheCleanupStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return new(VercelGatewayCacheCleanupStatus.AccessDenied);
        }
        catch (TimeoutException)
        {
            return new(VercelGatewayCacheCleanupStatus.LockTimedOut);
        }
        catch (InvalidOperationException)
        {
            return new(VercelGatewayCacheCleanupStatus.Rejected);
        }
    }

    private static CacheCleanupOutcome MapResult(
        SnapshotCacheRemoveResult cacheResult) => cacheResult switch
        {
            SnapshotCacheRemoveResult.Removed =>
                new(VercelGatewayCacheCleanupStatus.Removed),
            SnapshotCacheRemoveResult.Missing =>
                new(VercelGatewayCacheCleanupStatus.Missing),
            SnapshotCacheRemoveResult.Unreadable unreadable =>
                new(
                    VercelGatewayCacheCleanupStatus.Quarantined,
                    QuarantineFileName: unreadable.QuarantineFileName),
            SnapshotCacheRemoveResult.RefusedUnsupportedVersion unsupported =>
                new(
                    VercelGatewayCacheCleanupStatus.RefusedUnsupportedVersion,
                    UnsupportedSchemaVersion: unsupported.SchemaVersion),
            _ => new(VercelGatewayCacheCleanupStatus.Rejected),
        };

    private sealed record CacheCleanupOutcome(
        VercelGatewayCacheCleanupStatus Status,
        string? QuarantineFileName = null,
        int? UnsupportedSchemaVersion = null)
    {
        public VercelGatewayConnectResult ToConnectResult(bool credentialSaved) => new(
            credentialSaved,
            Status,
            QuarantineFileName,
            UnsupportedSchemaVersion);

        public VercelGatewayDisconnectResult ToDisconnectResult(bool credentialRemoved) => new(
            credentialRemoved,
            Status,
            QuarantineFileName,
            UnsupportedSchemaVersion);
    }
}

internal static class VercelGatewayCacheCleanupMetadata
{
    public static bool IsClear(VercelGatewayCacheCleanupStatus status) =>
        status is VercelGatewayCacheCleanupStatus.Removed
            or VercelGatewayCacheCleanupStatus.Missing;

    public static void Validate(
        VercelGatewayCacheCleanupStatus status,
        string? quarantineFileName,
        int? unsupportedSchemaVersion)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
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

        if (status == VercelGatewayCacheCleanupStatus.Quarantined
            != (quarantineFileName is not null))
        {
            throw new ArgumentException(
                "Quarantined cache cleanup requires a quarantine file name only.",
                nameof(quarantineFileName));
        }

        if (status == VercelGatewayCacheCleanupStatus.RefusedUnsupportedVersion
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
    }
}
