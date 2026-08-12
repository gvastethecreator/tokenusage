using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels;

/// <summary>
/// Turns what a read reported into the status a person sees. Two surfaces build provider rows,
/// and each used to carry its own version of this decision: one of them mapped three states it
/// never expected onto "pending", which would have shown the wrong word the day it received
/// one. One policy keeps the words honest for every state.
/// </summary>
public static class ProviderStatusPolicy
{
    /// <summary>
    /// Status of a local source. An issue outranks the read status, because a tool that is not
    /// installed is not "waiting", and a scan that could not finish is not "complete".
    /// </summary>
    public static ProviderStatusKind FromLocalDiagnostic(
        UsageSourceReadStatus status,
        UsageSourceIssueKind issue) => issue switch
        {
            UsageSourceIssueKind.RootUnavailable => ProviderStatusKind.Missing,
            UsageSourceIssueKind.UnsupportedSchema
                or UsageSourceIssueKind.PartialScan
                or UsageSourceIssueKind.AccessBlocked => ProviderStatusKind.Partial,
            _ => status switch
            {
                UsageSourceReadStatus.Complete => ProviderStatusKind.Available,
                UsageSourceReadStatus.Partial => ProviderStatusKind.Partial,
                _ => ProviderStatusKind.Pending,
            },
        };

    /// <summary>
    /// Resource key for the one-line state a compact row shows.
    /// </summary>
    public static string CompactStateKey(ProviderStatusKind kind) => kind switch
    {
        ProviderStatusKind.Available => "ProviderStatusSummaryAvailable",
        ProviderStatusKind.Partial => "ProviderStatusSummaryPartial",
        ProviderStatusKind.Missing => "ProviderStatusSummaryMissing",
        ProviderStatusKind.Pending => "ProviderStatusSummaryPending",
        ProviderStatusKind.Prepared => "ProviderStatusSummaryPrepared",
        ProviderStatusKind.Optional => "ProviderStatusSummaryOptional",
        ProviderStatusKind.Blocked => "ProviderStatusSummaryBlocked",
        _ => "ProviderStatusUnavailable",
    };
}
