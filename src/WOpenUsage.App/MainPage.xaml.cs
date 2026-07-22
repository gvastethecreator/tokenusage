using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.System;
using WOpenUsage.App.Controls;
using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels;

namespace WOpenUsage.App;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        string sampleCacheDirectory = Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            "cache",
            "sample");
        ViewModel = new FlyoutViewModel(
            new SampleRefreshCoordinator(sampleCacheDirectory, TimeProvider.System));
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        KeyDown += OnKeyDown;
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
