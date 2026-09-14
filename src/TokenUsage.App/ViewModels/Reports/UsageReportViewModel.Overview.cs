using System.Collections.ObjectModel;
using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel
{
    private IReadOnlyList<UsageOverviewObservation> _overviewObservations = [];

    public UsageReportOverview? Overview { get; private set; }

    public IReadOnlyList<UsageReportMetricCard> OverviewCards { get; private set; } = [];

    public ObservableCollection<UsageReportProjectOverviewRow> ProjectOverviewRows { get; } = [];

    public bool HasOverviewNotice => !string.IsNullOrEmpty(OverviewNoticeText);

    public string OverviewNoticeText { get; private set; } = string.Empty;

    public bool IsProjectBreakdown => Breakdown == UsageReportBreakdown.Project;

    public async Task OpenOverviewProjectAsync(string id)
    {
        if (Overview is null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        UsageOverviewRankedRow? ranked = Overview.Projects.FirstOrDefault(row => row.Id == id);
        if (ranked is null || ranked.IsOther)
        {
            return;
        }

        if (DetailModel is null)
        {
            UsageReportModelRow? model = ModelRows.FirstOrDefault(row =>
                string.Equals(row.ProviderId, "codex", StringComparison.Ordinal));
            if (model is null)
            {
                return;
            }

            OpenModelDetail(model.Id);
        }

        if (!CanOpenProjects && !HasProjects)
        {
            return;
        }

        await OpenProjectsAsync().ConfigureAwait(true);
        OpenProjectDetail(ranked.IsUnassigned ? null : ranked.ProjectKey?.Value);
    }

    private async Task<IReadOnlyList<UsageOverviewObservation>> ReadOverviewObservationsAsync(
        CancellationToken token)
    {
        if (!File.Exists(_databasePath))
        {
            return [];
        }

        long codexEpoch = 0;
        long cursorEpoch = 0;
        long projectEpoch = 0;
        if (_attributionConsent is not null)
        {
            AttributionConsent sessions = await _attributionConsent
                .LoadAsync(AttributionCapability.CodexSession, token)
                .ConfigureAwait(false);
            AttributionConsent cursor = await _attributionConsent
                .LoadAsync(AttributionCapability.CursorSession, token)
                .ConfigureAwait(false);
            AttributionConsent projects = await _attributionConsent
                .LoadAsync(AttributionCapability.CodexProject, token)
                .ConfigureAwait(false);
            codexEpoch = sessions.ActiveLinkEpoch;
            cursorEpoch = cursor.ActiveLinkEpoch;
            projectEpoch = projects.ActiveLinkEpoch;
        }

        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(_databasePath, token)
            .ConfigureAwait(false);
        return await repository.ReadOverviewObservationsAsync(
            StartDate,
            EndDate,
            codexEpoch,
            cursorEpoch,
            projectEpoch,
            cancellationToken: token).ConfigureAwait(false);
    }

    private void RebuildOverview()
    {
        if (IsCompareScope)
        {
            Overview = null;
            OverviewCards = [];
            OverviewNoticeText = string.Empty;
            ReconcileRows(ProjectOverviewRows, [], row => row.Id);
            NotifyOverviewChanged();
            return;
        }

        UsageReportSelection selection = ExplorerSelection();
        IReadOnlyList<UsageOverviewObservation> selected = UsageReportOverview.Filter(
            _overviewObservations,
            selection);
        bool attributionEnabled = _codexSessionsAllowed || _cursorSessionsAllowed;
        Overview = UsageReportOverview.Build(
            _report,
            selected,
            new UsageReportOverviewRequest(attributionEnabled, _report.CacheComposition));
        OverviewCards = CreateOverviewCards(Overview);
        OverviewNoticeText = Overview.SessionAvailability == UsageOverviewFactKind.Disabled
            ? GetString("UsageOverviewNoticeDisabled")
            : string.Empty;
        ReconcileRows(
            ProjectOverviewRows,
            OrderProjectOverviewRows(CreateProjectOverviewRows(Overview)).ToArray(),
            row => row.Id);
        NotifyOverviewChanged();
    }

    private IReadOnlyList<UsageReportMetricCard> CreateOverviewCards(UsageReportOverview overview)
    {
        string sessionsValue;
        string sessionsDetail;
        if (overview.SessionAvailability == UsageOverviewFactKind.Disabled)
        {
            sessionsValue = GetString("UsageExplorerNotAvailable");
            sessionsDetail = GetString("UsageOverviewSessionsDisabled");
        }
        else if (overview.AttributedSessionCount is 0)
        {
            sessionsValue = "0";
            sessionsDetail = GetString("UsageOverviewSessionsNone");
        }
        else
        {
            sessionsValue = overview.AttributedSessionCount!.Value.ToString("N0", CultureInfo.CurrentCulture);
            sessionsDetail = string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageOverviewSessionsDetailFormat"),
                overview.RootSessionCount ?? 0,
                overview.DelegatedSessionCount ?? 0,
                FormatTokens(overview.UnassignedTokens));
        }

        string cacheValue;
        string cacheDetail;
        if (overview.CacheShareAvailability == UsageOverviewFactKind.Measured
            && overview.CacheSharePercent is { } percent)
        {
            cacheValue = FormatPercent(percent / 100m);
            cacheDetail = overview.CacheShareMethod;
        }
        else
        {
            cacheValue = GetString("UsageExplorerNotAvailable");
            cacheDetail = GetString("UsageOverviewCacheUnknown");
        }

        return
        [
            new(
                GetString("UsageOverviewReportedLabel"),
                ExactCost(overview.ReportedCostUsd),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageOverviewPerSessionFormat"),
                    ExactCost(overview.AttributedReportedCostPerSession)),
                "UsageOverviewReported"),
            new(
                GetString("UsageOverviewEstimatedLabel"),
                ExactCost(overview.EstimatedCostUsd),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageOverviewPerSessionFormat"),
                    ExactCost(overview.AttributedEstimatedCostPerSession)),
                "UsageOverviewEstimated"),
            new(
                GetString("UsageOverviewUnpricedLabel"),
                FormatTokens(overview.UnpricedTokens),
                GetString("UsageOverviewUnpricedDetail"),
                "UsageOverviewUnpriced"),
            new(
                GetString("UsageOverviewSessionsLabel"),
                sessionsValue,
                sessionsDetail,
                "UsageOverviewSessions"),
            new(
                GetString("UsageOverviewCacheShareLabel"),
                cacheValue,
                cacheDetail,
                "UsageOverviewCacheShare"),
            new(
                GetString("UsageOverviewCallsLabel"),
                GetString("UsageExplorerNotAvailable"),
                GetString("UsageOverviewCallsUnavailable"),
                "UsageOverviewCalls"),
        ];
    }

    private UsageReportProjectOverviewRow[] CreateProjectOverviewRows(UsageReportOverview overview) =>
        overview.Projects.Select(row =>
        {
            var metrics = new UsageReportMetrics(
                row.ObservationCount,
                row.Tokens,
                row.ReportedCostUsd,
                row.EstimatedCostUsd,
                row.UnpricedTokens,
                0,
                row.UnpricedTokens > 0 && row.UnpricedTokens == row.Tokens.Total
                    ? CoverageKind.Unpriced
                    : CoverageKind.Complete);
            string name = row.IsOther
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageOverviewOtherProjectsFormat"),
                    row.OtherCount)
                : row.IsUnassigned
                    ? GetString("UsageOverviewUnassignedProject")
                    : GetString("UsageOverviewAssignedProject");
            return new UsageReportProjectOverviewRow(
                row.Id,
                name,
                metrics,
                FormatTokens(row.Tokens.Total),
                FormatMetricShare(metrics),
                overview.SessionAvailability == UsageOverviewFactKind.Disabled
                    ? GetString("UsageExplorerNotAvailable")
                    : row.AttributedSessionCount.ToString("N0", CultureInfo.CurrentCulture),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageExplorerReportedValueFormat"),
                    ExactCost(row.ReportedCostUsd)),
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetString("UsageExplorerEstimatedValueFormat"),
                    ExactCost(row.EstimatedCostUsd)),
                row.IsUnassigned,
                row.IsOther,
                row.ProjectKey?.Value);
        }).ToArray();

    private IEnumerable<UsageReportProjectOverviewRow> OrderProjectOverviewRows(
        IEnumerable<UsageReportProjectOverviewRow> rows) =>
        ReportDataProjection.Order(
            rows,
            GetSort(UsageReportBreakdown.Project),
            row => row.Name,
            row => SortValue(
                row.Metrics,
                0,
                null,
                GetSort(UsageReportBreakdown.Project).Column)
                ?? (GetSort(UsageReportBreakdown.Project).Column == ReportSortColumn.Sessions
                    ? Overview?.Projects.FirstOrDefault(item => item.Id == row.Id)?.AttributedSessionCount
                    : null));

    private string ModelSessionCountText(string modelId)
    {
        if (Overview is null || Overview.SessionAvailability == UsageOverviewFactKind.Disabled)
        {
            return GetString("UsageExplorerNotAvailable");
        }

        UsageOverviewRankedRow? row = Overview.Models.FirstOrDefault(item => item.Id == modelId);
        int count = row?.AttributedSessionCount ?? 0;
        return string.Format(
            CultureInfo.CurrentCulture,
            GetString("UsageOverviewModelSessionsFormat"),
            count);
    }

    private void NotifyOverviewChanged()
    {
        OnPropertyChanged(nameof(Overview));
        OnPropertyChanged(nameof(OverviewCards));
        OnPropertyChanged(nameof(OverviewNoticeText));
        OnPropertyChanged(nameof(HasOverviewNotice));
        OnPropertyChanged(nameof(IsProjectBreakdown));
    }
}
