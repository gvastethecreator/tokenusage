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
    private int _providerTransitionToken;

    public CompactUsageDashboard() => InitializeComponent();

    public event EventHandler<UsageReportRequestedEventArgs>? ReportRequested;

    public event EventHandler? OptionsRequested;

    public DashboardSurfaceViewModel ViewModel
    {
        get => _viewModel ?? throw new InvalidOperationException("ViewModel is not assigned.");
        set => _viewModel = value ?? throw new ArgumentNullException(nameof(value));
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
            return;
        }

        int direction = nextIndex < previousIndex ? -1 : 1;
        if (!MotionSettings.AreAnimationsEnabled())
        {
            CancelProviderTransition();
            ViewModel.SelectProvider(providerId);
            return;
        }

        PlayProviderTransition(
            ViewModel.IsProviderScope ? ProviderDetailStack : ScopeTransitionRoot,
            ViewModel.IsProviderScope ? ProviderDetailTransform : ScopeTransitionTransform,
            () => ViewModel.SelectProvider(providerId),
            direction);
    }

    private void ShowGlobalWithTransition()
    {
        if (!ViewModel.IsProviderScope)
        {
            return;
        }

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

        for (int index = 0; index < ViewModel.ProviderOptions.Count; index++)
        {
            if (string.Equals(
                ViewModel.ProviderOptions[index].ProviderId,
                providerId,
                StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

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
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            EnableDependentAnimation = false,
        };
        var translation = new DoubleAnimation
        {
            From = currentOffset,
            To = -8 * direction,
            Duration = MotionSettings.ProviderSwitchExitDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            EnableDependentAnimation = true,
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
            EnableDependentAnimation = true,
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
        ScopeTransitionRoot.Opacity = 1;
        ScopeTransitionRoot.IsHitTestVisible = true;
        ScopeTransitionTransform.TranslateX = 0;
        ProviderDetailStack.Opacity = 1;
        ProviderDetailStack.IsHitTestVisible = true;
        ProviderDetailTransform.TranslateX = 0;
    }

    private void OnHeatmapDayInvoked(object? sender, UsageHeatmapDayInvokedEventArgs e) =>
        ReportRequested?.Invoke(
            this,
            new UsageReportRequestedEventArgs(ViewModel.CreateReportRequest(e.Cell.Date)));

    private void OnOptionsClick(object sender, RoutedEventArgs e) =>
        OptionsRequested?.Invoke(this, EventArgs.Empty);
}
