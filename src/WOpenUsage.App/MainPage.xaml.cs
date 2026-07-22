using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.System;
using Windows.UI.ViewManagement;
using WOpenUsage.App.Controls;
using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels;
using WOpenUsage.Providers.Fakes;

namespace WOpenUsage.App;

public sealed partial class MainPage : Page
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _relativeTimeTimer;

    public MainPage()
    {
        TimeProvider clock = TimeProvider.System;
        string sampleCacheDirectory = Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            "cache",
            "sample");
        string codexCacheDirectory = Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            "cache",
            "providers",
            "codex");
        string usageDatabasePath = Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            "scanner",
            "usage.v1.db");
        var codexClientFactory = new CodexAppServerQuotaClientFactory(clock);
        ViewModel = new FlyoutViewModel(
            new SampleRefreshCoordinator(sampleCacheDirectory, clock),
            new CodexRefreshCoordinator(codexCacheDirectory, clock, codexClientFactory),
            new LocalUsageCoordinator(
                usageDatabasePath,
                new SyntheticUsageEventSource(clock, TimeZoneInfo.Local.Id),
                clock));
        InitializeComponent();
        ApplyTextScaleLayout();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        KeyDown += OnKeyDown;
        _relativeTimeTimer = DispatcherQueue.CreateTimer();
        _relativeTimeTimer.Interval = TimeSpan.FromSeconds(30);
        _relativeTimeTimer.Tick += (_, _) => ViewModel.RefreshRelativeTime();
        _relativeTimeTimer.Start();
    }

    public event EventHandler? HideRequested;

    public FlyoutViewModel ViewModel { get; }

    public FrameworkElement MeasureRoot => FlyoutChrome;

    public void FocusPrimaryAction()
    {
        UIElement target = ViewModel.SurfaceState switch
        {
            FlyoutSurfaceState.Options => CloseWhenInactiveToggle,
            FlyoutSurfaceState.Loading => FooterOptionsButton,
            FlyoutSurfaceState.Sample => HeaderRefreshButton,
            FlyoutSurfaceState.SampleUnavailable => SampleRetryButton,
            _ => EmptyOpenOptionsButton,
        };

        _ = target.Focus(FocusState.Programmatic);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        if (ViewModel.IsOptions)
        {
            ViewModel.CloseOptionsCommand.Execute(null);
        }
        else
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ViewModel.SurfaceState), StringComparison.Ordinal))
        {
            _ = DispatcherQueue.TryEnqueue(() =>
                BodyScrollViewer.ChangeView(null, 0, null, disableAnimation: true));

            if (ViewModel.IsSample)
            {
                ScheduleSampleReveal();
            }

            return;
        }

        if (string.Equals(e.PropertyName, nameof(ViewModel.SampleRevealToken), StringComparison.Ordinal)
            && ViewModel.IsSample)
        {
            ScheduleSampleReveal();
        }
    }

    private void OnSampleSpendLayoutLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid layout || new UISettings().TextScaleFactor < 1.5)
        {
            return;
        }

        layout.ColumnDefinitions.Clear();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Clear();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowSpacing = 8;

        for (int index = 0; index < layout.Children.Count; index++)
        {
            FrameworkElement child = (FrameworkElement)layout.Children[index];
            Grid.SetColumn(child, 0);
            Grid.SetRow(child, index);
            if (child is SpendDonutChart chart)
            {
                chart.HorizontalAlignment = HorizontalAlignment.Center;
            }
        }
    }

    private void ApplyTextScaleLayout()
    {
        if (new UISettings().TextScaleFactor < 1.5)
        {
            return;
        }

        FooterIdentityColumn.Width = new GridLength(0);
        FlyoutFooterIdentity.Visibility = Visibility.Collapsed;
        FlyoutStatusText.Opacity = 0;
        FlyoutStatusText.IsHitTestVisible = false;
    }

    private void ScheduleSampleReveal()
    {
        int token = ViewModel.SampleRevealToken;
        _ = DispatcherQueue.TryEnqueue(() =>
            _ = DispatcherQueue.TryEnqueue(() => PlaySampleReveal(this, token)));
    }

    private static void PlaySampleReveal(DependencyObject root, int token)
    {
        if (root is SpendDonutChart donut)
        {
            donut.PlayReveal(token);
        }
        else if (root is AnimatedProgressBar progressBar)
        {
            progressBar.PlayReveal(token);
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            PlaySampleReveal(VisualTreeHelper.GetChild(root, index), token);
        }
    }
}
