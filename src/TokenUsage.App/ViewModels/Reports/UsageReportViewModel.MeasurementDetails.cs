using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageMeasurementSection(string Title, string Text, string? CompactText = null)
{
    public string DisplayText => CompactText ?? Text;
}

public sealed partial class UsageReportViewModel
{
    public IReadOnlyList<UsageMeasurementSection> MeasurementSections { get; private set; } = [];
    public string ExplorerCollectionSummary { get; private set; } = string.Empty;

    public string MeasurementCopyText => "TokenUsage · " + GetString("UsageMeasurementDetails/Header")
        + Environment.NewLine + PeriodText + Environment.NewLine + Environment.NewLine + MeasurementEvidence;

    private void BuildMeasurementDetails()
    {
        BuildActivityDetails();
        BuildExplanations();
        var sections = new List<UsageMeasurementSection>();
        void Add(string key, string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) sections.Add(new(GetString(key), text.Trim()));
        }

        Add("UsageMeasurementScope", IsProviderScope && SelectedProvider is not null
            ? SelectedProvider.Name : string.Join(", ", _report.Agents.Concat(_compareRightReport.Agents)
                .Select(row => ProviderName(row.AgentId.Value)).Distinct()));
        if (_savedComparison is not null)
            sections.Add(new(GetString("UsageMeasurementMethod"), _measurementEvidence,
                (IsCompareRatesAxis && !UsageComparison.UsesFixedCohortRows(ActiveRateMethodId)
                    ? GetString("UsageComparisonLegacyMethod") + " " : string.Empty) + GetString("UsageComparisonSavedEvidence")));
        else Add("UsageMeasurementMethod", _measurementEvidence);
        if (HasExplanations) Add("UsageExplanationTitle", ExplanationEvidenceText);
        if (HasExplorer)
            Add("UsageExplorerSelectionEvidence", string.Format(CultureInfo.CurrentCulture,
                GetString("UsageExplorerSelectionFormat"),
                IsProviderScope ? SelectedProvider?.Name : ExplorerTool?.Name,
                ExplorerHost?.Name, ExplorerModel?.Name,
                string.IsNullOrWhiteSpace(ModelSearch) ? GetString("UsageExplorerNoSearch") : ModelSearch.Trim()));
        if (HasConfigurationOptions || _report.ConfigurationSelection is not null || _compareRightReport.ConfigurationSelection is not null)
            Add("UsageConfigurationEvidence", IsCompareScope
                ? "A: " + DescribeConfiguration(_report) + Environment.NewLine
                    + "B: " + DescribeConfiguration(_compareRightReport)
                : DescribeConfiguration(_report));
        Add("UsageMeasurementCost", GetString(IsCompareRatesAxis && UsageComparison.UsesFixedCohortRows(ActiveRateMethodId)
            ? "UsageComparisonRatesCostEvidence" : "UsageComparisonCostEvidence"));
        if (Overview is not null)
        {
            Add("UsageOverviewCallsLabel", OverviewCallsUnavailableText);
            Add("UsageOverviewSkillsLabel", GetString("UsageOverviewSkillsUnavailable"));
            if (Overview.CacheShareAvailability == UsageOverviewFactKind.Measured)
            {
                Add("UsageOverviewCacheShareLabel", Overview.CacheShareMethod);
            }
        }

