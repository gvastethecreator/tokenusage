using System.Collections.ObjectModel;
using System.Globalization;
using TokenUsage.App.Controls;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel
{
    private ReportChartStyle _chartStyle = ReportChartStyle.Bars;
    private ReportChartGrouping _chartGrouping = ReportChartGrouping.Combined;
    private readonly HashSet<UsageReportBreakdown> _sortedTables = [];
    private readonly Dictionary<UsageReportBreakdown, ReportSortState> _sortStates = new()
    {
        [UsageReportBreakdown.Model] = new(ReportSortColumn.Cost, true),
        [UsageReportBreakdown.Source] = new(ReportSortColumn.Tokens, true),
        [UsageReportBreakdown.Day] = new(ReportSortColumn.Date, true),
        [UsageReportBreakdown.Project] = new(ReportSortColumn.Tokens, true),
    };

    private bool _emphasizeSmallValues = true;
    public bool EmphasizeSmallValues => _emphasizeSmallValues;
    public string ChartStyleName
    {
        get
        {
            string resourceKey = "ReportChartStyle" + ChartStyle;
            return GetString(resourceKey);
        }
    }
    public string ChartStyleIcon => ChartStyle switch
    {
        ReportChartStyle.Step => "stairs",
        ReportChartStyle.Bars => "chart-bar",
        ReportChartStyle.TwoHourBars => "calendar-stats",
        ReportChartStyle.Area => "chart-area-line",
        _ => "chart-line",
    };
    public string ChartStyleTooltip => string.Format(CultureInfo.CurrentCulture,
        GetString("ReportChartStyleTooltipFormat"), ChartStyleName);

    public string ChartAppearanceSummary =>
        ChartTitle + " · " + GetString(ChartGrouping switch
        {
            ReportChartGrouping.Provider => "UsageReportChartGroupingProvider",
            ReportChartGrouping.Model => "UsageReportChartGroupingModel",
            _ => "UsageReportChartGroupingCombined",
        });

    public void SetSmallValueScale(bool enabled)
    {
        if (_emphasizeSmallValues == enabled) return;
        _emphasizeSmallValues = enabled;
        OnPropertyChanged(nameof(EmphasizeSmallValues));
        RebuildProjection();
    }

    public ReportChartStyle ChartStyle => _chartStyle;
    public ReportChartGrouping ChartGrouping => _chartGrouping;
    public bool IsCombinedChart => _chartGrouping == ReportChartGrouping.Combined;
    public bool IsSplitChart => !IsCombinedChart;
    public bool IsModelChart => _chartGrouping == ReportChartGrouping.Model;
    public bool IsProviderChart => _chartGrouping == ReportChartGrouping.Provider;
    public bool IsProviderTotalChart => !IsModelChart;

    public void SetChartAppearance(ReportChartStyle style, ReportChartGrouping grouping)
    {
        if (_chartStyle == style && _chartGrouping == grouping) return;
        bool changesTiming = _chartStyle != style
            && (_chartStyle == ReportChartStyle.TwoHourBars || style == ReportChartStyle.TwoHourBars);
        _chartStyle = style;
        OnPropertyChanged(nameof(ChartStyleName));
        OnPropertyChanged(nameof(ChartStyleIcon));
        OnPropertyChanged(nameof(ChartStyleTooltip));
        _chartGrouping = grouping;
        OnPropertyChanged(nameof(ChartGrouping));
        OnPropertyChanged(nameof(IsProviderChart));
        OnPropertyChanged(nameof(ChartAppearanceSummary));
        bool needsTiming = style == ReportChartStyle.TwoHourBars && (!IsCompareScope || !IsCompareRatesAxis && !UseReferencePrices)
            && (NeedsTimeDetails(_report) || IsPairComparison && NeedsTimeDetails(_compareRightReport));
        if (changesTiming && !HasSavedComparison && (IsLoading || needsTiming))
            _ = LoadAsync();
        else RebuildProjection();
    }

    private static bool NeedsTimeDetails(UsageReport report) =>
        !report.HasTimeBucketDetails && report.TimeBuckets.Count == 0 && !report.IsExactInterval;

    private string? HourlyUnavailableText(params UsageReport[] reports)
    {
        if (ChartStyle != ReportChartStyle.TwoHourBars) return null;
        if (reports.Any(NeedsTimeDetails))
            return GetString("UsageReportTimeDetailsUnavailable");
        if (reports.All(report => report.TimeBuckets.Count == 0 && report.ElapsedTwoHourBuckets.Count == 0))
            return GetString("UsageReportNoTimestampedUsage");
        return null;
    }

    private UsageReportTrendDataset CreateReportTrend(string? providerId, bool byModel, UsageReport? selectedReport = null)
    {
        UsageReport source = selectedReport ?? _report;
        UsageReportTrendDay[] days = Enumerable.Range(0, RangeDayCount)
            .Select(offset => StartDate.AddDays(offset))
            .Select(date => new UsageReportTrendDay(date, date.ToString("d MMM", CultureInfo.CurrentCulture)))
            .ToArray();
        bool percentage = selectedReport is null && IsGlobalScope && IsShareValueMode;
        var totals = source.Days.ToDictionary(day => day.Date, day => MetricValue(day.Metrics));
        (double Value, UsageTrendPointKind Kind) Point(DateOnly date, UsageReportMetrics? metrics)
        {
            if (metrics is null) return (0, UsageTrendPointKind.Unobserved);
            double value = MetricValue(metrics);
            if (!double.IsFinite(value)) return (value, UsageTrendPointKind.Unavailable);
            if (percentage)
            {
                double total = totals.GetValueOrDefault(date);
                value = total > 0 ? 100 * value / total : 0;
            }

            return (value, UsageTrendPointKind.Measured);
        }

        var series = new List<UsageReportTrendSeries>();
        var modelGroups = new Dictionary<string, UsageReport>(StringComparer.Ordinal);
        foreach (UsageAgentReport agent in source.Agents
            .Where(agent => providerId is null || agent.AgentId.Value == providerId)
            .ByCuratedRank(agent => agent.AgentId.Value))
        {
            string id = agent.AgentId.Value;
            string color = ProviderColorPalette.GetEffectiveHex(id, null);
            if (!byModel)
            {
                var daily = source.AgentDays.Where(day => day.AgentId == agent.AgentId)
                    .ToDictionary(day => day.Date, day => day.Metrics);
                (double Value, UsageTrendPointKind Kind)[] points = days
                    .Select(day => Point(day.Date, daily.GetValueOrDefault(day.Date))).ToArray();
                series.Add(new(id, id, ProviderName(id), color,
                    points.Select(item => item.Value).ToArray())
                {
                    PointKinds = points.Select(item => item.Kind).ToArray(),
                });
                continue;
            }

            IReadOnlyList<UsageModelChartGroup> groups = UsageReportQuery.TopModels(source, agent.AgentId, 5, IsCostMetric);
            string Key(UsageModelChartGroup group) => group.Model is { } model
                ? ReportDataProjection.ModelKey(id, model.ModelProviderId?.Value, model.ModelId.Value) : id + "/@other";
            var shades = ReportDataProjection.ModelShades(color, groups.Select(group =>
                (Key(group), IsCostMetric ? ReportDataProjection.KnownCost(group.Report.Totals) : (decimal?)group.Report.Totals.Tokens.Total)));
            foreach (UsageModelChartGroup group in groups)
            {
                string key = Key(group);
                modelGroups[key] = group.Report;
                var modelDays = group.Report.Days.ToDictionary(day => day.Date, day => day.Metrics);
                var points = days.Select(day => Point(day.Date, modelDays.GetValueOrDefault(day.Date))).ToArray();
                string name = group.Model is { } model
                    ? ReportDataProjection.ModelName(model.ModelId.Value) + " · "
                        + (model.ModelProviderId?.Value ?? GetString("UsageReportUnknownHost"))
                    : string.Format(CultureInfo.CurrentCulture, GetString("UsageExplorerOtherModelsFormat"), group.ModelCount);
                series.Add(new(key, id, name, shades[key], points.Select(item => item.Value).ToArray(), group.Model?.ModelId.Value)
                {
                    PointKinds = points.Select(item => item.Kind).ToArray(),
                });
            }
        }
        if (ChartStyle == ReportChartStyle.TwoHourBars)
        {
            var timeTotals = source.TimeBuckets.GroupBy(item => (item.Usage.Date, item.Hour))
                .ToDictionary(group => group.Key, group => MetricValue(UsageReportQuery.Aggregate(group.Select(item => item.Usage))));
            for (int index = 0; index < series.Count; index++)
            {
                UsageReportTrendSeries current = series[index];
                var buckets = (byModel ? modelGroups[current.Id].TimeBuckets : source.TimeBuckets)
                    .Where(item => item.Usage.AgentId.Value == current.ProviderId)
                    .GroupBy(item => (item.Usage.Date, item.Hour))
                    .ToDictionary(group => group.Key, group => MetricValue(UsageReportQuery.Aggregate(group.Select(item => item.Usage))));
                series[index] = current with
                {
                    TimeValues = days.SelectMany(day => Enumerable.Range(0, 12).Select(slot =>
                    {
                        double value = buckets.GetValueOrDefault((day.Date, slot * 2));
                        if (!percentage || !double.IsFinite(value)) return value;
                        double total = timeTotals.GetValueOrDefault((day.Date, slot * 2));
                        return total > 0 ? 100 * value / total : 0;
                    })).ToArray(),
                };
            }
        }
        AddResetMarkers(days, UsageReportResetMarkers.Calendar(_resetHistory.Resets,
            series.Select(item => item.ProviderId), StartDate, days.Length, TimeZoneInfo.Local));
        return new(percentage ? UsageReportMetric.Share : Metric, days, series, ChartStyle,
            EmphasizeSmallValues: EmphasizeSmallValues && !percentage)
            { UnavailableText = HourlyUnavailableText(source) };
    }

    private double MetricValue(UsageReportMetrics metrics) => IsCostMetric
        ? ReportDataProjection.KnownCost(metrics) is decimal cost ? (double)cost : double.NaN
        : metrics.Tokens.Total;

    public ReportSortState GetSort(UsageReportBreakdown table) => _sortStates[table];

    public IReadOnlyList<UsageReportCompactSortOption> CompactSortOptions { get; private set; } = [];

    public UsageReportCompactSortOption? SelectedCompactSort { get; private set; }

    public string CompactSortDirectionGlyph => GetSort(Breakdown).Descending ? "↓" : "↑";

    public string CompactSortDirectionName => GetString(
        GetSort(Breakdown).Descending ? "UsageReportSortDescending" : "UsageReportSortAscending");

    public void Sort(UsageReportBreakdown table, ReportSortColumn column)
    {
        bool descending = _sortedTables.Contains(table) && _sortStates[table].Column == column
            ? !_sortStates[table].Descending
            : column != ReportSortColumn.Name;
        ApplySort(table, column, descending);
    }

    public void ApplySort(UsageReportBreakdown table, ReportSortColumn column, bool descending)
    {
        _sortedTables.Add(table);
        _sortStates[table] = new ReportSortState(column, descending);
        switch (table)
        {
            case UsageReportBreakdown.Model:
                ReconcileRows(ModelRows, OrderModelRows(ModelRows).ToArray(), row => row.Id);
                break;
            case UsageReportBreakdown.Source:
                ReconcileRows(SourceRows, OrderSourceRows(SourceRows).ToArray(), row => row.Id);
                break;
            case UsageReportBreakdown.Day:
                ReconcileRows(DayRows, OrderDayRows(DayRows).ToArray(), row => row.Id);
                break;
            case UsageReportBreakdown.Project:
                ReconcileRows(
                    ProjectOverviewRows,
                    OrderProjectOverviewRows(ProjectOverviewRows).ToArray(),
                    row => row.Id);
                break;
        }

        SyncCompactSortSelection();
    }

    public void SelectCompactSort(UsageReportCompactSortOption? option)
    {
        if (option is null || option.Column == GetSort(Breakdown).Column)
        {
            return;
        }

        ApplySort(Breakdown, option.Column, option.Column != ReportSortColumn.Name);
    }

    public void ToggleCompactSortDirection()
    {
        ReportSortState current = GetSort(Breakdown);
        ApplySort(Breakdown, current.Column, !current.Descending);
    }

    private void RebuildCompactSortOptions()
    {
        CompactSortOptions = Breakdown switch
        {
            UsageReportBreakdown.Source =>
            [
                CompactSortOption(ReportSortColumn.Name, "UsageReportCompactSortName"),
                CompactSortOption(ReportSortColumn.ReportedCost, "UsageReportCompactSortReported"),
                CompactSortOption(ReportSortColumn.EstimatedCost, "UsageReportCompactSortEstimated"),
                CompactSortOption(ReportSortColumn.Tokens, "UsageReportCompactSortTokens"),
                CompactSortOption(ReportSortColumn.Coverage, "UsageReportCompactSortCoverage"),
                CompactSortOption(ReportSortColumn.ActiveDays, "UsageReportCompactSortActiveDays"),
            ],
            UsageReportBreakdown.Day =>
            [
                CompactSortOption(ReportSortColumn.Date, "UsageReportCompactSortDate"),
                CompactSortOption(ReportSortColumn.Cost, "UsageReportCompactSortCost"),
                CompactSortOption(ReportSortColumn.Tokens, "UsageReportCompactSortTokens"),
                CompactSortOption(ReportSortColumn.Events, "UsageReportCompactSortEvents"),
                CompactSortOption(ReportSortColumn.Coverage, "UsageReportCompactSortCoverage"),
            ],
            UsageReportBreakdown.Project =>
            [
                CompactSortOption(ReportSortColumn.Name, "UsageReportCompactSortName"),
                CompactSortOption(ReportSortColumn.Tokens, "UsageReportCompactSortTokens"),
                CompactSortOption(ReportSortColumn.Share, "UsageReportCompactSortShare"),
                CompactSortOption(ReportSortColumn.Sessions, "UsageReportCompactSortSessions"),
                CompactSortOption(ReportSortColumn.ReportedCost, "UsageReportCompactSortReported"),
            ],
            _ =>
            [
                CompactSortOption(ReportSortColumn.Name, "UsageReportCompactSortName"),
                CompactSortOption(ReportSortColumn.Cost, "UsageReportCompactSortCost"),
                CompactSortOption(ReportSortColumn.Share, "UsageReportCompactSortShare"),
                CompactSortOption(ReportSortColumn.Tokens, "UsageReportCompactSortTokens"),
                CompactSortOption(ReportSortColumn.Coverage, "UsageReportCompactSortCoverage"),
                CompactSortOption(ReportSortColumn.ActiveDays, "UsageReportCompactSortActiveDays"),
                CompactSortOption(ReportSortColumn.ReportedCost, "UsageReportCompactSortReported"),
                CompactSortOption(ReportSortColumn.EstimatedCost, "UsageReportCompactSortEstimated"),
                CompactSortOption(ReportSortColumn.UnpricedTokens, "UsageReportCompactSortUnpriced"),
            ],
        };
        OnPropertyChanged(nameof(CompactSortOptions));
        SyncCompactSortSelection();
    }

    private void SyncCompactSortSelection()
    {
        ReportSortColumn column = GetSort(Breakdown).Column;
        UsageReportCompactSortOption? selected = null;
        for (int index = 0; index < CompactSortOptions.Count; index++)
        {
            if (CompactSortOptions[index].Column == column)
            {
                selected = CompactSortOptions[index];
                break;
            }
        }

        SelectedCompactSort = selected ?? (CompactSortOptions.Count > 0 ? CompactSortOptions[0] : null);
        OnPropertyChanged(nameof(SelectedCompactSort));
        OnPropertyChanged(nameof(CompactSortDirectionGlyph));
        OnPropertyChanged(nameof(CompactSortDirectionName));
    }

    private UsageReportCompactSortOption CompactSortOption(ReportSortColumn column, string resourceKey) =>
        new(column, GetString(resourceKey));

    private IEnumerable<UsageReportModelRow> OrderModelRows(IEnumerable<UsageReportModelRow> rows) =>
        ReportDataProjection.Order(rows, GetSort(UsageReportBreakdown.Model), row => row.ModelName,
            row => SortValue(row.Metrics, row.ActiveDays, null, GetSort(UsageReportBreakdown.Model).Column));

    private IEnumerable<UsageReportSourceRow> OrderSourceRows(IEnumerable<UsageReportSourceRow> rows) =>
        ReportDataProjection.Order(rows, GetSort(UsageReportBreakdown.Source), row => row.Name,
            row => SortValue(row.Metrics, row.ActiveDays, null, GetSort(UsageReportBreakdown.Source).Column));

    private IEnumerable<UsageReportDayRow> OrderDayRows(IEnumerable<UsageReportDayRow> rows) =>
        ReportDataProjection.Order(rows, GetSort(UsageReportBreakdown.Day), row => row.DateText,
            row => SortValue(row.Metrics, 0, row.Date, GetSort(UsageReportBreakdown.Day).Column));

    private decimal? SortValue(UsageReportMetrics metrics, int activeDays, DateOnly? date, ReportSortColumn column) =>
        column switch
        {
            ReportSortColumn.Cost => ReportDataProjection.KnownCost(metrics),
            ReportSortColumn.ReportedCost => metrics.ReportedCostUsd,
            ReportSortColumn.EstimatedCost => metrics.EstimatedCostUsd,
            ReportSortColumn.Tokens => metrics.Tokens.Total,
            ReportSortColumn.UnpricedTokens => metrics.UnpricedTokens,
            ReportSortColumn.Share => IsCostMetric ? ReportDataProjection.KnownCost(metrics) : metrics.Tokens.Total,
            ReportSortColumn.Coverage => metrics.PriceCoveragePercent,
            ReportSortColumn.Events => metrics.EventCount,
            ReportSortColumn.ActiveDays => activeDays,
            ReportSortColumn.Date => date?.DayNumber,
            ReportSortColumn.Sessions => null,
            _ => null,
        };

    private static void ReconcileRows<T>(ObservableCollection<T> rows, IEnumerable<T> ordered, Func<T, string> id)
    {
        T[] target = ordered.ToArray();
        var ids = target.Select(id).ToHashSet(StringComparer.Ordinal);
        for (int i = rows.Count - 1; i >= 0; i--)
            if (!ids.Contains(id(rows[i]))) rows.RemoveAt(i);
        for (int i = 0; i < target.Length; i++)
        {
            string key = id(target[i]);
            if (i < rows.Count && id(rows[i]) == key)
            {
                if (!EqualityComparer<T>.Default.Equals(rows[i], target[i])) rows[i] = target[i];
                continue;
            }
            int current = -1;
            for (int j = i + 1; j < rows.Count; j++)
                if (id(rows[j]) == key) { current = j; break; }
            if (current >= 0)
            {
                rows.Move(current, i);
                if (!EqualityComparer<T>.Default.Equals(rows[i], target[i])) rows[i] = target[i];
            }
            else rows.Insert(i, target[i]);
        }
    }
}
