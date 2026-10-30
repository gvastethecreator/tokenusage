using TokenUsage.Core.Automation;

namespace TokenUsage.App.ViewModels.Reports;

/// <summary>A period and the one before it. <see cref="IsRecorded"/> is false before the first refresh.</summary>
public sealed record ReportUsage(UsageReport Current, UsageReport Previous, bool IsRecorded);

public static class ReportUsageReader
{
    /// <summary>
    /// Reads a period and the one before it. The usage database appears with the first refresh;
    /// until then nothing was recorded, so both periods are empty instead of a read failure.
    /// </summary>
    public static async Task<ReportUsage> ReadAsync(string databasePath, ReportPeriod period)
    {
        if (!File.Exists(databasePath))
        {
            UsageReport empty = UsageReportQuery.Build([]);
            return new ReportUsage(empty, empty, IsRecorded: false);
        }

        var query = new UsageReportQuery(databasePath);
        return new ReportUsage(
            await query.ReadAsync(period.From, period.To),
            await query.ReadAsync(period.Previous.From, period.Previous.To),
            IsRecorded: true);
    }
}
