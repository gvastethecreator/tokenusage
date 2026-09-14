using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Cli;

internal static class ReportJsonV2
{
    internal const string SchemaVersion = UsageReportSnapshotV2.SchemaVersion;

    internal static string Serialize(
        DateTimeOffset generatedAt,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int days,
        AgentId? agentId,
        TokenUsage.Core.Automation.UsageReport report) =>
        UsageReportSnapshotV2.Serialize(generatedAt, fromInclusive, toInclusive, days, agentId, report);

    internal static UsageReportSnapshotV2.Document Create(
        DateTimeOffset generatedAt,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int days,
        AgentId? agentId,
        TokenUsage.Core.Automation.UsageReport report,
        TokenUsage.Core.Automation.UsageReportSelection? selection = null,
        DateTimeOffset? exactFromInclusiveUtc = null,
        DateTimeOffset? exactToExclusiveUtc = null) =>
        UsageReportSnapshotV2.Create(
            generatedAt,
            fromInclusive,
            toInclusive,
            days,
            agentId,
            report,
            selection: selection,
            exactFromInclusiveUtc: exactFromInclusiveUtc,
            exactToExclusiveUtc: exactToExclusiveUtc);

    internal static string WriteCsv(UsageReportSnapshotV2.Document snapshot) =>
        UsageReportSnapshotV2.WriteCsv(snapshot);

    internal static string WriteHtml(UsageReportSnapshotV2.Document snapshot) =>
        UsageReportSnapshotV2.WriteHtml(snapshot);
}
