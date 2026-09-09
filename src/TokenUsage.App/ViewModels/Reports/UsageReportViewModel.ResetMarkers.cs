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
            var resets = group.Select(marker =>
            {
                var kind = UsageReportResetMarkers.Classify(marker.Reset);
                string text = GetString(UsageReportResetMarkers.LabelResourceKey(kind)) + " · " +
                    string.Format(CultureInfo.CurrentCulture, GetString("UsageReportChartResetFormat"),
                    ProviderName(marker.Reset.ProviderId), ResetWindowName(marker.Reset.MetricId, marker.Reset.WindowDurationMinutes),
                    marker.Reset.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
                return new UsageReportTrendReset(kind, comparison is null ? text : comparison + " · " + text);
            });
            UsageReportTrendDay day = days[group.Key];
            days[group.Key] = day with { Resets = day.Resets.Concat(resets).Distinct().ToArray() };
        }
    }
}
