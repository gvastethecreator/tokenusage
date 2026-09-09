using System.Globalization;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel
{
    private UsageReportResetLogFilter? _resetLogQuota;
    private UsageReportResetLogFilter? _resetLogClass;

    public IReadOnlyList<UsageReportResetLogRow> ResetLogRows { get; private set; } = [];
    public IReadOnlyList<UsageReportResetLogFilter> ResetLogQuotaOptions { get; private set; } = [];
    public IReadOnlyList<UsageReportResetLogFilter> ResetLogClassOptions { get; private set; } = [];
    public bool HasResetLog => ResetLogRows.Count > 0;
    public bool HasEmptyResetLog => ResetLogRows.Count == 0;

    public UsageReportResetLogFilter? ResetLogQuota
    {
        get => _resetLogQuota;
        set
        {
            if (value is null || value == _resetLogQuota) return;
            _resetLogQuota = value;
            OnPropertyChanged();
            RebuildResetLog();
        }
    }

    public UsageReportResetLogFilter? ResetLogClass
    {
        get => _resetLogClass;
        set
        {
            if (value is null || value == _resetLogClass) return;
            _resetLogClass = value;
            OnPropertyChanged();
            RebuildResetLog();
        }
    }

    private void RebuildResetLog()
    {
        DateOnly from = StartDate;
        DateOnly to = StartDate.AddDays(Math.Max(0, RangeDayCount - 1));
        if (IsCompareScope)
        {
            from = _compareLeftStart < _compareRightStart ? _compareLeftStart : _compareRightStart;
            to = _compareLeftEnd > _compareRightEnd ? _compareLeftEnd : _compareRightEnd;
        }

        TimeZoneInfo zone = TimeZoneInfo.Local;
        HashSet<string>? providers = null;
        if (IsProviderScope && SelectedProvider is { } selected)
            providers = new HashSet<string>(StringComparer.Ordinal) { selected.ProviderId };
        else if (IsCompareProvidersAxis)
            providers = new[] { CompareLeftProvider?.ProviderId, CompareRightProvider?.ProviderId }
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
        var rows = _resetHistory.Resets
            .Select(reset =>
            {
                DateOnly date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reset.OccurredAtUtc, zone).DateTime);
                var kind = UsageReportResetMarkers.Classify(reset);
                return (reset, date, kind);
            })
            .Where(item => item.date >= from && item.date <= to
                && (providers is null || providers.Contains(item.reset.ProviderId)))
            .OrderBy(item => item.reset.OccurredAtUtc)
            .ToArray();
        ResetLogQuotaOptions =
        [
            new("*", GetString("UsageReportResetLogAllQuotas")),
            .. rows.Select(item => item.reset.MetricId).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(id => new UsageReportResetLogFilter(id, ResetWindowName(id, rows.First(item => item.reset.MetricId == id).reset.WindowDurationMinutes))),
        ];
        ResetLogClassOptions =
        [
            new("*", GetString("UsageReportResetLogAllClasses")),
            new(nameof(UsageReportResetKind.Weekly), GetString("UsageReportResetKindWeekly")),
            new(nameof(UsageReportResetKind.Session), GetString("UsageReportResetKindSession")),
            new(nameof(UsageReportResetKind.Manual), GetString("UsageReportResetKindManual")),
            new(nameof(UsageReportResetKind.ResetCredit), GetString("UsageReportResetKindResetCredit")),
            new(nameof(UsageReportResetKind.Observed), GetString("UsageReportResetKindObserved")),
        ];
        _resetLogQuota = ResetLogQuotaOptions.FirstOrDefault(option => option.Id == _resetLogQuota?.Id)
            ?? ResetLogQuotaOptions[0];
        _resetLogClass = ResetLogClassOptions.FirstOrDefault(option => option.Id == _resetLogClass?.Id)
            ?? ResetLogClassOptions[0];
        ResetLogRows = rows
            .Where(item => (_resetLogQuota?.Id is "*" or null || item.reset.MetricId == _resetLogQuota.Id)
                && (_resetLogClass?.Id is "*" or null || item.kind.ToString() == _resetLogClass.Id))
            .Select(item => new UsageReportResetLogRow(
                item.reset.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                ProviderName(item.reset.ProviderId) + " · " + ResetWindowName(item.reset.MetricId, item.reset.WindowDurationMinutes),
                item.reset.PreviousExpectedResetAtUtc is { } expected
                    ? expected.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    : GetString("UsageReportCompareUnavailable"),
                GetString(UsageReportResetMarkers.LabelResourceKey(item.kind)),
                ResetEvidenceText(item.reset)))
            .ToArray();
        OnPropertyChanged(nameof(ResetLogRows));
        OnPropertyChanged(nameof(ResetLogQuotaOptions));
        OnPropertyChanged(nameof(ResetLogClassOptions));
        OnPropertyChanged(nameof(ResetLogQuota));
        OnPropertyChanged(nameof(ResetLogClass));
        OnPropertyChanged(nameof(HasResetLog));
        OnPropertyChanged(nameof(HasEmptyResetLog));
    }

    private string ResetEvidenceText(QuotaResetRecord reset) => reset.EvidenceKind switch
    {
        QuotaChangeEvidenceKind.OfficialManualSignal => GetString("UsageReportResetCycleManual"),
        QuotaChangeEvidenceKind.OfficialResetCreditSignal => GetString("UsageReportResetCycleCredit"),
        QuotaChangeEvidenceKind.InferredCreditDrop => GetString("UsageReportResetEvidenceInferredBanked"),
        QuotaChangeEvidenceKind.InferredNoCreditDrop => GetString("UsageReportResetEvidenceInferredGrant"),
        QuotaChangeEvidenceKind.ExpectedBoundaryCrossed => GetString("UsageReportResetEvidenceScheduled"),
        _ => GetString("UsageReportResetCycleObserved"),
    };

    private void AddResetMarkers(UsageReportTrendDay[] days,
        IEnumerable<UsageReportResetMarker> markers, string? comparison = null)
    {
        foreach (var group in markers.GroupBy(marker => marker.DayIndex))
        {
            if (group.Key < 0 || group.Key >= days.Length) continue;
            var resets = group.Select(marker =>
            {
                var kind = UsageReportResetMarkers.Classify(marker.Reset);
                string text = GetString(UsageReportResetMarkers.LabelResourceKey(kind)) + " · " +
                    string.Format(CultureInfo.CurrentCulture, GetString("UsageReportChartResetFormat"),
                    ProviderName(marker.Reset.ProviderId), ResetWindowName(marker.Reset.MetricId, marker.Reset.WindowDurationMinutes),
                    marker.Reset.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
                return new UsageReportTrendReset(kind, comparison is null ? text : comparison + " · " + text);
            });
            UsageReportTrendDay day = days[group.Key];
            days[group.Key] = day with { Resets = day.Resets.Concat(resets).Distinct().ToArray() };
        }
    }
}
