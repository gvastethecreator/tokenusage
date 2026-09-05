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
    private ReportChartGrouping _chartGrouping = ReportChartGrouping.Provider;
    private readonly HashSet<UsageReportBreakdown> _sortedTables = [];
    private readonly Dictionary<UsageReportBreakdown, ReportSortState> _sortStates = new()
    {
        [UsageReportBreakdown.Model] = new(ReportSortColumn.Cost, true),
        [UsageReportBreakdown.Source] = new(ReportSortColumn.Tokens, true),
        [UsageReportBreakdown.Day] = new(ReportSortColumn.Date, true),
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
        _chartStyle = style;
        OnPropertyChanged(nameof(ChartStyleName));
        OnPropertyChanged(nameof(ChartStyleIcon));
        OnPropertyChanged(nameof(ChartStyleTooltip));
        _chartGrouping = grouping;
        OnPropertyChanged(nameof(ChartGrouping));
        OnPropertyChanged(nameof(IsProviderChart));
        RebuildProjection();
    }

    private UsageReportTrendDataset CreateReportTrend(string? providerId, bool byModel)
    {
        UsageReportTrendDay[] days = Enumerable.Range(0, RangeDayCount)
            .Select(offset => StartDate.AddDays(offset))
            .Select(date => new UsageReportTrendDay(date, date.ToString("d MMM", CultureInfo.CurrentCulture)))
            .ToArray();
        bool percentage = IsGlobalScope && IsShareValueMode;
        var totals = _report.Days.ToDictionary(day => day.Date, day => MetricValue(day.Metrics));
        double Value(DateOnly date, UsageReportMetrics? metrics)
        {
            double value = metrics is null ? 0 : MetricValue(metrics);
            if (!percentage || !double.IsFinite(value)) return value;
            double total = totals.GetValueOrDefault(date);
            return total > 0 ? 100 * value / total : 0;
        }

        var series = new List<UsageReportTrendSeries>();
        foreach (UsageAgentReport agent in _report.Agents
            .Where(agent => providerId is null || agent.AgentId.Value == providerId)
            .ByCuratedRank(agent => agent.AgentId.Value))
        {
            string id = agent.AgentId.Value;
            string color = ProviderColorPalette.GetEffectiveHex(id, null);
            if (!byModel)
            {
                var daily = _report.AgentDays.Where(day => day.AgentId == agent.AgentId)
                    .ToDictionary(day => day.Date, day => day.Metrics);
                series.Add(new(id, id, ProviderName(id), color,
                    days.Select(day => Value(day.Date, daily.GetValueOrDefault(day.Date))).ToArray()));
                continue;
            }

            var models = _report.Models.Where(model => model.AgentId == agent.AgentId
                && model.ModelId.Value != "codex-account").ToArray();
            string Key(UsageModelReport model) => ReportDataProjection.ModelKey(
                id, model.ModelProviderId?.Value, model.ModelId.Value);
            var shades = ReportDataProjection.ModelShades(color,
                models.Select(model => (Key(model), IsCostMetric
                    ? ReportDataProjection.KnownCost(model.Metrics) : (decimal?)model.Metrics.Tokens.Total)));
            var modelDays = _report.ModelDays.Where(day => day.AgentId == agent.AgentId)
                .ToDictionary(day => (day.Date, ReportDataProjection.ModelKey(
                    id, day.ModelProviderId?.Value, day.ModelId.Value)), day => day.Metrics);
            foreach (UsageModelReport model in models.OrderBy(model => IsCostMetric ? ReportDataProjection.KnownCost(model.Metrics) is null : false)
                .ThenByDescending(model => IsCostMetric ? ReportDataProjection.KnownCost(model.Metrics) : model.Metrics.Tokens.Total)
                .ThenBy(Key, StringComparer.Ordinal))
            {
                string key = Key(model);
                series.Add(new(key, id, ReportDataProjection.ModelName(model.ModelId.Value), shades[key],
                    days.Select(day => Value(day.Date, modelDays.GetValueOrDefault((day.Date, key)))).ToArray(),
                    model.ModelId.Value));
            }
        }
        if (ChartStyle == ReportChartStyle.TwoHourBars)
        {
            var timeTotals = _report.TimeBuckets.GroupBy(item => (item.Usage.Date, item.Hour))
                .ToDictionary(group => group.Key, group => MetricValue(UsageReportQuery.Aggregate(group.Select(item => item.Usage))));
            for (int index = 0; index < series.Count; index++)
            {
                UsageReportTrendSeries current = series[index];
                var buckets = _report.TimeBuckets.Where(item => item.Usage.AgentId.Value == current.ProviderId
                    && (!byModel || ReportDataProjection.ModelKey(current.ProviderId,
                        item.Usage.ModelProviderId?.Value, item.Usage.ModelId.Value) == current.Id))
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
        return new(percentage ? UsageReportMetric.Share : Metric, days, series, ChartStyle,
            EmphasizeSmallValues: EmphasizeSmallValues && !percentage);
    }

    private double MetricValue(UsageReportMetrics metrics) => IsCostMetric
        ? ReportDataProjection.KnownCost(metrics) is decimal cost ? (double)cost : double.NaN
        : metrics.Tokens.Total;

    public ReportSortState GetSort(UsageReportBreakdown table) => _sortStates[table];

    public void Sort(UsageReportBreakdown table, ReportSortColumn column)
    {
        _sortStates[table] = _sortedTables.Add(table)
            ? new ReportSortState(column, column != ReportSortColumn.Name)
            : _sortStates[table].Toggle(column);
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
        }
    }

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
            ReportSortColumn.Share => IsCostMetric ? ReportDataProjection.KnownCost(metrics) : metrics.Tokens.Total,
            ReportSortColumn.Coverage => metrics.PriceCoveragePercent,
            ReportSortColumn.Events => metrics.EventCount,
            ReportSortColumn.ActiveDays => activeDays,
            ReportSortColumn.Date => date?.DayNumber,
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
