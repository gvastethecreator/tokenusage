using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using System.ComponentModel;
using System.Data.Common;
using WOpenUsage.App.Localization;
using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Layout;
using WOpenUsage.Core.Providers;
using WOpenUsage.Runtime.Windows.Codex;
using WOpenUsage.Runtime.Windows.VercelAiGateway;

namespace WOpenUsage.App.ViewModels;

public partial class FlyoutViewModel : ObservableObject
{
    private readonly ResourceLoader _resources = new();
    private readonly SampleRefreshCoordinator _sampleRefreshCoordinator;
    private readonly CodexRefreshCoordinator _codexRefreshCoordinator;
    private readonly LocalUsageCoordinator _localUsageCoordinator;
    private readonly DashboardLayoutStore _dashboardLayoutStore;
    private readonly Task _dashboardLayoutInitialization;
    private CancellationTokenSource? _refreshCancellation;
    private FlyoutSurfaceState _resultSurface = FlyoutSurfaceState.Loading;
    private SampleScenario? _activeScenario;
    private bool _hasPublishedDashboard;
    private int _refreshVersion;
    private DateTimeOffset? _publishedObservedAtUtc;
    private DateTimeOffset? _retryAtUtc;
    private bool _hasLocalUsage;
    private ProviderOutcome? _lastCodexOutcome;
    private ProviderSnapshot? _lastCodexSnapshot;
    private DashboardLayout _dashboardLayout = DashboardLayout.Empty;
    private SampleDashboardSnapshot? _rawDashboard;
    private bool _isDashboardLayoutReadOnly;
    private readonly HashSet<string> _expandedDashboardMetricProviders = new(StringComparer.Ordinal);

