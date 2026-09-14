using System.Globalization;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageActivityRow(string Label, string Records, string Tokens, string AccessibleText);

public sealed partial class UsageReportViewModel
{
    private UsageActivityRow[] _activityWindows = [];
    private int _activityWindowLimit = 24;
    public bool HasActivityDetail => HasExplorer && _report.ActivityCoverage is not null;
    public bool NeedsActivityDetail => HasExplorer && !HasActivityDetail;
    public bool HasNoActivityWindows => HasActivityDetail && _activityWindows.Length == 0;
    public bool HasMoreActivityWindows => HasActivityDetail && _activityWindowLimit < _activityWindows.Length;
    public IReadOnlyList<UsageActivityRow> ActivityCoverageRows { get; private set; } = [];
    public IReadOnlyList<UsageActivityRow> ActivityWindowRows => _activityWindows.Take(_activityWindowLimit).ToArray();
    public string ActivityWindowCountText => string.Format(CultureInfo.CurrentCulture,
        GetString("UsageActivityWindowCountFormat"), Math.Min(_activityWindowLimit, _activityWindows.Length), _activityWindows.Length);
    public string ActivityBoundaryText => _report.IsExactInterval
        ? string.Format(CultureInfo.CurrentCulture, GetString("UsageActivityExactHint"), _report.ExcludedTimingRecords)
            + (_report.HasTimingGaps ? " " + GetString("UsageComparisonTimingGaps") : string.Empty)
        : GetString("UsageActivityCalendarHint");

    public void ShowMoreActivityWindows()
    {
        _activityWindowLimit = Math.Min(_activityWindows.Length, _activityWindowLimit + 24);
        OnPropertyChanged(nameof(ActivityWindowRows));
        OnPropertyChanged(nameof(ActivityWindowCountText));
        OnPropertyChanged(nameof(HasMoreActivityWindows));
    }

    private UsageActivityRow ActivityRow(string label, int records, long tokens)
    {
        string count = records.ToString("N0", CultureInfo.CurrentCulture);
        string amount = tokens.ToString("N0", CultureInfo.CurrentCulture);
        return new(label, count, amount, string.Format(CultureInfo.CurrentCulture,
            GetString("UsageActivityAccessibleFormat"), label, count, amount));
    }

    private void BuildActivityDetails()
    {
        _activityWindowLimit = 24;
        ActivityCoverageRows = _report.ActivityCoverage is { } coverage
            ? [ActivityRow(GetString("UsageActivityTimestamped"), coverage.TimestampedRecords, coverage.TimestampedTokens),
                ActivityRow(GetString("UsageActivityIntervals"), coverage.IntervalRecords, coverage.IntervalTokens),
                ActivityRow(GetString("UsageActivityUnplaceable"), coverage.UnplaceableRecords, coverage.UnplaceableTokens),
                ActivityRow(GetString("UsageActivityMissing"), coverage.MissingDetailRecords, coverage.MissingDetailTokens)] : [];
        _activityWindows = _report.ActivityTimeBuckets.GroupBy(row => (row.Date, row.Hour, row.TimeZoneId))
            .OrderByDescending(group => group.Key.Date).ThenByDescending(group => group.Key.Hour)
            .ThenBy(group => group.Key.TimeZoneId, StringComparer.Ordinal)
            .Select(group => ActivityRow(string.Format(CultureInfo.CurrentCulture, GetString("UsageActivityWindowFormat"),
                group.Key.Date.ToString("d", CultureInfo.CurrentCulture), group.Key.Hour, group.Key.Hour + 2, group.Key.TimeZoneId),
                group.Sum(row => row.Records), group.Sum(row => row.Tokens))).ToArray();
        OnPropertyChanged(nameof(HasActivityDetail)); OnPropertyChanged(nameof(NeedsActivityDetail));
        OnPropertyChanged(nameof(HasNoActivityWindows)); OnPropertyChanged(nameof(HasMoreActivityWindows));
        OnPropertyChanged(nameof(ActivityCoverageRows)); OnPropertyChanged(nameof(ActivityWindowRows));
        OnPropertyChanged(nameof(ActivityWindowCountText)); OnPropertyChanged(nameof(ActivityBoundaryText));
    }
}
