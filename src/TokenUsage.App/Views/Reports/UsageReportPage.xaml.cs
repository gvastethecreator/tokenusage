using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.ApplicationModel.Resources;
using TokenUsage.App.Controls;
using TokenUsage.App.Services;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.Core.Appearance;

namespace TokenUsage.App.Views.Reports;

public sealed partial class UsageReportPage : Page
{
    private readonly ResourceLoader _resources = new();
    private readonly SpatialTransitionState _breakdownTransition = new();
    private int _reportDataTransitionToken;
    private Storyboard? _reportDataTransitionStoryboard;
    private FrameworkElement[] _reportDataTransitionTargets = [];
    private ReportDataTransitionIntent? _pendingReportDataIntent;
    private Action? _pendingReportDataCommit;
    private bool _reportDataCommitPending;
    private UsageReportBreakdown _pendingBreakdown;
    private int _reportRefreshGeneration;
    private int _lastCompletedRefreshGeneration;
    private bool _reportRefreshInProgress;
    private bool _reportRefreshLoading;
    private bool _isTransitionCommit;
    private bool _loadedOnce;
    private int _shareStatusToken;
    private readonly ObservableCollection<UsageReportProviderOption> _visibleProviderTabs = [];
    private Storyboard? _providerTabsStoryboard;
    private int _providerTabsTransitionToken;
    private int _providerTabPageSize = ProviderTabCarouselLayout.ReportMaximumPageSize;
    private int _providerTabStartIndex;
    private double _providerTabItemWidth = ProviderTabCarouselLayout.MinimumItemWidth;

    private enum ReportDataTransitionIntent
    {
        Period,
        Metric,
        Scope,
        ValueMode,
        Provider,
    }

    private sealed class SpatialTransitionState
    {
        public int Token { get; set; }

        public Storyboard? Storyboard { get; set; }

        public bool CommitPending { get; set; }
    }

