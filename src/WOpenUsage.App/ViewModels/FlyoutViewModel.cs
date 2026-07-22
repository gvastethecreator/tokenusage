using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using WOpenUsage.App.ViewModels.Sample;

namespace WOpenUsage.App.ViewModels;

public partial class FlyoutViewModel : ObservableObject
{
    private static readonly TimeSpan EmptyRefreshDuration = TimeSpan.FromMilliseconds(750);
    private readonly ResourceLoader _resources = new();
    private int _stateVersion;

    public FlyoutViewModel()
    {
        SampleScenarios =
        [
            new(SampleScenario.Normal, GetString("SampleScenarioNormal")),
            new(SampleScenario.NearLimit, GetString("SampleScenarioNearLimit")),
            new(SampleScenario.PartialStale, GetString("SampleScenarioPartialStale")),
        ];

        SelectedSampleScenario = SampleScenarios[0];
        RebuildSample();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsOptions))]
    [NotifyPropertyChangedFor(nameof(IsSample))]
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

    public bool IsLoading => SurfaceState == FlyoutSurfaceState.Loading;

    public bool IsEmpty => SurfaceState == FlyoutSurfaceState.Empty;

    public bool IsOptions => SurfaceState == FlyoutSurfaceState.Options;

    public bool IsSample => SurfaceState == FlyoutSurfaceState.Sample;

    public bool IsSampleContext => IsSampleModeEnabled && !IsOptions;

    public bool IsLiveLoading => IsLoading && !IsSampleModeEnabled;

    public bool IsSampleLoading => IsLoading && IsSampleModeEnabled;

    public bool IsCardSurface => !IsSample;

    public bool IsUsageSurface => !IsOptions;

    public bool IsSampleScenarioEnabled => IsSampleModeEnabled;

    public IReadOnlyList<SampleScenarioOption> SampleScenarios { get; }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        var refreshVersion = ++_stateVersion;
        SurfaceState = FlyoutSurfaceState.Loading;
        await Task.Delay(EmptyRefreshDuration);

        if (refreshVersion == _stateVersion)
        {
            if (IsSampleModeEnabled)
            {
                RebuildSample();
                SurfaceState = FlyoutSurfaceState.Sample;
            }
            else
            {
                SurfaceState = FlyoutSurfaceState.Empty;
            }
        }
    }

    private bool CanRefresh() => !IsLoading;

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
            ? FlyoutSurfaceState.Sample
            : FlyoutSurfaceState.Empty;
    }

    private bool CanCloseOptions() => IsOptions;

    partial void OnIsSampleModeEnabledChanged(bool value)
    {
        if (value)
        {
            RebuildSample();
        }
    }

    partial void OnSelectedSampleScenarioChanged(SampleScenarioOption value) => RebuildSample();

    private void RebuildSample()
    {
        if (SelectedSampleScenario is null)
        {
            return;
        }

        ActiveSample = SampleDashboardCatalog.Create(SelectedSampleScenario.Value, GetString);
        SampleRevealToken++;
    }

    private string GetString(string key) => _resources.GetString(key);

}
