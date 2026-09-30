using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TokenUsage.App.ViewModels.Tray;
using TokenUsage.App.ViewModels;
using TokenUsage.Core.Appearance;
using TokenUsage.Platform.Windows.Display;
using TokenUsage.Platform.Windows.Placement;
using TokenUsage.Platform.Windows.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace TokenUsage.App;

public sealed partial class TraySummaryWindow : Window, IDisposable
{
    private const double HorizontalChromeDips = 16d;
    private const double VerticalChromeDips = 16d;
    private const double IconGapDips = 5d;

    private readonly nint _windowHandle;
    private bool _disposed;
    private bool _contentInitialized;
    private PlatformRect _iconBounds;

    public TraySummaryWindow()
    {
        InitializeComponent();
        SummaryView.PointerEntered += (_, _) => IsPointerOver = true;
        SummaryView.PointerExited += (_, _) => IsPointerOver = false;
        SummaryView.Loaded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (IsVisible && !_disposed)
            {
                SummaryView.MeasureAccountContent();
                PositionAndShow(_iconBounds);
            }
        });
        _windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureWindow();
        AppWindow.Hide();
    }

    public bool IsVisible { get; private set; }
    public bool IsPointerOver { get; private set; }

    public void Show(
        IReadOnlyList<TrayProviderSummary> items,
        AppearanceSettings appearance,
        PlatformRect iconBounds,
        IReadOnlyList<CodexAccountQuota>? accounts = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(appearance);
        if (iconBounds.Width <= 0 || iconBounds.Height <= 0)
        {
            throw new ArgumentException("The tray icon bounds must have positive size.", nameof(iconBounds));
        }

        _iconBounds = iconBounds;
        SummaryView.Apply(items, appearance, accounts);
        PositionAndShow(iconBounds);
        IsVisible = true;
    }

    public void Refresh(
        IReadOnlyList<TrayProviderSummary> items,
        AppearanceSettings appearance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(appearance);
        SummaryView.Apply(items, appearance);
    }

    public void Hide()
    {
        if (_disposed || !IsVisible)
        {
            return;
        }

        AppWindow.Hide();
        IsVisible = false;
        IsPointerOver = false;
    }

    private void ConfigureWindow()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            throw new InvalidOperationException("The tray preview presenter is unavailable.");
        }

        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = "TokenUsage";
        _ = WindowBorderStyle.TryRemoveNonClientFrame(_windowHandle);
        _ = WindowBorderStyle.TryHideSystemBorder(_windowHandle);
        _ = WindowBorderStyle.TryUseSmallRoundedCorners(_windowHandle);
        _ = NonActivatingWindowStyle.TryApply(_windowHandle);
    }

    private void PositionAndShow(PlatformRect iconBounds)
    {
        MonitorPlacementContext display = MonitorPlacementContextProvider.Resolve(iconBounds);
        double widthDips = SummaryView.ContentWidthDips + HorizontalChromeDips;
        double heightDips = SummaryView.ContentHeightDips + VerticalChromeDips;
        FlyoutPlacementResult initial = FlyoutPlacementCalculator.Calculate(
            iconBounds,
            display.WorkArea,
            widthDips,
            heightDips,
            96,
            display.FallbackAnchor);
        PlatformRect initialBounds = TrayPopoverPlacement.MoveNextToIcon(
            initial.Bounds,
            iconBounds,
            initial.AnchorEdge,
            FlyoutPlacementCalculator.DipsToPixels(IconGapDips, 96));
        AppWindow.MoveAndResize(new RectInt32(
            initialBounds.Left,
            initialBounds.Top,
            initialBounds.Width,
            initialBounds.Height));

        uint dpi = MonitorPlacementContextProvider.GetWindowDpi(_windowHandle);
        FlyoutPlacementResult final = FlyoutPlacementCalculator.Calculate(
            iconBounds,
            display.WorkArea,
            widthDips,
            heightDips,
            dpi,
            display.FallbackAnchor);
        PlatformRect finalBounds = TrayPopoverPlacement.MoveNextToIcon(
            final.Bounds,
            iconBounds,
            final.AnchorEdge,
            FlyoutPlacementCalculator.DipsToPixels(IconGapDips, dpi));
        if (!_contentInitialized)
        {
            // WinUI must activate the XAML window once to connect its content tree.
            // Preserve the foreground window when initializing this hover preview.
            nint foreground = ForegroundWindowInspector.Current;
            Activate();
            _contentInitialized = true;
            if (ForegroundWindowInspector.IsForeground(_windowHandle))
                _ = ForegroundWindowActivator.TryActivate(foreground);
        }
        AppWindow.Show(activateWindow: false);
        if (!NonActivatingWindowStyle.TryShowAt(_windowHandle, finalBounds))
        {
            AppWindow.MoveAndResize(new RectInt32(
                finalBounds.Left,
                finalBounds.Top,
                finalBounds.Width,
                finalBounds.Height));
            AppWindow.Show();
        }

        _ = WindowBorderStyle.TryClipRoundedCorners(_windowHandle, radiusDips: 10);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsVisible = false;
        Close();
        GC.SuppressFinalize(this);
    }
}
