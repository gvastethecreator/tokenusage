using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WOpenUsage.App.ViewModels;

public partial class FlyoutViewModel : ObservableObject
{
    private static readonly TimeSpan EmptyRefreshDuration = TimeSpan.FromMilliseconds(750);
    private int _stateVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsOptions))]
    [NotifyPropertyChangedFor(nameof(IsUsageSurface))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenOptionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseOptionsCommand))]
    public partial FlyoutSurfaceState SurfaceState { get; set; } = FlyoutSurfaceState.Empty;

    [ObservableProperty]
    public partial bool CloseWhenInactive { get; set; } = true;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public bool IsLoading => SurfaceState == FlyoutSurfaceState.Loading;

    public bool IsEmpty => SurfaceState == FlyoutSurfaceState.Empty;

    public bool IsOptions => SurfaceState == FlyoutSurfaceState.Options;

    public bool IsUsageSurface => !IsOptions;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        var refreshVersion = ++_stateVersion;
        SurfaceState = FlyoutSurfaceState.Loading;
        await Task.Delay(EmptyRefreshDuration);

        if (refreshVersion == _stateVersion)
        {
            SurfaceState = FlyoutSurfaceState.Empty;
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
        SurfaceState = FlyoutSurfaceState.Empty;
    }

    private bool CanCloseOptions() => IsOptions;

}
