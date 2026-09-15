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

    public UsageReportMetricCard? OverviewSessionsCard =>
        OverviewCards.Count > 0 ? OverviewCards[0] : null;

    public UsageReportMetricCard? OverviewCacheCard =>
        OverviewCards.Count > 1 ? OverviewCards[1] : null;

    public bool HasOverviewCards => OverviewCards.Count > 0;

    public bool HasOverviewSessionsDetail => OverviewSessionsCard is { HasDetail: true };

    public bool HasOverviewCacheDetail => OverviewCacheCard is { HasDetail: true };

    public ObservableCollection<UsageReportProjectOverviewRow> ProjectOverviewRows { get; } = [];

    public bool HasOverviewNotice => !string.IsNullOrEmpty(OverviewNoticeText);

    public string OverviewNoticeText { get; private set; } = string.Empty;

    public bool HasUnpricedSummary =>
        (Overview?.UnpricedTokens ?? (!IsCompareScope ? _report.Totals.UnpricedTokens : 0)) > 0;

    public string SummaryUnpricedText => string.Format(
        CultureInfo.CurrentCulture,
        GetString("UsageOverviewUnpricedCoverageFormat"),
        FormatTokens(Overview?.UnpricedTokens ?? _report.Totals.UnpricedTokens));

    public string OverviewReportedValue => ExactCost(Overview?.ReportedCostUsd);

    public string OverviewEstimatedValue => ExactCost(Overview?.EstimatedCostUsd);

    public string OverviewUnpricedValue => FormatTokens(Overview?.UnpricedTokens ?? 0);

    public string OverviewUnpricedDetailText => GetString("UsageOverviewUnpricedDetail");

    public bool HasAttributedSessionCost =>
        Overview is { SessionAvailability: UsageOverviewFactKind.Measured, AttributedSessionCount: > 0 };

    public string OverviewAttributedReportedPerSession => string.Format(
        CultureInfo.CurrentCulture,
        GetString("UsageOverviewAttributedReportedPerSessionFormat"),
        ExactCost(Overview?.AttributedReportedCostPerSession));

    public string OverviewAttributedEstimatedPerSession => string.Format(
        CultureInfo.CurrentCulture,
        GetString("UsageOverviewAttributedEstimatedPerSessionFormat"),
        ExactCost(Overview?.AttributedEstimatedCostPerSession));

    public bool HasOverviewSessionPopulation =>
        Overview is { SessionAvailability: UsageOverviewFactKind.Measured, AttributedSessionCount: not null };

    public string OverviewSessionPopulationText =>
        Overview is { SessionAvailability: UsageOverviewFactKind.Measured }
            ? string.Format(
                CultureInfo.CurrentCulture,
                GetString("UsageOverviewSessionsDetailFormat"),
                Overview.RootSessionCount ?? 0,
                Overview.DelegatedSessionCount ?? 0,
                FormatTokens(Overview.UnassignedTokens))
            : string.Empty;

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

        _projectsFromOverview = true;
        _overviewProjectReturnId = id;
        OnPropertyChanged(nameof(IsOverviewProjectNavigation));
        OnPropertyChanged(nameof(OverviewProjectReturnId));
        OnPropertyChanged(nameof(CanOpenProjects));
        if (!CanOpenProjects && !HasProjects)
        {
            _projectsFromOverview = false;
            _overviewProjectReturnId = null;
            OnPropertyChanged(nameof(IsOverviewProjectNavigation));
            OnPropertyChanged(nameof(OverviewProjectReturnId));
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
            sessionsDetail = string.Empty;
        }

        string cacheValue;
        string cacheDetail;
        if (overview.CacheShareAvailability == UsageOverviewFactKind.Measured
            && overview.CacheSharePercent is { } percent)
        {
            cacheValue = FormatPercent(percent / 100m);
            cacheDetail = string.Empty;
        }
        else if (_report.CacheComposition is null)
        {
            cacheValue = GetString("UsageOverviewNotLoaded");
            cacheDetail = GetString("UsageOverviewCacheNotLoadedDetail");
        }
        else
        {
            cacheValue = GetString("UsageExplorerNotAvailable");
            cacheDetail = GetString("UsageOverviewCacheUnavailable");
        }

        return
        [
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
        ];
    }

    public string OverviewCallsUnavailableText => GetString("UsageOverviewCallsUnavailable");

    public bool HasOverviewCallsNotice => Overview is not null;

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
                row.ProjectKey?.Value)
            {
                CompactTokensText = CompactMetricLabel("UsageReportCompactTokensFormat", FormatTokens(row.Tokens.Total)),
                CompactShareText = CompactShareLabel(FormatMetricShare(metrics)),
                CompactSessionsText = CompactMetricLabel(
                    "UsageReportCompactSessionsFormat",
                    overview.SessionAvailability == UsageOverviewFactKind.Disabled
                        ? GetString("UsageExplorerNotAvailable")
                        : row.AttributedSessionCount.ToString("N0", CultureInfo.CurrentCulture)),
            };
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
        OnPropertyChanged(nameof(OverviewSessionsCard));
        OnPropertyChanged(nameof(OverviewCacheCard));
        OnPropertyChanged(nameof(HasOverviewCards));
        OnPropertyChanged(nameof(HasOverviewSessionsDetail));
        OnPropertyChanged(nameof(HasOverviewCacheDetail));
        OnPropertyChanged(nameof(OverviewNoticeText));
        OnPropertyChanged(nameof(HasOverviewNotice));
        OnPropertyChanged(nameof(HasUnpricedSummary));
        OnPropertyChanged(nameof(SummaryUnpricedText));
        OnPropertyChanged(nameof(OverviewReportedValue));
        OnPropertyChanged(nameof(OverviewEstimatedValue));
        OnPropertyChanged(nameof(OverviewUnpricedValue));
        OnPropertyChanged(nameof(OverviewUnpricedDetailText));
        OnPropertyChanged(nameof(HasAttributedSessionCost));
        OnPropertyChanged(nameof(OverviewAttributedReportedPerSession));
        OnPropertyChanged(nameof(OverviewAttributedEstimatedPerSession));
        OnPropertyChanged(nameof(HasOverviewSessionPopulation));
        OnPropertyChanged(nameof(OverviewSessionPopulationText));
        OnPropertyChanged(nameof(OverviewCallsUnavailableText));
        OnPropertyChanged(nameof(HasOverviewCallsNotice));
        OnPropertyChanged(nameof(IsProjectBreakdown));
    }
}
