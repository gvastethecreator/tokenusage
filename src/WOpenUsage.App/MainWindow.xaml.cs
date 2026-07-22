using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Graphics;
using WOpenUsage.Platform.Windows.Display;
using WOpenUsage.Platform.Windows.Placement;
using WOpenUsage.Platform.Windows.Tray;
using WinRT.Interop;

namespace WOpenUsage.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly ResourceLoader _resources = new();
    private readonly nint _windowHandle;
    private TrayIconHost? _trayIcon;
    private bool _isFlyoutVisible;
    private bool _suppressDeactivateHide;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();

        _windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureFlyoutWindow();
        RootPage.ViewModel.StatusText = GetString("StatusIdle");
        RootPage.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        RootPage.HideRequested += OnHideRequested;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;

        AppWindow.Hide();
        InstallTrayIcon();
    }

    private void ConfigureFlyoutWindow()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            throw new InvalidOperationException("The default window presenter is unavailable.");
        }

        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = false;

        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = GetString("AppTitle");
        AppWindow.SetIcon(GetIconPath());
    }

    private void InstallTrayIcon()
    {
        _trayIcon = new TrayIconHost(
            _windowHandle,
            GetIconPath(),
            GetString("TrayTooltip"),
            new TrayMenuLabels(
                GetString("TrayMenuUpdate"),
                GetString("TrayMenuSettings"),
                GetString("TrayMenuExit")));

        _trayIcon.Activated += OnTrayActivated;
        _trayIcon.UpdateRequested += OnTrayUpdateRequested;
        _trayIcon.SettingsRequested += OnTraySettingsRequested;
        _trayIcon.ExitRequested += OnTrayExitRequested;
    }

    private void OnTrayActivated(object? sender, TrayActivatedEventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Kind == TrayActivationKind.Mouse && _isFlyoutVisible)
            {
                HideFlyout();
                return;
            }

            ShowFlyout(e.Kind == TrayActivationKind.Keyboard);
        });
    }

    private void OnTrayUpdateRequested(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() => _ = RefreshFromTrayAsync());
    }

    private async Task RefreshFromTrayAsync()
    {
        ShowFlyout(false);
        if (RootPage.ViewModel.RefreshCommand.CanExecute(null))
        {
            await RootPage.ViewModel.RefreshCommand.ExecuteAsync(null);
        }
    }

    private void OnTraySettingsRequested(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            RootPage.ViewModel.OpenOptionsCommand.Execute(null);
            ShowFlyout(true);
        });
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            DisposeTrayIcon();
            Close();
        });
    }

    private void ShowFlyout(bool focusPrimaryAction)
    {
        PositionFlyout();
        _suppressDeactivateHide = true;
        _isFlyoutVisible = true;
        AppWindow.Show();
        Activate();

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            _suppressDeactivateHide = false;
            if (focusPrimaryAction && _isFlyoutVisible)
            {
                RootPage.FocusPrimaryAction();
            }
        });
    }

    private void PositionFlyout()
    {
        if (_trayIcon is null)
        {
            throw new InvalidOperationException("The tray icon has not been initialized.");
        }

        PlatformRect? iconBounds = _trayIcon.TryGetIconBounds(out var iconRect)
            ? iconRect
            : null;
        var display = MonitorPlacementContextProvider.Resolve(iconBounds);
        var desiredHeightDips = MeasureDesiredHeightDips();

        var initialHeightDips = FlyoutSizePolicy.ClampHeightDips(
            desiredHeightDips,
            display.WorkArea,
            96);
        var initialPlacement = FlyoutPlacementCalculator.Calculate(
            iconBounds,
            display.WorkArea,
            FlyoutSizePolicy.WidthDips,
            initialHeightDips,
            96,
            display.FallbackAnchor);
        MoveTo(initialPlacement.Bounds);

        var effectiveDpi = MonitorPlacementContextProvider.GetWindowDpi(_windowHandle);
        var finalHeightDips = FlyoutSizePolicy.ClampHeightDips(
            desiredHeightDips,
            display.WorkArea,
            effectiveDpi);
        var finalPlacement = FlyoutPlacementCalculator.Calculate(
            iconBounds,
            display.WorkArea,
            FlyoutSizePolicy.WidthDips,
            finalHeightDips,
            effectiveDpi,
            display.FallbackAnchor);
        MoveTo(finalPlacement.Bounds);
    }

    private double MeasureDesiredHeightDips()
    {
        RootPage.MeasureRoot.InvalidateMeasure();
        RootPage.MeasureRoot.UpdateLayout();
        RootPage.MeasureRoot.Measure(
            new Windows.Foundation.Size(
                FlyoutSizePolicy.WidthDips,
                double.PositiveInfinity));

        var desiredHeight = RootPage.MeasureRoot.DesiredSize.Height;
        return double.IsFinite(desiredHeight) && desiredHeight > 0
            ? desiredHeight
            : FlyoutSizePolicy.MinimumHeightDips;
    }

    private void MoveTo(PlatformRect bounds)
    {
        AppWindow.MoveAndResize(
            new RectInt32(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(
                e.PropertyName,
                nameof(RootPage.ViewModel.SurfaceState),
                StringComparison.Ordinal))
        {
            return;
        }

        RootPage.ViewModel.StatusText = RootPage.ViewModel.IsLoading
            ? GetString("StatusLoading")
            : GetString("StatusIdle");

        if (_isFlyoutVisible)
        {
            _ = DispatcherQueue.TryEnqueue(PositionFlyout);
        }
    }

    private void OnHideRequested(object? sender, EventArgs e) => HideFlyout();

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_suppressDeactivateHide || !_isFlyoutVisible)
        {
            return;
        }

        if (args.WindowActivationState == WindowActivationState.Deactivated
            && RootPage.ViewModel.CloseWhenInactive)
        {
            HideFlyout();
        }
    }

    private void HideFlyout()
    {
        if (!_isFlyoutVisible)
        {
            return;
        }

        AppWindow.Hide();
        _isFlyoutVisible = false;
        _suppressDeactivateHide = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RootPage.ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        RootPage.HideRequested -= OnHideRequested;
        DisposeTrayIcon();
        GC.SuppressFinalize(this);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args) => Dispose();

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Activated -= OnTrayActivated;
        _trayIcon.UpdateRequested -= OnTrayUpdateRequested;
        _trayIcon.SettingsRequested -= OnTraySettingsRequested;
        _trayIcon.ExitRequested -= OnTrayExitRequested;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private static string GetIconPath()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
    }

    private string GetString(string key)
    {
        var value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }
}
