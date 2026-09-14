using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel
{
    private UsageExplanationResult? _comparisonExplanation;
    public bool HasExplanations => IsCompareScope || HasExplorer;
    public bool CanLoadExplanationDetail => !HasSavedComparison && !IsLinearPriceScenario && !_loadConfigurations
        && (IsCompareScope ? CanChangeComparison : CanLoadConfigurations);
    public string ExplanationSummaryText { get; private set; } = string.Empty;
    public string ExplanationEvidenceText { get; private set; } = string.Empty;

    public async Task LoadExplanationDetailAsync()
    {
        if (!CanLoadExplanationDetail) return;
        if (HasExplorer) await LoadConfigurationsAsync();
        else { _loadConfigurations = true; await LoadAsync(); }
    }

    private string CacheExplanation(string label, UsageCacheComposition? cache)
    {
        if (cache is null) return label + ": " + GetString("UsageExplanationCacheNotLoaded");
        if (cache.MethodId != "measured-input-cache-share/v1")
            return label + ": " + GetString("UsageExplanationUnknownVersion");
        return label + ": " + string.Format(CultureInfo.CurrentCulture, GetString("UsageExplanationCacheFormat"),
            FormatOptionalPercent(cache.CacheReadPercent), cache.CacheReadTokens, cache.EligibleInputTokens,
            cache.EligibleRecords, cache.UnknownComponentRecords, cache.UnavailableComponentRecords, cache.MissingDetailRecords);
    }

    private void BuildExplanations()
    {
        _comparisonExplanation = null;
        var text = new List<string>();
        var evidence = new List<string>();
        if (IsLinearPriceScenario)
            BuildPriceScenarioExplanation(text, evidence);
        else if (IsCompareScope)
        {
            UsageExplanationMetric metric = IsCostMetric ? UsageExplanationMetric.KnownCost : UsageExplanationMetric.Tokens;
            _comparisonExplanation = _savedComparison is { } saved ? saved.Explanation
                : UsageExplanation.Compare(_report, _compareRightReport, CreateComparisonDefinition(_measurementEvidence), metric);
            UsageExplanationResult? result = _comparisonExplanation;
            if (result is null) text.Add(GetString("UsageExplanationNotSaved"));
            else if (result.RuleVersion != "observed-change-explanations/v1" || result.Metric != metric)
                text.Add(GetString("UsageExplanationUnknownVersion"));
            else
            {
                foreach (UsageExplanationIssue issue in result.Issues)
                    text.Add(ExplanationIssueText(issue));
                string Amount(decimal? amount) => amount is null ? GetString("UsageReportCompareUnavailable")
                    : metric == UsageExplanationMetric.KnownCost ? FormatOptionalUsd(amount)
                    : amount.Value.ToString("N0", CultureInfo.CurrentCulture);
                if (result.TotalChange is not null)
                {
                    text.Add(string.Format(CultureInfo.CurrentCulture, GetString("UsageExplanationChangeFormat"), Amount(result.TotalChange)));
                    foreach (UsageModelContribution row in result.LeadingModels)
                        text.Add(ProviderName(row.AgentId) + " · " + row.ModelId + ": "
                            + Amount(metric == UsageExplanationMetric.Tokens ? row.Tokens.Absolute : row.Cost.Absolute));
                    text.Add(string.Format(CultureInfo.CurrentCulture, GetString("UsageExplanationOtherFormat"), Amount(result.OtherModelsChange)));
                }
                text.Add(CacheExplanation("A", result.BaselineCache));
                text.Add(CacheExplanation("B", result.CurrentCache));
                evidence.Add(result.RuleVersion + " · " + result.Metric);
                evidence.Add(result.Selection.BaselineLabel + " · " + result.Selection.BaselineStart.ToString("O", CultureInfo.InvariantCulture)
                    + " – " + result.Selection.BaselineEnd.ToString("O", CultureInfo.InvariantCulture));
                evidence.Add(result.Selection.CurrentLabel + " · " + result.Selection.CurrentStart.ToString("O", CultureInfo.InvariantCulture)
                    + " – " + result.Selection.CurrentEnd.ToString("O", CultureInfo.InvariantCulture));
                string Points(decimal? value) => value?.ToString("N1", CultureInfo.CurrentCulture) ?? GetString("UsageReportCompareUnavailable");
                evidence.Add(string.Format(CultureInfo.CurrentCulture, GetString("UsageExplanationCoverageFormat"),
                    Points(result.PriceCoverageChangePoints), Points(result.DetailCoverageChangePoints)));
                evidence.Add(result.Selection.Evidence);
                evidence.Add("A: " + DescribeConfiguration(_report));
                evidence.Add("B: " + DescribeConfiguration(_compareRightReport));
            }
        }
        else
        {
            text.Add(CacheExplanation(GetString("UsageExplanationCurrentSelection"), _report.CacheComposition));
            if (_report.CacheComposition is { } cache) evidence.Add(cache.MethodId);
            evidence.Add(DescribeConfiguration(_report));
            evidence.Add(ConfigurationCoverageText);
        }
        if (!IsLinearPriceScenario) text.Add(GetString("UsageExplanationArithmeticHint"));
        ExplanationSummaryText = string.Join(Environment.NewLine, text);
        ExplanationEvidenceText = string.Join(Environment.NewLine, evidence.Concat(text));
        OnPropertyChanged(nameof(HasExplanations));
        OnPropertyChanged(nameof(CanLoadExplanationDetail));
        OnPropertyChanged(nameof(ExplanationSummaryText));
        OnPropertyChanged(nameof(ExplanationEvidenceText));
    }

    private string ExplanationIssueText(UsageExplanationIssue issue) => issue switch
    {
        UsageExplanationIssue.UnsupportedComparison => GetString("UsageExplanationIssueUnsupportedComparison"),
        UsageExplanationIssue.InvalidPeriod => GetString("UsageExplanationIssueInvalidPeriod"),
        UsageExplanationIssue.UnequalPeriods => GetString("UsageExplanationIssueUnequalPeriods"),
        UsageExplanationIssue.UnknownMethod => GetString("UsageExplanationIssueUnknownMethod"),
        UsageExplanationIssue.MethodChanged => GetString("UsageExplanationIssueMethodChanged"),
        UsageExplanationIssue.TimeZonesDiffer => GetString("UsageExplanationIssueTimeZonesDiffer"),
        UsageExplanationIssue.PriceCoverageChanged => GetString("UsageExplanationIssuePriceCoverageChanged"),
        UsageExplanationIssue.DetailCoverageChanged => GetString("UsageExplanationIssueDetailCoverageChanged"),
        UsageExplanationIssue.DetailCoverageUnknown => GetString("UsageExplanationIssueDetailCoverageUnknown"),
        UsageExplanationIssue.NoObservations => GetString("UsageExplanationIssueNoObservations"),
        UsageExplanationIssue.UnpricedCost => GetString("UsageExplanationIssueUnpricedCost"),
        UsageExplanationIssue.ModelTotalsDoNotReconcile => GetString("UsageExplanationIssueModelTotalsDoNotReconcile"),
        _ => GetString("UsageExplanationUnknownVersion"),
    };
}
