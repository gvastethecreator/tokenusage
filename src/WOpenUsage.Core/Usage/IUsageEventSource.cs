using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Usage;

public enum UsageSourceReadStatus
{
    Complete,
    Partial,
    NoData,
}

public sealed record UsageSourceReadResult
{
    public UsageSourceReadResult(
        IReadOnlyList<UsageEvent> events,
        UsageSourceReadStatus status)
    {
        Events = events ?? throw new ArgumentNullException(nameof(events));
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (status == UsageSourceReadStatus.NoData && events.Count != 0)
        {
            throw new ArgumentException("No-data results cannot contain events.", nameof(events));
        }

        Status = status;
    }

    public IReadOnlyList<UsageEvent> Events { get; }

    public UsageSourceReadStatus Status { get; }
}

public interface IUsageEventSource
{
    SourceKind SourceKind { get; }

    Task<UsageSourceReadResult> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface ISnapshotUsageEventSource : IUsageEventSource
{
    AgentId AgentId { get; }
}
