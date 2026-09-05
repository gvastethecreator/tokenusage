using System.Linq;
using TokenUsage.Core.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TokenUsage.App.Controls;
using TokenUsage.App.ViewModels.Reports;

namespace TokenUsage.App.Views.Reports;

public sealed partial class UsageReportPage
{
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

    private void OnGlobalChartLayoutClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }
            && Enum.TryParse(tag, out ReportChartGrouping grouping))
        {
            ViewModel.SetChartAppearance(ViewModel.ChartStyle, grouping);
            ChartGroupingChanged?.Invoke(this, grouping);
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

        if (ViewModel.IsCompareScope)
        {
            return new FrameworkElement[]
                {
                    ReportCompareSummary,
                    ReportCompareChart,
                    ReportCompareRows,
                }
                .Where(target => target.Visibility == Visibility.Visible)
                .ToArray();
        }

        var targets = new List<FrameworkElement>
        {
            ReportSummaryTokensValue,
            ReportSummaryCostValue,
            ReportSummaryCoverageValue,
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
        else if (ViewModel.IsProviderScope)
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
                : ViewModel.IsProviderScope
                    ? [ProviderChartContentRoot]
                    : ViewModel.IsCompareScope
                        ? [ReportCompareChart]
                        : [];

    private FrameworkElement[] GetAllReportDataTargets() =>
        [
            ReportSummaryTokensValue,
            ReportSummaryCostValue,
            ReportSummaryCoverageValue,
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
            ReportCompareSummary,
            ReportCompareChart,
            ReportCompareRows,
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
}
