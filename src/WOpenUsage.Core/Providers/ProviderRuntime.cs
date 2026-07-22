namespace WOpenUsage.Core.Providers;

public sealed record ProviderDescriptor
{
    public ProviderDescriptor(ProviderId id, string displayName, bool isExperimental = false)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        DisplayName = displayName;
        IsExperimental = isExperimental;
    }

    public ProviderId Id { get; }

    public string DisplayName { get; }

    public bool IsExperimental { get; }
}

public abstract class ProviderDetection
{
    private ProviderDetection()
    {
    }

    public sealed class Available : ProviderDetection;

    public sealed class NeedsLogin : ProviderDetection
    {
        public NeedsLogin(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        public string Reason { get; }
    }

    public sealed class UnsupportedAuth : ProviderDetection
    {
        public UnsupportedAuth(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        public string Reason { get; }
    }

    public sealed class Unavailable : ProviderDetection
    {
        public Unavailable(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        public string Reason { get; }
    }
}

public sealed class RefreshContext
{
    public RefreshContext(
        TimeProvider clock,
        ProviderSnapshot? lastGood = null,
        bool forceRefresh = false,
        TimeSpan? staleAfter = null)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        TimeSpan effectiveStaleAfter = staleAfter ?? SnapshotFreshness.DefaultMaxAge;
        if (effectiveStaleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfter), "Stale age must be positive.");
        }

        LastGood = lastGood;
        ForceRefresh = forceRefresh;
        StaleAfter = effectiveStaleAfter;
    }

    public TimeProvider Clock { get; }

    public ProviderSnapshot? LastGood { get; }

    public bool ForceRefresh { get; }

    public TimeSpan StaleAfter { get; }
}

public interface IProviderRuntime
{
    ProviderDescriptor Descriptor { get; }

    ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken);

    Task<ProviderOutcome> RefreshAsync(
        RefreshContext context,
        CancellationToken cancellationToken);
}
