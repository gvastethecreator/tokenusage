using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Session;

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

    public bool IsLoading => ResultSurface == FlyoutSurfaceState.Loading;

    public AppSessionHost Host => _liveSession.Host;

    public bool IsSampleUnavailable => ResultSurface == FlyoutSurfaceState.SampleUnavailable;

    public bool IsSampleModeEnabled => _general.IsSampleModeEnabled;

    public bool IsSampleContext => IsSampleModeEnabled;

    public bool IsLiveLoading => IsLoading && !IsSampleModeEnabled;

    public bool IsSampleLoading => IsLoading && IsSampleModeEnabled;

    public bool IsLocalUsageVisible => _hasLocalUsage && !IsSampleModeEnabled;

    public bool IsRefreshing => IsSessionRefreshing;

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

    public Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RunRefreshAsync(scenario: null, forceRefresh: false);
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
            LocalUsage = _personalization.Apply(session.RawLocalUsage);
            _hasLocalUsage = session.HasLocalUsage;
            RebuildProviderStatuses();
            OnPropertyChanged(nameof(IsLocalUsageVisible));
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
}