        UsageCollectionState[] collection = _report.CollectionState.Concat(_compareRightReport.CollectionState)
            .DistinctBy(row => row.AgentId).ToArray();
        if (collection.Any(row => row.AgentId == "codex")
            || _report.Agents.Concat(_compareRightReport.Agents).Any(row => row.AgentId.Value == "codex"))
        {
            string capability = GetString("UsageMeasurementCodexCapability");
            if (HasExplorer)
                capability += Environment.NewLine + string.Format(CultureInfo.CurrentCulture,
                    GetString("UsageMeasurementCodexAvailabilityFormat"),
                    _report.Agents.Where(row => row.AgentId.Value == "codex").Sum(row => row.Metrics.EventCount));
            Add("UsageMeasurementSourceCapability", capability);
        }
        string collectionDetails = GetString("UsageMeasurementStoredData") + Environment.NewLine
            + (collection.Length == 0 ? GetString("UsageComparisonFreshnessUnknown")
                : string.Join(Environment.NewLine, collection.Select(row => ProviderName(row.AgentId) + ": "
                    + string.Format(CultureInfo.CurrentCulture,
                GetString("UsageMeasurementCollectionFormat"),
                row.AttemptedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), row.Status, row.Issue,
                CollectionSuccessText(row.LastSuccessfulAtUtc)))));
        string collectionSummary = collection.Length == 0 ? GetString("UsageComparisonFreshnessUnknown")
            : string.Join(Environment.NewLine, collection.GroupBy(row => (
                    Attempt: row.AttemptedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    Success: CollectionSuccessText(row.LastSuccessfulAtUtc), row.Status, row.Issue))
                .Select(group => string.Join(", ", group.Select(row => ProviderName(row.AgentId))) + ": "
                    + CollectionStatusText(group.Key.Status, group.Key.Issue)
                    + " · " + string.Format(CultureInfo.CurrentCulture, GetString("UsageMeasurementFreshnessFormat"),
                        group.Key.Success, group.Key.Attempt)));
        sections.Add(new(GetString("UsageMeasurementCollection"), collectionDetails, collectionSummary));
        ExplorerCollectionSummary = GetString("UsageMeasurementStoredData") + " " + collectionSummary;
        ExplorerSourceStatus = collectionSummary;
        HasExplorerSourceWarning = collection.Any(row =>
            row.Status != UsageSourceReadStatus.Complete
            || row.Issue is UsageSourceIssueKind.UnresolvedHistory
                or UsageSourceIssueKind.ReadFailed
                or UsageSourceIssueKind.PartialScan
                or UsageSourceIssueKind.RootUnavailable
                or UsageSourceIssueKind.AccessBlocked);
        OnPropertyChanged(nameof(ExplorerCollectionSummary));
        OnPropertyChanged(nameof(ExplorerSourceStatus));
        OnPropertyChanged(nameof(HasExplorerSourceWarning));
        OnPropertyChanged(nameof(HasExplorerSourceOk));
        if (_report.HasTimingGaps || _compareRightReport.HasTimingGaps
            || IsCompareCyclesAxis && _cycleReports.Any(entry => entry.Report.HasTimingGaps))
            Add("UsageMeasurementTiming", GetString("UsageComparisonTimingGaps"));
        if ((!_report.HasTimeBucketDetails && _report.TimeBuckets.Count == 0 && !_report.IsExactInterval)
            || IsPairComparison && !_compareRightReport.HasTimeBucketDetails && _compareRightReport.TimeBuckets.Count == 0 && !_compareRightReport.IsExactInterval)
            Add("UsageMeasurementTiming", GetString("UsageReportTimeDetailsNotLoaded"));
        if ((UseReferencePrices || IsCompareRatesAxis) && IsCompareScope && !IsCompareCyclesAxis)
            Add("UsageMeasurementPricing", string.Format(CultureInfo.CurrentCulture,
                GetString("UsageComparisonPricingExclusions"), FormatTokens(_report.PriceReferenceExcludedTokens),
                FormatTokens(_compareRightReport.PriceReferenceExcludedTokens)));
        if (_report.GroupingTimeZoneIds.Concat(_compareRightReport.GroupingTimeZoneIds).Distinct().Count() > 1)
            Add("UsageMeasurementTimeZones", GetString("UsageComparisonMixedZones"));
        if (_report.AccountUsage.Count > 0 || _compareRightReport.AccountUsage.Count > 0)
            Add("UsageMeasurementAccount", string.Format(CultureInfo.CurrentCulture,
                GetString(IsCompareScope ? "UsageComparisonAccountEvidence" : "UsageMeasurementAccountFormat"),
                FormatTokens(_report.AccountUsage.Sum(item => item.Tokens)),
                FormatTokens(_compareRightReport.AccountUsage.Sum(item => item.Tokens))));
        if (IsCompareCyclesAxis)
            Add("UsageMeasurementCycles", CycleAvailabilityText + Environment.NewLine
                + GetString("UsageComparisonQuotaEvidence") + Environment.NewLine + CycleEvidenceText());
        if (ResetHistoryAvailability is not (QuotaHistoryAvailability.Available or QuotaHistoryAvailability.Missing))
            Add("UsageMeasurementHistory", GetString("UsageComparisonHistoryUnavailable") + " (" + ResetHistoryAvailability + ")");

        MeasurementSections = sections;
        OnPropertyChanged(nameof(MeasurementSections));
        OnPropertyChanged(nameof(MeasurementEvidence));
    }

    private string CollectionStatusText(UsageSourceReadStatus status, UsageSourceIssueKind issue) =>
        GetString(issue == UsageSourceIssueKind.UnresolvedHistory ? "UsageExplorerSourceUnresolved"
            : status == UsageSourceReadStatus.Partial ? "UsageExplorerSourcePartial"
            : status == UsageSourceReadStatus.Complete ? "UsageExplorerSourceComplete"
            : issue == UsageSourceIssueKind.Empty ? "UsageExplorerSourceEmpty"
            : "UsageExplorerSourceUnavailable");

    private string CollectionSuccessText(DateTimeOffset? time) =>
        time?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? GetString("UsageMeasurementSuccessUnknown");
}
