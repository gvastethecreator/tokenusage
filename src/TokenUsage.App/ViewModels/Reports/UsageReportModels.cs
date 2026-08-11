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

public sealed record UsageReportResetCycleOption(
    string Id,
    string MetricId,
    string DisplayName,
    string RangeText,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateOnly FromDate,
    DateOnly ToDate,
    bool IsCurrent)
{
    public string AutomationName => $"{DisplayName}. {RangeText}";
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
}

public sealed record UsageReportTrendDay(DateOnly Date, string Label);

public sealed record UsageReportTrendSeries(
    string ProviderId,
    string Name,
    string ColorHex,
    IReadOnlyList<double> Values);

public sealed record UsageReportTrendDataset(
    UsageReportMetric Metric,
    IReadOnlyList<UsageReportTrendDay> Days,
    IReadOnlyList<UsageReportTrendSeries> Series)
{
    public static UsageReportTrendDataset Empty { get; } = new(
        UsageReportMetric.Cost,
        [],
        []);
}