    public FlyoutViewModel(
        SampleRefreshCoordinator sampleRefreshCoordinator,
        CodexRefreshCoordinator codexRefreshCoordinator,
        LocalUsageCoordinator localUsageCoordinator,
        DashboardLayoutStore dashboardLayoutStore,
        VercelGatewayRefreshCoordinator vercelGatewayCoordinator)
    {
        _sampleRefreshCoordinator = sampleRefreshCoordinator
            ?? throw new ArgumentNullException(nameof(sampleRefreshCoordinator));
        _codexRefreshCoordinator = codexRefreshCoordinator
            ?? throw new ArgumentNullException(nameof(codexRefreshCoordinator));
        _localUsageCoordinator = localUsageCoordinator
            ?? throw new ArgumentNullException(nameof(localUsageCoordinator));
        _dashboardLayoutStore = dashboardLayoutStore
            ?? throw new ArgumentNullException(nameof(dashboardLayoutStore));
        Vercel = new VercelGatewaySettingsViewModel(
            vercelGatewayCoordinator
                ?? throw new ArgumentNullException(nameof(vercelGatewayCoordinator)),
            GetString);
        Vercel.PropertyChanged += OnVercelPropertyChanged;
        LanguageOptions =
        [
            new(AppLanguageCatalog.EnglishUnitedStates, GetString("LanguageEnglish")),
            new(AppLanguageCatalog.SpanishSpain, GetString("LanguageSpanish")),
        ];
        SelectedLanguage = LanguageOptions.Single(option => string.Equals(
            option.LanguageTag,
            AppLanguageRuntime.ActiveLanguageTag,
            StringComparison.OrdinalIgnoreCase));
        SampleScenarios =
        [
            new(SampleScenario.Normal, GetString("SampleScenarioNormal")),
            new(SampleScenario.NearLimit, GetString("SampleScenarioNearLimit")),
            new(SampleScenario.Partial, GetString("SampleScenarioPartial")),
            new(SampleScenario.Stale, GetString("SampleScenarioStale")),
            new(SampleScenario.Error, GetString("SampleScenarioError")),
        ];

        SelectedSampleScenario = SampleScenarios[0];
        UnavailableTitle = GetString("CodexUnavailableTitle");
        UnavailableBody = GetString("CodexUnavailableBody");
        RetryButtonText = GetString("SampleRetry");
        RetryAutomationName = GetString("CodexRetry");
        RebuildSamplePreview();
        IsDashboardLayoutBusy = true;
        _dashboardLayoutInitialization = InitializeDashboardLayoutAsync();
        _ = RefreshDashboardAsync(scenario: null, forceRefresh: false);
        _ = Vercel.InitializeAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsOptions))]
    [NotifyPropertyChangedFor(nameof(IsSample))]
    [NotifyPropertyChangedFor(nameof(IsSampleUnavailable))]
    [NotifyPropertyChangedFor(nameof(IsSampleContext))]
    [NotifyPropertyChangedFor(nameof(IsLiveLoading))]
    [NotifyPropertyChangedFor(nameof(IsSampleLoading))]
    [NotifyPropertyChangedFor(nameof(IsCardSurface))]
    [NotifyPropertyChangedFor(nameof(IsUsageSurface))]
    [NotifyPropertyChangedFor(nameof(IsLocalUsageVisible))]
    [NotifyPropertyChangedFor(nameof(IsLiveDataStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsSampleDataStateVisible))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenOptionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseOptionsCommand))]
    public partial FlyoutSurfaceState SurfaceState { get; set; } = FlyoutSurfaceState.Empty;

    [ObservableProperty]
    public partial bool CloseWhenInactive { get; set; } = true;

    [ObservableProperty]
    public partial AppLanguageOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool IsLanguageRestartRequired { get; set; }

    [ObservableProperty]
    public partial bool IsLanguageRestartErrorVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSampleScenarioEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSampleContext))]
    [NotifyPropertyChangedFor(nameof(IsLiveLoading))]
    [NotifyPropertyChangedFor(nameof(IsSampleLoading))]
    [NotifyPropertyChangedFor(nameof(DashboardHeading))]
    [NotifyPropertyChangedFor(nameof(IsLiveDataStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsSampleDataStateVisible))]
    [NotifyPropertyChangedFor(nameof(SampleDataStateText))]
    [NotifyPropertyChangedFor(nameof(SampleDataStateAutomationId))]
    [NotifyPropertyChangedFor(nameof(IsLocalUsageVisible))]
    public partial bool IsSampleModeEnabled { get; set; }

    [ObservableProperty]
    public partial SampleScenarioOption SelectedSampleScenario { get; set; }

    [ObservableProperty]
    public partial SampleDashboardSnapshot ActiveSample { get; set; } = null!;

    [ObservableProperty]
    public partial LocalUsageCard LocalUsage { get; set; } = new(
        "",
        "",
        "",
        "",
        [],
        [],
        new("", "", "", "", [], []),
        []);

    [ObservableProperty]
    public partial IReadOnlyList<ProviderStatusRow> ProviderStatuses { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDashboardLayoutProviders))]
    [NotifyPropertyChangedFor(nameof(AreAllDashboardProvidersHidden))]
    public partial IReadOnlyList<DashboardProviderLayoutRow> DashboardLayoutProviders { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardLayoutStatusVisible))]
    public partial string DashboardLayoutStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardLayoutEditable))]
    public partial bool IsDashboardLayoutBusy { get; set; }

    [ObservableProperty]
    public partial int SampleRevealToken { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(IsRefreshing))]
    public partial bool IsSampleRefreshing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveDataStateText))]
    [NotifyPropertyChangedFor(nameof(SampleDataStateText))]
    [NotifyPropertyChangedFor(nameof(SampleDataStateAutomationId))]
    public partial SampleDataState CurrentSampleDataState { get; set; } = SampleDataState.Idle;

    [ObservableProperty]
    public partial string UnavailableTitle { get; set; }

    [ObservableProperty]
    public partial string UnavailableBody { get; set; }

    [ObservableProperty]
    public partial string RetryButtonText { get; set; }

    [ObservableProperty]
    public partial string RetryAutomationName { get; set; }

    public bool IsLoading => SurfaceState == FlyoutSurfaceState.Loading;

    public bool IsEmpty => SurfaceState == FlyoutSurfaceState.Empty;

    public bool IsOptions => SurfaceState == FlyoutSurfaceState.Options;

    public bool IsSample => SurfaceState == FlyoutSurfaceState.Sample;

    public bool IsSampleUnavailable => SurfaceState == FlyoutSurfaceState.SampleUnavailable;

    public bool IsSampleContext => IsSampleModeEnabled && !IsOptions;

    public bool IsLiveLoading => IsLoading && !IsSampleModeEnabled;

    public bool IsSampleLoading => IsLoading && IsSampleModeEnabled;

    public bool IsCardSurface => !IsSample;

    public bool IsUsageSurface => !IsOptions;

    public bool IsLocalUsageVisible =>
        _hasLocalUsage && !IsSampleModeEnabled && IsUsageSurface;

    public bool IsSampleScenarioEnabled => IsSampleModeEnabled;

    public bool IsRefreshing => IsSampleRefreshing || Vercel.IsBusy;

    public bool HasDashboardLayoutProviders => DashboardLayoutProviders.Count > 0;

    public bool AreAllDashboardProvidersHidden =>
        DashboardLayoutProviders.Count > 0 && ActiveSample.Providers.Count == 0;

    public bool IsDashboardLayoutStatusVisible =>
        !string.IsNullOrWhiteSpace(DashboardLayoutStatusText);

    public bool IsDashboardLayoutEditable =>
        !_isDashboardLayoutReadOnly && !IsDashboardLayoutBusy;

    public string DashboardHeading => GetString(
        IsSampleModeEnabled ? "SampleTotalSpendHeading" : "LiveDashboardHeading");

    public bool IsLiveDataStateVisible => !IsSampleModeEnabled && IsSample;

    public bool IsSampleDataStateVisible => IsSampleModeEnabled && IsSample;

    public string LiveDataStateText => CodexLiveStateFormatter.Format(
        CurrentSampleDataState,
        IsSampleModeEnabled,
        _publishedObservedAtUtc,
        _retryAtUtc,
        _codexRefreshCoordinator.Clock.GetUtcNow(),
        GetString);

    public string SampleDataStateText => GetString(CurrentSampleDataState switch
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

    public string SampleDataStateAutomationId => CurrentSampleDataState switch
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

    public IReadOnlyList<SampleScenarioOption> SampleScenarios { get; }

    public IReadOnlyList<AppLanguageOption> LanguageOptions { get; }

    public VercelGatewaySettingsViewModel Vercel { get; }

    public string PendingLanguageTag => SelectedLanguage.LanguageTag;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => IsSampleModeEnabled
        ? RefreshDashboardAsync(SelectedSampleScenario.Value, forceRefresh: true)
        : Task.WhenAll(
            RefreshDashboardAsync(scenario: null, forceRefresh: true),
            Vercel.RefreshAsync(forceRefresh: true));

    private bool CanRefresh() => !IsLoading && !IsRefreshing;

    [RelayCommand(CanExecute = nameof(CanOpenOptions))]
    private void OpenOptions()
    {
        SurfaceState = FlyoutSurfaceState.Options;
    }

    private bool CanOpenOptions() => !IsOptions;

    [RelayCommand(CanExecute = nameof(CanCloseOptions))]
    private void CloseOptions()
    {
        SurfaceState = _resultSurface;
    }

    private bool CanCloseOptions() => IsOptions;

    partial void OnSelectedLanguageChanged(AppLanguageOption value)
    {
        if (value is null)
        {
            return;
        }

        IsLanguageRestartRequired = AppLanguageRuntime.RequiresRestart(value.LanguageTag);
        IsLanguageRestartErrorVisible = false;
    }

    public void ReportLanguageRestartFailure()
    {
        IsLanguageRestartErrorVisible = true;
    }

    partial void OnIsSampleModeEnabledChanged(bool value)
    {
        CancelRefresh();
        _hasPublishedDashboard = false;
        _activeScenario = null;
        _resultSurface = FlyoutSurfaceState.Loading;
        if (value)
        {
            RebuildSamplePreview();
        }

        _ = RefreshDashboardAsync(
            value ? SelectedSampleScenario.Value : null,
            forceRefresh: false);
    }

    partial void OnSelectedSampleScenarioChanged(SampleScenarioOption value)
    {
        if (value is null)
        {
            return;
        }

        if (IsSampleModeEnabled)
        {
            _ = RefreshDashboardAsync(value.Value, forceRefresh: true);
        }
        else
        {
            RebuildSamplePreview();
        }
    }

    private void OnVercelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(Vercel.IsBusy), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsRefreshing));
            RefreshCommand.NotifyCanExecuteChanged();
        }

        if (string.Equals(e.PropertyName, nameof(Vercel.ProviderCard), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(Vercel.State), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(Vercel.IsConfigured), StringComparison.Ordinal))
        {
            RebuildProviderStatuses();
        }

        if (string.Equals(e.PropertyName, nameof(Vercel.ProviderCard), StringComparison.Ordinal)
            && !IsSampleModeEnabled)
        {
            if (!PublishCombinedLiveDashboard(reveal: true) && _hasLocalUsage)
            {
                PublishActiveDashboard(new SampleDashboardSnapshot(
                    SampleScenario.Normal,
                    string.Empty,
                    GetString("LiveDashboardPeriod"),
                    string.Empty,
                    [],
                    []));
                _hasPublishedDashboard = true;
                _resultSurface = FlyoutSurfaceState.Sample;
                ApplyResultSurfaceIfVisible();
            }
        }
    }

    private async Task RefreshDashboardAsync(
        SampleScenario? scenario,
        bool forceRefresh)
    {
        int refreshVersion = ++_refreshVersion;
        _refreshCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        IsSampleRefreshing = true;
        Task localUsageRefresh = scenario is null
            ? RefreshLocalUsageAsync(refreshVersion, cancellation.Token)
            : Task.CompletedTask;

        if (!_hasPublishedDashboard)
        {
            _resultSurface = FlyoutSurfaceState.Loading;
            ApplyResultSurfaceIfVisible();
        }

        try
        {
            IAsyncEnumerable<CacheFirstEvent> events = scenario is SampleScenario sampleScenario
                ? _sampleRefreshCoordinator.RunAsync(
                    sampleScenario,
                    forceRefresh,
                    cancellation.Token)
                : _codexRefreshCoordinator.RunAsync(forceRefresh, cancellation.Token);

            await foreach (CacheFirstEvent refreshEvent in events)
            {
                if (refreshVersion != _refreshVersion || cancellation.IsCancellationRequested)
                {
                    return;
                }

                switch (refreshEvent)
                {
                    case CacheFirstEvent.CachePublished cache:
                        PublishCachedDashboard(scenario, cache);
                        break;
                    case CacheFirstEvent.ProviderCompleted provider:
                        PublishProviderOutcome(scenario, provider);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or TimeoutException)
        {
            if (refreshVersion == _refreshVersion)
            {
                PublishUnavailable(outcome: null);
            }
        }
        finally
        {
            await localUsageRefresh.ConfigureAwait(true);
            if (refreshVersion == _refreshVersion
                && scenario is null
                && _hasLocalUsage
                && _resultSurface == FlyoutSurfaceState.SampleUnavailable)
            {
                PublishActiveDashboard(new SampleDashboardSnapshot(
                    SampleScenario.Normal,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    [],
                    []));
                _resultSurface = FlyoutSurfaceState.Sample;
                ApplyResultSurfaceIfVisible();
            }

            if (refreshVersion == _refreshVersion)
            {
                IsSampleRefreshing = false;
                if (ReferenceEquals(_refreshCancellation, cancellation))
                {
                    _refreshCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private async Task RefreshLocalUsageAsync(
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            LocalUsageCard card = await _localUsageCoordinator.RefreshAsync(
                GetString,
                cancellationToken);
            if (refreshVersion == _refreshVersion && !cancellationToken.IsCancellationRequested)
            {
                LocalUsage = card;
                RebuildProviderStatuses();
                _hasLocalUsage = true;
                OnPropertyChanged(nameof(IsLocalUsageVisible));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (refreshVersion == _refreshVersion)
            {
                LocalUsage = LocalUsageCardProjector.CreateUnavailable(
                    GetString,
                    _localUsageCoordinator.SourceKind) with
                {
                    ProviderStatuses = LocalUsage.ProviderStatuses,
                };
                RebuildProviderStatuses();
                _hasLocalUsage = true;
                OnPropertyChanged(nameof(IsLocalUsageVisible));
            }
        }
    }

    private void PublishCachedDashboard(
        SampleScenario? scenario,
        CacheFirstEvent.CachePublished cache)
    {
        ProviderSnapshot? snapshot = cache.Snapshots.FirstOrDefault(candidate =>
            string.Equals(candidate.ProviderId.Value, "codex", StringComparison.Ordinal));
        if (snapshot is null)
        {
            if (!_hasPublishedDashboard)
            {
                _resultSurface = FlyoutSurfaceState.Loading;
                ApplyResultSurfaceIfVisible();
            }

            return;
        }

        bool reveal = !_hasPublishedDashboard || _activeScenario != scenario;
        PublishDashboard(scenario, snapshot, reveal);
        bool isStale = SnapshotFreshness.IsStale(snapshot, GetClock(scenario));
        SetDataState(isStale
            ? SampleDataState.StaleCacheRefreshing
            : SampleDataState.CacheRefreshing);
    }

    private void PublishProviderOutcome(
        SampleScenario? scenario,
        CacheFirstEvent.ProviderCompleted provider)
    {
        if (scenario is null)
        {
            _lastCodexOutcome = provider.Outcome;
        }

        ProviderSnapshot? snapshot = provider.Outcome switch
        {
            ProviderOutcome.Success success => success.Snapshot,
            ProviderOutcome.PartialSuccess partial => partial.Snapshot,
            ProviderOutcome.Throttled throttled => throttled.LastGood,
            ProviderOutcome.TransientFailure failure => failure.LastGood,
            ProviderOutcome.ContractFailure failure => failure.LastGood,
            _ => null,
        };

        _retryAtUtc = provider.Outcome switch
        {
            ProviderOutcome.Throttled throttled => throttled.RetryAtUtc,
            ProviderOutcome.TransientFailure failure => failure.RetryAtUtc,
            ProviderOutcome.ContractFailure failure => failure.RetryAtUtc,
            _ => null,
        };

        if (snapshot is null)
        {
            PublishUnavailable(provider.Outcome);
            return;
        }

        PublishDashboard(scenario, snapshot, reveal: true);
        if (provider.Outcome is ProviderOutcome.Throttled)
        {
            SetDataState(SampleDataState.Throttled);
        }
        else if (provider.Outcome is ProviderOutcome.TransientFailure
            or ProviderOutcome.ContractFailure)
        {
            SetDataState(SampleDataState.Error);
        }
        else if (provider.CacheStatus is not CacheUpdateStatus.Updated)
        {
            SetDataState(SampleDataState.NotSaved);
        }
        else if (provider.Outcome is ProviderOutcome.PartialSuccess)
        {
            SetDataState(SampleDataState.Partial);
        }
        else if (SnapshotFreshness.IsStale(snapshot, GetClock(scenario)))
        {
            SetDataState(SampleDataState.Stale);
        }
        else
        {
            SetDataState(SampleDataState.Fresh);
        }
    }

    private void PublishDashboard(
        SampleScenario? scenario,
        ProviderSnapshot snapshot,
        bool reveal)
    {
        _publishedObservedAtUtc = snapshot.SourceObservedAtUtc;
        if (scenario is SampleScenario sampleScenario)
        {
            PublishActiveDashboard(
                SampleDashboardProjector.Create(sampleScenario, snapshot, GetString));
        }
        else
        {
            _lastCodexSnapshot = snapshot;
            if (!PublishCombinedLiveDashboard(reveal))
            {
                return;
            }
        }

        _activeScenario = scenario;
        _hasPublishedDashboard = true;
        _resultSurface = FlyoutSurfaceState.Sample;
        ApplyResultSurfaceIfVisible();
        if (reveal && scenario is not null)
        {
            SampleRevealToken++;
        }
    }

    private bool PublishCombinedLiveDashboard(bool reveal)
    {
        var providers = new List<SampleProviderCard>();
        if (_lastCodexSnapshot is not null)
        {
            providers.AddRange(CodexDashboardProjector.Create(
                _lastCodexSnapshot,
                _codexRefreshCoordinator.Clock,
                GetString).Providers);
        }

        if (Vercel.ProviderCard is SampleProviderCard vercelCard)
        {
            providers.Add(vercelCard);
        }

        if (providers.Count == 0)
        {
            return false;
        }

        PublishActiveDashboard(new SampleDashboardSnapshot(
            SampleScenario.Normal,
            string.Empty,
            GetString("LiveDashboardPeriod"),
            string.Empty,
            [],
            providers));
        _activeScenario = null;
        _hasPublishedDashboard = true;
        _resultSurface = FlyoutSurfaceState.Sample;
        ApplyResultSurfaceIfVisible();
        if (reveal)
        {
            SampleRevealToken++;
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
        if (!IsSampleModeEnabled)
        {
            _lastCodexSnapshot = null;
            if (PublishCombinedLiveDashboard(reveal: false))
            {
                SetDataState(SampleDataState.Unavailable);
                return;
            }
        }

        _resultSurface = FlyoutSurfaceState.SampleUnavailable;
        SetDataState(SampleDataState.Unavailable);

        if (IsSampleModeEnabled)
        {
            UnavailableTitle = GetString("SampleUnavailableTitleValue");
            UnavailableBody = GetString("SampleUnavailableBodyValue");
            RetryButtonText = GetString("SampleRetry");
            RetryAutomationName = GetString("SampleRetryAutomationName");
        }
        else if (outcome is ProviderOutcome.NotConfigured)
        {
            UnavailableTitle = GetString("CodexNotConfiguredTitle");
            UnavailableBody = GetString("CodexNotConfiguredBody");
            RetryButtonText = GetString("SampleRetry");
            RetryAutomationName = GetString("CodexRetry");
        }
        else if (outcome is ProviderOutcome.UnsupportedAccount)
        {
            UnavailableTitle = GetString("CodexUnsupportedTitle");
            UnavailableBody = GetString("CodexUnsupportedBody");
            RetryButtonText = GetString("SampleRetry");
            RetryAutomationName = GetString("CodexRetry");
        }
        else
        {
            UnavailableTitle = GetString("CodexUnavailableTitle");
            UnavailableBody = GetString("CodexUnavailableBody");
            RetryButtonText = GetString("SampleRetry");
            RetryAutomationName = GetString("CodexRetry");
        }

        ApplyResultSurfaceIfVisible();
    }

    private void SetDataState(SampleDataState state)
    {
        CurrentSampleDataState = state;
        RebuildProviderStatuses();
        OnPropertyChanged(nameof(LiveDataStateText));
    }

    private void RebuildProviderStatuses()
    {
        var statuses = new List<ProviderStatusRow>
        {
            new(
                "codex",
                GetString("LocalUsageAgentCodex"),
                _lastCodexOutcome is ProviderOutcome.NotConfigured
                    ? GetString("ProviderStatusRootMissing")
                    : _lastCodexOutcome is null && !_hasPublishedDashboard
                        ? GetString("ProviderStatusRootPending")
                        : GetString("ProviderStatusRootDetected"),
                GetString(_lastCodexOutcome switch
                {
                    ProviderOutcome.NotConfigured => "ProviderStatusRecoveryOpenTool",
                    ProviderOutcome.UnsupportedAccount or ProviderOutcome.PolicyBlocked =>
                        "ProviderStatusRecoveryUnavailable",
                    ProviderOutcome.ContractFailure => "ProviderStatusRecoveryUpdate",
                    ProviderOutcome.Throttled or ProviderOutcome.TransientFailure =>
                        "ProviderStatusRecoveryRetry",
                    _ => "ProviderStatusRecoveryRefresh",
                }),
                [
                    new(
                        GetString("ProviderStatusQuota"),
                        CurrentSampleDataState switch
                        {
                            _ when _lastCodexOutcome is ProviderOutcome.NotConfigured =>
                                GetString("ProviderStatusNotConfigured"),
                            _ when _lastCodexOutcome is ProviderOutcome.UnsupportedAccount =>
                                GetString("ProviderStatusUnsupported"),
                            _ when _lastCodexOutcome is ProviderOutcome.PolicyBlocked =>
                                GetString("ProviderStatusBlocked"),
                            _ when _lastCodexOutcome is ProviderOutcome.ContractFailure =>
                                GetString("ProviderStatusContractChanged"),
                            _ when _lastCodexOutcome is ProviderOutcome.Throttled
                                or ProviderOutcome.TransientFailure =>
                                GetString("ProviderStatusPartial"),
                            SampleDataState.Partial => GetString("ProviderStatusPartial"),
                            SampleDataState.Fresh or SampleDataState.CacheRefreshing
                                or SampleDataState.StaleCacheRefreshing or SampleDataState.Stale
                                or SampleDataState.NotSaved => GetString("ProviderStatusAvailable"),
                            _ => GetString("ProviderStatusUnavailable"),
                        },
                        "ProviderStatus.codex.Quota"),
                    new(GetString("ProviderStatusUsage"), GetString("ProviderStatusUnavailable"), "ProviderStatus.codex.Usage"),
                    new(GetString("ProviderStatusSpend"), GetString("ProviderStatusUnavailable"), "ProviderStatus.codex.Spend"),
                    new(GetString("ProviderStatusCoverage"), GetString("CodexUsageMissing"), "ProviderStatus.codex.Coverage"),
                ],
                "ProviderStatus.codex"),
        };
        statuses.Add(Vercel.CreateStatusRow());
        statuses.AddRange(LocalUsage.ProviderStatuses);
        ProviderStatuses = statuses;
    }

    public void RefreshRelativeTime()
    {
        if (IsSampleModeEnabled)
        {
            return;
        }

        if (_retryAtUtc is DateTimeOffset retryAtUtc
            && retryAtUtc <= _codexRefreshCoordinator.Clock.GetUtcNow().ToUniversalTime()
            && !IsSampleRefreshing)
        {
            _retryAtUtc = null;
            _ = RefreshDashboardAsync(scenario: null, forceRefresh: false);
            return;
        }

        if (_publishedObservedAtUtc is not null)
        {
            OnPropertyChanged(nameof(LiveDataStateText));
        }
    }

    private void ApplyResultSurfaceIfVisible()
    {
        if (!IsOptions)
        {
            SurfaceState = _resultSurface;
        }
    }

    private void CancelRefresh()
    {
        _refreshVersion++;
        _refreshCancellation?.Cancel();
        _refreshCancellation = null;
        IsSampleRefreshing = false;
    }

    private void RebuildSamplePreview()
    {
        if (SelectedSampleScenario is null)
        {
            return;
        }

        PublishActiveDashboard(
            SampleDashboardCatalog.Create(SelectedSampleScenario.Value, GetString));
        CurrentSampleDataState = SampleDataState.Idle;
    }

    public Task MoveDashboardProviderAsync(string providerId, int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return MutateDashboardLayoutAsync(layout =>
            MoveCurrentDashboardProvider(layout, new ProviderId(providerId), offset));
    }

    public Task SetDashboardProviderVisibleAsync(string providerId, bool isVisible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var id = new ProviderId(providerId);
        return MutateDashboardLayoutAsync(layout => layout.SetProviderVisible(id, isVisible));
    }

    public Task SetDashboardProviderHighlightedAsync(string providerId, bool isHighlighted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var id = new ProviderId(providerId);
        return MutateDashboardLayoutAsync(layout =>
            layout.SetProviderHighlighted(id, isHighlighted));
    }

    public Task MoveDashboardMetricAsync(
        string providerId,
        string metricId,
        int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return MutateDashboardLayoutAsync(layout =>
            MoveCurrentDashboardMetric(
                layout,
                new ProviderId(providerId),
                new MetricId(metricId),
                offset));
    }

    public Task SetDashboardMetricVisibleAsync(
        string providerId,
        string metricId,
        bool isVisible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
        return MutateDashboardLayoutAsync(layout => layout.SetMetricVisible(
            new ProviderId(providerId),
            new MetricId(metricId),
            isVisible));
    }

    public Task SetDashboardMetricHighlightedAsync(
        string providerId,
        string metricId,
        bool isHighlighted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
        return MutateDashboardLayoutAsync(layout =>
        {
            var provider = new ProviderId(providerId);
            var metric = new MetricId(metricId);
            ProviderLayoutPreference currentProvider = layout.Providers.Single(item =>
                item.ProviderId == provider);
            MetricLayoutPreference currentMetric = currentProvider.Metrics.Single(item =>
                item.MetricId == metric);
            DashboardLayout next = layout.SetMetricHighlighted(provider, metric, isHighlighted);

            if (isHighlighted && !currentMetric.IsHighlighted && next.Equals(layout))
            {
                DashboardLayoutStatusText = GetString("DashboardMetricHighlightLimitReached");
            }

            return next;
        });
    }

    public Task SetDashboardMetricOnDemandAsync(
        string providerId,
        string metricId,
        bool isOnDemand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
        return MutateDashboardLayoutAsync(layout => layout.SetMetricOnDemand(
            new ProviderId(providerId),
            new MetricId(metricId),
            isOnDemand));
    }

    private async Task InitializeDashboardLayoutAsync()
    {
        try
        {
            DashboardLayoutLoadResult result = await _dashboardLayoutStore.LoadAsync();
            switch (result)
            {
                case DashboardLayoutLoadResult.Loaded loaded:
                    _dashboardLayout = loaded.Layout;
                    break;
                case DashboardLayoutLoadResult.Corrupt corrupt:
                    _dashboardLayout = DashboardLayout.Empty;
                    DashboardLayoutStatusText = string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        GetString("DashboardLayoutRecoveredFormat"),
                        corrupt.QuarantineFileName);
                    break;
                case DashboardLayoutLoadResult.UnsupportedVersion unsupported:
                    _dashboardLayout = DashboardLayout.Empty;
                    SetDashboardLayoutReadOnly(string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        GetString("DashboardLayoutNewerVersionFormat"),
                        unsupported.SchemaVersion));
                    break;
                case DashboardLayoutLoadResult.Empty:
                    _dashboardLayout = DashboardLayout.Empty;
                    break;
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or TimeoutException)
        {
            SetDashboardLayoutReadOnly(GetString("DashboardLayoutUnavailable"));
        }

        if (_rawDashboard is not null)
        {
            PublishActiveDashboard(_rawDashboard);
        }

        IsDashboardLayoutBusy = false;
    }

    private async Task MutateDashboardLayoutAsync(
        Func<DashboardLayout, DashboardLayout> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await _dashboardLayoutInitialization;
        if (IsDashboardLayoutBusy)
        {
            return;
        }

        IsDashboardLayoutBusy = true;
        try
        {
            if (_isDashboardLayoutReadOnly || _rawDashboard is null)
            {
                return;
            }

            DashboardLayout next = mutation(_dashboardLayout);
            if (next.Equals(_dashboardLayout))
            {
                PublishActiveDashboard(_rawDashboard);
                return;
            }

            DashboardLayoutSaveResult save = await _dashboardLayoutStore.SaveAsync(next);
            if (save is DashboardLayoutSaveResult.RefusedUnsupportedVersion unsupported)
            {
                SetDashboardLayoutReadOnly(string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    GetString("DashboardLayoutNewerVersionFormat"),
                    unsupported.SchemaVersion));
                PublishActiveDashboard(_rawDashboard);
                return;
            }

            _dashboardLayout = next;
            DashboardLayoutStatusText = string.Empty;
            PublishActiveDashboard(_rawDashboard);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TimeoutException)
        {
            DashboardLayoutStatusText = GetString("DashboardLayoutSaveFailed");
            if (_rawDashboard is not null)
            {
                PublishActiveDashboard(_rawDashboard);
            }
        }
        finally
        {
            IsDashboardLayoutBusy = false;
        }
    }

    private DashboardLayout MoveCurrentDashboardProvider(
        DashboardLayout layout,
        ProviderId providerId,
        int offset)
    {
        int currentRowIndex = -1;
        for (int index = 0; index < DashboardLayoutProviders.Count; index++)
        {
            if (string.Equals(
                    DashboardLayoutProviders[index].ProviderId,
                    providerId.Value,
                    StringComparison.Ordinal))
            {
                currentRowIndex = index;
                break;
            }
        }

        int targetRowIndex = currentRowIndex + offset;
        if (currentRowIndex < 0
            || targetRowIndex < 0
            || targetRowIndex >= DashboardLayoutProviders.Count)
        {
            return layout;
        }

        var targetId = new ProviderId(DashboardLayoutProviders[targetRowIndex].ProviderId);
        int currentLayoutIndex = FindDashboardProviderIndex(layout, providerId);
        int targetLayoutIndex = FindDashboardProviderIndex(layout, targetId);
        while (currentLayoutIndex != targetLayoutIndex)
        {
            int step = currentLayoutIndex < targetLayoutIndex ? 1 : -1;
            layout = layout.MoveProvider(providerId, step);
            currentLayoutIndex += step;
        }

        return layout;
    }

    private static int FindDashboardProviderIndex(
        DashboardLayout layout,
        ProviderId providerId)
    {
        for (int index = 0; index < layout.Providers.Count; index++)
        {
            if (layout.Providers[index].ProviderId == providerId)
            {
                return index;
            }
        }

        throw new KeyNotFoundException($"Provider '{providerId.Value}' is absent from the dashboard layout.");
    }

    private DashboardLayout MoveCurrentDashboardMetric(
        DashboardLayout layout,
        ProviderId providerId,
        MetricId metricId,
        int offset)
    {
        DashboardProviderLayoutRow providerRow = DashboardLayoutProviders.FirstOrDefault(row =>
            string.Equals(row.ProviderId, providerId.Value, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException(
                $"Provider '{providerId.Value}' is absent from the dashboard layout rows.");

        int currentRowIndex = -1;
        for (int index = 0; index < providerRow.Metrics.Count; index++)
        {
            if (string.Equals(
                    providerRow.Metrics[index].MetricId,
                    metricId.Value,
                    StringComparison.Ordinal))
            {
                currentRowIndex = index;
                break;
            }
        }

        int targetRowIndex = currentRowIndex + offset;
        if (currentRowIndex < 0
            || targetRowIndex < 0
            || targetRowIndex >= providerRow.Metrics.Count)
        {
            return layout;
        }

        var targetId = new MetricId(providerRow.Metrics[targetRowIndex].MetricId);
        ProviderLayoutPreference providerPreference = layout.Providers.Single(provider =>
            provider.ProviderId == providerId);
        int currentLayoutIndex = FindDashboardMetricIndex(providerPreference, metricId);
        int targetLayoutIndex = FindDashboardMetricIndex(providerPreference, targetId);
        while (currentLayoutIndex != targetLayoutIndex)
        {
            int step = currentLayoutIndex < targetLayoutIndex ? 1 : -1;
            layout = layout.MoveMetric(providerId, metricId, step);
            currentLayoutIndex += step;
        }

        return layout;
    }

    private static int FindDashboardMetricIndex(
        ProviderLayoutPreference provider,
        MetricId metricId)
    {
        for (int index = 0; index < provider.Metrics.Count; index++)
        {
            if (provider.Metrics[index].MetricId == metricId)
            {
                return index;
            }
        }

        throw new KeyNotFoundException(
            $"Metric '{metricId.Value}' is absent from provider '{provider.ProviderId.Value}'.");
    }

    private void PublishActiveDashboard(SampleDashboardSnapshot dashboard)
    {
        _rawDashboard = dashboard;
        DashboardLayoutProjection projection = DashboardLayoutProjector.Apply(
            dashboard,
            _dashboardLayout,
            GetString("DashboardProviderHighlightedLabel"),
            new DashboardProviderActionNameFormats(
                GetString("DashboardProviderMoveUpAutomationNameFormat"),
                GetString("DashboardProviderMoveDownAutomationNameFormat"),
                GetString("DashboardProviderVisibilityAutomationNameFormat"),
                GetString("DashboardProviderHighlightAutomationNameFormat"),
                GetString("DashboardProviderMetricsAutomationNameFormat")),
            new DashboardMetricActionNameFormats(
                GetString("DashboardMetricMoveUpAutomationNameFormat"),
                GetString("DashboardMetricMoveDownAutomationNameFormat"),
                GetString("DashboardMetricVisibilityAutomationNameFormat"),
                GetString("DashboardMetricHighlightAutomationNameFormat"),
                GetString("DashboardMetricAlwaysVisibleSection"),
                GetString("DashboardMetricOnDemandSection"),
                GetString("DashboardMetricMoveToAlwaysVisibleAutomationNameFormat"),
                GetString("DashboardMetricMoveToOnDemandAutomationNameFormat")));
        _dashboardLayout = projection.Layout;
        ActiveSample = projection.Dashboard;
        DashboardLayoutProviders = projection.Providers
            .Select(row => row with
            {
                IsMetricsExpanded = _expandedDashboardMetricProviders.Contains(row.ProviderId),
            })
            .ToArray();
        OnPropertyChanged(nameof(AreAllDashboardProvidersHidden));
    }

    public void SetDashboardProviderMetricsExpanded(string providerId, bool isExpanded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        if (isExpanded)
        {
            _expandedDashboardMetricProviders.Add(providerId);
        }
        else
        {
            _expandedDashboardMetricProviders.Remove(providerId);
        }
    }

    private void SetDashboardLayoutReadOnly(string statusText)
    {
        _isDashboardLayoutReadOnly = true;
        DashboardLayoutStatusText = statusText;
        OnPropertyChanged(nameof(IsDashboardLayoutEditable));
    }

    private TimeProvider GetClock(SampleScenario? scenario) =>
        scenario is null ? _codexRefreshCoordinator.Clock : _sampleRefreshCoordinator.Clock;

    private string GetString(string key)
    {
        string value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }
}
