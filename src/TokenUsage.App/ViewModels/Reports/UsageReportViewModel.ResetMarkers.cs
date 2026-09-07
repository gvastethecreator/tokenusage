using System.Globalization;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel
{
    private void AddResetMarkers(UsageReportTrendDay[] days,
        IEnumerable<UsageReportResetMarker> markers, string? comparison = null)
    {
        foreach (var group in markers.GroupBy(marker => marker.DayIndex))
        {
            if (group.Key < 0 || group.Key >= days.Length) continue;
            string text = string.Join("\n", group.Select(marker =>
                string.Format(CultureInfo.CurrentCulture, GetString("UsageReportChartResetFormat"),
                    ProviderName(marker.Reset.ProviderId), ResetWindowName(marker.Reset.MetricId, marker.Reset.WindowDurationMinutes),
                    marker.Reset.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)))
                .Distinct().Select(line => comparison is null ? line : comparison + " · " + line));
            UsageReportTrendDay day = days[group.Key];
            days[group.Key] = day with { ResetText = string.IsNullOrEmpty(day.ResetText) ? text : day.ResetText + "\n" + text };
        }
    }
}
