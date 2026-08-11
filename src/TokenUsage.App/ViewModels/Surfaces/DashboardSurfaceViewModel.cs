using System.ComponentModel;
using System.Globalization;
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
    private CancellationTokenSource? _refreshCancellation;
    private SampleScenario? _activeScenario;
    private bool _hasPublishedDashboard;
    private DateTimeOffset? _publishedObservedAtUtc;
    private DateTimeOffset? _retryAtUtc;
    private bool _hasLocalUsage;
    private LocalUsageCard? _rawLocalUsage;
    private IReadOnlyList<DailyUsageRollup> _localUsageRollups = [];
    private ProviderOutcome? _lastCodexOutcome;
    private ProviderSnapshot? _lastCodexSnapshot;
    private DashboardSnapshot? _rawDashboard;
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
    [NotifyPropertyChangedFor(nameof(HasGlobalCodexLimits))]
    public partial IReadOnlyList<QuotaWindow> GlobalCodexLimits { get; private set; } = [];

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

    public bool HasGlobalCodexLimits => GlobalCodexLimits.Count > 0;

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
            return statuses.Length == 0
                ? detail
                : $"{string.Join(". ", statuses)}. {detail}";
        }
    }

    public string CompactPeriodText => _getString("CompactPeriod30Days");

    public bool AreAllProvidersHidden => _personalization.AreAllProvidersHidden;

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

        if (_rawDashboard is not null
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

    public IReadOnlyList<QuotaWindow> GetProviderLimits(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        DashboardSnapshot source = _rawDashboard is null
            ? ActiveSample
            : AppearanceDashboardProjector.Apply(
                _rawDashboard,
                _appearance.Settings,
                _liveSession.Clock.GetUtcNow(),
                _getString);
        ProviderCard? providerCard = source.Providers.FirstOrDefault(card =>
            string.Equals(card.ProviderId, providerId, StringComparison.Ordinal));
        return providerCard is null
            ? []
            : providerCard.Windows.Concat(providerCard.SecondaryWindowItems).ToArray();
    }

    public async Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await RunRefreshAsync(scenario: null, forceRefresh: false).ConfigureAwait(true);
        if (!_disposed && !HasGlobalCodexLimits)
        {
            // Finish startup through the same refresh cycle as the toolbar action when the
            // cache-first pass did not project the official Codex quota windows.
            await RunRefreshAsync(scenario: null, forceRefresh: true).ConfigureAwait(true);
        }
    }

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
        RebuildSelectedProviderProjection();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => RunRefreshAsync(
        IsSampleModeEnabled ? _general.SelectedSampleScenario.Value : null,
        forceRefresh: true);

    private bool CanRefresh() => !_disposed && !IsLoading && !IsRefreshing;

    private async Task RunRefreshAsync(SampleScenario? scenario, bool forceRefresh)
    {
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

    private void OnLiveSessionChanged(LiveDashboardSession session)
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
                _getString).Providers);
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

    private void RebuildSamplePreview()
    {
        PublishActiveDashboard(SampleDashboardCatalog.Create(
            _general.SelectedSampleScenario.Value,
            _getString));
        DataState = SampleDataState.Idle;
        RebuildCompactProjection();
    }

    private void PublishActiveDashboard(DashboardSnapshot dashboard)
    {
        _rawDashboard = dashboard;
        DashboardSnapshot appearanceDashboard = AppearanceDashboardProjector.Apply(
            dashboard,
            _appearance.Settings,
            GetClock(IsSampleModeEnabled ? _activeScenario : null).GetUtcNow(),
            _getString);
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
        OnPropertyChanged(nameof(Visualization));
        OnPropertyChanged(nameof(IsListVisualization));
        OnPropertyChanged(nameof(IsDonutVisualization));
        OnPropertyChanged(nameof(IsHeatmapVisualization));
        OnPropertyChanged(nameof(VisualizationToggleGlyph));
        OnPropertyChanged(nameof(VisualizationToggleText));
        OnPropertyChanged(nameof(IsActivitySummaryVisible));
        if (_rawDashboard is not null)
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

    private void RebuildCompactProjection()
    {
        DateOnly today = DateOnly.FromDateTime(
            _liveSession.Clock.GetLocalNow().DateTime);
        DateOnly from = today.AddDays(-29);
        DailyUsageRollup[] rollups = _localUsageRollups
            .Where(rollup => rollup.Date >= from && rollup.Date <= today)
            .ToArray();

        DashboardProviderSummary[] summaries = rollups.Length == 0
            ? CreateFallbackProviderSummaries()
            : CreateProviderSummaries(rollups);
        ProviderSummaries = summaries;
        string? selectedId = SelectedProvider?.ProviderId;
        string? nextSelectedId = summaries.Any(summary => string.Equals(
            summary.ProviderId,
            selectedId,
            StringComparison.Ordinal))
                ? selectedId
                : summaries.FirstOrDefault(summary => string.Equals(
                    summary.ProviderId,
                    "codex",
                    StringComparison.Ordinal))?.ProviderId
                    ?? summaries.FirstOrDefault()?.ProviderId;
        ProviderOptions = summaries
            .Select(summary => new DashboardProviderOption(
                summary.ProviderId,
                summary.Name,
                string.Equals(
                    summary.ProviderId,
                    nextSelectedId,
                    StringComparison.Ordinal)))
            .ToArray();
        GlobalSpendSlices = summaries
            .Where(summary => summary.CostUsd > 0m)
            .Select(summary => new SpendSlice(
                summary.ProviderId,
                summary.Name,
                decimal.ToDouble(summary.CostUsd),
                summary.CostText,
                summary.ColorHex,
                summary.CostText))
            .ToArray();
        decimal totalCost = summaries.Sum(summary => summary.CostUsd);
        long totalTokens = summaries.Sum(summary => summary.TotalTokens);
        GlobalCostText = summaries.Length == 0 && ActiveSample is not null
            ? ActiveSample.TotalSpendAmount
            : FormatCost(totalCost);
        GlobalDonutCenterText = GlobalCostText.Replace(" USD", "\nUSD", StringComparison.Ordinal);
        GlobalFooterText = string.Format(
            CultureInfo.CurrentCulture,
            _getString("CompactGlobalFooterFormat"),
            GlobalCostText,
            summaries.Length);
        GlobalTokensText = totalTokens == 0
            ? LocalUsage.TotalTokensMetric.Value
            : FormatCompactTokens(totalTokens);
        GlobalHeatmap = rollups.Length == 0
            ? LocalUsage.Heatmap
            : UsageHeatmapProjector.Create(
                rollups,
                today,
                _getString,
                "CompactUsageHeatmap");
        GlobalActivity = CreateActivitySummaries(rollups, today);
        GlobalCodexLimits = GetProviderLimits("codex");

        DashboardProviderOption? nextSelection = ProviderOptions.FirstOrDefault(option =>
            string.Equals(option.ProviderId, nextSelectedId, StringComparison.Ordinal))
            ?? (ProviderOptions.Count == 0 ? null : ProviderOptions[0]);
        if (!Equals(SelectedProvider, nextSelection))
        {
            SelectedProvider = nextSelection;
        }
        else
        {
            RebuildSelectedProviderProjection();
        }

        OnPropertyChanged(nameof(HasCoverageHint));
        OnPropertyChanged(nameof(CoverageHintText));
    }

    private DashboardProviderSummary[] CreateProviderSummaries(
        IReadOnlyList<DailyUsageRollup> rollups)
    {
        string[] providerIds = ["codex", "opencode", "antigravity", "grok", "cursor"];
        var grouped = rollups
            .Where(rollup => providerIds.Contains(rollup.AgentId.Value, StringComparer.Ordinal))
            .GroupBy(rollup => rollup.AgentId.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        decimal totalCost = grouped.Values
            .SelectMany(items => items)
            .Sum(item => (item.ReportedCostUsd ?? 0m) + (item.EstimatedCostUsd ?? 0m));
        long totalTokens = grouped.Values.SelectMany(items => items).Sum(item => item.Tokens.Total);

        return providerIds
            .Select(providerId =>
            {
                DailyUsageRollup[] items = grouped.GetValueOrDefault(providerId) ?? [];
                bool hasData = items.Length > 0;
                bool hasCostData = items.Any(item =>
                    item.ReportedCostUsd is not null || item.EstimatedCostUsd is not null);
                bool isPartial = items.Any(item => item.Coverage is
                    CoverageKind.Partial or CoverageKind.SummaryOnly);
                bool hasUnpricedData = items.Any(item =>
                    item.UnpricedTokens > 0 || item.Coverage == CoverageKind.Unpriced);
                decimal cost = items.Sum(item =>
                    (item.ReportedCostUsd ?? 0m) + (item.EstimatedCostUsd ?? 0m));
                long tokens = items.Sum(item => item.Tokens.Total);
                double share = totalCost > 0
                    ? decimal.ToDouble(cost * 100m / totalCost)
                    : totalTokens > 0
                        ? (double)tokens * 100d / totalTokens
                        : 0d;
                string name = ProviderName(providerId);
                string costText = !hasData
                    ? _getString("CodexUsageMissing")
                    : hasCostData
                        ? FormatCost(cost)
                        : _getString("CompactCostUnavailable");
                string tokensText = hasData
                    ? FormatCompactTokens(tokens)
                    : _getString("CodexUsageMissing");
                string detailText = hasData
                    ? string.Format(CultureInfo.CurrentCulture, "{0:0.#}%", share)
                    : "—";
                return new DashboardProviderSummary(
                    providerId,
                    name,
                    cost,
                    tokens,
                    share,
                    costText,
                    tokensText,
                    detailText,
                    hasData
                        ? $"{name}: {costText}, {tokensText} tokens, {share:0.#}%"
                            + (hasUnpricedData
                                ? $". {_getString("UsageReportCoverageUnpriced")}"
                                : string.Empty)
                        : $"{name}: {_getString("ProviderStatusNoData")}",
                    ProviderColorHex(providerId),
                    $"CompactProvider.{providerId}",
                    share <= 0d ? 0d : Math.Max(2d, share * 4.36d),
                    hasData,
                    hasCostData,
                    isPartial,
                    hasUnpricedData);
            })
            .ToArray();
    }

    private DashboardProviderSummary[] CreateFallbackProviderSummaries()
    {
        if (ActiveSample?.SpendSlices is not { Count: > 0 } slices)
        {
            return [];
        }

        double total = slices.Sum(slice => Math.Max(0, slice.Amount));
        return slices.Select(slice =>
        {
            double share = total <= 0 ? 0 : slice.Amount * 100 / total;
            decimal cost = Convert.ToDecimal(slice.Amount, CultureInfo.InvariantCulture);
            string costText = string.IsNullOrWhiteSpace(slice.LegendAmountText)
                ? FormatCost(cost)
                : slice.LegendAmountText;
            return new DashboardProviderSummary(
                slice.ProviderId,
                slice.ProviderName,
                cost,
                0,
                share,
                costText,
                "—",
                string.Format(CultureInfo.CurrentCulture, "{0:0.#}%", share),
                $"{slice.ProviderName}: {costText}, {share:0.#}%",
                slice.ColorHex ?? ProviderColorHex(slice.ProviderId),
                $"CompactProvider.{slice.ProviderId}",
                Math.Max(2d, share * 4.36d));
        }).ToArray();
    }

    private IReadOnlyList<DashboardActivitySummary> CreateActivitySummaries(
        IReadOnlyList<DailyUsageRollup> rollups,
        DateOnly today)
    {
        long SumSince(int days) => rollups
            .Where(item => item.Date >= today.AddDays(-(days - 1)) && item.Date <= today)
            .Sum(item => item.Tokens.Total);

        return
        [
            new(
                _getString("CompactActivityToday"),
                FormatCompactTokens(SumSince(1)),
                _getString("CompactActivityTokens")),
            new(
                _getString("CompactActivity7Days"),
                FormatCompactTokens(SumSince(7)),
                _getString("CompactActivityTokens")),
            new(
                _getString("CompactActivity30Days"),
                FormatCompactTokens(SumSince(30)),
                _getString("CompactActivityTokens")),
        ];
    }

    private void RebuildSelectedProviderProjection()
    {
        string? providerId = SelectedProvider?.ProviderId;
        if (string.IsNullOrWhiteSpace(providerId))
        {
            SelectedProviderHeatmap = UsageHeatmapModel.Empty;
            SelectedProviderTrend = UsageReportTrendDataset.Empty;
            SelectedProviderLimits = [];
            return;
        }

        DateOnly today = DateOnly.FromDateTime(_liveSession.Clock.GetLocalNow().DateTime);
        DailyUsageRollup[] providerRollups = _localUsageRollups
            .Where(rollup => string.Equals(
                rollup.AgentId.Value,
                providerId,
                StringComparison.Ordinal))
            .ToArray();
        SelectedProviderHeatmap = providerRollups.Length == 0
            ? UsageHeatmapModel.Empty
            : UsageHeatmapProjector.Create(
                providerRollups,
                today,
                _getString,
                $"ProviderUsageHeatmap.{providerId}");
        UsageReportTrendDay[] days = Enumerable.Range(0, 30)
            .Select(offset => today.AddDays(offset - 29))
            .Select(date => new UsageReportTrendDay(
                date,
                date.ToString("d MMM", CultureInfo.CurrentCulture)))
            .ToArray();
        var dailyTokens = providerRollups
            .GroupBy(rollup => rollup.Date)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Tokens.Total));
        SelectedProviderTrend = providerRollups.Length == 0
            ? UsageReportTrendDataset.Empty
            : new UsageReportTrendDataset(
                UsageReportMetric.Tokens,
                days,
                [
                    new UsageReportTrendSeries(
                        providerId,
                        ProviderName(providerId),
                        ProviderColorHex(providerId),
                        days.Select(day => (double)dailyTokens.GetValueOrDefault(day.Date, 0)).ToArray()),
                ]);
        SelectedProviderLimits = GetProviderLimits(providerId);
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

    private string FormatCost(decimal cost) => string.Format(
        CultureInfo.CurrentCulture,
        _getString("LocalUsageUsdFormat"),
        cost);

    private static string FormatCompactTokens(long value)
    {
        double numeric = value;
        double absolute = Math.Abs(numeric);
        return absolute switch
        {
            >= 1_000_000_000 => string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.##}B",
                numeric / 1_000_000_000),
            >= 1_000_000 => string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.#}M",
                numeric / 1_000_000),
            >= 1_000 => string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.#}K",
                numeric / 1_000),
            _ => value.ToString("N0", CultureInfo.CurrentCulture),
        };
    }

    private static string ProviderColorHex(string providerId) => providerId switch
    {
        "antigravity" => "#4285F4",
        "codex" => "#10A37F",
        "cursor" => "#D7D7D7",
        "grok" => "#7C5CFC",
        "opencode" => "#E5488C",
        _ => "#6B7280",
    };

    private string ProviderName(string providerId) => providerId switch
    {
        "antigravity" => _getString("LocalUsageAgentAntigravity"),
        "codex" => _getString("LocalUsageAgentCodex"),
        "cursor" => _getString("LocalUsageAgentCursor"),
        "grok" => _getString("LocalUsageAgentGrok"),
        "opencode" => _getString("LocalUsageAgentOpenCode"),
        _ => providerId,
    };
}
