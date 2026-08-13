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
    private readonly SpatialTransitionState _chartLayoutTransition = new();
    private readonly SpatialTransitionState _breakdownTransition = new();
    private int _reportDataTransitionToken;
    private Storyboard? _reportDataTransitionStoryboard;
    private FrameworkElement[] _reportDataTransitionTargets = [];
    private ReportDataTransitionIntent? _pendingReportDataIntent;
    private Action? _pendingReportDataCommit;
    private bool _reportDataCommitPending;
    private bool _pendingShowCombined;
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

    public object VisibleProviderTabs => _visibleProviderTabs;

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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        await ViewModel.LoadAsync();
        SynchronizeProviderTabs();
        _ = DispatcherQueue.TryEnqueue(() =>
            ReportScrollViewer.ChangeView(null, 0, null, disableAnimation: true));
    }

    private void OnPeriodClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value }
            && int.TryParse(value, out int days))
        {
            bool changesPeriod = days != ViewModel.WindowDays || ViewModel.IsResetCycleWindow;
            if (ShouldStartReportDataTransition(ReportDataTransitionIntent.Period, changesPeriod))
            {
                PlayReportDataTransition(
                    () => ViewModel.SetWindowDays(days),
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
        Visibility controlBarVisibility = ReportControlBar.Visibility;
        Visibility coverageHintVisibility = ReportCoverageHintButton.Visibility;
        Visibility captureBrandVisibility = ReportCaptureBrand.Visibility;
        try
        {
            ReportCaptureFocusSink.Focus(FocusState.Programmatic);
            ReportControlBar.Visibility = Visibility.Collapsed;
            ReportCoverageHintButton.Visibility = Visibility.Collapsed;
            ReportCaptureBrand.Visibility = Visibility.Visible;
            ReportCaptureRoot.UpdateLayout();
            ShareCaptureResult result = await ShareCaptureService.CaptureAsync(
                ReportCaptureRoot,
                "report",
                ReportCaptureRoot.ActualTheme == ElementTheme.Light
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
            ReportControlBar.Visibility = controlBarVisibility;
            ReportCoverageHintButton.Visibility = coverageHintVisibility;
            ReportCaptureBrand.Visibility = captureBrandVisibility;
            ReportCaptureRoot.UpdateLayout();
            source?.Focus(FocusState.Programmatic);
        }
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
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _reportDataTransitionToken++;
        StopReportDataTransition(resetTargets: true);
        ResetReportDataTargets(GetAllReportDataTargets());
        CancelSpatialTransition(_chartLayoutTransition);
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

    private void OnCoverageHintEntered(object sender, RoutedEventArgs e) =>
        CoverageHintToolTip.IsOpen = true;

    private void OnCoverageHintExited(object sender, RoutedEventArgs e) =>
        CoverageHintToolTip.IsOpen = false;

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
