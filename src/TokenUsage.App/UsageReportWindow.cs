using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.Views.Reports;
using TokenUsage.Core.Appearance;
using TokenUsage.Platform.Windows.Display;
using TokenUsage.Platform.Windows.Placement;

namespace TokenUsage.App;

public sealed class UsageReportWindow : Window, IDisposable
{
    private readonly ResourceLoader _resources = new();
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private readonly UISettings _uiSettings = new();
    private readonly UsageReportViewModel _viewModel;
    private readonly UsageReportPage _page;
    private bool _disposed;

    public UsageReportWindow(
        string databasePath,
        string resetHistoryPath,
        Func<Task> refreshSourceAsync,
        AppearanceSettings appearance,
        UsageReportRequest request,
        Func<string, IReadOnlyList<QuotaWindow>> getProviderLimits,
        PlatformRect workArea,
        uint dpi)
    {
        _viewModel = new UsageReportViewModel(
            databasePath,
            refreshSourceAsync,
            request,
            getProviderLimits,
            new TokenUsage.Core.Usage.QuotaResetHistoryStore(resetHistoryPath));
        _page = new UsageReportPage(_viewModel);
        Content = _page;
        Title = GetString("UsageReportWindowTitle");
        AppWindow.Title = Title;
        AppWindow.SetIcon(MainWindow.GetIconPath());
        ConfigureSize(workArea, dpi);
        ApplyAppearance(appearance);
        Closed += OnClosed;
    }

    public void ApplyAppearance(AppearanceSettings settings)
    {
        _page.ApplyAppearance(settings);
        ApplyTitleBarAppearance(settings);
    }

    public void ApplyRequest(UsageReportRequest request) => _viewModel.ApplyRequest(request);

    private void ConfigureSize(PlatformRect workArea, uint dpi)
    {
        PlatformRect bounds = ReportWindowPlacementPolicy.Calculate(workArea, dpi);
        AppWindow.MoveAndResize(
            new RectInt32(bounds.Left, bounds.Top, bounds.Width, bounds.Height));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }
    }

    private void ApplyTitleBarAppearance(AppearanceSettings settings)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        bool highContrast = _accessibilitySettings.HighContrast;
        bool dark = settings.Theme switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
        Windows.UI.Color background = highContrast
            ? _uiSettings.GetColorValue(UIColorType.Background)
            : dark
                ? Windows.UI.Color.FromArgb(255, 12, 12, 12)
                : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        Windows.UI.Color foreground = highContrast
            ? _uiSettings.GetColorValue(UIColorType.Foreground)
            : dark
                ? Microsoft.UI.Colors.White
                : Microsoft.UI.Colors.Black;

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = background;
        titleBar.ButtonPressedBackgroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = background;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Closed -= OnClosed;
        _viewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    private string GetString(string key)
    {
        string value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }
}
