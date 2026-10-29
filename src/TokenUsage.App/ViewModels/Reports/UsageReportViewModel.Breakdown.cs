using System.Collections.ObjectModel;
using System.Globalization;
using TokenUsage.App.Controls;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageReportBreakdownRow(string Id, string Name, string Context,
    UsageReportMetrics Metrics, string TokensText, string CostText, string ShareText,
    string CoverageText, string? EvidenceId = null, DateOnly? Date = null)
{
    public bool IsSelected { get; init; }
    public bool HasContext => !string.IsNullOrWhiteSpace(Context);
    public string AutomationName => $"{Name}. {(HasContext ? Context + ". " : string.Empty)}{TokensText}. {CostText}. {ShareText}. {CoverageText}";
}

public sealed partial class UsageReportViewModel
{
    public ObservableCollection<UsageReportBreakdownRow> BreakdownRows { get; } = [];
    public UsageReportBreakdownRow? SelectedBreakdownRow { get; private set; }
    public bool HasBreakdownDetail { get; private set; }
    public bool IsApiProviderBreakdown => Breakdown == UsageReportBreakdown.ApiProvider;
    public bool CanOpenBreakdownEvidence => SelectedBreakdownRow?.EvidenceId is not null
        && Breakdown is UsageReportBreakdown.Model or UsageReportBreakdown.Project;
    public string BreakdownDetailName => SelectedBreakdownRow?.Name ?? string.Empty;
    public string BreakdownDetailContext => SelectedBreakdownRow?.Context ?? string.Empty;
    public bool HasBreakdownDetailContext => SelectedBreakdownRow?.HasContext == true;
    public IReadOnlyList<UsageReportMetricCard> BreakdownDetailValues { get; private set; } = [];
    public string BreakdownFilteredTotal => string.Format(CultureInfo.CurrentCulture,
        GetString("UsageBreakdownTotalFormat"), BreakdownRows.Count, FormatTokens(_report.Totals.Tokens.Total),
        FormatKnownCost(_report.Totals), FormatPercent(_report.Totals.PriceCoveragePercent / 100m));
    public string BreakdownCollectionText => GetString("UsageBreakdownCollectionNote");
    public string BreakdownSessionText => GetString("UsageBreakdownSessionNote");

    public async Task SelectBreakdownRowAsync(string id)
    {
        SelectBreakdownRow(id);
        if (Breakdown != UsageReportBreakdown.Project && !_report.HasConfigurationDetails)
            await LoadConfigurationsAsync();
        if (SelectedBreakdownRow?.Id == id && HasBreakdownDetail) SelectBreakdownRow(id);
    }

    private void RebuildBreakdownRows()
    {
        UsageReportBreakdownRow Row(string id, string name, string context, UsageReportMetrics metrics,
            string? evidence = null, DateOnly? date = null) => new(id, name, context, metrics,
                FormatTokens(metrics.Tokens.Total), FormatKnownCost(metrics), FormatMetricShare(metrics),
                FormatPercent(metrics.PriceCoveragePercent / 100m), evidence, date);
        IEnumerable<UsageReportBreakdownRow> rows = Breakdown switch
        {
            UsageReportBreakdown.Source => SourceRows.Select(row => Row(row.Id, row.Name,
                string.Empty, row.Metrics)),
            UsageReportBreakdown.ApiProvider => ModelRows.GroupBy(row => row.ModelProviderId, StringComparer.Ordinal)
                .Select(group => Row(group.Key ?? "unknown", group.Key ?? GetString("UsageReportUnknownHost"),
                    string.Join(" · ", group.Select(row => row.ProviderName).Distinct()), UsageReportQuery.CombineMetrics(group.Select(row => row.Metrics)))),
            UsageReportBreakdown.Day => DayRows.Select(row => Row(row.Id, row.DateText,
                string.Empty, row.Metrics, date: row.Date)),
            UsageReportBreakdown.Project => ProjectOverviewRows.Select(row => Row(row.Id, row.Name,
                string.Empty,
                row.Metrics, row.IsOther ? null : row.Id)),
            _ => ModelRows.GroupBy(row => row.ModelId, StringComparer.Ordinal).Select(group => Row(group.Key,
                group.First().ModelName,
                string.Join(" · ", group.Select(row => row.ProviderName + " / " + row.HostName).Distinct()),
                UsageReportQuery.CombineMetrics(group.Select(row => row.Metrics)), group.Count() == 1 ? group.First().Id : null)),
        };
        ReportSortState sort = GetSort(Breakdown);
        ReconcileRows(BreakdownRows, ReportDataProjection.Order(rows, sort, row => row.Name,
            row => SortValue(row.Metrics, 0, row.Date, sort.Column)), row => row.Id);
        OnPropertyChanged(nameof(BreakdownFilteredTotal));
        if (SelectedBreakdownRow is { } selected)
            SelectBreakdownRow(BreakdownRows.FirstOrDefault(row => row.Id == selected.Id)?.Id, HasBreakdownDetail);
    }

