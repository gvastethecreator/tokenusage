using System.Globalization;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Usage;
using TokenUsage.Core.Automation;
using TokenUsage.App.ViewModels.Reports;

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
                    group.Sum(rollup => rollup.Tokens.CacheRead),
                    group.Sum(rollup => rollup.Tokens.Input),
                    group.Sum(rollup => rollup.Tokens.Output),
                    group.Sum(rollup => rollup.Tokens.Reasoning),
                    group.Where(rollup => rollup.ReportedCostUsd is not null)
                        .Sum(rollup => rollup.ReportedCostUsd ?? 0m),
                    group.Where(rollup => rollup.EstimatedCostUsd is not null)
                        .Sum(rollup => rollup.EstimatedCostUsd ?? 0m),
                    group.Any(rollup => rollup.ReportedCostUsd is not null),
                    group.Any(rollup => rollup.EstimatedCostUsd is not null),
                    group.Select(rollup => rollup.AgentId.Value)
                        .Distinct(StringComparer.Ordinal)
                        .ByCuratedRank(providerId => providerId)
                        .ToArray()));

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
            string costText = FormatCost(activity, getString);
            string totalTokensText = activity.TotalTokens.ToString(
                "N0",
                CultureInfo.CurrentCulture);
            string cachedInputText = activity.CachedInputTokens.ToString(
                "N0",
                CultureInfo.CurrentCulture);
            string uncachedInputText = activity.UncachedInputTokens.ToString(
                "N0",
                CultureInfo.CurrentCulture);
            string outputText = activity.OutputTokens.ToString(
                "N0",
                CultureInfo.CurrentCulture);
            string accessibleName = level == 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    getString("UsageHeatmapEmptyDayFormat"),
                    formattedDate)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    getString("UsageHeatmapDayDetailFormat"),
                    formattedDate,
                    totalTokensText,
                    costText,
                    activity.EventCount,
                    cachedInputText,
                    uncachedInputText,
                    outputText);
            UsageHeatmapTooltip? tooltip = level == 0
                ? null
                : new UsageHeatmapTooltip(
                    date.ToString("D", CultureInfo.CurrentCulture),
                    [
                        new(getString("UsageHeatmapTooltipTokensLabel"), totalTokensText),
                        new(getString("UsageHeatmapTooltipCostLabel"), costText),
                        new(
                            getString("UsageHeatmapTooltipEventsLabel"),
                            activity.EventCount.ToString("N0", CultureInfo.CurrentCulture)),
                        new(getString("UsageHeatmapTooltipCachedInputLabel"), cachedInputText),
                        new(getString("UsageHeatmapTooltipUncachedInputLabel"), uncachedInputText),
                        new(getString("UsageHeatmapTooltipOutputLabel"), outputText),
                        new(
                            getString("UsageHeatmapTooltipReasoningLabel"),
                            activity.ReasoningTokens.ToString("N0", CultureInfo.CurrentCulture)),
                    ]);
            cells[index] = new UsageHeatmapCell(
                date,
                level,
                activity.TotalTokens,
                activity.EventCount,
                $"{automationPrefix}.{date:yyyy-MM-dd}",
                accessibleName,
                activity.HasReportedCost || activity.HasEstimatedCost
                    ? activity.ReportedCost + activity.EstimatedCost
                    : null,
                activity.CachedInputTokens,
                activity.UncachedInputTokens,
                activity.OutputTokens,
                activity.ReasoningTokens,
                accessibleName,
                activity.ProviderIds,
                tooltip);
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

    public static UsageHeatmapModel CreateReport(
        UsageReport report, DateOnly start, DateOnly end, bool showCost,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(getString);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        var byDate = report.Days.Where(day => day.Date >= start && day.Date <= end)
            .ToDictionary(day => day.Date, day => day.Metrics);
        decimal Amount(UsageReportMetrics metrics) => showCost ? metrics.TotalCostUsd : metrics.Tokens.Total;
        decimal maximum = byDate.Values.Select(Amount).DefaultIfEmpty(0).Max();
        var leaders = report.AgentDays.Where(day => day.Date >= start && day.Date <= end && Amount(day.Metrics) > 0)
            .GroupBy(day => day.Date).ToDictionary(group => group.Key, group => group
                .OrderByDescending(day => Amount(day.Metrics)).ThenBy(day => day.AgentId.Value, StringComparer.Ordinal)
                .First().AgentId.Value);
        var cells = new UsageHeatmapCell[end.DayNumber - start.DayNumber + 1];
        for (int index = 0; index < cells.Length; index++)
        {
            DateOnly date = start.AddDays(index);
            byDate.TryGetValue(date, out UsageReportMetrics? metrics);
            decimal amount = metrics is null ? 0 : Amount(metrics);
            int records = metrics?.EventCount ?? 0;
            int level = records == 0 ? 0 : maximum <= 0 || amount <= 0 ? 1
                : Math.Clamp((int)Math.Ceiling(Math.Sqrt((double)(amount / maximum)) * 4), 1, 4);
            decimal? cost = metrics is null ? null : ReportDataProjection.KnownCost(metrics);
            string costText = cost is null ? getString("UsageHeatmapCostUnavailable")
                : string.Format(CultureInfo.CurrentCulture, getString("UsageHeatmapCostFormat"), cost);
            string tokenText = (metrics?.Tokens.Total ?? 0).ToString("N0", CultureInfo.CurrentCulture);
            string label = $"{date:D}: {tokenText} {getString("UsageHeatmapTooltipTokensLabel")}; {costText}";
            var rows = new List<UsageHeatmapTooltipRow>
            {
                new(getString("UsageHeatmapTooltipTokensLabel"), tokenText),
                new(getString("UsageHeatmapTooltipCostLabel"), costText),
                new(getString("UsageHeatmapTooltipEventsLabel"), records.ToString("N0", CultureInfo.CurrentCulture)),
            };
            if (metrics?.UnpricedTokens > 0)
            {
                string unpriced = metrics.UnpricedTokens.ToString("N0", CultureInfo.CurrentCulture);
                rows.Add(new(getString("ReportHeatmapUnpriced"), unpriced));
                label += $"; {getString("ReportHeatmapUnpriced")}: {unpriced}";
            }
            leaders.TryGetValue(date, out string? leader);
            if (leader is not null)
            {
                string name = TokenUsage.Providers.Catalog.ProviderPresentationCatalog.DisplayNameResourceKey(leader) is { } key
                    ? getString(key) : leader;
                rows.Add(new(getString("ReportHeatmapLeadingProvider"), name));
                label += $"; {getString("ReportHeatmapLeadingProvider")}: {name}";
            }
            cells[index] = new UsageHeatmapCell(date, level, metrics?.Tokens.Total ?? 0, records,
                $"ReportHeatmap.{date:yyyy-MM-dd}", label, cost, TooltipText: label,
                Tooltip: new UsageHeatmapTooltip(date.ToString("D", CultureInfo.CurrentCulture), rows),
                DominantProviderId: leader);
        }
        string title = getString("ReportHeatmapTitle");
        string metric = getString(showCost ? "UsageHeatmapTooltipCostLabel" : "UsageHeatmapTooltipTokensLabel");
        return new UsageHeatmapModel(title, metric, $"{title}: {metric}", "ReportHeatmap", cells);
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
        long CachedInputTokens,
        long UncachedInputTokens,
        long OutputTokens,
        long ReasoningTokens,
        decimal ReportedCost,
        decimal EstimatedCost,
        bool HasReportedCost,
        bool HasEstimatedCost,
        IReadOnlyList<string> ProviderIds)
    {
        public static DailyActivity Empty { get; } = new(
            0,
            0,
            0,
            0,
            0,
            0,
            0m,
            0m,
            false,
            false,
            []);
    }
}
