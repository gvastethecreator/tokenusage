using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using TokenUsage.Core.Appearance;
using TokenUsage.App.Composition;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.App.ViewModels.Tray;
using TokenUsage.Core.Session;
using TokenUsage.Platform.Windows.Display;
using TokenUsage.Platform.Windows.Placement;
using TokenUsage.Platform.Windows.Storage;
using TokenUsage.Platform.Windows.Tray;
using TokenUsage.Platform.Windows.Windowing;
using WinRT.Interop;
using Windows.Storage;

namespace TokenUsage.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly ResourceLoader _resources = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _activationGuardTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _systemVisualSettingsTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _traySummaryDismissTimer;
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private readonly UISettings _uiSettings = new();
    private readonly nint _windowHandle;
    private readonly double _preferredWidthDips;
    private AppearanceSettings? _appliedAppearance;
    private TrayIconHost? _trayIcon;
    private TraySummaryWindow? _traySummaryWindow;
    private UsageReportWindow? _reportWindow;
    private bool _isFlyoutVisible;
    private bool _isTransparencyActive;
    private bool _suppressDeactivateHide;
    private bool _hasRequestedInitialOfficialLimits;
    private bool _layoutAnimationPositionPending;
    private readonly bool _traySummaryPinnedForTest;
    private bool _disposed;
    private bool _lastHighContrast;
    private Windows.UI.Color _lastSystemBackground;
    private Windows.UI.Color _lastSystemForeground;

    public MainWindow(
        bool showForTest = false,
        bool useSampleForTest = false,
        double? preferredWidthDipsForTest = null,
        bool showTraySummaryForTest = false)
    {
        InitializeComponent();

        _preferredWidthDips = preferredWidthDipsForTest ?? FlyoutSizePolicy.WidthDips;
        _traySummaryPinnedForTest = showTraySummaryForTest;

        _activationGuardTimer = DispatcherQueue.CreateTimer();
        _activationGuardTimer.Interval = TimeSpan.FromMilliseconds(500);
        _activationGuardTimer.IsRepeating = false;
        _activationGuardTimer.Tick += OnActivationGuardElapsed;

        _systemVisualSettingsTimer = DispatcherQueue.CreateTimer();
        _systemVisualSettingsTimer.Interval = TimeSpan.FromSeconds(1);
        _systemVisualSettingsTimer.IsRepeating = true;
        _systemVisualSettingsTimer.Tick += OnSystemVisualSettingsTimerElapsed;
        CaptureSystemVisualSettings();

        _traySummaryDismissTimer = DispatcherQueue.CreateTimer();
        _traySummaryDismissTimer.Interval = TimeSpan.FromMilliseconds(120);
        _traySummaryDismissTimer.IsRepeating = true;
        _traySummaryDismissTimer.Tick += OnTraySummaryDismissTimerElapsed;

        _windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureFlyoutWindow();
        RootPage.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyAppearance();
        UpdateStatusText();
        RootPage.HideRequested += OnHideRequested;
        RootPage.UsageReportRequested += OnUsageReportRequested;
        RootPage.LayoutAnimationProgressed += OnLayoutAnimationProgressed;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;

        AppWindow.Hide();
        InstallTrayIcon();

        if (showTraySummaryForTest)
        {
            RootPage.ViewModel.IsSampleModeEnabled = useSampleForTest;
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(750);
                if (!_disposed
                    && _trayIcon is not null
                    && _trayIcon.TryGetIconBounds(out PlatformRect iconBounds))
                {
                    ShowTraySummary(iconBounds);
                }
            });
        }

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
        _trayIcon.Hovered += OnTrayHovered;
        _trayIcon.ContextMenuOpening += OnTrayContextMenuOpening;
        _trayIcon.UpdateRequested += OnTrayUpdateRequested;
        _trayIcon.SettingsRequested += OnTraySettingsRequested;
        _trayIcon.ExitRequested += OnTrayExitRequested;
    }

    private void OnTrayActivated(object? sender, TrayActivatedEventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            HideTraySummary(force: true);
            if (e.Kind == TrayActivationKind.Mouse && _isFlyoutVisible)
            {
                HideFlyout();
                return;
            }

            ShowFlyout(e.Kind == TrayActivationKind.Keyboard);
        });
    }

    private void OnTrayHovered(object? sender, TrayHoveredEventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed || _isFlyoutVisible || _traySummaryWindow?.IsVisible is true)
            {
                return;
            }

            ShowTraySummary(e.IconBounds);
        });
    }

    private void OnTrayContextMenuOpening(object? sender, EventArgs e) =>
        _ = DispatcherQueue.TryEnqueue(() => HideTraySummary(force: true));

    private void OnTrayUpdateRequested(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            HideTraySummary(force: true);
            _ = RefreshFromTrayAsync();
        });
    }

    private async Task RefreshFromTrayAsync()
    {
        ShowFlyout(false);
        await RootPage.SessionHost.RefreshAsync(
            AppSessionRefreshReason.Manual,
            forceRefresh: true);
    }

    private void OnTraySettingsRequested(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            HideTraySummary(force: true);
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
        HideTraySummary(force: true);
        BeginActivationGuard();
        ApplyAppearance();
        PositionFlyout();
        _isFlyoutVisible = true;
        AppWindow.Show();
        ApplyBorderlessChrome();
        _systemVisualSettingsTimer.Start();
        Activate();
        _ = ForegroundWindowActivator.TryActivate(_windowHandle);
        EnsureOfficialCodexLimitsOnFirstOpen();

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

    private void EnsureOfficialCodexLimitsOnFirstOpen()
    {
        if (_hasRequestedInitialOfficialLimits)
        {
            return;
        }

        _hasRequestedInitialOfficialLimits = true;
        if (!RootPage.ViewModel.Dashboard.HasGlobalCodexLimits)
        {
            // Run after the window is active so this follows the same stable UI/session path
            // as the refresh action instead of racing the hidden startup surface.
            _ = RootPage.ViewModel.Dashboard.RefreshLiveAsync();
        }
    }

    internal IDisposable SuppressDeactivateHide()
    {
        _suppressDeactivateHide = true;
        _activationGuardTimer.Stop();
        return new DeactivateHideLease(this);
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
        _ = WindowBorderStyle.TryClipRoundedCorners(_windowHandle, radiusDips: 12);
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
        bool optionsSectionChanged = string.Equals(
            e.PropertyName,
            nameof(RootPage.ViewModel.ActiveOptionsSection),
            StringComparison.Ordinal);
        bool refreshChanged = string.Equals(
            e.PropertyName,
            nameof(RootPage.ViewModel.IsRefreshing),
            StringComparison.Ordinal);
        bool layoutChanged = string.Equals(
            e.PropertyName,
            nameof(RootPage.ViewModel.LayoutRevision),
            StringComparison.Ordinal);
        if (appearanceChanged)
        {
            AppearanceSettings settings = RootPage.ViewModel.Appearance;
            bool shellAppearanceChanged = HasShellAppearanceChanged(
                _appliedAppearance,
                settings);
            _appliedAppearance = settings;
            if (shellAppearanceChanged)
            {
                ApplyAppearance();
                _reportWindow?.ApplyAppearance(settings);
                UpdateVisibleTraySummary();
                ApplyBorderlessChrome();
                if (_isFlyoutVisible)
                {
                    SchedulePositionAfterLayout();
                }
            }
        }

        if (!surfaceChanged && !refreshChanged && !optionsSectionChanged && !layoutChanged)
        {
            return;
        }

        if (surfaceChanged || refreshChanged)
        {
            UpdateStatusText();
        }

        if ((surfaceChanged || optionsSectionChanged || layoutChanged) && _isFlyoutVisible)
        {
            BeginActivationGuard();
            SchedulePositionAfterLayout();
        }

        if (layoutChanged)
        {
            UpdateVisibleTraySummary();
        }
    }

    private void ApplyAppearance()
    {
        AppearanceSettings settings = RootPage.ViewModel.Appearance;
        _appliedAppearance = settings;
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

    private void ShowTraySummary(PlatformRect iconBounds)
    {
        _traySummaryWindow ??= new TraySummaryWindow();
        _traySummaryWindow.Show(
            CreateTrayProviderSummaries(),
            RootPage.ViewModel.Appearance,
            iconBounds);
        _traySummaryDismissTimer.Start();
        _systemVisualSettingsTimer.Start();
    }

    private IReadOnlyList<TrayProviderSummary> CreateTrayProviderSummaries()
    {
        TrayProviderPreference[] preferences = RootPage.ViewModel.Personalization.Providers
            .Select(row => new TrayProviderPreference(
                row.ProviderId,
                row.Name,
                row.IsVisible,
                row.IsHighlighted))
            .ToArray();
        return TraySummaryProjector.Create(
            preferences,
            RootPage.ViewModel.Dashboard.ProviderSummaries,
            RootPage.ViewModel.Dashboard.GetProviderLimits,
            GetString);
    }

    private void UpdateVisibleTraySummary()
    {
        if (_traySummaryWindow?.IsVisible is not true
            || _trayIcon is null
            || !_trayIcon.TryGetIconBounds(out PlatformRect iconBounds))
        {
            return;
        }

        _traySummaryWindow.Show(
            CreateTrayProviderSummaries(),
            RootPage.ViewModel.Appearance,
            iconBounds);
    }

    private void HideTraySummary(bool force = false)
    {
        if (_traySummaryPinnedForTest && !force)
        {
            return;
        }

        _traySummaryDismissTimer.Stop();
        _traySummaryWindow?.Hide();
        if (!_isFlyoutVisible)
        {
            _systemVisualSettingsTimer.Stop();
        }
    }

    private void OnTraySummaryDismissTimerElapsed(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        if (_traySummaryPinnedForTest)
        {
            return;
        }

        if (_trayIcon is null || !_trayIcon.IsPointerOverIcon())
        {
            HideTraySummary();
        }
    }

    private static bool HasShellAppearanceChanged(
        AppearanceSettings? previous,
        AppearanceSettings current) => previous is null
        || previous.Theme != current.Theme
        || previous.Density != current.Density
        || previous.IncreaseTransparency != current.IncreaseTransparency;

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
                : "LiveDashboardHeading";
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

    private void OnLayoutAnimationProgressed(object? sender, EventArgs e)
    {
        if (_disposed || !_isFlyoutVisible || _layoutAnimationPositionPending)
        {
            return;
        }

        _layoutAnimationPositionPending = true;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            _layoutAnimationPositionPending = false;
            if (!_disposed && _isFlyoutVisible)
            {
                PositionFlyout();
            }
        });
    }

    private void OnHideRequested(object? sender, EventArgs e) => HideFlyout();

    private void OnUsageReportRequested(object? sender, UsageReportRequestedEventArgs e)
    {
        if (_reportWindow is null)
        {
            string localFolderPath = TokenUsageDataDirectory.Resolve(
                () => ApplicationData.Current.LocalFolder.Path);
            string databasePath = AppComposition.GetUsageDatabasePath(localFolderPath);
            string resetHistoryPath = AppComposition.GetQuotaResetHistoryPath(localFolderPath);
            PlatformRect? iconBounds = _trayIcon is not null
                && _trayIcon.TryGetIconBounds(out PlatformRect trayIconBounds)
                    ? trayIconBounds
                    : null;
            MonitorPlacementContext display = MonitorPlacementContextProvider.Resolve(iconBounds);
            uint reportDpi = MonitorPlacementContextProvider.GetWindowDpi(_windowHandle);
            _reportWindow = new UsageReportWindow(
                databasePath,
                resetHistoryPath,
                () => RootPage.ViewModel.Dashboard.RefreshLiveAsync(),
                RootPage.ViewModel.Appearance,
                e.Request,
                RootPage.ViewModel.Dashboard.GetProviderLimits,
                display.WorkArea,
                reportDpi);
            _reportWindow.Closed += OnUsageReportWindowClosed;
        }

        _reportWindow.ApplyRequest(e.Request);

        _reportWindow.Activate();
    }

    private void OnUsageReportWindowClosed(object sender, WindowEventArgs args)
    {
        if (_reportWindow is not null)
        {
            _reportWindow.Closed -= OnUsageReportWindowClosed;
            _reportWindow = null;
        }
    }

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
        _reportWindow?.ApplyAppearance(RootPage.ViewModel.Appearance);
        UpdateVisibleTraySummary();
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

    private sealed class DeactivateHideLease : IDisposable
    {
        private MainWindow? _owner;

        public DeactivateHideLease(MainWindow owner) => _owner = owner;

        public void Dispose()
        {
            MainWindow? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null || owner._disposed)
            {
                return;
            }

            owner.BeginActivationGuard();
            owner.Activate();
            _ = ForegroundWindowActivator.TryActivate(owner._windowHandle);
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
        _traySummaryDismissTimer.Stop();
        _traySummaryDismissTimer.Tick -= OnTraySummaryDismissTimerElapsed;
        RootPage.ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        RootPage.HideRequested -= OnHideRequested;
        RootPage.UsageReportRequested -= OnUsageReportRequested;
        RootPage.LayoutAnimationProgressed -= OnLayoutAnimationProgressed;
        if (_reportWindow is not null)
        {
            _reportWindow.Closed -= OnUsageReportWindowClosed;
            _reportWindow.Close();
            _reportWindow = null;
        }
        _traySummaryWindow?.Dispose();
        _traySummaryWindow = null;
        RootPage.Dispose();
        DisposeTrayIcon();
        GC.SuppressFinalize(this);
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        RootPage.Dispose();
        await RootPage.SessionHost.DisposeAsync();
        Dispose();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Activated -= OnTrayActivated;
        _trayIcon.Hovered -= OnTrayHovered;
        _trayIcon.ContextMenuOpening -= OnTrayContextMenuOpening;
        _trayIcon.UpdateRequested -= OnTrayUpdateRequested;
        _trayIcon.SettingsRequested -= OnTraySettingsRequested;
        _trayIcon.ExitRequested -= OnTrayExitRequested;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    internal static string GetIconPath()
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
