using System.Globalization;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Usage;

using TokenUsage.App.ViewModels.Dashboard;

namespace TokenUsage.App.ViewModels;

public static class UsageHeatmapProjector
{
    public const int DayCount = 35;

    public static UsageHeatmapModel Create(
        IReadOnlyList<DailyUsageRollup> rollups,
        DateOnly today,
        Func<string, string> getString,
        string automationPrefix = "UsageHeatmap")
    {
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(getString);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationPrefix);

        DateOnly firstDay = today.AddDays(-(DayCount - 1));
        Dictionary<DateOnly, DailyActivity> activityByDay = rollups
            .Where(rollup => rollup.Date >= firstDay && rollup.Date <= today)
            .GroupBy(rollup => rollup.Date)
            .ToDictionary(
                group => group.Key,
                group => new DailyActivity(
                    group.Sum(rollup => rollup.Tokens.Total),
                    group.Sum(rollup => rollup.EventCount),
                    group.Where(rollup => rollup.ReportedCostUsd is not null)
                        .Sum(rollup => rollup.ReportedCostUsd ?? 0m),
                    group.Where(rollup => rollup.EstimatedCostUsd is not null)
                        .Sum(rollup => rollup.EstimatedCostUsd ?? 0m),
                    group.Any(rollup => rollup.ReportedCostUsd is not null),
                    group.Any(rollup => rollup.EstimatedCostUsd is not null)));

        long maximumTokens = activityByDay.Count == 0
            ? 0
            : activityByDay.Values.Max(day => day.TotalTokens);
        var cells = new UsageHeatmapCell[DayCount];
        int activeDays = 0;
        for (int index = 0; index < DayCount; index++)
        {
            DateOnly date = firstDay.AddDays(index);
            activityByDay.TryGetValue(date, out DailyActivity? activity);
            activity ??= DailyActivity.Empty;
            int level = GetLevel(activity, maximumTokens);
            if (level > 0)
            {
                activeDays++;
            }

            string formattedDate = date.ToString("d", CultureInfo.CurrentCulture);
            string accessibleName = level == 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    getString("UsageHeatmapEmptyDayFormat"),
                    formattedDate)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    getString("UsageHeatmapDayFormat"),
                    formattedDate,
                    activity.TotalTokens.ToString("N0", CultureInfo.CurrentCulture),
                    activity.EventCount,
                    FormatCost(activity, getString));
            cells[index] = new UsageHeatmapCell(
                date,
                level,
                activity.TotalTokens,
                activity.EventCount,
                $"{automationPrefix}.{date:yyyy-MM-dd}",
                accessibleName);
        }

        string title = getString("UsageHeatmapTitle");
        string summary = string.Format(
            CultureInfo.CurrentCulture,
            getString("UsageHeatmapSummaryFormat"),
            activeDays,
            DayCount);
        return new UsageHeatmapModel(
            title,
            summary,
            $"{title}. {summary}",
            automationPrefix,
            cells);
    }

    private static int GetLevel(DailyActivity activity, long maximumTokens)
    {
        if (activity.EventCount == 0)
        {
            return 0;
        }

        if (maximumTokens <= 0 || activity.TotalTokens <= 0)
        {
            return 1;
        }

        double ratio = Math.Sqrt((double)activity.TotalTokens / maximumTokens);
        return Math.Clamp((int)Math.Ceiling(ratio * 4), 1, 4);
    }

    private static string FormatCost(
        DailyActivity activity,
        Func<string, string> getString)
    {
        if (!activity.HasReportedCost && !activity.HasEstimatedCost)
        {
            return getString("UsageHeatmapCostUnavailable");
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            getString("UsageHeatmapCostFormat"),
            activity.ReportedCost + activity.EstimatedCost);
    }

    private sealed record DailyActivity(
        long TotalTokens,
        int EventCount,
        decimal ReportedCost,
        decimal EstimatedCost,
        bool HasReportedCost,
        bool HasEstimatedCost)
    {
        public static DailyActivity Empty { get; } = new(0, 0, 0m, 0m, false, false);
    }
}
