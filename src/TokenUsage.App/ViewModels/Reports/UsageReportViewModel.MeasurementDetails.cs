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

    public string MeasurementCopyText => "TokenUsage · " + GetString("UsageMeasurementDetails/Header")
        + Environment.NewLine + PeriodText + Environment.NewLine + Environment.NewLine + MeasurementEvidence;

    private void BuildMeasurementDetails()
    {
        var sections = new List<UsageMeasurementSection>();
        void Add(string key, string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) sections.Add(new(GetString(key), text.Trim()));
        }

        Add("UsageMeasurementScope", IsProviderScope && SelectedProvider is not null
            ? SelectedProvider.Name : string.Join(", ", _report.Agents.Concat(_compareRightReport.Agents)
                .Select(row => ProviderName(row.AgentId.Value)).Distinct()));
        Add("UsageMeasurementMethod", _measurementEvidence);
        Add("UsageMeasurementCost", GetString("UsageComparisonCostEvidence"));

        UsageCollectionState[] collection = _report.CollectionState.Concat(_compareRightReport.CollectionState)
            .DistinctBy(row => row.AgentId).ToArray();
        string collectionDetails = GetString("UsageMeasurementStoredData") + Environment.NewLine
            + (collection.Length == 0 ? GetString("UsageComparisonFreshnessUnknown")
                : string.Join(Environment.NewLine, collection.Select(row => ProviderName(row.AgentId) + ": "
                    + string.Format(CultureInfo.CurrentCulture,
                GetString("UsageMeasurementCollectionFormat"),
                row.AttemptedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), row.Status, row.Issue))));
        string collectionSummary = collection.Length == 0 ? GetString("UsageComparisonFreshnessUnknown")
            : string.Join("; ", collection.GroupBy(row => (
                    Attempt: row.AttemptedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), row.Status, row.Issue))
                .Select(group => string.Join(", ", group.Select(row => ProviderName(row.AgentId))) + ": "
                    + group.Key.Status + (group.Key.Issue == UsageSourceIssueKind.None ? string.Empty : "/" + group.Key.Issue)
                    + " · " + group.Key.Attempt));
        sections.Add(new(GetString("UsageMeasurementCollection"), collectionDetails, collectionSummary));
        if (_report.HasTimingGaps || _compareRightReport.HasTimingGaps
            || IsCompareCyclesAxis && _cycleReports.Any(entry => entry.Report.HasTimingGaps))
            Add("UsageMeasurementTiming", GetString("UsageComparisonTimingGaps"));
        if (UseReferencePrices && IsCompareScope && !IsCompareCyclesAxis)
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
        _measurementEvidence = string.Join(Environment.NewLine + Environment.NewLine,
            sections.Select(section => section.Title + Environment.NewLine + section.Text));
        OnPropertyChanged(nameof(MeasurementSections));
        OnPropertyChanged(nameof(MeasurementEvidence));
    }
}
