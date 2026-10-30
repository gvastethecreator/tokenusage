using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using TokenUsage.App.Services;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.Core.Appearance;

namespace TokenUsage.App.Views.Reports;

public sealed partial class ReportPage : Page
{
    private readonly ResourceLoader _resources = new();

    public ReportPage(ReportViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    public ReportViewModel ViewModel { get; }

    public UIElement DragRegion => ReportDragRegion;

    public void SetCaptionInset(double width) => ReportCaptionInset.Width = new GridLength(width);

    public void ApplyAppearance(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RequestedTheme = settings.Theme switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public static Brush ToneBrush(ReportTone tone) => (Brush)Application.Current.Resources[tone switch
    {
        ReportTone.Up or ReportTone.Warning => "ReportToneAmberBrush",
        ReportTone.Critical => "QuotaCriticalBrush",
        _ => "TextFillColorSecondaryBrush",
    }];

    public static Visibility Show(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    private void OnPeriodChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, CultureInfo.InvariantCulture, out int days))
            ViewModel.PeriodDays = days;
    }

    private void OnGroupingChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out ReportGrouping grouping))
            ViewModel.Grouping = grouping;
    }

    private async void OnShareClick(object sender, RoutedEventArgs e)
    {
        double actionsOpacity = ReportActions.Opacity, captureBrandOpacity = ReportCaptureBrand.Opacity;
        ReportActions.Opacity = 0;
        ReportCaptureBrand.Opacity = 1;
        ShareStatus.Visibility = Visibility.Collapsed;
        try
        {
            ShareCaptureResult result = await ShareCaptureService.CaptureScrollableAsync(
                ReportHeaderRoot, ReportScrollViewer, ReportCaptureRoot, "report",
                ActualTheme == ElementTheme.Light ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black);
            ShareStatus.Text = string.Format(CultureInfo.CurrentCulture,
                _resources.GetString("ShareCaptureSuccessFormat"), result.FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ShareStatus.Text = _resources.GetString("ShareCaptureError");
        }
        finally
        {
            ReportActions.Opacity = actionsOpacity;
            ReportCaptureBrand.Opacity = captureBrandOpacity;
            ShareStatus.Visibility = Visibility.Visible;
        }
    }
}
