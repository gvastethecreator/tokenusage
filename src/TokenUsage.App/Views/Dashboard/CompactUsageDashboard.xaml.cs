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
    private DashboardSurfaceViewModel? _viewModel;
    private Storyboard? _providerTransitionStoryboard;
    private Storyboard? _providerLimitsStoryboard;
    private int _providerTransitionToken;
    private int _providerLimitsTransitionToken;
    private bool _isProviderLimitsHeightAnimating;
    private bool _suppressProviderLimitsPropertyTransition;

    public CompactUsageDashboard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event EventHandler<UsageReportRequestedEventArgs>? ReportRequested;

    public event EventHandler? OptionsRequested;

    public event EventHandler? LayoutAnimationProgressed;

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

    private void OnProviderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string providerId })
        {
            SetProviderTabSelection(providerId);
            SelectProviderWithTransition(providerId);
        }
    }

    private void OnGlobalClick(object sender, RoutedEventArgs e) => ShowGlobalWithTransition();

    private void OnVisualizationClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string value }
            && Enum.TryParse(value, ignoreCase: true, out DashboardVisualizationMode mode))
        {
            ViewModel.SetVisualization(mode);
        }
    }

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
        if (previousIndex == nextIndex && ViewModel.IsProviderScope)
        {
            SetProviderTabSelection(ViewModel.SelectedProvider?.ProviderId);
            return;
        }

        int direction = nextIndex < previousIndex ? -1 : 1;
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
        int providerIndex = IndexOfProvider(providerId);
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
            && args.Index < ViewModel.ProviderSummaries.Count
            && string.Equals(
                ViewModel.ProviderSummaries[args.Index].ProviderId,
                ViewModel.SelectedProvider?.ProviderId,
                StringComparison.Ordinal))
        {
            tab.IsChecked = true;
        }
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

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        SynchronizeProviderLimitsImmediately();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _providerTransitionStoryboard?.Stop();
        _providerTransitionStoryboard = null;
        StopProviderLimitsTransition();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
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
