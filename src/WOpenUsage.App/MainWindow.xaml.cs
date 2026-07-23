using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WOpenUsage.Core.Appearance;
using WOpenUsage.Platform.Windows.Display;
using WOpenUsage.Platform.Windows.Placement;
using WOpenUsage.Platform.Windows.Tray;
using WOpenUsage.Platform.Windows.Windowing;
using WinRT.Interop;

namespace WOpenUsage.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly ResourceLoader _resources = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _activationGuardTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _systemVisualSettingsTimer;
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private readonly UISettings _uiSettings = new();
    private readonly nint _windowHandle;
    private readonly double _preferredWidthDips;
    private TrayIconHost? _trayIcon;
    private bool _isFlyoutVisible;
    private bool _isTransparencyActive;
    private bool _suppressDeactivateHide;
    private bool _disposed;
    private bool _lastHighContrast;
    private Windows.UI.Color _lastSystemBackground;
    private Windows.UI.Color _lastSystemForeground;

    public MainWindow(
        bool showForTest = false,
        bool useSampleForTest = false,
        double? preferredWidthDipsForTest = null)
    {
        InitializeComponent();

        _preferredWidthDips = preferredWidthDipsForTest ?? FlyoutSizePolicy.WidthDips;

        _activationGuardTimer = DispatcherQueue.CreateTimer();
        _activationGuardTimer.Interval = TimeSpan.FromMilliseconds(500);
        _activationGuardTimer.IsRepeating = false;
        _activationGuardTimer.Tick += OnActivationGuardElapsed;

        _systemVisualSettingsTimer = DispatcherQueue.CreateTimer();
        _systemVisualSettingsTimer.Interval = TimeSpan.FromSeconds(1);
        _systemVisualSettingsTimer.IsRepeating = true;
        _systemVisualSettingsTimer.Tick += OnSystemVisualSettingsTimerElapsed;
        CaptureSystemVisualSettings();

        _windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureFlyoutWindow();
        RootPage.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyAppearance();
        UpdateStatusText();
        RootPage.HideRequested += OnHideRequested;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;

        AppWindow.Hide();
        InstallTrayIcon();

        if (showForTest)
        {
            RootPage.ViewModel.CloseWhenInactive = false;
            RootPage.ViewModel.IsSampleModeEnabled = useSampleForTest;
            _ = DispatcherQueue.TryEnqueue(() => ShowFlyout(true));
        }
    }

    private void ConfigureFlyoutWindow()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            throw new InvalidOperationException("The default window presenter is unavailable.");
        }

        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = false;
        presenter.SetBorderAndTitleBar(false, false);

        ApplyBorderlessChrome();

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
        BeginActivationGuard();
        ApplyAppearance();
        PositionFlyout();
        _isFlyoutVisible = true;
        AppWindow.Show();
        ApplyBorderlessChrome();
        _systemVisualSettingsTimer.Start();
        Activate();

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isFlyoutVisible)
            {
                PositionFlyout();
            }

            if (focusPrimaryAction && _isFlyoutVisible)
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (_isFlyoutVisible)
                    {
                        RootPage.FocusPrimaryAction();
                    }
                });
            }
        });
    }

    internal void ShowFromExternalActivation()
    {
        if (!_disposed)
        {
            ShowFlyout(true);
        }
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
        var desiredHeightDips = MeasureDesiredHeightDips(_preferredWidthDips);

        var initialHeightDips = FlyoutSizePolicy.ClampHeightDips(
            desiredHeightDips,
            display.WorkArea,
            96);
        var initialPlacement = FlyoutPlacementCalculator.Calculate(
            iconBounds,
            display.WorkArea,
            _preferredWidthDips,
            initialHeightDips,
            96,
            display.FallbackAnchor);
        MoveTo(initialPlacement.Bounds);

        var effectiveDpi = MonitorPlacementContextProvider.GetWindowDpi(_windowHandle);
        var finalWidthDips = FlyoutSizePolicy.ClampWidthDips(
            _preferredWidthDips,
            display.WorkArea,
            effectiveDpi);
        desiredHeightDips = MeasureDesiredHeightDips(finalWidthDips);
        var finalHeightDips = FlyoutSizePolicy.ClampHeightDips(
            desiredHeightDips,
            display.WorkArea,
            effectiveDpi);
        var finalPlacement = FlyoutPlacementCalculator.Calculate(
            iconBounds,
            display.WorkArea,
            finalWidthDips,
            finalHeightDips,
            effectiveDpi,
            display.FallbackAnchor);
        RootPage.MeasureRoot.Width = finalWidthDips;
        RootPage.MeasureRoot.Height = finalHeightDips;
        RootPage.MeasureRoot.UpdateLayout();
        MoveTo(finalPlacement.Bounds);
    }

    private double MeasureDesiredHeightDips(double widthDips)
    {
        RootPage.MeasureRoot.Height = double.NaN;
        RootPage.MeasureRoot.Width = widthDips;
        RootPage.MeasureRoot.InvalidateMeasure();
        RootPage.MeasureRoot.UpdateLayout();
        RootPage.MeasureRoot.Measure(
            new Windows.Foundation.Size(
                widthDips,
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
        bool appearanceChanged = string.Equals(
            e.PropertyName,
            nameof(RootPage.ViewModel.Appearance),
            StringComparison.Ordinal);
        bool surfaceChanged = string.Equals(
            e.PropertyName,
            nameof(RootPage.ViewModel.SurfaceState),
            StringComparison.Ordinal);
        bool refreshChanged = string.Equals(
            e.PropertyName,
            nameof(RootPage.ViewModel.IsRefreshing),
            StringComparison.Ordinal);
        if (appearanceChanged)
        {
            ApplyAppearance();
            ApplyBorderlessChrome();
            if (_isFlyoutVisible)
            {
                SchedulePositionAfterLayout();
            }
        }

        if (!surfaceChanged && !refreshChanged)
        {
            return;
        }

        UpdateStatusText();

        if (surfaceChanged && _isFlyoutVisible)
        {
            BeginActivationGuard();
            SchedulePositionAfterLayout();
        }
    }

    private void ApplyAppearance()
    {
        AppearanceSettings settings = RootPage.ViewModel.Appearance;
        bool transparencyActive = settings.IncreaseTransparency
            && !_accessibilitySettings.HighContrast
            && _uiSettings.AdvancedEffectsEnabled;

        if (transparencyActive != _isTransparencyActive)
        {
            SystemBackdrop = transparencyActive
                ? new DesktopAcrylicBackdrop()
                : null;
            _isTransparencyActive = transparencyActive;
        }

        RootPage.ApplyAppearance(settings, transparencyActive);
    }

    private void UpdateStatusText()
    {
        string resourceKey;
        if (RootPage.ViewModel.IsRefreshing || RootPage.ViewModel.IsLoading)
        {
            resourceKey = RootPage.ViewModel.IsSampleModeEnabled
                ? "SampleStatusLoading"
                : "StatusLoading";
        }
        else if (RootPage.ViewModel.IsSample || RootPage.ViewModel.IsSampleUnavailable)
        {
            resourceKey = RootPage.ViewModel.IsSampleModeEnabled
                ? "SampleStatus"
                : "CodexQuotaTitle";
        }
        else
        {
            resourceKey = "StatusIdle";
        }

        RootPage.ViewModel.StatusText = GetString(resourceKey);
    }

    private void SchedulePositionAfterLayout()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isFlyoutVisible)
            {
                return;
            }

            PositionFlyout();
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (_isFlyoutVisible)
                {
                    PositionFlyout();
                }
            });
        });
    }

    private void OnHideRequested(object? sender, EventArgs e) => HideFlyout();

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        ApplyBorderlessChrome();

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

    private void ApplyBorderlessChrome()
    {
        _ = _accessibilitySettings.HighContrast
            ? WindowBorderStyle.TryRestoreAccessibleFrame(_windowHandle)
            : WindowBorderStyle.TryRemoveNonClientFrame(_windowHandle);
        UpdateSystemBorder();
    }

    private void UpdateSystemBorder()
    {
        if (!_accessibilitySettings.HighContrast)
        {
            _ = WindowBorderStyle.TryHideSystemBorder(_windowHandle);
            return;
        }

        Windows.UI.Color color = _uiSettings.GetColorValue(UIColorType.Foreground);

        _ = WindowBorderStyle.TryMatchSystemBorder(
            _windowHandle,
            color.R,
            color.G,
            color.B);
    }

    private void OnSystemVisualSettingsTimerElapsed(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        bool highContrast = _accessibilitySettings.HighContrast;
        Windows.UI.Color background = _uiSettings.GetColorValue(UIColorType.Background);
        Windows.UI.Color foreground = _uiSettings.GetColorValue(UIColorType.Foreground);
        if (highContrast == _lastHighContrast
            && background == _lastSystemBackground
            && foreground == _lastSystemForeground)
        {
            return;
        }

        _lastHighContrast = highContrast;
        _lastSystemBackground = background;
        _lastSystemForeground = foreground;
        ApplyAppearance();
        ApplyBorderlessChrome();
    }

    private void CaptureSystemVisualSettings()
    {
        _lastHighContrast = _accessibilitySettings.HighContrast;
        _lastSystemBackground = _uiSettings.GetColorValue(UIColorType.Background);
        _lastSystemForeground = _uiSettings.GetColorValue(UIColorType.Foreground);
    }

    private void HideFlyout()
    {
        if (!_isFlyoutVisible)
        {
            return;
        }

        AppWindow.Hide();
        _isFlyoutVisible = false;
        _activationGuardTimer.Stop();
        _systemVisualSettingsTimer.Stop();
        _suppressDeactivateHide = false;
    }

    private void BeginActivationGuard()
    {
        _suppressDeactivateHide = true;
        _activationGuardTimer.Stop();
        _activationGuardTimer.Start();
    }

    private void OnActivationGuardElapsed(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        UpdateSystemBorder();
        _suppressDeactivateHide = false;
        if (_isFlyoutVisible
            && RootPage.ViewModel.CloseWhenInactive
            && !ForegroundWindowInspector.IsForeground(_windowHandle))
        {
            HideFlyout();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activationGuardTimer.Stop();
        _activationGuardTimer.Tick -= OnActivationGuardElapsed;
        _systemVisualSettingsTimer.Stop();
        _systemVisualSettingsTimer.Tick -= OnSystemVisualSettingsTimerElapsed;
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
        var appLocalPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        if (File.Exists(appLocalPath))
        {
            return appLocalPath;
        }

        // WAP places app binaries in a child folder while package assets stay
        // at the package root.
        var packageRootPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "Assets", "AppIcon.ico"));
        return File.Exists(packageRootPath)
            ? packageRootPath
            : appLocalPath;
    }

    private string GetString(string key)
    {
        var value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }
}
