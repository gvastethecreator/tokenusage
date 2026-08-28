using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Session;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed partial class DashboardSurfaceViewModel : ObservableObject, IDisposable
{
    private readonly SampleDashboardSession _sampleSession;
    private readonly LiveDashboardSession _liveSession;
    private readonly GeneralOptionsViewModel _general;
    private readonly AppearanceSurfaceViewModel _appearance;
    private readonly PersonalizationSurfaceViewModel _personalization;
    private readonly ProviderStatusSurfaceViewModel _providerStatus;
    private readonly Func<string, string> _getString;
    private AppearanceSettings _lastAppearanceSettings;
    private CancellationTokenSource? _refreshCancellation;
    private int _projectionBatchDepth;
    private bool _compactProjectionIsStale;
    private bool _isPanelVisible = true;
    private SampleScenario? _activeScenario;
    private bool _hasPublishedDashboard;
    private DateTimeOffset? _publishedObservedAtUtc;
    private DateTimeOffset? _retryAtUtc;
    private bool _hasLocalUsage;
    private LocalUsageCard? _rawLocalUsage;
    private IReadOnlyList<DailyUsageRollup> _localUsageRollups = [];
    private readonly Dictionary<string, IReadOnlyList<QuotaWindow>> _providerLimitsById =
        new(StringComparer.Ordinal);
    private ProviderOutcome? _lastCodexOutcome;
    private ProviderSnapshot? _lastCodexSnapshot;
    private DashboardSnapshot? _rawDashboard;
    private DashboardSnapshot? _appearanceDashboard;
    private bool _disposed;

    public DashboardSurfaceViewModel(
        SampleDashboardSession sampleSession,
        LiveDashboardSession liveSession,
        GeneralOptionsViewModel general,
        AppearanceSurfaceViewModel appearance,
        PersonalizationSurfaceViewModel personalization,
        ProviderStatusSurfaceViewModel providerStatus,
        Func<string, string> getString,
        SynchronizationContext? synchronizationContext)
    {
        _sampleSession = sampleSession ?? throw new ArgumentNullException(nameof(sampleSession));
        _liveSession = liveSession ?? throw new ArgumentNullException(nameof(liveSession));
        _general = general ?? throw new ArgumentNullException(nameof(general));
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        _personalization = personalization
            ?? throw new ArgumentNullException(nameof(personalization));
        _providerStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));
        _getString = getString ?? throw new ArgumentNullException(nameof(getString));
        _lastAppearanceSettings = _appearance.Settings;
        _providerStatus.BindRefresh(() => RunRefreshAsync(scenario: null, forceRefresh: true));
        _general.SampleModeChanged += OnSampleModeChanged;
        _general.SampleScenarioChanged += OnSampleScenarioChanged;
        _appearance.SettingsChanged += OnAppearanceChanged;
        _personalization.LayoutChanged += OnLayoutChanged;
        _personalization.PropertyChanged += OnPersonalizationPropertyChanged;
        _liveSession.Bind(
            _getString,
            ApplyProviderUpdateAsync,
            OnLiveSessionChanged,
            synchronizationContext);
        UnavailableTitle = _getString("CodexUnavailableTitle");
        UnavailableBody = _getString("CodexUnavailableBody");
        RetryButtonText = _getString("SampleRetry");
        RetryAutomationName = _getString("CodexRetry");
        RebuildSamplePreview();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsSampleUnavailable))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial FlyoutSurfaceState ResultSurface { get; private set; } =
        FlyoutSurfaceState.Loading;

    [ObservableProperty]
    public partial DashboardSnapshot ActiveSample { get; private set; } = null!;

    [ObservableProperty]
    public partial LocalUsageCard LocalUsage { get; private set; } = new(
        "",
        "",
        "",
        "",
        [],
        [],
        new("", "", "", "", [], []),
        []);

    [ObservableProperty]
    public partial int RevealToken { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(IsRefreshing))]
    public partial bool IsSessionRefreshing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveDataStateText))]
    [NotifyPropertyChangedFor(nameof(SampleDataStateText))]
    [NotifyPropertyChangedFor(nameof(SampleDataStateAutomationId))]
    public partial SampleDataState DataState { get; private set; } = SampleDataState.Idle;

    [ObservableProperty]
    public partial string UnavailableTitle { get; private set; }

    [ObservableProperty]
    public partial string UnavailableBody { get; private set; }

    [ObservableProperty]
    public partial string RetryButtonText { get; private set; }

    [ObservableProperty]
    public partial string RetryAutomationName { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGlobalScope))]
    [NotifyPropertyChangedFor(nameof(IsProviderScope))]
    public partial DashboardScopeMode Scope { get; private set; } = DashboardScopeMode.Global;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProviderSummary))]
    [NotifyPropertyChangedFor(nameof(SelectedProviderName))]
    [NotifyPropertyChangedFor(nameof(SelectedProviderCostText))]
    [NotifyPropertyChangedFor(nameof(SelectedProviderTokensText))]
    [NotifyPropertyChangedFor(nameof(SelectedProviderHasLimits))]
    [NotifyPropertyChangedFor(nameof(SelectedProviderHasCoverageHint))]
    [NotifyPropertyChangedFor(nameof(SelectedProviderCoverageHintText))]
    public partial DashboardProviderOption? SelectedProvider { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<DashboardProviderOption> ProviderOptions { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProviderSummary))]
    [NotifyPropertyChangedFor(nameof(SelectedProviderCostText))]
    [NotifyPropertyChangedFor(nameof(SelectedProviderTokensText))]
    public partial IReadOnlyList<DashboardProviderSummary> ProviderSummaries { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<SpendSlice> GlobalSpendSlices { get; private set; } = [];

    [ObservableProperty]
    public partial UsageHeatmapModel GlobalHeatmap { get; private set; } = UsageHeatmapModel.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<DashboardActivitySummary> GlobalActivity { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGlobalProviderLimits))]
    public partial IReadOnlyList<QuotaWindow> GlobalProviderLimits { get; private set; } = [];

    [ObservableProperty]
    public partial UsageHeatmapModel SelectedProviderHeatmap { get; private set; } = UsageHeatmapModel.Empty;

    [ObservableProperty]
    public partial UsageReportTrendDataset SelectedProviderTrend { get; private set; } = UsageReportTrendDataset.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProviderHasLimits))]
    public partial IReadOnlyList<QuotaWindow> SelectedProviderLimits { get; private set; } = [];

    [ObservableProperty]
    public partial string GlobalCostText { get; private set; } = "$0";

    [ObservableProperty]
    public partial string GlobalDonutCenterText { get; private set; } = "$0";

    [ObservableProperty]
    public partial string GlobalFooterText { get; private set; } = "Global";

    [ObservableProperty]
    public partial string GlobalTokensText { get; private set; } = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGlobalCostBreakdown))]
    public partial string? GlobalCostBreakdownText { get; private set; }

    public bool HasGlobalCostBreakdown => !string.IsNullOrWhiteSpace(GlobalCostBreakdownText);

    public bool IsLoading => ResultSurface == FlyoutSurfaceState.Loading;

    public AppSessionHost Host => _liveSession.Host;

    public bool IsSampleUnavailable => ResultSurface == FlyoutSurfaceState.SampleUnavailable;

    public bool IsSampleModeEnabled => _general.IsSampleModeEnabled;

    public bool IsSampleContext => IsSampleModeEnabled;

    public bool IsLiveLoading => IsLoading && !IsSampleModeEnabled;

    public bool IsSampleLoading => IsLoading && IsSampleModeEnabled;

    public bool IsLocalUsageVisible => _hasLocalUsage && !IsSampleModeEnabled;

    public bool IsRefreshing => IsSessionRefreshing;

    public bool IsGlobalScope => Scope == DashboardScopeMode.Global;

    public bool IsProviderScope => Scope == DashboardScopeMode.Provider;

    public DashboardVisualizationMode Visualization =>
        _appearance.Settings.DashboardVisualization;

    public bool IsListVisualization => Visualization == DashboardVisualizationMode.List;

    public bool IsDonutVisualization => Visualization == DashboardVisualizationMode.Donut;

    public bool IsHeatmapVisualization => Visualization == DashboardVisualizationMode.Heatmap;

    public string VisualizationToggleGlyph => Visualization switch
    {
        DashboardVisualizationMode.List => "\uEB05",
        DashboardVisualizationMode.Donut => "\uE787",
        _ => "\uE8FD",
    };

    public string VisualizationToggleText => _getString(Visualization switch
    {
        DashboardVisualizationMode.List => "HeaderVisualizationToDonut",
        DashboardVisualizationMode.Donut => "HeaderVisualizationToHeatmap",
        _ => "HeaderVisualizationToList",
    });

    public bool IsActivitySummaryVisible => !IsHeatmapVisualization;

    public bool HasCoverageHint => !string.IsNullOrWhiteSpace(LocalUsage.NoticeText);

    public string CoverageHintText => LocalUsage.NoticeText;

    public DashboardProviderSummary? SelectedProviderSummary => SelectedProvider is null
        ? null
        : ProviderSummaries.FirstOrDefault(summary => string.Equals(
            summary.ProviderId,
            SelectedProvider.ProviderId,
            StringComparison.Ordinal));

    public string SelectedProviderName => SelectedProvider?.Name ?? string.Empty;

    public string SelectedProviderCostText => SelectedProviderSummary?.CostText ?? "$0";

    public string SelectedProviderTokensText => SelectedProviderSummary?.TokensText ?? "0";

    public bool SelectedProviderHasData => SelectedProviderSummary?.HasData ?? false;

    public bool SelectedProviderIsPartial => SelectedProviderSummary?.IsPartial ?? false;

    public bool SelectedProviderHasUnpricedData =>
        SelectedProviderSummary?.HasUnpricedData ?? false;

    public bool SelectedProviderHasLimits => SelectedProviderLimits.Count > 0;

    public bool HasGlobalProviderLimits => GlobalProviderLimits.Count > 0;

    public bool SelectedProviderHasCoverageHint => SelectedProvider is not null;

    public string SelectedProviderCoverageGlyph => !SelectedProviderHasData
        ? "\uE783"
        : SelectedProviderIsPartial || SelectedProviderHasUnpricedData
            ? "\uE7BA"
            : "\uE946";

    public string SelectedProviderCoverageHintText
    {
        get
        {
            string detail = SelectedProvider?.ProviderId switch
            {
                "codex" => _getString("CompactProviderCodexCoverageHint"),
                "cursor" => _getString("CompactProviderCursorCoverageHint"),
                "grok" => _getString("CompactProviderGrokCoverageHint"),
                "opencode" => _getString("CompactProviderOpenCodeCoverageHint"),
                "antigravity" => _getString("CompactProviderAntigravityCoverageHint"),
                "claude" => _getString("CompactProviderClaudeCoverageHint"),
                "amp" => _getString("CompactProviderAmpCoverageHint"),
                "mux" => _getString("CompactProviderMuxCoverageHint"),
                "goose" => _getString("CompactProviderGooseCoverageHint"),
                "hermes" => _getString("CompactProviderHermesCoverageHint"),
                "zcode" => _getString("CompactProviderZcodeCoverageHint"),
                _ => string.Empty,
            };
            string[] statuses = !SelectedProviderHasData
                ? [_getString("ProviderStatusNoData")]
                : [
                    .. SelectedProviderIsPartial
                        ? [_getString("UsageReportCoveragePartial")]
                        : Array.Empty<string>(),
                    .. SelectedProviderHasUnpricedData
                        ? [_getString("UsageReportCoverageUnpriced")]
                        : Array.Empty<string>(),
                ];
            if (statuses.Length == 0)
            {
                return detail;
            }

            return string.IsNullOrWhiteSpace(detail)
                ? string.Join(". ", statuses)
                : $"{string.Join(". ", statuses)}. {detail}";
        }
    }

    public string CompactPeriodText => _getString("CompactPeriod30Days");

    public bool AreAllProvidersHidden => _personalization.AreAllProvidersHidden;

    /// <summary>
    /// True once a presence probe or a scan has answered for every configured provider.
    /// </summary>
    public bool HasProviderDetection => _rawLocalUsage?.ProviderStatuses.Count > 0;

    /// <summary>
    /// True when detection ran and found no installed tool. This is a real state, not a
    /// provider failure, so it deserves its own message.
    /// </summary>
    public bool HasNoDetectedProviders => !IsSampleModeEnabled
        && HasProviderDetection
        && DetectedProviderIds.Length == 0;

    /// <summary>
    /// Providers whose local root was found. A missing root means the tool is not installed,
    /// so it must not appear in the dashboard list or the tray popover.
    /// </summary>
    private string[] DetectedProviderIds => _rawLocalUsage is null
        ? []
        : _rawLocalUsage.ProviderStatuses
            .Where(status => status.StatusKind != ProviderStatusKind.Missing)
            .Select(status => status.ProviderId)
            .ToArray();

    public string Heading => _getString(
        IsSampleModeEnabled ? "SampleTotalSpendHeading" : "LiveDashboardHeading");

    public bool IsLiveDataStateVisible => !IsSampleModeEnabled;

    public bool IsSampleDataStateVisible => IsSampleModeEnabled;

    public string LiveDataStateText => _hasLocalUsage && ActiveSample.HasSpend
        ? ActiveSample.PeriodLabel
        : CodexLiveStateFormatter.Format(
            DataState,
            IsSampleModeEnabled,
            _publishedObservedAtUtc,
            _retryAtUtc,
            _liveSession.Clock.GetUtcNow(),
            _getString);

    public string SampleDataStateText => _getString(DataState switch
    {
        SampleDataState.CacheRefreshing => "SampleStateCacheRefreshing",
        SampleDataState.StaleCacheRefreshing => "SampleStateStaleCacheRefreshing",
        SampleDataState.Fresh => "SampleStateFresh",
        SampleDataState.Partial => "SampleStatePartial",
        SampleDataState.Stale => "SampleStateStale",
        SampleDataState.Error => "SampleStateError",
        SampleDataState.Throttled => "SampleStateThrottled",
        SampleDataState.NotSaved => "SampleStateNotSaved",
        _ => "SamplePeriodNormal",
    });

    public string SampleDataStateAutomationId => DataState switch
    {
        SampleDataState.CacheRefreshing => "SampleStateCacheRefreshing",
        SampleDataState.StaleCacheRefreshing => "SampleStateStaleCacheRefreshing",
        SampleDataState.Fresh => "SampleStateFresh",
        SampleDataState.Partial => "SampleStatePartial",
        SampleDataState.Stale => "SampleStateStale",
        SampleDataState.Error => "SampleStateError",
        SampleDataState.Throttled => "SampleStateThrottled",
        SampleDataState.NotSaved => "SampleStateNotSaved",
        _ => "SampleStateIdle",
    };

    /// <summary>
    /// Whether the panel is on screen. A hidden panel keeps watching for a due retry, and stops
    /// reprojecting the dashboard on every tick, because nobody can read the result.
    /// </summary>
    public void SetPanelVisible(bool isVisible)
    {
        if (_isPanelVisible == isVisible)
        {
            return;
        }

        _isPanelVisible = isVisible;
        if (isVisible)
        {
            RefreshRelativeTime();
        }
    }

    public void RefreshRelativeTime()
    {
        if (IsSampleModeEnabled)
        {
            return;
        }

        if (_retryAtUtc is DateTimeOffset retryAtUtc
            && retryAtUtc <= _liveSession.Clock.GetUtcNow().ToUniversalTime()
            && !IsSessionRefreshing)
        {
            _retryAtUtc = null;
            _ = RunRefreshAsync(scenario: null, forceRefresh: false);
            return;
        }

        if (_publishedObservedAtUtc is not null)
        {
            OnPropertyChanged(nameof(LiveDataStateText));
        }

        if (_isPanelVisible
            && _rawDashboard is not null
            && _appearance.Settings.ResetTimeDisplay == ResetTimeDisplayMode.Relative)
        {
            PublishActiveDashboard(_rawDashboard);
        }
    }

    public void ShowGlobal() => Scope = DashboardScopeMode.Global;

    public void SelectProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        DashboardProviderOption? option = ProviderOptions.FirstOrDefault(candidate =>
            string.Equals(candidate.ProviderId, providerId, StringComparison.Ordinal));
        if (option is null)
        {
            return;
        }

        SelectedProvider = option;
        Scope = DashboardScopeMode.Provider;
    }

    public void SetVisualization(DashboardVisualizationMode visualization)
    {
        AppearanceOption<DashboardVisualizationMode>? option =
            _appearance.DashboardVisualizationOptions.FirstOrDefault(candidate =>
                candidate.Value == visualization);
        if (option is not null)
        {
            _appearance.SelectedDashboardVisualization = option;
        }
    }

    public void CycleVisualization() => SetVisualization(Visualization switch
    {
        DashboardVisualizationMode.List => DashboardVisualizationMode.Donut,
        DashboardVisualizationMode.Donut => DashboardVisualizationMode.Heatmap,
        _ => DashboardVisualizationMode.List,
    });

    public UsageReportRequest CreateReportRequest(DateOnly? focusDate = null) => new(
        Scope == DashboardScopeMode.Provider
            ? UsageReportScope.Provider
            : UsageReportScope.Global,
        Scope == DashboardScopeMode.Provider ? SelectedProvider?.ProviderId : null,
        windowDays: 30,
        metric: UsageReportMetric.Cost,
        breakdown: focusDate is null
            ? UsageReportBreakdown.Model
            : UsageReportBreakdown.Day,
        focusDate: focusDate);

    /// <summary>
    /// Quota windows for one provider, taken from the projection the panel is already showing.
    /// The tray asks this once per provider it displays, and each call used to reproject the
    /// whole dashboard; reading the published projection also guarantees the tray and the panel
    /// never disagree.
    /// </summary>
    public IReadOnlyList<QuotaWindow> GetProviderLimits(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (_providerLimitsById.TryGetValue(
                providerId,
                out IReadOnlyList<QuotaWindow>? cachedLimits))
        {
            return cachedLimits;
        }

        DashboardSnapshot source = _appearanceDashboard ?? ActiveSample;
        ProviderCard? providerCard = source.Providers.FirstOrDefault(card =>
            string.Equals(card.ProviderId, providerId, StringComparison.Ordinal));
        IReadOnlyList<QuotaWindow> limits = providerCard is null
            ? []
            : providerCard.Windows.Concat(providerCard.SecondaryWindowItems).ToArray();
        _providerLimitsById.Add(providerId, limits);
        return limits;
    }

    public async Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await RunRefreshAsync(scenario: null, forceRefresh: false).ConfigureAwait(true);
        if (!_disposed && !HasGlobalProviderLimits && !HasRequestedForcedRefresh)
        {
            // One live pass per process when the cache-first snapshot has no official
            // Codex quota windows. The first panel open must not start a second pass.
            await RunRefreshAsync(scenario: null, forceRefresh: true).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// True once this process has asked for a live refresh. Startup uses this so the first
    /// panel open cannot start a second live pass.
    /// </summary>
    public bool HasRequestedForcedRefresh { get; private set; }

    /// <summary>
    /// True once a forced refresh has run to completion.
    /// </summary>
    public bool HasCompletedForcedRefresh { get; private set; }

    public Task RefreshLiveAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RunRefreshAsync(scenario: null, forceRefresh: true);
    }

    public void Cancel()
    {
        _sampleSession.Cancel();
        _liveSession.Cancel();
        _refreshCancellation?.Cancel();
        _refreshCancellation = null;
        IsSessionRefreshing = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel();
        _general.SampleModeChanged -= OnSampleModeChanged;
        _general.SampleScenarioChanged -= OnSampleScenarioChanged;
        _appearance.SettingsChanged -= OnAppearanceChanged;
        _personalization.LayoutChanged -= OnLayoutChanged;
        _personalization.PropertyChanged -= OnPersonalizationPropertyChanged;
        _providerStatus.UnbindRefresh();
        _liveSession.Dispose();
        RefreshCommand.NotifyCanExecuteChanged();
        GC.SuppressFinalize(this);
    }

    partial void OnSelectedProviderChanged(DashboardProviderOption? value)
    {
        if (value is null)
        {
            return;
        }

        ProviderOptions = ProviderOptions
            .Select(option => option with
            {
                IsSelected = string.Equals(
                    option.ProviderId,
                    value.ProviderId,
                    StringComparison.Ordinal),
            })
            .ToArray();
        ApplySelectedProviderProjection(CompactDashboardProjector.CreateSelectedProvider(
            value.ProviderId,
            _localUsageRollups,
            DateOnly.FromDateTime(_liveSession.Clock.GetLocalNow().DateTime),
            _getString,
            GetProviderLimits));
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => RunRefreshAsync(
        IsSampleModeEnabled ? _general.SelectedSampleScenario.Value : null,
        forceRefresh: true);

    private bool CanRefresh() => !_disposed && !IsLoading && !IsRefreshing;

    private async Task RunRefreshAsync(SampleScenario? scenario, bool forceRefresh)
    {
        if (forceRefresh)
        {
            HasRequestedForcedRefresh = true;
        }

        _refreshCancellation?.Cancel();
        _sampleSession.Cancel();
        _liveSession.Cancel();
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        IsSessionRefreshing = true;

        if (!_hasPublishedDashboard)
        {
            ResultSurface = FlyoutSurfaceState.Loading;
        }

        try
        {
            if (scenario is SampleScenario sampleScenario)
            {
                await _sampleSession.RunAsync(
                    sampleScenario,
                    forceRefresh,
                    _getString,
                    OnSampleSessionChanged,
                    cancellation.Token).ConfigureAwait(true);
            }
            else
            {
                await _liveSession.RunAsync(
                    forceRefresh,
                    cancellation.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, cancellation))
            {
                IsSessionRefreshing = false;
                _refreshCancellation = null;
                if (forceRefresh && !cancellation.IsCancellationRequested)
                {
                    HasCompletedForcedRefresh = true;
                }
            }

            cancellation.Dispose();
        }
    }

    private void OnSampleSessionChanged(SampleDashboardSession session)
    {
        _activeScenario = session.ActiveScenario;
        _publishedObservedAtUtc = session.PublishedObservedAtUtc;
        _retryAtUtc = session.RetryAtUtc;
        SetDataState(session.DataState);
        if (session.LastDashboard is not null)
        {
            PublishActiveDashboard(session.LastDashboard);
            _hasPublishedDashboard = true;
            ResultSurface = FlyoutSurfaceState.Sample;
            RevealToken++;
        }
        else if (!session.HasPublished)
        {
            PublishUnavailable(outcome: null);
        }
    }

    private void OnLiveSessionChanged(LiveDashboardSession session) =>
        InOnePass(() => ApplyLiveSessionChange(session));

    private void ApplyLiveSessionChange(LiveDashboardSession session)
    {
        _lastCodexSnapshot = session.LastCodexSnapshot;
        _lastCodexOutcome = session.LastCodexOutcome;
        _publishedObservedAtUtc = session.PublishedObservedAtUtc;
        _retryAtUtc = session.RetryAtUtc;
        SetDataState(session.DataState);
        if (session.RawLocalUsage is not null)
        {
            _rawLocalUsage = session.RawLocalUsage;
            _localUsageRollups = session.LocalUsageRollups;
            LocalUsage = _personalization.Apply(session.RawLocalUsage);
            _hasLocalUsage = session.HasLocalUsage;
            RebuildProviderStatuses();
            OnPropertyChanged(nameof(IsLocalUsageVisible));
            OnPropertyChanged(nameof(HasProviderDetection));
            OnPropertyChanged(nameof(HasNoDetectedProviders));
            RebuildCompactProjection();
        }

        if (session.LastCodexSnapshot is null
            && session.HasLocalUsage
            && (session.RawLocalUsage is null
                || session.RawLocalUsage.SpendBreakdown.AgentSlices.Count == 0))
        {
            if (!session.HasPublished && _lastCodexOutcome is not null)
            {
                PublishUnavailable(_lastCodexOutcome);
            }

            return;
        }

        if (PublishCombinedLiveDashboard(reveal: true))
        {
            _hasPublishedDashboard = true;
            ResultSurface = FlyoutSurfaceState.Sample;
        }
        else if (session.LastCodexOutcome is not null
            && session.LastCodexSnapshot is null)
        {
            PublishUnavailable(session.LastCodexOutcome);
        }
    }

    private static Task ApplyProviderUpdateAsync(AppSessionUpdateEventArgs update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return Task.CompletedTask;
    }

    private bool PublishCombinedLiveDashboard(bool reveal)
    {
        var providers = new List<ProviderCard>();
        if (_lastCodexSnapshot is not null)
        {
            providers.AddRange(CodexDashboardProjector.Create(
                _lastCodexSnapshot,
                _liveSession.Clock,
                _getString,
                _liveSession.CodexWindowUsedTokens).Providers);
        }


        IReadOnlyList<SpendSlice> spendSlices = _hasLocalUsage && _rawLocalUsage is not null
            ? _rawLocalUsage.SpendBreakdown.AgentSlices
            : [];
        IReadOnlyList<SpendSlice> additionalSpendSlices = [];
        if (providers.Count == 0 && spendSlices.Count == 0 && additionalSpendSlices.Count == 0)
        {
            return false;
        }

        PublishActiveDashboard(LiveDashboardComposer.Create(
            providers,
            _hasLocalUsage ? _rawLocalUsage : null,
            additionalSpendSlices,
            _getString("LiveDashboardPeriod"),
            _personalization.SummarizeSpend));
        _activeScenario = null;
        _hasPublishedDashboard = true;
        ResultSurface = FlyoutSurfaceState.Sample;
        if (reveal)
        {
            RevealToken++;
        }

        return true;
    }

    private void PublishUnavailable(ProviderOutcome? outcome)
    {
        if (!IsSampleModeEnabled)
        {
            _lastCodexOutcome = outcome;
        }

        _hasPublishedDashboard = false;
        if (!IsSampleModeEnabled
            && _hasLocalUsage
            && _rawLocalUsage?.SpendBreakdown.AgentSlices.Count > 0)
        {
            _ = PublishCombinedLiveDashboard(reveal: true);
            return;
        }

        ResultSurface = FlyoutSurfaceState.SampleUnavailable;
        DataState = outcome switch
        {
            ProviderOutcome.Throttled => SampleDataState.Throttled,
            ProviderOutcome.UnsupportedAccount or ProviderOutcome.PolicyBlocked
                or ProviderOutcome.NotConfigured => SampleDataState.Unavailable,
            _ => SampleDataState.Error,
        };
        RebuildProviderStatuses();
        if (IsSampleModeEnabled)
        {
            UnavailableTitle = _getString("SampleUnavailableTitle");
            UnavailableBody = _getString("SampleUnavailableBody");
            RetryButtonText = _getString("SampleRetry");
            RetryAutomationName = _getString("SampleRetry");
        }
        else if (HasNoDetectedProviders)
        {
            UnavailableTitle = _getString("ProvidersUndetectedTitle");
            UnavailableBody = _getString("ProvidersUndetectedBody");
            RetryButtonText = _getString("SampleRetry");
            RetryAutomationName = _getString("CodexRetry");
        }
        else
        {
            UnavailableTitle = _getString("CodexUnavailableTitle");
            UnavailableBody = _getString("CodexUnavailableBody");
            RetryButtonText = _getString("SampleRetry");
            RetryAutomationName = _getString("CodexRetry");
        }
    }

    private void SetDataState(SampleDataState state)
    {
        DataState = state;
        RebuildProviderStatuses();
        OnPropertyChanged(nameof(LiveDataStateText));
    }

    private void RebuildProviderStatuses() =>
        _providerStatus.Update(
            _lastCodexOutcome,
            _hasPublishedDashboard,
            DataState,
            LocalUsage.ProviderStatuses);

    private void RebuildSamplePreview() => InOnePass(() =>
    {
        PublishActiveDashboard(SampleDashboardCatalog.Create(
            _general.SelectedSampleScenario.Value,
            _getString));
        DataState = SampleDataState.Idle;
        RebuildCompactProjection();
    });

    private void PublishActiveDashboard(DashboardSnapshot dashboard)
    {
        _rawDashboard = dashboard;
        DashboardSnapshot appearanceDashboard = AppearanceDashboardProjector.Apply(
            dashboard,
            _appearance.Settings,
            GetClock(IsSampleModeEnabled ? _activeScenario : null).GetUtcNow(),
            _getString);
        _appearanceDashboard = appearanceDashboard;
        _providerLimitsById.Clear();
        ActiveSample = _personalization.Apply(appearanceDashboard);
        RebuildCompactProjection();
        OnPropertyChanged(nameof(LiveDataStateText));
        if (_rawLocalUsage is not null)
        {
            LocalUsage = _personalization.Apply(_rawLocalUsage);
        }

        OnPropertyChanged(nameof(AreAllProvidersHidden));
    }

    private void OnSampleModeChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsSampleModeEnabled));
        OnPropertyChanged(nameof(IsSampleContext));
        OnPropertyChanged(nameof(IsLiveLoading));
        OnPropertyChanged(nameof(IsSampleLoading));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(IsLiveDataStateVisible));
        OnPropertyChanged(nameof(IsSampleDataStateVisible));
        OnPropertyChanged(nameof(SampleDataStateText));
        OnPropertyChanged(nameof(SampleDataStateAutomationId));
        OnPropertyChanged(nameof(IsLocalUsageVisible));
        Cancel();
        _hasPublishedDashboard = false;
        _activeScenario = null;
        ResultSurface = FlyoutSurfaceState.Loading;
        if (IsSampleModeEnabled)
        {
            RebuildSamplePreview();
        }

        _ = RunRefreshAsync(
            IsSampleModeEnabled ? _general.SelectedSampleScenario.Value : null,
            forceRefresh: false);
    }

    private void OnSampleScenarioChanged(object? sender, EventArgs e)
    {
        if (IsSampleModeEnabled)
        {
            _ = RunRefreshAsync(_general.SelectedSampleScenario.Value, forceRefresh: true);
        }
        else
        {
            RebuildSamplePreview();
        }
    }

    private void OnAppearanceChanged(object? sender, AppearanceSettings settings)
    {
        AppearanceSettings previous = _lastAppearanceSettings;
        _lastAppearanceSettings = settings;
        OnPropertyChanged(nameof(Visualization));
        OnPropertyChanged(nameof(IsListVisualization));
        OnPropertyChanged(nameof(IsDonutVisualization));
        OnPropertyChanged(nameof(IsHeatmapVisualization));
        OnPropertyChanged(nameof(VisualizationToggleGlyph));
        OnPropertyChanged(nameof(VisualizationToggleText));
        OnPropertyChanged(nameof(IsActivitySummaryVisible));
        bool dashboardProjectionChanged = previous.UsageDisplay != settings.UsageDisplay
            || previous.ResetTimeDisplay != settings.ResetTimeDisplay;
        if (dashboardProjectionChanged && _rawDashboard is not null)
        {
            PublishActiveDashboard(_rawDashboard);
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        if (_rawDashboard is not null)
        {
            PublishActiveDashboard(_rawDashboard);
        }
    }

    private void OnPersonalizationPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.Equals(
                e.PropertyName,
                nameof(_personalization.Providers),
                StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(AreAllProvidersHidden));
        }
    }

    private TimeProvider GetClock(SampleScenario? scenario) =>
        scenario is null ? _liveSession.Clock : _sampleSession.Clock;

    /// <summary>
    /// Groups updates that each ask for the compact projection, and rebuilds it once when the
    /// group ends. One session update used to rebuild it twice: once for the local usage rows
    /// and again for the composed dashboard, and the first result was thrown away unseen.
    /// </summary>
    private void InOnePass(Action apply)
    {
        _projectionBatchDepth++;
        try
        {
            apply();
        }
        finally
        {
            _projectionBatchDepth--;
            if (_projectionBatchDepth == 0 && _compactProjectionIsStale)
            {
                _compactProjectionIsStale = false;
                RebuildCompactProjectionCore();
            }
        }
    }

    private void RebuildCompactProjection()
    {
        if (_projectionBatchDepth > 0)
        {
            _compactProjectionIsStale = true;
            return;
        }

        RebuildCompactProjectionCore();
    }

    private void RebuildCompactProjectionCore()
    {
        CompactDashboardProjection projection = CompactDashboardProjector.Create(
            DateOnly.FromDateTime(_liveSession.Clock.GetLocalNow().DateTime),
            _localUsageRollups,
            DetectedProviderIds,
            IsSampleModeEnabled,
            ActiveSample,
            LocalUsage,
            SelectedProvider?.ProviderId,
            _getString,
            GetProviderLimits);
        ProviderSummaries = projection.ProviderSummaries;
        ProviderOptions = projection.ProviderOptions;
        GlobalSpendSlices = projection.GlobalSpendSlices;
        GlobalCostText = projection.GlobalCostText;
        GlobalDonutCenterText = projection.GlobalDonutCenterText;
        GlobalFooterText = projection.GlobalFooterText;
        GlobalTokensText = projection.GlobalTokensText;
        GlobalCostBreakdownText = projection.GlobalCostBreakdownText;
        GlobalHeatmap = projection.GlobalHeatmap;
        GlobalActivity = projection.GlobalActivity;
        GlobalProviderLimits = projection.GlobalProviderLimits;

        DashboardProviderOption? nextSelection = ProviderOptions.FirstOrDefault(option =>
            string.Equals(option.ProviderId, projection.SelectedProviderId, StringComparison.Ordinal))
            ?? (ProviderOptions.Count == 0 ? null : ProviderOptions[0]);
        if (!Equals(SelectedProvider, nextSelection))
        {
            SelectedProvider = nextSelection;
        }
        else
        {
            ApplySelectedProviderProjection(new CompactSelectedProviderProjection(
                projection.SelectedProviderHeatmap,
                projection.SelectedProviderTrend,
                projection.SelectedProviderLimits));
        }

        OnPropertyChanged(nameof(HasCoverageHint));
        OnPropertyChanged(nameof(CoverageHintText));
    }

    private void ApplySelectedProviderProjection(CompactSelectedProviderProjection projection)
    {
        SelectedProviderHeatmap = projection.Heatmap;
        SelectedProviderTrend = projection.Trend;
        SelectedProviderLimits = projection.Limits;
        OnPropertyChanged(nameof(SelectedProviderSummary));
        OnPropertyChanged(nameof(SelectedProviderName));
        OnPropertyChanged(nameof(SelectedProviderCostText));
        OnPropertyChanged(nameof(SelectedProviderTokensText));
        OnPropertyChanged(nameof(SelectedProviderHasData));
        OnPropertyChanged(nameof(SelectedProviderIsPartial));
        OnPropertyChanged(nameof(SelectedProviderHasUnpricedData));
        OnPropertyChanged(nameof(SelectedProviderCoverageGlyph));
        OnPropertyChanged(nameof(SelectedProviderCoverageHintText));
        OnPropertyChanged(nameof(SelectedProviderHasLimits));
    }
}
