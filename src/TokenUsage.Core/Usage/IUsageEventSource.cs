using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public enum UsageSourceReadStatus
{
    Complete,
    Partial,
    NoData,
}

public enum UsageSourceIssueKind
{
    None,
    RootUnavailable,
    Empty,
    PartialScan,
    AccessBlocked,
    UnsupportedSchema,
    ReadFailed,
    UnresolvedHistory,
}

public sealed record UsageSourceReadResult
{
    /// <summary>The profile binding proved for this read, rather than merely the configured root.</summary>
    public UsageSourceInstanceId? SourceInstance { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IUsageReadCheckpoint? Checkpoint { get; init; }
    public UsageSourceReadResult(
        IReadOnlyList<UsageEvent> events,
        UsageSourceReadStatus status,
        UsageSourceIssueKind? issue = null)
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

        UsageSourceIssueKind resolvedIssue = issue ?? status switch
        {
            UsageSourceReadStatus.Complete => UsageSourceIssueKind.None,
            UsageSourceReadStatus.Partial => UsageSourceIssueKind.PartialScan,
            _ => UsageSourceIssueKind.Empty,
        };
        bool validIssue = status switch
        {
            UsageSourceReadStatus.Complete => resolvedIssue == UsageSourceIssueKind.None,
            UsageSourceReadStatus.Partial => resolvedIssue is UsageSourceIssueKind.PartialScan
                or UsageSourceIssueKind.AccessBlocked
                or UsageSourceIssueKind.UnsupportedSchema
                or UsageSourceIssueKind.UnresolvedHistory
                or UsageSourceIssueKind.ReadFailed,
            UsageSourceReadStatus.NoData => resolvedIssue is UsageSourceIssueKind.RootUnavailable
                or UsageSourceIssueKind.Empty
                or UsageSourceIssueKind.AccessBlocked
                or UsageSourceIssueKind.UnsupportedSchema
                or UsageSourceIssueKind.ReadFailed,
            _ => false,
        };
        if (!validIssue)
        {
            throw new ArgumentException("The issue is not valid for the read status.", nameof(issue));
        }

        Status = status;
        Issue = resolvedIssue;
    }

    public IReadOnlyList<AccountUsageAggregate> AccountAggregates { get; init; } = [];

    public IReadOnlyList<UsageSessionLink> SessionLinks { get; init; } = [];

    public IReadOnlyList<UsageProjectLink> ProjectLinks { get; init; } = [];

    public IReadOnlyList<UsageOperationFact> OperationFacts { get; init; } = [];

    public IReadOnlyList<UsageEvent> Events { get; }

    public UsageSourceReadStatus Status { get; }

    public UsageSourceIssueKind Issue { get; }
}

public interface IUsageReadCheckpoint
{
    /// <summary>Reject stale reads before running persistence under the source progress lock.
    /// The callback returns true only when all admitted numeric records are durable and
    /// source progress may advance. False preserves the cursor, but invalidates older reads.</summary>
    Task PersistAsync(Func<Task<bool>> persist, CancellationToken cancellationToken = default);
}

public sealed record UsageSourceDiagnostic(
    AgentId AgentId,
    UsageSourceReadStatus Status,
    UsageSourceIssueKind Issue,
    bool RetainsLastReliableSnapshot);

public interface IUsageEventSource
{
    AgentId AgentId { get; }

    SourceKind SourceKind { get; }

    Task<UsageSourceReadResult> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface IRootDetectingUsageEventSource : IUsageEventSource
{
    bool IsRootAvailable { get; }
}

public interface ISnapshotUsageEventSource : IUsageEventSource
{
}

/// <summary>
/// A source whose current files can revise events that were read before their
/// final usage counters were written. Complete reads are authoritative only for
/// the civil-date window they contain, so older retained history stays intact.
/// </summary>
public interface IWindowedSnapshotUsageEventSource : IUsageEventSource
{
    string EventParserVersion { get; }

    int ReconciliationWindowDays { get; }
}

public interface ISourceScopedUsageEventSource : IWindowedSnapshotUsageEventSource
{
    UsageSourceInstanceId SourceInstance { get; }
}
