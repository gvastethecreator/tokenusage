using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TokenUsage.App.Controls;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.App.ViewModels.Surfaces;
using TokenUsage.Core.Appearance;

namespace TokenUsage.App.Views.Dashboard;

public sealed partial class CompactUsageDashboard : UserControl
{
    private const int ProviderTabMaximumPageSize = 5;
    private const double ProviderTabMinimumWidth = 92;
    private const double ProviderTabSpacing = 2;
    private const double ProviderTabNavigationWidth = 64;
    private const double ProviderTabViewportInset = 2;
    private readonly ObservableCollection<DashboardProviderSummary> _visibleProviderTabs = [];
    private DashboardSurfaceViewModel? _viewModel;
    private Storyboard? _providerTransitionStoryboard;
    private Storyboard? _providerTabsStoryboard;
    private Storyboard? _providerLimitsStoryboard;
    private Storyboard? _visualizationTransitionStoryboard;
    private int _providerTransitionToken;
    private int _providerTabsTransitionToken;
    private int _providerLimitsTransitionToken;
    private int _visualizationTransitionToken;
    private int _providerTabPageSize = ProviderTabMaximumPageSize;
    private int _providerTabStartIndex;
    private bool _isProviderLimitsHeightAnimating;
    private bool _suppressProviderLimitsPropertyTransition;
    private bool _suppressVisualizationPropertyTransition;

    public CompactUsageDashboard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event EventHandler<UsageReportRequestedEventArgs>? ReportRequested;

    public event EventHandler? OptionsRequested;

    public event EventHandler? LayoutAnimationProgressed;

    public object VisibleProviderTabs => _visibleProviderTabs;

