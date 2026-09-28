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
    public string ResetLogSummary { get; private set; } = string.Empty;
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
        else if (IsCompareScope && IsCompareProvidersAxis)
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
            .OrderByDescending(item => item.reset.OccurredAtUtc)
            .ToArray();
        ResetLogQuotaOptions =
        [
            new("*", GetString("UsageReportResetLogAllQuotas")),
            .. rows.GroupBy(item => item.reset.MetricId, StringComparer.Ordinal)
                .Select(group => new UsageReportResetLogFilter(group.Key, ProviderName(group.First().reset.ProviderId)
                    + " · " + ResetWindowName(group.Key, group.First().reset.WindowDurationMinutes)))
                .OrderBy(option => option.Name, StringComparer.CurrentCulture),
        ];
        ResetLogClassOptions =
        [
            new("*", GetString("UsageReportResetLogAllClasses")),
            .. Enum.GetValues<UsageReportResetKind>().Select(kind =>
                new UsageReportResetLogFilter(kind.ToString(), GetString(UsageReportResetMarkers.LabelResourceKey(kind)))),
        ];
        _resetLogQuota = ResetLogQuotaOptions.FirstOrDefault(option => option.Id == _resetLogQuota?.Id)
            ?? ResetLogQuotaOptions[0];
        _resetLogClass = ResetLogClassOptions.FirstOrDefault(option => option.Id == _resetLogClass?.Id)
            ?? ResetLogClassOptions[0];
        UsageReportResetLogRow[] nextRows = rows
            .Where(item => (_resetLogQuota?.Id is "*" or null || item.reset.MetricId == _resetLogQuota.Id)
                && (_resetLogClass?.Id is "*" or null || item.kind.ToString() == _resetLogClass.Id))
            .Select(item => CreateResetLogRow(item.reset, item.kind))
            .ToArray();
        // Resets do not depend on the metric; unchanged rows keep their list so the table keeps
        // its elements instead of recreating every row on each projection.
        if (!nextRows.SequenceEqual(ResetLogRows)) ResetLogRows = nextRows;
        int onSchedule = ResetLogRows.Count(row => row.IsOnSchedule);
        int offSchedule = ResetLogRows.Count(row => row.IsOffSchedule);
        int unscheduled = ResetLogRows.Count - onSchedule - offSchedule;
        ResetLogSummary = ResetLogRows.Count == 0 ? string.Empty : string.Format(CultureInfo.CurrentCulture,
            GetString(unscheduled > 0 ? "UsageReportResetLogSummaryUnknownFormat" : "UsageReportResetLogSummaryFormat"),
            ResetLogRows.Count, onSchedule, offSchedule, unscheduled);
        OnPropertyChanged(nameof(ResetLogRows));
        OnPropertyChanged(nameof(ResetLogSummary));
        OnPropertyChanged(nameof(ResetLogQuotaOptions));
        OnPropertyChanged(nameof(ResetLogClassOptions));
        OnPropertyChanged(nameof(ResetLogQuota));
        OnPropertyChanged(nameof(ResetLogClass));
        OnPropertyChanged(nameof(HasResetLog));
        OnPropertyChanged(nameof(HasEmptyResetLog));
    }

    // Newest first, in the report's own date style. Timing compares the reset with the time the
    // previous cycle said it would end; within ten minutes it reads as on schedule.
    private UsageReportResetLogRow CreateResetLogRow(QuotaResetRecord reset, UsageReportResetKind kind)
    {
        DateTimeOffset local = reset.OccurredAtUtc.ToLocalTime();
        string timing;
        string timingDetail = string.Empty;
        bool onSchedule = false;
        if (reset.PreviousExpectedResetAtUtc is { } expected)
        {
            TimeSpan offset = expected - reset.OccurredAtUtc;
            onSchedule = Math.Abs(offset.TotalMinutes) < 10;
            timing = onSchedule
                ? GetString("UsageReportResetLogOnSchedule")
                : string.Format(CultureInfo.CurrentCulture,
                    GetString(offset > TimeSpan.Zero ? "UsageReportResetLogEarlyFormat" : "UsageReportResetLogLateFormat"),
                    FormatCycleDuration(offset.Duration()));
            DateTimeOffset expectedLocal = expected.ToLocalTime();
            timingDetail = string.Format(CultureInfo.CurrentCulture, GetString("UsageReportResetLogExpectedFormat"),
                expectedLocal.ToString("ddd d MMM", CultureInfo.CurrentCulture) + " · "
                + expectedLocal.ToString("t", CultureInfo.CurrentCulture));
        }
        else
        {
            timing = GetString("UsageReportResetLogNoSchedule");
        }

        return new UsageReportResetLogRow(
            local.ToString("ddd d MMM", CultureInfo.CurrentCulture),
            local.ToString("t", CultureInfo.CurrentCulture),
            reset.ProviderId,
            ProviderName(reset.ProviderId) + " · " + ResetWindowName(reset.MetricId, reset.WindowDurationMinutes),
            GetString(UsageReportResetMarkers.LabelResourceKey(kind)),
            kind switch
            {
                UsageReportResetKind.Weekly => "WeeklyReset",
                UsageReportResetKind.Session => "SessionReset",
                UsageReportResetKind.Scheduled => "Blue",
                UsageReportResetKind.Manual => "ManualReset",
                UsageReportResetKind.ResetCredit => "ResetCredit",
                _ => "TextLabel",
            },
            ResetEvidenceText(reset),
            timing,
            timingDetail,
            onSchedule);
    }

    private string ResetEvidenceText(QuotaResetRecord reset) => GetString(reset.EvidenceKind switch
    {
        QuotaChangeEvidenceKind.OfficialManualSignal or QuotaChangeEvidenceKind.OfficialResetCreditSignal
            => "UsageReportResetLogEvidenceProvider",
        QuotaChangeEvidenceKind.InferredCreditDrop => "UsageReportResetLogEvidenceCreditUsed",
        QuotaChangeEvidenceKind.InferredNoCreditDrop => "UsageReportResetLogEvidenceNoCredit",
        QuotaChangeEvidenceKind.ExpectedBoundaryCrossed => "UsageReportResetLogEvidenceBoundary",
        _ => "UsageReportResetLogEvidenceFull",
    });

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
