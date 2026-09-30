using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Cache;

public enum CacheUpdateStatus
{
    NotAttempted,
    Updated,
    RefusedUnsupportedVersion,
    IoFailure,
    AccessDenied,
    LockTimedOut,
    Rejected,
}

public abstract class CacheFirstEvent
{
    private CacheFirstEvent()
    {
    }

    public sealed class CachePublished : CacheFirstEvent
    {
        public CachePublished(SnapshotCacheReadResult readResult, IReadOnlySet<string>? registeredInstances = null)
        {
            ReadResult = readResult ?? throw new ArgumentNullException(nameof(readResult));
            RegisteredInstances = registeredInstances;
            Snapshots = readResult is SnapshotCacheReadResult.Loaded loaded
                ? loaded.Snapshots
                : Array.Empty<ProviderSnapshot>();
        }

        public SnapshotCacheReadResult ReadResult { get; }

        public IReadOnlySet<string>? RegisteredInstances { get; }

        public IReadOnlyList<ProviderSnapshot> Snapshots { get; }
    }

    public sealed class ProviderCompleted : CacheFirstEvent
    {
        public ProviderCompleted(
            ProviderId providerId,
            ProviderOutcome outcome,
            CacheUpdateStatus cacheStatus,
            ProviderInstanceKey? instanceKey = null)
        {
            ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
            InstanceKey = instanceKey ?? new ProviderInstanceKey(providerId);
            Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
            if (!Enum.IsDefined(cacheStatus))
            {
                throw new ArgumentOutOfRangeException(nameof(cacheStatus));
            }

            CacheStatus = cacheStatus;
        }

        public ProviderId ProviderId { get; }

        public ProviderInstanceKey InstanceKey { get; }

        public ProviderOutcome Outcome { get; }

        public CacheUpdateStatus CacheStatus { get; }

        public bool CacheUpdated => CacheStatus == CacheUpdateStatus.Updated;
    }
}
