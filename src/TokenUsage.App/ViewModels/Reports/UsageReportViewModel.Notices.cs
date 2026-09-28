using System.Globalization;

namespace TokenUsage.App.ViewModels.Reports;

// Data-quality notices live behind the title bar's notification button instead of stacking
// across the report. The badge count says something needs a look; the report stays readable.
public sealed partial class UsageReportViewModel
{
    public int NoticeCount => (HasExplorerSourceWarning ? 1 : 0) + (HasCoverageHint ? 1 : 0);

    public bool HasNotices => NoticeCount > 0;

    public bool HasNoNotices => NoticeCount == 0;

    public string NoticeCountText => NoticeCount.ToString(CultureInfo.CurrentCulture);

    public string NoticeButtonName => NoticeCount == 0
        ? GetString("UsageReportNoticesNone")
        : string.Format(CultureInfo.CurrentCulture, GetString("UsageReportNoticesCountFormat"), NoticeCount);

    public string CoverageHintTitle { get; private set; } = string.Empty;

    private void NotifyNotices()
    {
        OnPropertyChanged(nameof(NoticeCount));
        OnPropertyChanged(nameof(HasNotices));
        OnPropertyChanged(nameof(HasNoNotices));
        OnPropertyChanged(nameof(NoticeCountText));
        OnPropertyChanged(nameof(NoticeButtonName));
    }
}
