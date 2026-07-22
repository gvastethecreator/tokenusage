using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.App.ViewModels;

public partial class FlyoutViewModel : ObservableObject
{
    private readonly ResourceLoader _resources = new();
    private readonly SampleRefreshCoordinator _sampleRefreshCoordinator;
    private readonly CodexRefreshCoordinator _codexRefreshCoordinator;
    private CancellationTokenSource? _refreshCancellation;
    private FlyoutSurfaceState _resultSurface = FlyoutSurfaceState.Loading;
    private SampleScenario? _activeScenario;
    private bool _hasPublishedDashboard;
    private int _refreshVersion;
    private DateTimeOffset? _publishedObservedAtUtc;
    private DateTimeOffset? _retryAtUtc;

    public FlyoutViewModel(
        SampleRefreshCoordinator sampleRefreshCoordinator,
        CodexRefreshCoordinator codexRefreshCoordinator)
    {
        _sampleRefreshCoordinator = sampleRefreshCoordinator
            ?? throw new ArgumentNullException(nameof(sampleRefreshCoordinator));
        _codexRefreshCoordinator = codexRefreshCoordinator
            ?? throw new ArgumentNullException(nameof(codexRefreshCoordinator));
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
        _ = RefreshDashboardAsync(scenario: null, forceRefresh: false);
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
    [NotifyPropertyChangedFor(nameof(IsLiveDataStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsSampleDataStateVisible))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenOptionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseOptionsCommand))]
    public partial FlyoutSurfaceState SurfaceState { get; set; } = FlyoutSurfaceState.Empty;

    [ObservableProperty]
    public partial bool CloseWhenInactive { get; set; } = true;

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
    public partial bool IsSampleModeEnabled { get; set; }

    [ObservableProperty]
    public partial SampleScenarioOption SelectedSampleScenario { get; set; }

    [ObservableProperty]
    public partial SampleDashboardSnapshot ActiveSample { get; set; } = null!;

    [ObservableProperty]
    public partial int SampleRevealToken { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
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

    public bool IsSampleScenarioEnabled => IsSampleModeEnabled;

    public string DashboardHeading => GetString(
        IsSampleModeEnabled ? "SampleTotalSpendHeading" : "CodexQuotaTitle");

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

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => RefreshDashboardAsync(
        IsSampleModeEnabled ? SelectedSampleScenario.Value : null,
        forceRefresh: true);

    private bool CanRefresh() => !IsLoading && !IsSampleRefreshing;

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

    private async Task RefreshDashboardAsync(
        SampleScenario? scenario,
        bool forceRefresh)
    {
        int refreshVersion = ++_refreshVersion;
        _refreshCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        IsSampleRefreshing = true;

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
        ActiveSample = scenario is SampleScenario sampleScenario
            ? SampleDashboardProjector.Create(sampleScenario, snapshot, GetString)
            : CodexDashboardProjector.Create(snapshot, _codexRefreshCoordinator.Clock, GetString);
        _activeScenario = scenario;
        _hasPublishedDashboard = true;
        _resultSurface = FlyoutSurfaceState.Sample;
        ApplyResultSurfaceIfVisible();
        if (reveal)
        {
            SampleRevealToken++;
        }
    }

    private void PublishUnavailable(ProviderOutcome? outcome)
    {
        _hasPublishedDashboard = false;
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
        OnPropertyChanged(nameof(LiveDataStateText));
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

        ActiveSample = SampleDashboardCatalog.Create(SelectedSampleScenario.Value, GetString);
        CurrentSampleDataState = SampleDataState.Idle;
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
