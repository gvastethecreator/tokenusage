using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;
using WOpenUsage.App.Controls;
using WOpenUsage.App.ViewModels.Surfaces;
using WOpenUsage.Core.Appearance;

namespace WOpenUsage.App.Views.Dashboard;

public sealed partial class DashboardView : UserControl
{
    private DashboardSurfaceViewModel? _viewModel;
    private bool _isInitialized;
    private int _detailRevealToken;

    public DashboardView()
    {
        InitializeComponent();
        _isInitialized = true;
    }

    public DashboardSurfaceViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value ?? throw new ArgumentNullException(nameof(value));
            if (_isInitialized)
            {
                Bindings.Update();
            }
        }
    }

    public void ApplyAppearance(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DashboardStack.Spacing = settings.Density == AppDensityMode.Compact ? 8 : 10;
    }

    public void ScheduleReveal()
    {
        int token = ViewModel?.RevealToken ?? 0;
        _ = DispatcherQueue.TryEnqueue(() =>
            _ = DispatcherQueue.TryEnqueue(() => PlayReveal(this, token)));
    }

    private void OnSampleSpendLayoutLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid layout)
        {
            UpdateSampleSpendLayout(layout);
        }
    }

    private void OnSampleSpendLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Grid layout)
        {
            UpdateSampleSpendLayout(layout);
        }
    }

    private static void UpdateSampleSpendLayout(Grid layout)
    {
        bool useStackedLayout = layout.ActualWidth < 300
            || new UISettings().TextScaleFactor >= 1.5;

        layout.ColumnDefinitions.Clear();
        layout.RowDefinitions.Clear();
        if (useStackedLayout)
        {
            layout.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.ColumnSpacing = 0;
            layout.RowSpacing = 8;
        }
        else
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.ColumnSpacing = 12;
            layout.RowSpacing = 0;
        }

        for (int index = 0; index < layout.Children.Count; index++)
        {
            FrameworkElement child = (FrameworkElement)layout.Children[index];
            Grid.SetColumn(child, useStackedLayout ? 0 : index);
            Grid.SetRow(child, useStackedLayout ? index : 0);
            if (child is SpendDonutChart chart)
            {
                chart.HorizontalAlignment = useStackedLayout
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Stretch;
            }
        }
    }

    private void OnProviderUsageDetailsChecked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle)
        {
            DependencyObject header = VisualTreeHelper.GetParent(toggle);
            DependencyObject provider = VisualTreeHelper.GetParent(header);
            ScheduleDetailReveal(provider);
        }
    }

    private void OnUsageDetailsExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        ScheduleDetailReveal(sender);

    private void OnLocalUsageDetailsChecked(object sender, RoutedEventArgs e)
    {
        if (UsageProductDetailsPanel is null)
        {
            return;
        }

        UsageProductDetailsPanel.Visibility = Visibility.Visible;
        ScheduleDetailReveal(UsageProductDetailsPanel);
    }

    private void OnLocalUsageDetailsUnchecked(object sender, RoutedEventArgs e)
    {
        if (UsageProductDetailsPanel is not null)
        {
            UsageProductDetailsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ScheduleDetailReveal(DependencyObject root)
    {
        int token = unchecked(++_detailRevealToken);
        _ = DispatcherQueue.TryEnqueue(() =>
            _ = DispatcherQueue.TryEnqueue(() => PlayReveal(root, token)));
    }

    private static void PlayReveal(DependencyObject root, int token)
    {
        if (root is SpendDonutChart donut)
        {
            donut.PlayReveal(token);
        }
        else if (root is AnimatedProgressBar progressBar)
        {
            progressBar.PlayReveal(token);
        }
        else if (root is UsageHeatmap heatmap)
        {
            heatmap.PlayReveal(token);
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            PlayReveal(VisualTreeHelper.GetChild(root, index), token);
        }
    }
}
