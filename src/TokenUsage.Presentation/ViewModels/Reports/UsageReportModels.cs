using TokenUsage.Core.Usage;
using TokenUsage.Core.Appearance;

namespace TokenUsage.App.ViewModels.Reports;

public enum UsageReportMetric
{
    Cost,
    Tokens,
    Share,
}

public enum UsageReportBreakdown
{
    Model,
    Source,
    Day,
}

public enum UsageReportValueMode
{
    Absolute,
    Share,
}

public sealed record UsageReportProviderOption(string ProviderId, string Name);

public sealed record UsageReportPeriodOption(int Days, string DisplayName)
{
    public const int AllHistoryDays = 0;
}

public sealed record UsageReportResetCycleOption(
    string Id,
    string MetricId,
    string GroupId,
    string GroupName,
    string DisplayName,
    string RangeText,
    string DetailText,
    string DurationText,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateOnly FromDate,
    DateOnly ToDate,
    decimal UsedPercent,
    decimal? WindowDurationMinutes,
    bool IsCurrent,
    QuotaResetCause? EndingResetCause,
    decimal? ProjectedUsagePercent,
    string PaceText)
{
    public string AutomationName => $"{DisplayName}. {DurationText}. {RangeText}. {DetailText}";

    public bool HasPace => ProjectedUsagePercent is not null
        && !string.IsNullOrWhiteSpace(PaceText);

    public override string ToString() => $"{DisplayName} · {DurationText}";
}

public sealed record UsageReportResetCycleGroupOption(
    string Id,
    string DisplayName,
    IReadOnlyList<UsageReportResetCycleOption> Cycles)
{
    public override string ToString() => DisplayName;
}

public sealed record UsageReportProviderSelectionState(
    IReadOnlyList<UsageReportProviderOption> Options,
    UsageReportProviderOption? Selected,
    bool OptionsChanged);

public static class UsageReportProviderOptionReconciler
{
    public static UsageReportProviderSelectionState Reconcile(
        IReadOnlyList<UsageReportProviderOption> current,
        string? selectedProviderId,
        IEnumerable<string> providerIds,
        Func<string, string> providerName)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(providerIds);
        ArgumentNullException.ThrowIfNull(providerName);

        var currentById = current.ToDictionary(
            option => option.ProviderId,
            StringComparer.Ordinal);
        UsageReportProviderOption[] options = providerIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => currentById.TryGetValue(id, out UsageReportProviderOption? existing)
                && string.Equals(existing.Name, providerName(id), StringComparison.Ordinal)
                    ? existing
                    : new UsageReportProviderOption(id, providerName(id)))
            .ToArray();
        bool optionsChanged = current.Count != options.Length
            || current.Where((option, index) => !ReferenceEquals(option, options[index])).Any();
        UsageReportProviderOption? selected = options.FirstOrDefault(option => string.Equals(
            option.ProviderId,
            selectedProviderId,
            StringComparison.Ordinal))
            ?? options.FirstOrDefault();

        return new UsageReportProviderSelectionState(options, selected, optionsChanged);
    }

    public static IReadOnlyList<string> SelectUsedProviderIds(
        IEnumerable<(string ProviderId, int EventCount, long TokenCount)> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        return agents
            .Where(agent => !string.IsNullOrWhiteSpace(agent.ProviderId)
                && (agent.EventCount > 0 || agent.TokenCount > 0))
            .Select(agent => agent.ProviderId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record UsageReportTrendDay(
    DateOnly Date,
    string Label,
    string? HoverText = null);

public sealed record UsageReportTrendSeries(
    string Id,
    string ProviderId,
    string Name,
    string ColorHex,
    IReadOnlyList<double> Values,
    string? ModelId = null)
{
    public IReadOnlyList<double> TimeValues { get; init; } = [];

    public bool IsReserve => ModelId == "gpt-reserve";
}

public sealed record UsageReportTrendDataset(
    UsageReportMetric Metric,
    IReadOnlyList<UsageReportTrendDay> Days,
    IReadOnlyList<UsageReportTrendSeries> Series,
    ReportChartStyle Style = ReportChartStyle.Bars,
    bool IsComparison = false,
    bool EmphasizeSmallValues = false)
{
    public static UsageReportTrendDataset Empty { get; } = new(
        UsageReportMetric.Cost,
        [],
        []);
}
