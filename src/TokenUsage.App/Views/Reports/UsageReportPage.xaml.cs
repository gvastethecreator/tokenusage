using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.ApplicationModel.Resources;
using TokenUsage.App.Controls;
using TokenUsage.App.Services;
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
        try
        {
            ReportCaptureFocusSink.Focus(FocusState.Programmatic);
            ReportControlBar.Visibility = Visibility.Collapsed;
            ReportCoverageHintButton.Visibility = Visibility.Collapsed;
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
            ReportCaptureRoot.UpdateLayout();
            source?.Focus(FocusState.Programmatic);
        }
    }

    private void OnProviderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: UsageReportProviderOption option })
        {
            bool changesProvider = !string.Equals(
                    ViewModel.SelectedProvider?.ProviderId,
                    option.ProviderId,
                    StringComparison.Ordinal);
            if (ShouldStartReportDataTransition(
                ReportDataTransitionIntent.Provider,
                changesProvider))
            {
                PlayReportDataTransition(
                    () => ViewModel.SelectedProvider = option,
                    ReportDataTransitionIntent.Provider);
            }
        }
    }

    private void OnProviderContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (ReferenceEquals(sender, ReportProviderSelector)
            && args.ItemContainer is ListViewItem container
            && args.Item is UsageReportProviderOption option)
        {
            AutomationProperties.SetAutomationId(container, option.ProviderId);
            AutomationProperties.SetName(container, option.Name);
            _ = DispatcherQueue.TryEnqueue(() =>
                UpdateProviderSelectorWidths(ReportProviderSelector.ActualWidth));
        }
    }

    private void OnProviderSelectorSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateProviderSelectorWidths(e.NewSize.Width);

    private void UpdateProviderSelectorWidths(double availableWidth)
    {
        int count = ViewModel.ProviderOptions.Count;
        if (availableWidth <= 0 || count == 0)
        {
            return;
        }

        const double spacing = 2;
        double itemWidth = Math.Max(0, (availableWidth - (spacing * (count - 1))) / count);
        for (int index = 0; index < count; index++)
        {
            if (ReportProviderSelector.ContainerFromIndex(index) is not ListViewItem container)
            {
                continue;
            }

            container.Width = itemWidth;
            container.Margin = index == count - 1
                ? new Thickness(0)
                : new Thickness(0, 0, spacing, 0);
        }
    }

    private void OnReportCompositionSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateReportCompositionWidths(e.NewSize.Width);

    private void OnReportCompositionElementPrepared(
        ItemsRepeater sender,
        ItemsRepeaterElementPreparedEventArgs args) =>
        _ = DispatcherQueue.TryEnqueue(() =>
            UpdateReportCompositionWidths(ReportCompositionBar.ActualWidth));

    private void UpdateReportCompositionWidths(double availableWidth)
    {
        if (availableWidth <= 0 || ViewModel.Providers.Count == 0)
        {
            return;
        }

        const double minimumSegmentWidth = 2;
        double[] shares = ViewModel.Providers
            .Select(provider => Math.Max(0, provider.SharePercent))
            .ToArray();
        bool[] usesMinimum = shares
            .Select(share => (share / 100d) * availableWidth < minimumSegmentWidth)
            .ToArray();
        double reservedWidth = usesMinimum.Count(value => value) * minimumSegmentWidth;
        double flexibleWidth = Math.Max(0, availableWidth - reservedWidth);
        double flexibleShare = shares
            .Where((_, index) => !usesMinimum[index])
            .Sum();
        double assignedWidth = 0;

        for (int index = 0; index < shares.Length; index++)
        {
            if (ReportCompositionBar.TryGetElement(index) is not ProviderColorSwatch swatch)
            {
                continue;
            }

            double width = usesMinimum[index]
                ? minimumSegmentWidth
                : flexibleShare <= 0
                    ? flexibleWidth / Math.Max(1, shares.Length - usesMinimum.Count(value => value))
                    : flexibleWidth * shares[index] / flexibleShare;
            if (index == shares.Length - 1)
            {
                width = Math.Max(0, availableWidth - assignedWidth);
            }

            swatch.Width = width;
            assignedWidth += width;
        }
    }

    private void OnBreakdownClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value }
            && Enum.TryParse(value, ignoreCase: true, out UsageReportBreakdown breakdown))
        {
            bool changesBreakdown = breakdown != ViewModel.Breakdown;
            if (!changesBreakdown
                && (!_breakdownTransition.CommitPending || _pendingBreakdown == breakdown))
            {
                return;
            }

            (FrameworkElement currentContent, CompositeTransform currentTransform) =
                GetBreakdownTransitionTarget(ViewModel.Breakdown);
            _pendingBreakdown = breakdown;
            _breakdownTransition.CommitPending = true;
            PlaySpatialTransition(
                _breakdownTransition,
                currentContent,
                currentTransform,
                () => ViewModel.SetBreakdown(breakdown),
                () => GetBreakdownTransitionTarget(ViewModel.Breakdown),
                disableHitTesting: false);
        }
    }

    private (FrameworkElement Rows, CompositeTransform Transform) GetBreakdownTransitionTarget(
        UsageReportBreakdown breakdown) => breakdown switch
        {
            UsageReportBreakdown.Source => (SourceBreakdownRows, SourceBreakdownRowsTransform),
            UsageReportBreakdown.Day => (DayBreakdownRows, DayBreakdownRowsTransform),
            _ => (ModelBreakdownRows, ModelBreakdownRowsTransform),
        };

    private void OnGlobalChartLayoutClick(object sender, RoutedEventArgs e)
    {
        bool showCombined = sender is FrameworkElement { Tag: "Combined" };
        bool currentlyCombined = GlobalCombinedChart.Visibility == Visibility.Visible;
        if (showCombined == currentlyCombined
            && (!_chartLayoutTransition.CommitPending || _pendingShowCombined == showCombined))
        {
            return;
        }

        _pendingShowCombined = showCombined;
        _chartLayoutTransition.CommitPending = true;
        PlaySpatialTransition(
            _chartLayoutTransition,
            GlobalChartTransitionRoot,
            GlobalChartTransitionTransform,
            () =>
            {
                GlobalCombinedChartButton.IsChecked = showCombined;
                GlobalSplitChartsButton.IsChecked = !showCombined;
                GlobalCombinedChart.Visibility = showCombined
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                GlobalSplitCharts.Visibility = showCombined
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            },
            () => (GlobalChartTransitionRoot, GlobalChartTransitionTransform),
            disableHitTesting: true);
    }

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

        if (string.Equals(e.PropertyName, nameof(UsageReportViewModel.Trend), StringComparison.Ordinal)
            && !ViewModel.IsLoading
            && !_reportRefreshInProgress
            && !_reportDataCommitPending)
        {
            PlayIsolatedTrendEntry();
        }
    }

    private bool ShouldStartReportDataTransition(
        ReportDataTransitionIntent intent,
        bool changesState) => changesState
            || (_reportDataCommitPending && _pendingReportDataIntent == intent);

    private FrameworkElement[] GetVisibleReportDataTargets()
    {
        if (!ViewModel.HasData)
        {
            return [];
        }

        var targets = new List<FrameworkElement>
        {
            ReportSummaryTokensValue,
            ReportSummaryCostValue,
            ReportSummaryCoverageValue,
            ReportSummaryQualityProgress,
            ReportSummaryQualityValue,
            ReportCachedInputValue,
            ReportUncachedInputValue,
            ReportOutputTokensValue,
            GetBreakdownTransitionTarget(ViewModel.Breakdown).Rows,
        };
        if (ViewModel.IsGlobalScope)
        {
            targets.Add(ReportCompositionLegendRoot);
            targets.Add(ReportCompositionBar);
            targets.Add(GlobalChartTransitionRoot);
        }
        else
        {
            targets.Add(ProviderChartContentRoot);
        }

        if (ViewModel.HasProviderLimits)
        {
            targets.Add(ReportProviderLimitsContentRoot);
        }

        return targets
            .Where(target => target.Visibility == Visibility.Visible)
            .Distinct()
            .ToArray();
    }

    private FrameworkElement[] GetVisibleReportChartTargets() =>
        !ViewModel.HasData
            ? []
            : ViewModel.IsGlobalScope
                ? [GlobalChartTransitionRoot]
                : [ProviderChartContentRoot];

    private FrameworkElement[] GetAllReportDataTargets() =>
        [
            ReportSummaryTokensValue,
            ReportSummaryCostValue,
            ReportSummaryCoverageValue,
            ReportSummaryQualityProgress,
            ReportSummaryQualityValue,
            ReportCompositionLegendRoot,
            ReportCompositionBar,
            GlobalChartTransitionRoot,
            ProviderChartContentRoot,
            ReportCachedInputValue,
            ReportUncachedInputValue,
            ReportOutputTokensValue,
            ReportProviderLimitsContentRoot,
            ModelBreakdownRows,
            SourceBreakdownRows,
            DayBreakdownRows,
        ];

    private void PlayReportDataTransition(
        Action commit,
        ReportDataTransitionIntent intent)
    {
        CommitPendingReportDataIntentBeforeReplacement(intent);
        int transitionToken = ++_reportDataTransitionToken;
        _pendingReportDataIntent = intent;
        _pendingReportDataCommit = commit;
        _reportDataCommitPending = true;
        if (!MotionSettings.AreAnimationsEnabled())
        {
            StopReportDataTransition(resetTargets: true);
            CompleteReportDataCommit(commit);
            ResetReportDataTargets(GetAllReportDataTargets());
            return;
        }

        FrameworkElement[] targets = NormalizeTargets(GetVisibleReportDataTargets());
        StartReportOpacityTransition(
            targets,
            transitionToken,
            MotionSettings.ReportSwitchMinimumOpacity,
            MotionSettings.ReportSwitchExitDuration,
            startAtMinimum: false,
            resetOnComplete: false,
            completed: () =>
        {
            CompleteReportDataCommit(commit);
            ReportDataContent.UpdateLayout();
            FrameworkElement[] entryTargets = NormalizeTargets(GetVisibleReportDataTargets());
            ResetReportDataTargets(targets.Except(entryTargets));
            if (ViewModel.IsLoading)
            {
                HoldReportDataTargets(entryTargets);
                return;
            }

            StartReportOpacityTransition(
                entryTargets,
                transitionToken,
                1,
                MotionSettings.ReportSwitchDuration,
                startAtMinimum: true,
                resetOnComplete: true);
        });
    }

    private void StartReportOpacityTransition(
        FrameworkElement[] targets,
        int transitionToken,
        double toOpacity,
        TimeSpan duration,
        bool startAtMinimum,
        bool resetOnComplete,
        Action? completed = null)
    {
        FrameworkElement[] previousTargets = _reportDataTransitionTargets;
        var previousTargetSet = previousTargets.ToHashSet();
        Dictionary<FrameworkElement, double> currentOpacities = PrepareReportDataTargets(targets);
        if (targets.Length == 0)
        {
            completed?.Invoke();
            return;
        }

        var storyboard = new Storyboard();
        foreach (FrameworkElement target in targets)
        {
            double fromOpacity = startAtMinimum
                || (toOpacity == 1 && !previousTargetSet.Contains(target))
                ? MotionSettings.ReportSwitchMinimumOpacity
                : currentOpacities[target];
            target.Opacity = fromOpacity;
            SetReportTargetHitTesting(target, isEnabled: false);
            var opacity = new DoubleAnimation
            {
                From = fromOpacity,
                To = toOpacity,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(opacity, target);
            Storyboard.SetTargetProperty(opacity, nameof(Opacity));
            storyboard.Children.Add(opacity);
        }

        storyboard.Completed += (_, _) =>
        {
            if (transitionToken != _reportDataTransitionToken
                || !ReferenceEquals(_reportDataTransitionStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            _reportDataTransitionStoryboard = null;
            foreach (FrameworkElement target in targets)
            {
                target.Opacity = toOpacity;
            }
            if (resetOnComplete)
            {
                ResetReportDataTargets(targets);
                _reportDataTransitionTargets = [];
            }
            completed?.Invoke();
        };
        _reportDataTransitionStoryboard = storyboard;
        storyboard.Begin();
    }

    private void CompleteReportDataCommit(Action commit)
    {
        ExecuteTransitionCommit(commit);
        _pendingReportDataIntent = null;
        _pendingReportDataCommit = null;
        _reportDataCommitPending = false;
        EnsureReportRefreshCycleForCurrentLoad();
    }

    private void CommitPendingReportDataIntentBeforeReplacement(
        ReportDataTransitionIntent nextIntent)
    {
        if (!_reportDataCommitPending
            || _pendingReportDataIntent == nextIntent
            || _pendingReportDataCommit is not Action pendingCommit)
        {
            return;
        }

        _reportDataTransitionToken++;
        _pendingReportDataIntent = null;
        _pendingReportDataCommit = null;
        _reportDataCommitPending = false;
        ExecuteTransitionCommit(pendingCommit);
        EnsureReportRefreshCycleForCurrentLoad();
    }

    private Dictionary<FrameworkElement, double> PrepareReportDataTargets(
        FrameworkElement[] nextTargets)
    {
        FrameworkElement[] previousTargets = _reportDataTransitionTargets;
        FrameworkElement[] allTargets = previousTargets
            .Concat(nextTargets)
            .Distinct()
            .ToArray();
        Dictionary<FrameworkElement, double> currentOpacities = allTargets
            .ToDictionary(target => target, target => Math.Clamp(target.Opacity, 0, 1));

        _reportDataTransitionStoryboard?.Stop();
        _reportDataTransitionStoryboard = null;
        foreach ((FrameworkElement target, double opacity) in currentOpacities)
        {
            target.Opacity = opacity;
        }

        ResetReportDataTargets(previousTargets.Except(nextTargets));
        _reportDataTransitionTargets = nextTargets;
        return currentOpacities;
    }

    private static FrameworkElement[] NormalizeTargets(
        IEnumerable<FrameworkElement> targets) => targets
            .Where(target => target.Visibility == Visibility.Visible)
            .Distinct()
            .ToArray();

    private void HoldReportDataTargets(FrameworkElement[] targets)
    {
        PrepareReportDataTargets(targets);
        foreach (FrameworkElement target in targets)
        {
            target.Opacity = MotionSettings.ReportSwitchMinimumOpacity;
            SetReportTargetHitTesting(target, isEnabled: false);
        }
    }

    private void BeginReportRefreshCycle()
    {
        if (_reportRefreshLoading)
        {
            return;
        }

        if (_reportDataCommitPending && _pendingReportDataCommit is Action pendingCommit)
        {
            _reportDataTransitionToken++;
            _pendingReportDataIntent = null;
            _pendingReportDataCommit = null;
            _reportDataCommitPending = false;
            ExecuteTransitionCommit(pendingCommit);
        }

        _reportRefreshGeneration++;
        _reportRefreshInProgress = true;
        _reportRefreshLoading = true;
        if (!MotionSettings.AreAnimationsEnabled())
        {
            StopReportDataTransition(resetTargets: true);
            return;
        }

        int transitionToken = ++_reportDataTransitionToken;
        FrameworkElement[] targets = NormalizeTargets(GetVisibleReportDataTargets());
        StartReportOpacityTransition(
            targets,
            transitionToken,
            MotionSettings.ReportSwitchMinimumOpacity,
            MotionSettings.ReportSwitchExitDuration,
            startAtMinimum: false,
            resetOnComplete: false);
    }

    private void EnsureReportRefreshCycleForCurrentLoad()
    {
        if (!ViewModel.IsLoading || _reportRefreshLoading)
        {
            return;
        }

        _reportRefreshGeneration++;
        _reportRefreshInProgress = true;
        _reportRefreshLoading = true;
    }

    private void CompleteReportRefreshCycle()
    {
        if (!_reportRefreshInProgress)
        {
            return;
        }

        int refreshGeneration = _reportRefreshGeneration;
        _reportRefreshLoading = false;
        if (refreshGeneration <= _lastCompletedRefreshGeneration)
        {
            return;
        }

        if (!ViewModel.HasData || !MotionSettings.AreAnimationsEnabled())
        {
            StopReportDataTransition(resetTargets: true);
            _lastCompletedRefreshGeneration = refreshGeneration;
            _reportRefreshInProgress = false;
            return;
        }

        int transitionToken = ++_reportDataTransitionToken;
        ReportDataContent.UpdateLayout();
        StartReportOpacityTransition(
            NormalizeTargets(GetVisibleReportDataTargets()),
            transitionToken,
            1,
            MotionSettings.ReportSwitchDuration,
            startAtMinimum: false,
            resetOnComplete: true,
            completed: () =>
            {
                if (refreshGeneration != _reportRefreshGeneration)
                {
                    return;
                }

                _lastCompletedRefreshGeneration = refreshGeneration;
                _reportRefreshInProgress = false;
            });
    }

    private void PlayIsolatedTrendEntry()
    {
        if (!MotionSettings.AreAnimationsEnabled())
        {
            ResetReportDataTargets(GetVisibleReportChartTargets());
            return;
        }

        int transitionToken = ++_reportDataTransitionToken;
        StartReportOpacityTransition(
            NormalizeTargets(GetVisibleReportChartTargets()),
            transitionToken,
            1,
            MotionSettings.ReportSwitchDuration,
            startAtMinimum: true,
            resetOnComplete: true);
    }

    private void StopReportDataTransition(bool resetTargets)
    {
        FrameworkElement[] targets = _reportDataTransitionTargets;
        Dictionary<FrameworkElement, double> currentOpacities = targets
            .ToDictionary(target => target, target => Math.Clamp(target.Opacity, 0, 1));
        _reportDataTransitionStoryboard?.Stop();
        _reportDataTransitionStoryboard = null;
        foreach ((FrameworkElement target, double opacity) in currentOpacities)
        {
            target.Opacity = opacity;
        }

        if (resetTargets)
        {
            ResetReportDataTargets(targets);
            _reportDataTransitionTargets = [];
        }
    }

    private void ResetReportDataTargets(IEnumerable<FrameworkElement> targets)
    {
        foreach (FrameworkElement target in targets.Distinct())
        {
            target.Opacity = 1;
            SetReportTargetHitTesting(target, isEnabled: true);
        }
    }

    private void SetReportTargetHitTesting(FrameworkElement target, bool isEnabled)
    {
        if (ReferenceEquals(target, GlobalChartTransitionRoot)
            || ReferenceEquals(target, ProviderChartContentRoot))
        {
            target.IsHitTestVisible = isEnabled;
        }
    }

    private void PlaySpatialTransition(
        SpatialTransitionState state,
        FrameworkElement element,
        CompositeTransform transform,
        Action commit,
        Func<(FrameworkElement Rows, CompositeTransform Transform)> entryTargetFactory,
        bool disableHitTesting)
    {
        int transitionToken = ++state.Token;
        double currentOpacity = Math.Clamp(element.Opacity, 0, 1);
        double currentOffset = transform.TranslateX;
        state.Storyboard?.Stop();
        state.Storyboard = null;
        element.Opacity = currentOpacity;
        transform.TranslateX = currentOffset;
        if (!MotionSettings.AreAnimationsEnabled())
        {
            ExecuteTransitionCommit(commit);
            state.CommitPending = false;
            (FrameworkElement destination, CompositeTransform destinationTransform) =
                entryTargetFactory();
            ResetSpatialTarget(element, transform);
            ResetSpatialTarget(destination, destinationTransform);
            return;
        }

        StartSpatialPhase(
            state,
            element,
            transform,
            transitionToken,
            MotionSettings.ReportSwitchMinimumOpacity,
            -MotionSettings.ReportSwitchOffset,
            MotionSettings.ReportSwitchExitDuration,
            disableHitTesting,
            () =>
            {
                ExecuteTransitionCommit(commit);
                state.CommitPending = false;
                ReportDataContent.UpdateLayout();
                (FrameworkElement destination, CompositeTransform destinationTransform) =
                    entryTargetFactory();
                if (!ReferenceEquals(element, destination))
                {
                    ResetSpatialTarget(element, transform);
                }

                destination.Opacity = MotionSettings.ReportSwitchMinimumOpacity;
                destinationTransform.TranslateX = MotionSettings.ReportSwitchOffset;
                StartSpatialPhase(
                    state,
                    destination,
                    destinationTransform,
                    transitionToken,
                    1,
                    0,
                    MotionSettings.ReportSwitchDuration,
                    disableHitTesting,
                    () =>
                        ResetSpatialTarget(destination, destinationTransform));
            });
    }

    private static void StartSpatialPhase(
        SpatialTransitionState state,
        FrameworkElement element,
        CompositeTransform transform,
        int transitionToken,
        double toOpacity,
        double toOffset,
        TimeSpan duration,
        bool disableHitTesting,
        Action completed)
    {
        if (transitionToken != state.Token)
        {
            return;
        }

        if (disableHitTesting)
        {
            element.IsHitTestVisible = false;
        }

        var opacity = new DoubleAnimation
        {
            From = Math.Clamp(element.Opacity, 0, 1),
            To = toOpacity,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var translation = new DoubleAnimation
        {
            From = transform.TranslateX,
            To = toOffset,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacity, element);
        Storyboard.SetTargetProperty(opacity, nameof(Opacity));
        Storyboard.SetTarget(translation, transform);
        Storyboard.SetTargetProperty(translation, nameof(CompositeTransform.TranslateX));

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translation);
        storyboard.Completed += (_, _) =>
        {
            if (transitionToken != state.Token
                || !ReferenceEquals(state.Storyboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            state.Storyboard = null;
            element.Opacity = toOpacity;
            transform.TranslateX = toOffset;
            completed();
        };
        state.Storyboard = storyboard;
        storyboard.Begin();
    }

    private static void ResetSpatialTarget(
        FrameworkElement element,
        CompositeTransform transform)
    {
        element.Opacity = 1;
        element.IsHitTestVisible = true;
        transform.TranslateX = 0;
        transform.TranslateY = 0;
    }

    private static void CancelSpatialTransition(SpatialTransitionState state)
    {
        state.Token++;
        state.Storyboard?.Stop();
        state.Storyboard = null;
        state.CommitPending = false;
    }

    private void ExecuteTransitionCommit(Action commit)
    {
        _isTransitionCommit = true;
        try
        {
            commit();
        }
        finally
        {
            _isTransitionCommit = false;
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
