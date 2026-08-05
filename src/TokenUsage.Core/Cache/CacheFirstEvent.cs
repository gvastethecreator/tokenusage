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
        public CachePublished(SnapshotCacheReadResult readResult)
        {
            ReadResult = readResult ?? throw new ArgumentNullException(nameof(readResult));
            Snapshots = readResult is SnapshotCacheReadResult.Loaded loaded
                ? loaded.Snapshots
                : Array.Empty<ProviderSnapshot>();
        }

        public SnapshotCacheReadResult ReadResult { get; }

        public IReadOnlyList<ProviderSnapshot> Snapshots { get; }
    }

    public sealed class ProviderCompleted : CacheFirstEvent
    {
        public ProviderCompleted(
            ProviderId providerId,
            ProviderOutcome outcome,
            CacheUpdateStatus cacheStatus)
        {
            ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
            Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
            if (!Enum.IsDefined(cacheStatus))
            {
                throw new ArgumentOutOfRangeException(nameof(cacheStatus));
            }

            CacheStatus = cacheStatus;
        }

        public ProviderId ProviderId { get; }

        public ProviderOutcome Outcome { get; }

        public CacheUpdateStatus CacheStatus { get; }

        public bool CacheUpdated => CacheStatus == CacheUpdateStatus.Updated;
    }
}