    public DashboardSurfaceViewModel ViewModel
    {
        get => _viewModel ?? throw new InvalidOperationException("ViewModel is not assigned.");
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_viewModel, value))
            {
                return;
            }

            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = value;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SynchronizeProviderTabs();
            if (IsLoaded)
            {
                SynchronizeProviderLimitsImmediately();
            }
        }
    }

    public void ApplyAppearance(AppearanceSettings settings) =>
        RootStack.Spacing = settings.Density == AppDensityMode.Compact ? 8 : 10;

    public void ScheduleReveal()
    {
        _ = ViewModel.RevealToken;
    }

    public void CycleVisualizationWithTransition() =>
        SetVisualizationWithTransition(ViewModel.Visualization switch
        {
            DashboardVisualizationMode.List => DashboardVisualizationMode.Donut,
            DashboardVisualizationMode.Donut => DashboardVisualizationMode.Heatmap,
            _ => DashboardVisualizationMode.List,
        });

    private void OnProviderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string providerId })
        {
            SetProviderTabSelection(providerId);
            SelectProviderWithTransition(providerId);
        }
    }

    private void OnGlobalClick(object sender, RoutedEventArgs e) => ShowGlobalWithTransition();

    private void OnPreviousProviderTabClick(object sender, RoutedEventArgs e) =>
        NavigateProviderTab(-1);

    private void OnNextProviderTabClick(object sender, RoutedEventArgs e) =>
        NavigateProviderTab(1);

    private void OnVisualizationClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string value }
            && Enum.TryParse(value, ignoreCase: true, out DashboardVisualizationMode mode))
        {
            SetVisualizationWithTransition(mode);
        }
    }

    private void SetVisualizationWithTransition(DashboardVisualizationMode mode)
    {
        if (mode == ViewModel.Visualization)
        {
            return;
        }

        if (!IsLoaded
            || !ViewModel.IsGlobalScope
            || !MotionSettings.AreAnimationsEnabled())
        {
            CommitVisualization(mode);
            SynchronizeVisualizationImmediately();
            return;
        }

        PlayVisualizationTransition(mode);
    }

    private void PlayVisualizationTransition(DashboardVisualizationMode mode)
    {
        int transitionToken = ++_visualizationTransitionToken;
        _visualizationTransitionStoryboard?.Stop();
        _visualizationTransitionStoryboard = null;

        FrameworkElement target = GetVisualizationContent(mode);
        bool targetWasVisible = target.Visibility == Visibility.Visible;
        FrameworkElement? outgoing = GetDominantOutgoingVisualization(target);
        foreach (FrameworkElement content in GetVisualizationContents())
        {
            bool participates = ReferenceEquals(content, target)
                || ReferenceEquals(content, outgoing);
            content.Visibility = participates ? Visibility.Visible : Visibility.Collapsed;
            content.IsHitTestVisible = ReferenceEquals(content, target);
            if (!participates)
            {
                content.Opacity = 1;
            }
        }

        double startHeight = Math.Max(0, VisualizationTransitionHost.ActualHeight);
        double availableWidth = Math.Max(1, VisualizationTransitionHost.ActualWidth);
        target.Visibility = Visibility.Visible;
        target.Measure(new Windows.Foundation.Size(availableWidth, double.PositiveInfinity));
        double targetHeight = target.DesiredSize.Height;
        if (!double.IsFinite(targetHeight) || targetHeight <= 0)
        {
            CommitVisualization(mode);
            SynchronizeVisualizationImmediately();
            return;
        }

        if (!double.IsFinite(startHeight) || startHeight <= 0)
        {
            startHeight = targetHeight;
        }

        double targetStartOpacity = targetWasVisible
            ? Math.Clamp(target.Opacity, 0, 1)
            : 0;
        target.Opacity = targetStartOpacity;
        VisualizationTransitionHost.Height = startHeight;
        bool shouldShowActivity = mode != DashboardVisualizationMode.Heatmap;
        bool activityWasVisible = ActivitySummaryTransitionHost.Visibility == Visibility.Visible;
        bool activityVisibilityChanges = activityWasVisible != shouldShowActivity;
        double activityStartHeight = activityWasVisible
            ? Math.Max(0, ActivitySummaryTransitionHost.ActualHeight)
            : 0;
        double activityTargetHeight = 0;
        if (activityVisibilityChanges && shouldShowActivity)
        {
            ActivitySummaryTransitionHost.Visibility = Visibility.Visible;
            ActivitySummaryTransitionHost.Height = double.NaN;
            ActivitySummaryTransitionHost.Measure(new Windows.Foundation.Size(
                Math.Max(1, RootStack.ActualWidth),
                double.PositiveInfinity));
            activityTargetHeight = Math.Max(
                0,
                ActivitySummaryTransitionHost.DesiredSize.Height);
        }

        double activityStartOpacity = 0;
        if (activityVisibilityChanges)
        {
            activityStartOpacity = activityWasVisible
                ? Math.Clamp(ActivitySummaryTransitionHost.Opacity, 0, 1)
                : 0;
            ActivitySummaryTransitionHost.Height = activityStartHeight;
            ActivitySummaryTransitionHost.Opacity = activityStartOpacity;
            ActivitySummaryTransitionHost.IsHitTestVisible = shouldShowActivity;
        }

        CommitVisualization(mode);

        var storyboard = new Storyboard();
        if (outgoing is not null && !ReferenceEquals(outgoing, target))
        {
            AddVisualizationOpacityAnimation(
                storyboard,
                outgoing,
                Math.Clamp(outgoing.Opacity, 0, 1),
                0);
        }

        AddVisualizationOpacityAnimation(
            storyboard,
            target,
            targetStartOpacity,
            1);

        var height = new DoubleAnimation
        {
            From = startHeight,
            To = targetHeight,
            Duration = MotionSettings.VisualizationSwitchDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(height, VisualizationTransitionHost);
        Storyboard.SetTargetProperty(height, nameof(Height));
        storyboard.Children.Add(height);

        if (activityVisibilityChanges)
        {
            var activityHeight = new DoubleAnimation
            {
                From = activityStartHeight,
                To = activityTargetHeight,
                Duration = MotionSettings.VisualizationSwitchDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(activityHeight, ActivitySummaryTransitionHost);
            Storyboard.SetTargetProperty(activityHeight, nameof(Height));
            storyboard.Children.Add(activityHeight);

            var activityOpacity = new DoubleAnimation
            {
                From = activityStartOpacity,
                To = shouldShowActivity ? 1 : 0,
                Duration = MotionSettings.VisualizationSwitchDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(activityOpacity, ActivitySummaryTransitionHost);
            Storyboard.SetTargetProperty(activityOpacity, nameof(Opacity));
            storyboard.Children.Add(activityOpacity);
        }

        storyboard.Completed += (_, _) =>
        {
            if (transitionToken != _visualizationTransitionToken
                || !ReferenceEquals(_visualizationTransitionStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            _visualizationTransitionStoryboard = null;
            CompleteVisualizationTransition(mode);
        };
        _visualizationTransitionStoryboard = storyboard;
        storyboard.Begin();
    }

    private static void AddVisualizationOpacityAnimation(
        Storyboard storyboard,
        FrameworkElement target,
        double from,
        double to)
    {
        var opacity = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = MotionSettings.VisualizationSwitchDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacity, target);
        Storyboard.SetTargetProperty(opacity, nameof(Opacity));
        storyboard.Children.Add(opacity);
    }

    private FrameworkElement? GetDominantOutgoingVisualization(FrameworkElement target) =>
        GetVisualizationContents()
            .Where(content => content.Visibility == Visibility.Visible
                && !ReferenceEquals(content, target))
            .OrderByDescending(content => content.Opacity)
            .FirstOrDefault();

    private void CompleteVisualizationTransition(DashboardVisualizationMode mode)
    {
        FrameworkElement target = GetVisualizationContent(mode);
        foreach (FrameworkElement content in GetVisualizationContents())
        {
            bool isTarget = ReferenceEquals(content, target);
            content.Opacity = 1;
            content.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
            content.IsHitTestVisible = isTarget;
        }

        VisualizationTransitionHost.Height = double.NaN;
        bool shouldShowActivity = mode != DashboardVisualizationMode.Heatmap;
        ActivitySummaryTransitionHost.Height = shouldShowActivity ? double.NaN : 0;
        ActivitySummaryTransitionHost.Opacity = shouldShowActivity ? 1 : 0;
        ActivitySummaryTransitionHost.Visibility = shouldShowActivity
            ? Visibility.Visible
            : Visibility.Collapsed;
        ActivitySummaryTransitionHost.IsHitTestVisible = shouldShowActivity;
        UpdateVisualizationClip(
            VisualizationTransitionHost.ActualWidth,
            VisualizationTransitionHost.ActualHeight);
        UpdateActivitySummaryClip(
            ActivitySummaryTransitionHost.ActualWidth,
            ActivitySummaryTransitionHost.ActualHeight);
    }

    private void CommitVisualization(DashboardVisualizationMode mode)
    {
        _suppressVisualizationPropertyTransition = true;
        try
        {
            ViewModel.SetVisualization(mode);
        }
        finally
        {
            _suppressVisualizationPropertyTransition = false;
        }
    }

    private FrameworkElement GetVisualizationContent(DashboardVisualizationMode mode) => mode switch
    {
        DashboardVisualizationMode.List => ListVisualizationContent,
        DashboardVisualizationMode.Donut => DonutVisualizationContent,
        _ => HeatmapVisualizationContent,
    };

    private FrameworkElement[] GetVisualizationContents() =>
        [ListVisualizationContent, DonutVisualizationContent, HeatmapVisualizationContent];

    private void SynchronizeVisualizationImmediately()
    {
        _visualizationTransitionToken++;
        _visualizationTransitionStoryboard?.Stop();
        _visualizationTransitionStoryboard = null;
        CompleteVisualizationTransition(ViewModel.Visualization);
    }

    private void OnVisualizationTransitionHostSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateVisualizationClip(e.NewSize.Width, e.NewSize.Height);

    private void UpdateVisualizationClip(double width, double height) =>
        VisualizationTransitionClip.Rect = new Windows.Foundation.Rect(
            0,
            0,
            Math.Max(0, width),
            Math.Max(0, height));

    private void OnActivitySummaryTransitionHostSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateActivitySummaryClip(e.NewSize.Width, e.NewSize.Height);

    private void UpdateActivitySummaryClip(double width, double height) =>
        ActivitySummaryTransitionClip.Rect = new Windows.Foundation.Rect(
            0,
            0,
            Math.Max(0, width),
            Math.Max(0, height));

    private void OnProviderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.IsProviderScope
            && sender is ComboBox { SelectedItem: DashboardProviderOption option })
        {
            SelectProviderWithTransition(option.ProviderId);
        }
    }

    private void OnDonutProviderInvoked(object? sender, ProviderInvokedEventArgs e) =>
        SelectProviderWithTransition(e.ProviderId);

    private void SelectProviderWithTransition(string providerId)
    {
        int previousIndex = IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        int nextIndex = IndexOfProvider(providerId);
        int direction = nextIndex < previousIndex ? -1 : 1;
        EnsureProviderTabVisible(nextIndex, direction, animate: true);
        if (previousIndex == nextIndex && ViewModel.IsProviderScope)
        {
            SetProviderTabSelection(ViewModel.SelectedProvider?.ProviderId);
            return;
        }

        bool forceLimitsReveal = !ViewModel.IsProviderScope;
        if (!MotionSettings.AreAnimationsEnabled())
        {
            CancelProviderTransition();
            CommitProviderSelection(providerId, animateLimits: false, forceLimitsReveal);
            return;
        }

        Action commit = () => CommitProviderSelection(
            providerId,
            animateLimits: true,
            forceLimitsReveal);
        if (ViewModel.IsProviderScope)
        {
            PlayProviderContentTransition(commit);
            return;
        }

        PlayProviderTransition(
            ScopeTransitionRoot,
            ScopeTransitionTransform,
            commit,
            direction);
    }

    private void CommitProviderSelection(
        string providerId,
        bool animateLimits,
        bool forceLimitsReveal)
    {
        _suppressProviderLimitsPropertyTransition = true;
        try
        {
            ViewModel.SelectProvider(providerId);
        }
        finally
        {
            _suppressProviderLimitsPropertyTransition = false;
        }

        SetProviderTabSelection(ViewModel.SelectedProvider?.ProviderId);
        if (!animateLimits)
        {
            SynchronizeProviderLimitsImmediately();
            return;
        }

        bool shouldShow = ViewModel.SelectedProviderHasLimits;
        _ = DispatcherQueue.TryEnqueue(() =>
            PlayProviderLimitsTransition(shouldShow, forceLimitsReveal));
    }

    private void ShowGlobalWithTransition()
    {
        if (!ViewModel.IsProviderScope)
        {
            return;
        }

        StopProviderLimitsTransition();
        if (!MotionSettings.AreAnimationsEnabled())
        {
            CancelProviderTransition();
            ViewModel.ShowGlobal();
            return;
        }

        PlayProviderTransition(
            ScopeTransitionRoot,
            ScopeTransitionTransform,
            ViewModel.ShowGlobal,
            direction: -1);
    }

    private int IndexOfProvider(string? providerId)
    {
        if (providerId is null)
        {
            return -1;
        }

        for (int index = 0; index < ViewModel.ProviderSummaries.Count; index++)
        {
            if (string.Equals(
                ViewModel.ProviderSummaries[index].ProviderId,
                providerId,
                StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void SetProviderTabSelection(string? providerId)
    {
        int providerIndex = IndexOfVisibleProvider(providerId);
        if (providerIndex >= 0
            && ProviderTabsRepeater.TryGetElement(providerIndex) is RadioButton tab
            && tab.IsChecked != true)
        {
            tab.IsChecked = true;
        }
    }

    private void OnProviderTabPrepared(
        ItemsRepeater sender,
        ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is RadioButton tab
            && args.Index >= 0
            && args.Index < _visibleProviderTabs.Count)
        {
            tab.IsChecked = string.Equals(
                _visibleProviderTabs[args.Index].ProviderId,
                ViewModel.SelectedProvider?.ProviderId,
                StringComparison.Ordinal);
        }

    }

    private void NavigateProviderTab(int direction)
    {
        int selectedIndex = IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        if (selectedIndex < 0)
        {
            if (ViewModel.ProviderSummaries.Count > 0)
            {
                SelectProviderWithTransition(ViewModel.ProviderSummaries[0].ProviderId);
            }

            return;
        }

        int targetIndex = Math.Clamp(
            selectedIndex + direction,
            0,
            Math.Max(0, ViewModel.ProviderSummaries.Count - 1));
        if (targetIndex == selectedIndex || targetIndex >= ViewModel.ProviderSummaries.Count)
        {
            return;
        }

        SelectProviderWithTransition(ViewModel.ProviderSummaries[targetIndex].ProviderId);
    }

    private int IndexOfVisibleProvider(string? providerId)
    {
        if (providerId is null)
        {
            return -1;
        }

        for (int index = 0; index < _visibleProviderTabs.Count; index++)
        {
            if (string.Equals(
                _visibleProviderTabs[index].ProviderId,
                providerId,
                StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void SynchronizeProviderTabs()
    {
        if (_viewModel is null)
        {
            return;
        }

        _ = UpdateProviderTabPageSize();
        int selectedIndex = IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        int maxStart = Math.Max(0, ViewModel.ProviderSummaries.Count - _providerTabPageSize);
        _providerTabStartIndex = Math.Clamp(_providerTabStartIndex, 0, maxStart);
        if (selectedIndex >= 0)
        {
            if (selectedIndex < _providerTabStartIndex)
            {
                _providerTabStartIndex = selectedIndex;
            }
            else if (selectedIndex >= _providerTabStartIndex + _providerTabPageSize)
            {
                _providerTabStartIndex = selectedIndex - _providerTabPageSize + 1;
            }
        }

        ReplaceVisibleProviderTabs();
        UpdateProviderTabNavigationButtons();
        SetProviderTabSelection(ViewModel.SelectedProvider?.ProviderId);
    }

    private void EnsureProviderTabVisible(int providerIndex, int direction, bool animate)
    {
        if (providerIndex < 0 || providerIndex >= ViewModel.ProviderSummaries.Count)
        {
            UpdateProviderTabNavigationButtons();
            return;
        }

        int nextStart = _providerTabStartIndex;
        if (providerIndex < nextStart)
        {
            nextStart = providerIndex;
        }
        else if (providerIndex >= nextStart + _providerTabPageSize)
        {
            nextStart = providerIndex - _providerTabPageSize + 1;
        }

        int maxStart = Math.Max(0, ViewModel.ProviderSummaries.Count - _providerTabPageSize);
        nextStart = Math.Clamp(nextStart, 0, maxStart);
        if (nextStart == _providerTabStartIndex)
        {
            UpdateProviderTabNavigationButtons(providerIndex);
            return;
        }

        _providerTabStartIndex = nextStart;
        ReplaceVisibleProviderTabs();
        UpdateProviderTabNavigationButtons(providerIndex);
        SetProviderTabSelection(ViewModel.ProviderSummaries[providerIndex].ProviderId);
        if (animate && IsLoaded && MotionSettings.AreAnimationsEnabled())
        {
            PlayProviderTabsTransition(direction);
        }
        else
        {
            CancelProviderTabsTransition();
        }
    }

    private void ReplaceVisibleProviderTabs()
    {
        int end = Math.Min(
            ViewModel.ProviderSummaries.Count,
            _providerTabStartIndex + _providerTabPageSize);
        int count = end - _providerTabStartIndex;
        bool alreadySynchronized = _visibleProviderTabs.Count == count;
        for (int visibleIndex = 0; alreadySynchronized && visibleIndex < count; visibleIndex++)
        {
            alreadySynchronized = ReferenceEquals(
                _visibleProviderTabs[visibleIndex],
                ViewModel.ProviderSummaries[_providerTabStartIndex + visibleIndex]);
        }

        if (alreadySynchronized)
        {
            UpdateProviderTabLayout();
            return;
        }

        _visibleProviderTabs.Clear();
        for (int index = _providerTabStartIndex; index < end; index++)
        {
            _visibleProviderTabs.Add(ViewModel.ProviderSummaries[index]);
        }

        _ = DispatcherQueue.TryEnqueue(UpdateProviderTabLayout);

    }

    private void UpdateProviderTabNavigationButtons(int? selectedIndexOverride = null)
    {
        int providerCount = ViewModel.ProviderSummaries.Count;
        bool hasOverflow = providerCount > _providerTabPageSize;
        PreviousProviderTabButton.Visibility = hasOverflow
            ? Visibility.Visible
            : Visibility.Collapsed;
        NextProviderTabButton.Visibility = hasOverflow
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProviderTabCarousel.ColumnSpacing = hasOverflow ? 4 : 0;

        int selectedIndex = selectedIndexOverride
            ?? IndexOfProvider(ViewModel.SelectedProvider?.ProviderId);
        PreviousProviderTabButton.IsEnabled = hasOverflow && selectedIndex > 0;
        NextProviderTabButton.IsEnabled = hasOverflow
            && selectedIndex >= 0
            && selectedIndex < providerCount - 1;
    }

    private void OnProviderTabsViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ProviderTabsClip.Rect = new Windows.Foundation.Rect(
            0,
            0,
            Math.Max(0, e.NewSize.Width),
            Math.Max(0, e.NewSize.Height));
        if (UpdateProviderTabPageSize())
        {
            SynchronizeProviderTabs();
            return;
        }

        UpdateProviderTabLayout();
    }

    private bool UpdateProviderTabPageSize()
    {
        int providerCount = ViewModel.ProviderSummaries.Count;
        double carouselWidth = ProviderTabCarousel.ActualWidth;
        if (providerCount == 0 || carouselWidth <= 0)
        {
            return false;
        }

        int nextPageSize = Math.Min(ProviderTabMaximumPageSize, providerCount);
        for (int candidate = nextPageSize; candidate >= 1; candidate--)
        {
            bool needsNavigation = providerCount > candidate;
            double availableWidth = carouselWidth
                - (needsNavigation ? ProviderTabNavigationWidth : 0);
            double itemWidth = (
                availableWidth - (candidate - 1) * ProviderTabSpacing)
                / candidate;
            if (itemWidth >= ProviderTabMinimumWidth || candidate == 1)
            {
                nextPageSize = candidate;
                break;
            }
        }

        if (nextPageSize == _providerTabPageSize)
        {
            return false;
        }

        _providerTabPageSize = nextPageSize;
        return true;
    }

    private void UpdateProviderTabLayout()
    {
        if (_visibleProviderTabs.Count == 0 || ProviderTabsViewport.ActualWidth <= 0)
        {
            return;
        }

        const double spacing = ProviderTabSpacing;
        double availableWidth = Math.Max(
            0,
            ProviderTabsViewport.ActualWidth - ProviderTabViewportInset * 2);
        ProviderTabsLayout.MinItemWidth = Math.Max(
            64,
            (availableWidth
                - (_visibleProviderTabs.Count - 1) * spacing)
            / _visibleProviderTabs.Count);
    }

    private void PlayProviderTabsTransition(int direction)
    {
        int transitionToken = ++_providerTabsTransitionToken;
        _providerTabsStoryboard?.Stop();
        _providerTabsStoryboard = null;
        ProviderTabsTransitionRoot.Opacity = MotionSettings.ProviderCarouselMinimumOpacity;
        ProviderTabsTransitionTransform.TranslateX = MotionSettings.ProviderCarouselOffset * direction;

        var opacity = new DoubleAnimation
        {
            From = ProviderTabsTransitionRoot.Opacity,
            To = 1,
            Duration = MotionSettings.ProviderCarouselDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var translation = new DoubleAnimation
        {
            From = ProviderTabsTransitionTransform.TranslateX,
            To = 0,
            Duration = MotionSettings.ProviderCarouselDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacity, ProviderTabsTransitionRoot);
        Storyboard.SetTargetProperty(opacity, nameof(Opacity));
        Storyboard.SetTarget(translation, ProviderTabsTransitionTransform);
        Storyboard.SetTargetProperty(translation, nameof(CompositeTransform.TranslateX));

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translation);
        storyboard.Completed += (_, _) =>
        {
            if (transitionToken != _providerTabsTransitionToken
                || !ReferenceEquals(_providerTabsStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            _providerTabsStoryboard = null;
            ProviderTabsTransitionRoot.Opacity = 1;
            ProviderTabsTransitionTransform.TranslateX = 0;
        };
        _providerTabsStoryboard = storyboard;
        storyboard.Begin();
    }

    private void CancelProviderTabsTransition()
    {
        _providerTabsTransitionToken++;
        _providerTabsStoryboard?.Stop();
        _providerTabsStoryboard = null;
        ProviderTabsTransitionRoot.Opacity = 1;
        ProviderTabsTransitionTransform.TranslateX = 0;
    }

    private void PlayProviderContentTransition(Action commit)
    {
        int transitionToken = ++_providerTransitionToken;
        _providerTransitionStoryboard?.Stop();
        _providerTransitionStoryboard = null;
        FrameworkElement[] targets = GetProviderContentTransitionTargets();
        var storyboard = new Storyboard();
        foreach (FrameworkElement target in targets)
        {
            double currentOpacity = Math.Clamp(target.Opacity, 0, 1);
            target.Opacity = currentOpacity;
            target.IsHitTestVisible = false;
            var opacity = new DoubleAnimation
            {
                From = currentOpacity,
                To = 0,
                Duration = MotionSettings.ProviderSwitchExitDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(opacity, target);
            Storyboard.SetTargetProperty(opacity, nameof(Opacity));
            storyboard.Children.Add(opacity);
        }

        storyboard.Completed += (_, _) =>
        {
            if (transitionToken != _providerTransitionToken
                || !ReferenceEquals(_providerTransitionStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            _providerTransitionStoryboard = null;
            commit();
            foreach (FrameworkElement target in targets)
            {
                target.Opacity = 0;
            }

            _ = DispatcherQueue.TryEnqueue(() =>
                PlayProviderContentTransitionEntry(targets, transitionToken));
        };
        _providerTransitionStoryboard = storyboard;
        storyboard.Begin();
    }

    private void PlayProviderContentTransitionEntry(
        FrameworkElement[] targets,
        int transitionToken)
    {
        if (transitionToken != _providerTransitionToken)
        {
            return;
        }

        var storyboard = new Storyboard();
        for (int index = 0; index < targets.Length; index++)
        {
            FrameworkElement target = targets[index];
            var opacity = new DoubleAnimation
            {
                From = Math.Clamp(target.Opacity, 0, 1),
                To = 1,
                BeginTime = TimeSpan.FromMilliseconds(index * 20),
                Duration = MotionSettings.ProviderSwitchDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(opacity, target);
            Storyboard.SetTargetProperty(opacity, nameof(Opacity));
            storyboard.Children.Add(opacity);
        }

        storyboard.Completed += (_, _) =>
        {
            if (transitionToken != _providerTransitionToken
                || !ReferenceEquals(_providerTransitionStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            foreach (FrameworkElement target in targets)
            {
                target.Opacity = 1;
                target.IsHitTestVisible = true;
            }

            _providerTransitionStoryboard = null;
        };
        _providerTransitionStoryboard = storyboard;
        storyboard.Begin();
    }

    private FrameworkElement[] GetProviderContentTransitionTargets() =>
        [ProviderIdentityContent, ProviderMetricsContent, ProviderTrendContent];

    private void PlayProviderTransition(
        FrameworkElement transitionElement,
        CompositeTransform transitionTransform,
        Action commit,
        int direction)
    {
        int transitionToken = ++_providerTransitionToken;
        double currentOpacity = Math.Clamp(transitionElement.Opacity, 0, 1);
        double currentOffset = transitionTransform.TranslateX;
        _providerTransitionStoryboard?.Stop();
        _providerTransitionStoryboard = null;
        transitionElement.Opacity = currentOpacity;
        transitionElement.IsHitTestVisible = false;
        transitionTransform.TranslateX = currentOffset;
        var opacity = new DoubleAnimation
        {
            From = currentOpacity,
            To = MotionSettings.ProviderSwitchMinimumOpacity,
            Duration = MotionSettings.ProviderSwitchExitDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = false,
        };
        var translation = new DoubleAnimation
        {
            From = currentOffset,
            To = -8 * direction,
            Duration = MotionSettings.ProviderSwitchExitDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacity, transitionElement);
        Storyboard.SetTargetProperty(opacity, nameof(Opacity));
        Storyboard.SetTarget(translation, transitionTransform);
        Storyboard.SetTargetProperty(translation, nameof(CompositeTransform.TranslateX));

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translation);
        storyboard.Completed += (_, _) =>
        {
            if (transitionToken != _providerTransitionToken
                || !ReferenceEquals(_providerTransitionStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            _providerTransitionStoryboard = null;
            commit();
            transitionElement.Opacity = MotionSettings.ProviderSwitchMinimumOpacity;
            transitionTransform.TranslateX = 10 * direction;
            _ = DispatcherQueue.TryEnqueue(() => PlayProviderTransitionEntry(
                transitionElement,
                transitionTransform,
                transitionToken));
        };
        _providerTransitionStoryboard = storyboard;
        storyboard.Begin();
    }

    private void PlayProviderTransitionEntry(
        FrameworkElement transitionElement,
        CompositeTransform transitionTransform,
        int transitionToken)
    {
        if (transitionToken != _providerTransitionToken)
        {
            return;
        }

        var opacity = new DoubleAnimation
        {
            From = MotionSettings.ProviderSwitchMinimumOpacity,
            To = 1,
            Duration = MotionSettings.ProviderSwitchDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var translation = new DoubleAnimation
        {
            From = transitionTransform.TranslateX,
            To = 0,
            Duration = MotionSettings.ProviderSwitchDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacity, transitionElement);
        Storyboard.SetTargetProperty(opacity, nameof(Opacity));
        Storyboard.SetTarget(translation, transitionTransform);
        Storyboard.SetTargetProperty(translation, nameof(CompositeTransform.TranslateX));

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translation);
        storyboard.Completed += (_, _) =>
        {
            if (transitionToken != _providerTransitionToken
                || !ReferenceEquals(_providerTransitionStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            transitionElement.Opacity = 1;
            transitionElement.IsHitTestVisible = true;
            transitionTransform.TranslateX = 0;
            _providerTransitionStoryboard = null;
        };
        _providerTransitionStoryboard = storyboard;
        storyboard.Begin();
    }

    private void CancelProviderTransition()
    {
        _providerTransitionToken++;
        _providerTransitionStoryboard?.Stop();
        _providerTransitionStoryboard = null;
        StopProviderLimitsTransition();
        ScopeTransitionRoot.Opacity = 1;
        ScopeTransitionRoot.IsHitTestVisible = true;
        ScopeTransitionTransform.TranslateX = 0;
        foreach (FrameworkElement target in GetProviderContentTransitionTargets())
        {
            target.Opacity = 1;
            target.IsHitTestVisible = true;
        }
    }

    private void PlayProviderLimitsTransition(bool shouldShow, bool forceReveal)
    {
        StopProviderLimitsTransition();
        if (!MotionSettings.AreAnimationsEnabled())
        {
            SynchronizeProviderLimitsImmediately();
            return;
        }

        if (shouldShow)
        {
            PlayProviderLimitsReveal(forceReveal);
            return;
        }

        PlayProviderLimitsCollapse();
    }

    private void PlayProviderLimitsReveal(bool forceReveal)
    {
        bool wasVisible = ProviderLimitsRevealHost.Visibility == Visibility.Visible
            && ProviderLimitsRevealHost.ActualHeight > 0;
        if (wasVisible && !forceReveal && double.IsNaN(ProviderLimitsRevealHost.Height))
        {
            ProviderLimitsRevealHost.Opacity = 1;
            ProviderLimitsRevealHost.IsHitTestVisible = true;
            return;
        }

        ProviderLimitsRevealHost.Visibility = Visibility.Visible;
        ProviderLimitsRevealHost.Height = double.NaN;
        double availableWidth = Math.Max(1, ProviderDetailStack.ActualWidth);
        ProviderLimitsRevealHost.Measure(new Windows.Foundation.Size(
            availableWidth,
            double.PositiveInfinity));
        double targetHeight = ProviderLimitsRevealHost.DesiredSize.Height;
        if (!double.IsFinite(targetHeight) || targetHeight <= 0)
        {
            SynchronizeProviderLimitsImmediately();
            return;
        }

        double startHeight = forceReveal || !wasVisible
            ? 0
            : Math.Min(ProviderLimitsRevealHost.ActualHeight, targetHeight);
        double startOpacity = forceReveal || !wasVisible
            ? 0
            : Math.Clamp(ProviderLimitsRevealHost.Opacity, 0, 1);
        ProviderLimitsRevealHost.Height = startHeight;
        ProviderLimitsRevealHost.Opacity = startOpacity;
        ProviderLimitsRevealHost.IsHitTestVisible = false;
        StartProviderLimitsStoryboard(
            startHeight,
            targetHeight,
            startOpacity,
            1,
            expanding: true);
    }

    private void PlayProviderLimitsCollapse()
    {
        if (ProviderLimitsRevealHost.Visibility != Visibility.Visible)
        {
            SynchronizeProviderLimitsImmediately();
            return;
        }

        double startHeight = ProviderLimitsRevealHost.ActualHeight;
        if (!double.IsFinite(startHeight) || startHeight <= 0)
        {
            SynchronizeProviderLimitsImmediately();
            return;
        }

        ProviderLimitsRevealHost.Height = startHeight;
        ProviderLimitsRevealHost.IsHitTestVisible = false;
        StartProviderLimitsStoryboard(
            startHeight,
            0,
            Math.Clamp(ProviderLimitsRevealHost.Opacity, 0, 1),
            0,
            expanding: false);
    }

    private void StartProviderLimitsStoryboard(
        double fromHeight,
        double toHeight,
        double fromOpacity,
        double toOpacity,
        bool expanding)
    {
        int transitionToken = ++_providerLimitsTransitionToken;
        _isProviderLimitsHeightAnimating = true;
        var height = new DoubleAnimation
        {
            From = fromHeight,
            To = toHeight,
            Duration = MotionSettings.ProviderLimitsRevealDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            EnableDependentAnimation = true,
        };
        var opacity = new DoubleAnimation
        {
            From = fromOpacity,
            To = toOpacity,
            Duration = MotionSettings.ProviderLimitsFadeDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(height, ProviderLimitsRevealHost);
        Storyboard.SetTargetProperty(height, nameof(Height));
        Storyboard.SetTarget(opacity, ProviderLimitsRevealHost);
        Storyboard.SetTargetProperty(opacity, nameof(Opacity));

        var storyboard = new Storyboard();
        storyboard.Children.Add(height);
        storyboard.Children.Add(opacity);
        storyboard.Completed += (_, _) =>
        {
            if (transitionToken != _providerLimitsTransitionToken
                || !ReferenceEquals(_providerLimitsStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            _providerLimitsStoryboard = null;
            _isProviderLimitsHeightAnimating = false;
            ProviderLimitsRevealHost.Height = expanding ? double.NaN : 0;
            ProviderLimitsRevealHost.Opacity = expanding ? 1 : 0;
            ProviderLimitsRevealHost.Visibility = expanding
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProviderLimitsRevealHost.IsHitTestVisible = expanding;
            UpdateProviderLimitsClip(
                ProviderLimitsRevealHost.ActualWidth,
                ProviderLimitsRevealHost.ActualHeight);
            LayoutAnimationProgressed?.Invoke(this, EventArgs.Empty);
        };
        _providerLimitsStoryboard = storyboard;
        storyboard.Begin();
    }

    private void StopProviderLimitsTransition()
    {
        _providerLimitsTransitionToken++;
        _providerLimitsStoryboard?.Stop();
        _providerLimitsStoryboard = null;
        _isProviderLimitsHeightAnimating = false;
    }

    private void SynchronizeProviderLimitsImmediately()
    {
        StopProviderLimitsTransition();
        bool shouldShow = _viewModel?.SelectedProviderHasLimits == true;
        ProviderLimitsRevealHost.Height = shouldShow ? double.NaN : 0;
        ProviderLimitsRevealHost.Opacity = shouldShow ? 1 : 0;
        ProviderLimitsRevealHost.Visibility = shouldShow
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProviderLimitsRevealHost.IsHitTestVisible = shouldShow;
        UpdateProviderLimitsClip(
            ProviderLimitsRevealHost.ActualWidth,
            ProviderLimitsRevealHost.ActualHeight);
    }

    private void OnProviderLimitsRevealHostSizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        UpdateProviderLimitsClip(e.NewSize.Width, e.NewSize.Height);
        if (_isProviderLimitsHeightAnimating)
        {
            LayoutAnimationProgressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateProviderLimitsClip(double width, double height) =>
        ProviderLimitsClip.Rect = new Windows.Foundation.Rect(
            0,
            0,
            Math.Max(0, width),
            Math.Max(0, height));

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SynchronizeProviderTabs();
        SynchronizeProviderLimitsImmediately();
        SynchronizeVisualizationImmediately();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _providerTransitionStoryboard?.Stop();
        _providerTransitionStoryboard = null;
        CancelProviderTabsTransition();
        StopProviderLimitsTransition();
        _visualizationTransitionToken++;
        _visualizationTransitionStoryboard?.Stop();
        _visualizationTransitionStoryboard = null;
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.Equals(
                e.PropertyName,
                nameof(DashboardSurfaceViewModel.Visualization),
                StringComparison.Ordinal))
        {
            if (!_suppressVisualizationPropertyTransition)
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (_viewModel is not null && IsLoaded)
                    {
                        SynchronizeVisualizationImmediately();
                    }
                });
            }

            return;
        }

        if (string.Equals(
                e.PropertyName,
                nameof(DashboardSurfaceViewModel.ProviderSummaries),
                StringComparison.Ordinal))
        {
            _ = DispatcherQueue.TryEnqueue(SynchronizeProviderTabs);
            return;
        }

        if (string.Equals(
                e.PropertyName,
                nameof(DashboardSurfaceViewModel.SelectedProvider),
                StringComparison.Ordinal))
        {
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (_viewModel is null)
                {
                    return;
                }

                SynchronizeProviderTabs();
            });
            return;
        }

        if (_suppressProviderLimitsPropertyTransition
            || !string.Equals(
                e.PropertyName,
                nameof(DashboardSurfaceViewModel.SelectedProviderLimits),
                StringComparison.Ordinal))
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_viewModel is null || !IsLoaded)
            {
                return;
            }

            if (!ViewModel.IsProviderScope)
            {
                SynchronizeProviderLimitsImmediately();
                return;
            }

            PlayProviderLimitsTransition(
                ViewModel.SelectedProviderHasLimits,
                forceReveal: false);
        });
    }

    private void OnHeatmapDayInvoked(object? sender, UsageHeatmapDayInvokedEventArgs e) =>
        ReportRequested?.Invoke(
            this,
            new UsageReportRequestedEventArgs(ViewModel.CreateReportRequest(e.Cell.Date)));

    private void OnOptionsClick(object sender, RoutedEventArgs e) =>
        OptionsRequested?.Invoke(this, EventArgs.Empty);
}
