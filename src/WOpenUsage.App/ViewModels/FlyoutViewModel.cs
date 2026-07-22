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
    private static readonly TimeSpan EmptyRefreshDuration = TimeSpan.FromMilliseconds(750);
    private readonly ResourceLoader _resources = new();
    private readonly SampleRefreshCoordinator _sampleRefreshCoordinator;
    private CancellationTokenSource? _sampleRefreshCancellation;
    private FlyoutSurfaceState _sampleResultSurface = FlyoutSurfaceState.Loading;
    private bool _hasPublishedSample;
    private int _stateVersion;
    private int _sampleRefreshVersion;

    public FlyoutViewModel(SampleRefreshCoordinator sampleRefreshCoordinator)
    {
        _sampleRefreshCoordinator = sampleRefreshCoordinator
            ?? throw new ArgumentNullException(nameof(sampleRefreshCoordinator));
        SampleScenarios =
        [
            new(SampleScenario.Normal, GetString("SampleScenarioNormal")),
            new(SampleScenario.NearLimit, GetString("SampleScenarioNearLimit")),
            new(SampleScenario.Partial, GetString("SampleScenarioPartial")),
            new(SampleScenario.Stale, GetString("SampleScenarioStale")),
            new(SampleScenario.Error, GetString("SampleScenarioError")),
        ];

        SelectedSampleScenario = SampleScenarios[0];
        RebuildSamplePreview();
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
    [NotifyPropertyChangedFor(nameof(IsSampleStateCacheRefreshing))]
    [NotifyPropertyChangedFor(nameof(IsSampleStateStaleCacheRefreshing))]
    [NotifyPropertyChangedFor(nameof(IsSampleStateFresh))]
    [NotifyPropertyChangedFor(nameof(IsSampleStatePartial))]
    [NotifyPropertyChangedFor(nameof(IsSampleStateStale))]
    [NotifyPropertyChangedFor(nameof(IsSampleStateError))]
    [NotifyPropertyChangedFor(nameof(IsSampleStateNotSaved))]
    public partial SampleDataState CurrentSampleDataState { get; set; } = SampleDataState.Idle;

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

    public bool IsSampleStateCacheRefreshing => CurrentSampleDataState == SampleDataState.CacheRefreshing;

    public bool IsSampleStateStaleCacheRefreshing => CurrentSampleDataState == SampleDataState.StaleCacheRefreshing;

    public bool IsSampleStateFresh => CurrentSampleDataState == SampleDataState.Fresh;

    public bool IsSampleStatePartial => CurrentSampleDataState == SampleDataState.Partial;

    public bool IsSampleStateStale => CurrentSampleDataState == SampleDataState.Stale;

    public bool IsSampleStateError => CurrentSampleDataState == SampleDataState.Error;

    public bool IsSampleStateNotSaved => CurrentSampleDataState == SampleDataState.NotSaved;

    public IReadOnlyList<SampleScenarioOption> SampleScenarios { get; }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (IsSampleModeEnabled)
        {
            await RefreshSampleAsync(forceRefresh: true);
            return;
        }

        int refreshVersion = ++_stateVersion;
        SurfaceState = FlyoutSurfaceState.Loading;
        await Task.Delay(EmptyRefreshDuration);

        if (refreshVersion == _stateVersion)
        {
            SurfaceState = FlyoutSurfaceState.Empty;
        }
    }

    private bool CanRefresh() => !IsLoading && !IsSampleRefreshing;

    [RelayCommand(CanExecute = nameof(CanOpenOptions))]
    private void OpenOptions()
    {
        _stateVersion++;
        SurfaceState = FlyoutSurfaceState.Options;
    }

    private bool CanOpenOptions() => !IsOptions;

    [RelayCommand(CanExecute = nameof(CanCloseOptions))]
    private void CloseOptions()
    {
        _stateVersion++;
        SurfaceState = IsSampleModeEnabled
            ? _sampleResultSurface
            : FlyoutSurfaceState.Empty;
    }

    private bool CanCloseOptions() => IsOptions;

    partial void OnIsSampleModeEnabledChanged(bool value)
    {
        if (value)
        {
            _ = RefreshSampleAsync(forceRefresh: false);
            return;
        }

        CancelSampleRefresh();
        _hasPublishedSample = false;
        _sampleResultSurface = FlyoutSurfaceState.Loading;
        RebuildSamplePreview();
        if (!IsOptions)
        {
            SurfaceState = FlyoutSurfaceState.Empty;
        }
    }

    partial void OnSelectedSampleScenarioChanged(SampleScenarioOption value)
    {
        if (value is null)
        {
            return;
        }

        if (IsSampleModeEnabled)
        {
            _ = RefreshSampleAsync(forceRefresh: true);
        }
        else
        {
            RebuildSamplePreview();
        }
    }

    private async Task RefreshSampleAsync(bool forceRefresh)
    {
        SampleScenario scenario = SelectedSampleScenario.Value;
        int refreshVersion = ++_sampleRefreshVersion;
        _sampleRefreshCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _sampleRefreshCancellation = cancellation;
        IsSampleRefreshing = true;

        if (!_hasPublishedSample)
        {
            _sampleResultSurface = FlyoutSurfaceState.Loading;
            ApplySampleSurfaceIfVisible();
        }

        try
        {
            await foreach (CacheFirstEvent refreshEvent in _sampleRefreshCoordinator.RunAsync(
                scenario,
                forceRefresh,
                cancellation.Token))
            {
                if (refreshVersion != _sampleRefreshVersion || cancellation.IsCancellationRequested)
                {
                    return;
                }

                switch (refreshEvent)
                {
                    case CacheFirstEvent.CachePublished cache:
                        PublishCachedSample(scenario, cache);
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
            if (refreshVersion == _sampleRefreshVersion)
            {
                PublishUnavailable();
            }
        }
        finally
        {
            if (refreshVersion == _sampleRefreshVersion)
            {
                IsSampleRefreshing = false;
                if (ReferenceEquals(_sampleRefreshCancellation, cancellation))
                {
                    _sampleRefreshCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void PublishCachedSample(
        SampleScenario scenario,
        CacheFirstEvent.CachePublished cache)
    {
        ProviderSnapshot? snapshot = cache.Snapshots.FirstOrDefault(candidate =>
            string.Equals(candidate.ProviderId.Value, "codex", StringComparison.Ordinal));
        if (snapshot is null)
        {
            if (!_hasPublishedSample)
            {
                _sampleResultSurface = FlyoutSurfaceState.Loading;
                ApplySampleSurfaceIfVisible();
            }

            return;
        }

        if (_hasPublishedSample && ActiveSample.Scenario != scenario)
        {
            bool priorSnapshotIsStale = SnapshotFreshness.IsStale(
                snapshot,
                _sampleRefreshCoordinator.Clock);
            SetSampleState(priorSnapshotIsStale
                ? SampleDataState.StaleCacheRefreshing
                : SampleDataState.CacheRefreshing);
            return;
        }

        bool reveal = !_hasPublishedSample || ActiveSample.Scenario != scenario;
        PublishSample(scenario, snapshot, reveal);
        bool isStale = SnapshotFreshness.IsStale(snapshot, _sampleRefreshCoordinator.Clock);
        SetSampleState(isStale
            ? SampleDataState.StaleCacheRefreshing
            : SampleDataState.CacheRefreshing);
    }

    private void PublishProviderOutcome(
        SampleScenario scenario,
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

        if (snapshot is null)
        {
            PublishUnavailable();
            return;
        }

        PublishSample(scenario, snapshot, reveal: true);
        if (provider.Outcome is ProviderOutcome.TransientFailure
            or ProviderOutcome.ContractFailure
            or ProviderOutcome.Throttled)
        {
            SetSampleState(SampleDataState.Error);
        }
        else if (provider.CacheStatus is not CacheUpdateStatus.Updated)
        {
            SetSampleState(SampleDataState.NotSaved);
        }
        else if (provider.Outcome is ProviderOutcome.PartialSuccess)
        {
            SetSampleState(SampleDataState.Partial);
        }
        else if (SnapshotFreshness.IsStale(snapshot, _sampleRefreshCoordinator.Clock))
        {
            SetSampleState(SampleDataState.Stale);
        }
        else
        {
            SetSampleState(SampleDataState.Fresh);
        }
    }

    private void PublishSample(
        SampleScenario scenario,
        ProviderSnapshot snapshot,
        bool reveal)
    {
        ActiveSample = SampleDashboardProjector.Create(scenario, snapshot, GetString);
        _hasPublishedSample = true;
        _sampleResultSurface = FlyoutSurfaceState.Sample;
        ApplySampleSurfaceIfVisible();
        if (reveal)
        {
            SampleRevealToken++;
        }
    }

    private void PublishUnavailable()
    {
        _hasPublishedSample = false;
        _sampleResultSurface = FlyoutSurfaceState.SampleUnavailable;
        SetSampleState(SampleDataState.Unavailable);
        ApplySampleSurfaceIfVisible();
    }

    private void SetSampleState(SampleDataState state) => CurrentSampleDataState = state;

    private void ApplySampleSurfaceIfVisible()
    {
        if (!IsOptions && IsSampleModeEnabled)
        {
            SurfaceState = _sampleResultSurface;
        }
    }

    private void CancelSampleRefresh()
    {
        _sampleRefreshVersion++;
        _sampleRefreshCancellation?.Cancel();
        _sampleRefreshCancellation = null;
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

    private string GetString(string key)
    {
        string value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }
}