    public UsageReportPage(UsageReportViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public UsageReportViewModel ViewModel { get; }

    public UIElement DragRegion => ReportDragRegion;
    public event EventHandler<ReportChartGrouping>? ChartGroupingChanged;
    public event EventHandler<ReportChartStyle>? ChartStyleChanged;
    public void SetCaptionInset(double width) => ReportCaptionInset.Width = new GridLength(width);

    public object VisibleProviderTabs => _visibleProviderTabs;

    public void ApplyAppearance(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ViewModel.SetChartAppearance(settings.ReportChartStyle, settings.ReportChartGrouping);
        RequestedTheme = settings.Theme switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private void OnCycleChartStyleClick(object sender, RoutedEventArgs e)
    {
        ReportChartStyle[] styles = Enum.GetValues<ReportChartStyle>();
        ReportChartStyle next = styles[(Array.IndexOf(styles, ViewModel.ChartStyle) + 1) % styles.Length];
        ViewModel.SetChartAppearance(next, ViewModel.ChartGrouping);
        ChartStyleChanged?.Invoke(this, next);
    }

    private void OnSmallValuesClick(object sender, RoutedEventArgs e) =>
        ViewModel.SetSmallValueScale(sender is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { IsChecked: true });

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        // The report must not open on a stale scanner database. A live refresh also discovers
        // Codex sessions created since the tray dashboard was last opened.
        await ViewModel.LoadAsync(refreshSource: true);
        SynchronizeProviderTabs();
        UpdateSortHeaders();
        _ = DispatcherQueue.TryEnqueue(() =>
            ReportScrollViewer.ChangeView(null, 0, null, disableAnimation: true));
    }

    private void OnPeriodSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: UsageReportPeriodOption period })
        {
            bool changesPeriod = period.Days != ViewModel.WindowDays
                || ViewModel.IsResetCycleWindow;
            if (ShouldStartReportDataTransition(ReportDataTransitionIntent.Period, changesPeriod))
            {
                PlayReportDataTransition(
                    () => ViewModel.SetWindowDays(period.Days),
                    ReportDataTransitionIntent.Period);
            }
        }
    }

    private void OnResetCycleClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanUseResetCycles
            && ShouldStartReportDataTransition(
                ReportDataTransitionIntent.Period,
                !ViewModel.IsResetCycleWindow))
        {
            PlayReportDataTransition(
                ViewModel.SetResetCycleWindow,
                ReportDataTransitionIntent.Period);
        }
    }

    private void OnPreviousResetCycleClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanSelectPreviousResetCycle)
        {
            PlayReportDataTransition(
                ViewModel.SelectPreviousResetCycle,
                ReportDataTransitionIntent.Period);
        }
    }

    private void OnNextResetCycleClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanSelectNextResetCycle)
        {
            PlayReportDataTransition(
                ViewModel.SelectNextResetCycle,
                ReportDataTransitionIntent.Period);
        }
    }

    private void OnMetricClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value }
            && Enum.TryParse(value, ignoreCase: true, out UsageReportMetric metric)
            && metric != UsageReportMetric.Share
            && ShouldStartReportDataTransition(
                ReportDataTransitionIntent.Metric,
                metric != ViewModel.Metric))
        {
            PlayReportDataTransition(
                () => ViewModel.SetMetric(metric),
                ReportDataTransitionIntent.Metric);
        }
    }

    private void OnScopeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value }
            && Enum.TryParse(value, ignoreCase: true, out UsageReportScope scope)
            && ShouldStartReportDataTransition(
                ReportDataTransitionIntent.Scope,
                scope != ViewModel.Scope))
        {
            PlayReportDataTransition(
                () => ViewModel.SetScope(scope),
                ReportDataTransitionIntent.Scope);
        }
    }

    private void OnCompareAxisClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value }
            && Enum.TryParse(value, ignoreCase: true, out UsageReportCompareAxis axis)
            && ShouldStartReportDataTransition(
                ReportDataTransitionIntent.Scope,
                axis != ViewModel.CompareAxis))
        {
            PlayReportDataTransition(
                () => ViewModel.SetCompareAxis(axis),
                ReportDataTransitionIntent.Scope);
        }
    }

    private void OnValueModeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value }
            && Enum.TryParse(value, ignoreCase: true, out UsageReportValueMode mode)
            && ViewModel.IsGlobalScope
            && ShouldStartReportDataTransition(
                ReportDataTransitionIntent.ValueMode,
                mode != ViewModel.ValueMode))
        {
            PlayReportDataTransition(
                () => ViewModel.SetValueMode(mode),
                ReportDataTransitionIntent.ValueMode);
        }
    }

    private async void OnShareCaptureClick(object sender, RoutedEventArgs e)
    {
        Control? source = sender as Control;
        var captureSelectors = Descendants(ReportCaptureRoot).OfType<RadioButton>()
            .Select(control => (Control: control, control.Opacity, control.IsHitTestVisible)).ToArray();
        double controlBarOpacity = ReportControlBar.Opacity;
        bool controlBarHitTest = ReportControlBar.IsHitTestVisible;
        double coverageHintOpacity = ReportCoverageHintButton.Opacity;
        bool coverageHintHitTest = ReportCoverageHintButton.IsHitTestVisible;
        double captureBrandOpacity = ReportCaptureBrand.Opacity;
        Visibility captureBrandVisibility = ReportCaptureBrand.Visibility;
        Thickness capturePadding = ReportCaptureRoot.Padding;
        GridLength captionInset = ReportCaptionInset.Width;
        Visibility toolbarVisibility = ReportToolbarHost.Visibility;
        Visibility windowBrandVisibility = ReportWindowBrandRoot.Visibility;
        try
        {
            if (source is not null)
            {
                source.IsEnabled = false;
            }

            ReportCaptureFocusSink.Focus(FocusState.Programmatic);
            ReportCoverageHintButton.Flyout.Hide();
            DismissChartHovers(ReportCaptureRoot);
            foreach (var item in captureSelectors)
            {
                item.Control.Opacity = 0;
                item.Control.IsHitTestVisible = false;
            }
            ReportToolbarHost.Visibility = Visibility.Collapsed;
            ReportWindowBrandRoot.Visibility = Visibility.Collapsed;
            ReportCaptionInset.Width = new GridLength(0);
            ReportControlBar.Opacity = 0;
            ReportControlBar.IsHitTestVisible = false;
            ReportCoverageHintButton.Opacity = 0;
            ReportCoverageHintButton.IsHitTestVisible = false;
            ReportCaptureBrand.Visibility = Visibility.Visible;
            ReportCaptureBrand.Opacity = 1;
            ReportCaptureRoot.Padding = new Thickness(
                capturePadding.Left,
                capturePadding.Top,
                capturePadding.Right,
                10);
            ReportHeaderRoot.UpdateLayout();
            ReportCaptureRoot.UpdateLayout();
            await Task.Yield();
            ShareCaptureResult result = await ShareCaptureService.CaptureScrollableAsync(
                ReportHeaderRoot,
                ReportScrollViewer,
                ReportCaptureRoot,
                "report",
                ReportCaptureSurface.ActualTheme == ElementTheme.Light
                    ? Microsoft.UI.Colors.White
                    : Microsoft.UI.Colors.Black);
            ShowShareStatus(
                string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    GetString("ShareCaptureSuccessFormat"),
                    result.FilePath),
                isError: false);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            ShowShareStatus(GetString("ShareCaptureError"), isError: true);
        }
        finally
        {
            foreach (var item in captureSelectors)
            {
                item.Control.Opacity = item.Opacity;
                item.Control.IsHitTestVisible = item.IsHitTestVisible;
            }
            ReportToolbarHost.Visibility = toolbarVisibility;
            ReportWindowBrandRoot.Visibility = windowBrandVisibility;
            ReportCaptionInset.Width = captionInset;
            ReportControlBar.Opacity = controlBarOpacity;
            ReportControlBar.IsHitTestVisible = controlBarHitTest;
            ReportCoverageHintButton.Opacity = coverageHintOpacity;
            ReportCoverageHintButton.IsHitTestVisible = coverageHintHitTest;
            ReportCaptureBrand.Opacity = captureBrandOpacity;
            ReportCaptureBrand.Visibility = captureBrandVisibility;
            ReportCaptureRoot.Padding = capturePadding;
            ReportHeaderRoot.UpdateLayout();
            ReportCaptureRoot.UpdateLayout();
            if (source is not null)
            {
                source.IsEnabled = true;
                source.Focus(FocusState.Programmatic);
            }
        }
    }


    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void DismissChartHovers(DependencyObject parent)
    {
        if (parent is UsageTrendChart chart) chart.DismissHover();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            DismissChartHovers(VisualTreeHelper.GetChild(parent, i));
    }

    private (FrameworkElement Rows, CompositeTransform Transform) GetBreakdownTransitionTarget(
        UsageReportBreakdown breakdown) => breakdown switch
        {
            UsageReportBreakdown.Source => (SourceBreakdownRows, SourceBreakdownRowsTransform),
            UsageReportBreakdown.Day => (DayBreakdownRows, DayBreakdownRowsTransform),
            _ => (ModelBreakdownRows, ModelBreakdownRowsTransform),
        };


    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loadedOnce)
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(UsageReportViewModel.IsLoading), StringComparison.Ordinal))
        {
            if (_isTransitionCommit)
            {
                return;
            }

            if (ViewModel.IsLoading)
            {
                BeginReportRefreshCycle();
            }
            else
            {
                CompleteReportRefreshCycle();
            }
            return;
        }

        if (e.PropertyName == nameof(UsageReportViewModel.ModelShareLabel))
            _ = DispatcherQueue.TryEnqueue(UpdateSortHeaders);

        if (_isTransitionCommit)
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(UsageReportViewModel.ProviderOptions), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(UsageReportViewModel.SelectedProvider), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(UsageReportViewModel.IsProviderPickerVisible), StringComparison.Ordinal))
        {
            _ = DispatcherQueue.TryEnqueue(SynchronizeProviderTabs);
        }

        if (string.Equals(e.PropertyName, nameof(UsageReportViewModel.Trend), StringComparison.Ordinal)
            && !ViewModel.IsLoading
            && !_reportRefreshInProgress
            && !_reportDataCommitPending)
        {
            PlayIsolatedTrendEntry();
        }
    }


    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _rowMotions.Clear();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _reportDataTransitionToken++;
        StopReportDataTransition(resetTargets: true);
        ResetReportDataTargets(GetAllReportDataTargets());
        CancelSpatialTransition(_breakdownTransition);
        CancelProviderTabsTransition();
        ResetSpatialTarget(GlobalChartTransitionRoot, GlobalChartTransitionTransform);
        ResetSpatialTarget(ModelBreakdownRows, ModelBreakdownRowsTransform);
        ResetSpatialTarget(SourceBreakdownRows, SourceBreakdownRowsTransform);
        ResetSpatialTarget(DayBreakdownRows, DayBreakdownRowsTransform);
        _pendingReportDataIntent = null;
        _pendingReportDataCommit = null;
        _reportDataCommitPending = false;
        _reportRefreshGeneration = 0;
        _lastCompletedRefreshGeneration = 0;
        _reportRefreshInProgress = false;
        _reportRefreshLoading = false;
    }

    private async void ShowShareStatus(string message, bool isError)
    {
        int token = ++_shareStatusToken;
        ShareStatusInfoBar.Message = message;
        ShareStatusInfoBar.Severity = isError
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Success;
        ShareStatusInfoBar.IsOpen = true;
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (token == _shareStatusToken)
        {
            ShareStatusInfoBar.IsOpen = false;
        }
    }

    private string GetString(string key)
    {
        string value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }
}
