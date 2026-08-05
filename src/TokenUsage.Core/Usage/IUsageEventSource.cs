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
}

public sealed record UsageSourceReadResult
{
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
                or UsageSourceIssueKind.UnsupportedSchema,
            UsageSourceReadStatus.NoData => resolvedIssue is UsageSourceIssueKind.RootUnavailable
                or UsageSourceIssueKind.Empty
                or UsageSourceIssueKind.AccessBlocked
                or UsageSourceIssueKind.UnsupportedSchema,
            _ => false,
        };
        if (!validIssue)
        {
            throw new ArgumentException("The issue is not valid for the read status.", nameof(issue));
        }

        Status = status;
        Issue = resolvedIssue;
    }

    public IReadOnlyList<UsageEvent> Events { get; }

    public UsageSourceReadStatus Status { get; }

    public UsageSourceIssueKind Issue { get; }
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
