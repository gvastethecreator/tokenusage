using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TokenUsage.App.Controls;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.App.ViewModels.Surfaces;
using TokenUsage.Core.Appearance;

namespace TokenUsage.App.Views.Dashboard;

public sealed partial class CompactUsageDashboard : UserControl
{
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
    private int _providerTabPageSize = ProviderTabCarouselLayout.MaximumPageSize;
    private int _providerTabStartIndex;
    private double _providerTabItemWidth = 64;
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


    private void OnGlobalClick(object sender, RoutedEventArgs e) => ShowGlobalWithTransition();


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