    public void CloseBreakdownDetail()
    {
        HasBreakdownDetail = false;
        OnPropertyChanged(nameof(HasBreakdownDetail));
    }

    public void SelectBreakdownRow(string? id, bool openDetail = true)
    {
        for (int index = 0; index < BreakdownRows.Count; index++)
        {
            bool selected = BreakdownRows[index].Id == id;
            if (BreakdownRows[index].IsSelected != selected)
                BreakdownRows[index] = BreakdownRows[index] with { IsSelected = selected };
        }
        SelectedBreakdownRow = BreakdownRows.FirstOrDefault(row => row.Id == id);
        HasBreakdownDetail = openDetail && SelectedBreakdownRow is not null;
        if (SelectedBreakdownRow is { } row)
        {
            UsageReportMetrics metrics = row.Metrics;
            string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
            UsageReportComponents components;
            IEnumerable<string> measurementAgents;
            if (Breakdown == UsageReportBreakdown.Project)
            {
                UsageReportProjectOverviewRow? project = ProjectOverviewRows.FirstOrDefault(item => item.Id == row.Id);
                UsageOverviewObservation[] observations = UsageReportOverview.Filter(_overviewObservations, ExplorerSelection())
                        .Where(item => project is not null && (project.IsUnassigned
                            ? item.ProjectKey is null : item.ProjectKey?.Value == project.ProjectKey)).ToArray();
                components = UsageReportQuery.Components(observations, metrics.EventCount);
                measurementAgents = observations.Select(item => item.AgentId.Value);
            }
            else
            {
                UsageReportSelection selection = Breakdown switch
                {
                    UsageReportBreakdown.Model => new() { Models = [new ModelId(row.Id)] },
                    UsageReportBreakdown.Source => new() { Agents = [new AgentId(row.Id)] },
                    UsageReportBreakdown.ApiProvider => new() { ModelProviders = [row.Id == "unknown" ? null : new ModelProviderId(row.Id)] },
                    _ => new(),
                };
                components = UsageReportQuery.Components(_report, selection, row.Date);
                UsageReport selected = UsageReportQuery.Select(_report, selection);
                measurementAgents = row.Date is { } date
                    ? selected.AgentDays.Where(item => item.Date == date).Select(item => item.AgentId.Value)
                    : selected.Agents.Select(item => item.AgentId.Value);
            }
            UsageReportMetricCard Component(string key, UsageComponentSummary component) => new(GetString(key),
                component.MeasuredRecords > 0 ? Number(component.MeasuredTokens) : GetString("UsageExplorerNotAvailable"),
                component.MeasuredRecords > 0
                    && component.UnknownRecords + component.UnavailableRecords + component.MissingDetailRecords > 0
                    ? string.Format(CultureInfo.CurrentCulture, GetString("UsageBreakdownComponentCoverageFormat"),
                        component.MeasuredRecords, component.MeasuredRecords + component.UnknownRecords
                            + component.UnavailableRecords + component.MissingDetailRecords)
                    : string.Empty);
            BreakdownDetailValues = [
                new(GetString("UsageBreakdownTokensLabel"), Number(metrics.Tokens.Total), string.Empty),
                Component("UsageBreakdownInput", components.Input),
                Component("UsageBreakdownOutput", components.Output),
                Component("UsageBreakdownReasoning", components.Reasoning),
                Component("UsageBreakdownCacheRead", components.CacheRead),
                Component("UsageBreakdownCacheWrite", components.CacheWrite),
                new(GetString("UsageReportReportedCostLabel"), metrics.ReportedCostUsd.HasValue ? ExactCost(metrics.ReportedCostUsd) : GetString("UsageExplorerNotAvailable"), string.Empty),
                new(GetString("UsageReportEstimatedCostLabel"), metrics.EstimatedCostUsd.HasValue ? ExactCost(metrics.EstimatedCostUsd) : GetString("UsageExplorerNotAvailable"), string.Empty),
                new(GetString("UsageReportUnpricedLabel"), Number(metrics.UnpricedTokens), string.Empty),
                new(GetString("UsageBreakdownMeasurement"),
                    string.Join(" · ", measurementAgents.Distinct().Select(ProviderName)), string.Empty),
            ];
        }
        else BreakdownDetailValues = [];
        foreach (string property in new[] { nameof(SelectedBreakdownRow), nameof(HasBreakdownDetail),
            nameof(CanOpenBreakdownEvidence), nameof(BreakdownDetailName), nameof(BreakdownDetailContext),
            nameof(HasBreakdownDetailContext), nameof(BreakdownDetailValues) }) OnPropertyChanged(property);
    }

}
