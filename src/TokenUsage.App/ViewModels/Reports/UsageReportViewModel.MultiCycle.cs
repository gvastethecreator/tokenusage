using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageCycleSummary(string Label, string Cost, string Tokens, string Change)
{
    public string Dates { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
public sealed record UsageCycleCompareCell(string Value, string Change);
public sealed record UsageCycleCompareRow(string Metric, IReadOnlyList<UsageCycleCompareCell> Cells);

public sealed partial class UsageReportViewModel
{
    private int _cycleCount = 2;
    private UsageReportResetCycleOption? _compareThirdCycle;
    private UsageReportResetCycleOption? _compareFourthCycle;
    private IReadOnlyList<UsageCycleComparisonEntry> _cycleReports = [];

    public IReadOnlyList<int> CycleCounts => Enumerable.Range(2, Math.Max(0, Math.Min(4, ResetCycleOptions.Count) - 1)).ToArray();
    public int CycleCount
    {
        get => _cycleCount;
        set
        {
            if (!CanChangeComparison || !CycleCounts.Contains(value) || !SetProperty(ref _cycleCount, value)) return;
            NormalizeCycleSelections();
            _ = LoadAsync();
        }
    }
    public bool HasThirdCycle => _cycleCount >= 3;
    public bool HasFourthCycle => _cycleCount >= 4;
    public bool IsPairComparison => !IsCompareCyclesAxis;
    public string CycleProviderText => GetString("UsageComparisonCycleProvider");
    public string CycleAvailabilityText => GetString("UsageComparisonCycleCountHint");
    public IReadOnlyList<UsageCycleSummary> CycleSummaries { get; private set; } = [];
    public IReadOnlyList<UsageCycleCompareRow> CycleRows { get; private set; } = [];

    public UsageReportResetCycleOption? CompareThirdCycle
    {
        get => _compareThirdCycle;
        set => SelectCycle(ref _compareThirdCycle, value, nameof(CompareThirdCycle));
    }
    public UsageReportResetCycleOption? CompareFourthCycle
    {
        get => _compareFourthCycle;
        set => SelectCycle(ref _compareFourthCycle, value, nameof(CompareFourthCycle));
    }

    private void SelectCycle(ref UsageReportResetCycleOption? field, UsageReportResetCycleOption? value, string property)
    {
        if (value is null || !CanChangeComparison || !SetProperty(ref field, value, property)) return;
        NormalizeCycleSelections(property);
        if (IsCompareScope && IsCompareCyclesAxis) _ = LoadAsync();
    }

    private UsageReportResetCycleOption[] ComparisonCycles() =>
        new[] { _compareLeftCycle, _compareRightCycle, _compareThirdCycle, _compareFourthCycle }
            .Take(_cycleCount).OfType<UsageReportResetCycleOption>().ToArray();

    private void NormalizeCycleSelections(string? preferred = null)
    {
        _cycleCount = Math.Clamp(_cycleCount, 2, Math.Max(2, Math.Min(4, ResetCycleOptions.Count)));
        string[] names = [nameof(CompareLeftCycle), nameof(CompareRightCycle), nameof(CompareThirdCycle), nameof(CompareFourthCycle)];
        UsageReportResetCycleOption?[] selected = [_compareLeftCycle, _compareRightCycle, _compareThirdCycle, _compareFourthCycle];
        HashSet<string> used = new(StringComparer.Ordinal);
        foreach (int slot in Enumerable.Range(0, _cycleCount).OrderBy(index => names[index] == preferred ? 0 : 1))
        {
            UsageReportResetCycleOption? current = FindCycle(ResetCycleOptions, selected[slot]?.Id);
            if (current is null || used.Contains(current.Id))
                current = ResetCycleOptions.OrderBy(option => option.GroupId == _compareLeftCycle?.GroupId ? 0 : 1)
                    .FirstOrDefault(option => !used.Contains(option.Id));
            selected[slot] = current;
            if (current is not null) used.Add(current.Id);
        }
        (_compareLeftCycle, _compareRightCycle, _compareThirdCycle, _compareFourthCycle) =
            (selected[0], selected[1], selected[2], selected[3]);
        foreach (string name in names) OnPropertyChanged(name);
        OnPropertyChanged(nameof(CycleCounts)); OnPropertyChanged(nameof(CycleCount));
        OnPropertyChanged(nameof(HasThirdCycle)); OnPropertyChanged(nameof(HasFourthCycle));
    }

    private async Task ApplyCycleSetAsync(Func<UsageReportResetCycleOption, Task<UsageReport>> read,
        DateOnly fallbackStart, DateOnly fallbackEnd, CancellationToken token)
    {
        NormalizeCycleSelections();
        UsageReportResetCycleOption[] selected = ComparisonCycles();
        _matchedCycleDuration = selected.Length >= 2
            ? UsageCycleComparisonSet.MatchedDuration(selected.Select(option => (option.Id, SupportedCycleDuration(option))).ToArray())
            : TimeSpan.Zero;
        List<UsageCycleComparisonEntry> entries = [];
        foreach (UsageReportResetCycleOption cycle in selected)
        {
            UsageReport report = await read(cycle);
            token.ThrowIfCancellationRequested();
            string label = ((char)('A' + entries.Count)).ToString() + " · " + cycle.ProviderDisplayName;
            entries.Add(new(cycle.Id, cycle.ProviderId, cycle.ProviderName, label, cycle.FromUtc,
                cycle.FromUtc + _matchedCycleDuration, report, CreateCycleObservation(cycle, report.Totals)));
        }
        _cycleReports = entries;
        _report = entries.Count > 0 ? entries[0].Report : UsageReportQuery.Build([]);
        _compareRightReport = entries.Count > 1 ? entries[1].Report : UsageReportQuery.Build([]);
        _compareLeftStart = selected.Length > 0 ? selected[0].FromDate : fallbackStart;
        _compareLeftEnd = selected.Length > 0 ? MatchedCycleEndDate(selected[0]) : fallbackEnd;
        _compareRightStart = selected.Length > 1 ? selected[1].FromDate : fallbackStart;
        _compareRightEnd = selected.Length > 1 ? MatchedCycleEndDate(selected[1]) : fallbackEnd;
    }

    private void RebuildCycleProjection()
    {
        CycleSummaries = _cycleReports.Select((entry, index) => new UsageCycleSummary($"{(char)('A' + index)} · {entry.ProviderName}",
            FormatKnownCost(entry.Report.Totals), string.Format(CultureInfo.CurrentCulture, GetString("UsageComparisonTokensFormat"), FormatTokens(entry.Report.Totals.Tokens.Total)),
            index == 0 ? GetString("UsageComparisonBaselineCaption")
                : FormatOptionalSignedUsd(ComparableCost(_report.Totals), ComparableCost(entry.Report.Totals))
                    + " · " + FormatSignedTokens(entry.Report.Totals.Tokens.Total - _report.Totals.Tokens.Total)
                    + " " + GetString("UsageComparisonVersusA"))
            {
                Dates = $"{entry.FromUtc.ToLocalTime():d MMM} – {entry.ToUtc.ToLocalTime():d MMM}",
                Description = entry.Label,
            }).ToArray();
        var pairs = _cycleReports.Skip(1).Select(entry => CreatePairRows(entry.Report,
            UsageReportCycleComparisonCalculator.Compare(_cycleReports[0].Observation, entry.Observation))
            .ToDictionary(row => row.Metric, StringComparer.Ordinal)).ToArray();
        string unavailable = GetString("UsageReportCompareUnavailable");
        CycleRows = pairs.SelectMany(rows => rows.Keys).Distinct(StringComparer.Ordinal).Select(metric =>
        {
            var cells = new List<UsageCycleCompareCell>
            {
                new(pairs.Select(rows => rows.GetValueOrDefault(metric)).OfType<UsageReportCompareRow>().First().LeftText, string.Empty),
            };
            cells.AddRange(pairs.Select(rows => rows.TryGetValue(metric, out UsageReportCompareRow? row)
                ? new UsageCycleCompareCell(row.RightText, row.DeltaText)
                : new UsageCycleCompareCell(unavailable, unavailable)));
            return new UsageCycleCompareRow(metric, cells);
        }).ToArray();
        OnPropertyChanged(nameof(CycleSummaries)); OnPropertyChanged(nameof(CycleRows));
        OnPropertyChanged(nameof(IsPairComparison));
    }

    private UsageReportTrendDataset CreateCycleTrend()
    {
        int dayCount = _cycleReports.Count == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(
            (_cycleReports[0].ToUtc - _cycleReports[0].FromUtc).TotalDays));
        UsageReportTrendDay[] days = Enumerable.Range(0, dayCount).Select(index => new UsageReportTrendDay(
            _compareLeftStart.AddDays(index), string.Format(CultureInfo.CurrentCulture,
                GetString("UsageReportCompareDayFormat"), index + 1), string.Format(CultureInfo.CurrentCulture,
                GetString("UsageReportCompareDayFormat"), index + 1))).ToArray();
        string[] colors = ["#3B82F6", "#F97316", "#A78BFA", "#14B8A6"];
        UsageReportTrendSeries[] series = _cycleReports.Select((entry, index) =>
        {
            var buckets = entry.Report.ElapsedTwoHourBuckets.ToDictionary(bucket => bucket.Index, bucket => MetricValue(bucket.Metrics));
            double[] values = Enumerable.Range(0, dayCount * 12).Select(slot => buckets.GetValueOrDefault(slot)).ToArray();
            return new UsageReportTrendSeries(entry.Id, entry.ProviderId, entry.Label, colors[index],
                Enumerable.Range(0, dayCount).Select(day => values.Skip(day * 12).Take(12).Sum()).ToArray())
                {
                    TimeValues = values,
                    LegendName = $"{(char)('A' + index)} · {entry.ProviderName} · {entry.FromUtc.ToLocalTime():d MMM}",
                };
        }).ToArray();
        for (int index = 0; index < _cycleReports.Count; index++)
        {
            var entry = _cycleReports[index];
            AddResetMarkers(days, UsageReportResetMarkers.Elapsed(_resetHistory.Resets,
                entry.ProviderId, entry.FromUtc, entry.ToUtc), ((char)('A' + index)).ToString());
        }
        return new(Metric, days, series, ChartStyle, IsComparison: true, EmphasizeSmallValues: EmphasizeSmallValues);
    }
}
