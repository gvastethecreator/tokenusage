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
    private readonly Dictionary<FrameworkElement, Storyboard> _activeViewTransitions = [];
    private bool _isTransitionCommit;
    private bool _loadedOnce;
    private int _shareStatusToken;

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
            && int.TryParse(value, out int days)
            && (days != ViewModel.WindowDays || ViewModel.IsResetCycleWindow))
        {
            PlayViewTransition(
                ReportDataContent,
                ReportDataTransitionTransform,
                () => ViewModel.SetWindowDays(days));
        }
    }

    private void OnResetCycleClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanUseResetCycles && !ViewModel.IsResetCycleWindow)
        {
            PlayViewTransition(
                ReportDataContent,
                ReportDataTransitionTransform,
                ViewModel.SetResetCycleWindow);
        }
    }

    private void OnPreviousResetCycleClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanSelectPreviousResetCycle)
        {
            PlayViewTransition(
                ReportDataContent,
                ReportDataTransitionTransform,
                ViewModel.SelectPreviousResetCycle);
        }
    }

    private void OnNextResetCycleClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanSelectNextResetCycle)
        {
            PlayViewTransition(
                ReportDataContent,
                ReportDataTransitionTransform,
                ViewModel.SelectNextResetCycle);
        }
    }

    private void OnMetricClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value }
            && Enum.TryParse(value, ignoreCase: true, out UsageReportMetric metric)
            && metric != UsageReportMetric.Share
            && metric != ViewModel.Metric)
        {
            PlayViewTransition(
                ReportDataContent,
                ReportDataTransitionTransform,
                () => ViewModel.SetMetric(metric));
        }
    }

    private void OnScopeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value }
            && Enum.TryParse(value, ignoreCase: true, out UsageReportScope scope)
            && scope != ViewModel.Scope)
        {
            PlayViewTransition(
                ReportDataContent,
                ReportDataTransitionTransform,
                () => ViewModel.SetScope(scope));
        }
    }

    private void OnValueModeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value }
            && Enum.TryParse(value, ignoreCase: true, out UsageReportValueMode mode)
            && ViewModel.IsGlobalScope
            && mode != ViewModel.ValueMode)
        {
            PlayViewTransition(
                ReportDataContent,
                ReportDataTransitionTransform,
                () => ViewModel.SetValueMode(mode));
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
            if (!string.Equals(
                    ViewModel.SelectedProvider?.ProviderId,
                    option.ProviderId,
                    StringComparison.Ordinal))
            {
                PlayViewTransition(
                    ReportDataContent,
                    ReportDataTransitionTransform,
                    () => ViewModel.SelectedProvider = option);
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
            && Enum.TryParse(value, ignoreCase: true, out UsageReportBreakdown breakdown)
            && breakdown != ViewModel.Breakdown)
        {
            (FrameworkElement currentRows, CompositeTransform currentTransform) =
                GetBreakdownTransitionTarget(ViewModel.Breakdown);
            (FrameworkElement nextRows, CompositeTransform nextTransform) =
                GetBreakdownTransitionTarget(breakdown);
            PlayViewTransition(
                currentRows,
                currentTransform,
                () => ViewModel.SetBreakdown(breakdown),
                nextRows,
                nextTransform);
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
        if (showCombined == (GlobalCombinedChart.Visibility == Visibility.Visible))
        {
            return;
        }

        PlayViewTransition(
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
            });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loadedOnce || _isTransitionCommit)
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(UsageReportViewModel.IsLoading), StringComparison.Ordinal))
        {
            if (ViewModel.IsLoading)
            {
                PrepareViewTransitionExit(ReportDataContent, ReportDataTransitionTransform);
            }
            else if (ViewModel.HasData)
            {
                PlayViewTransitionEntry(ReportDataContent, ReportDataTransitionTransform);
            }
            return;
        }

        if (string.Equals(e.PropertyName, nameof(UsageReportViewModel.Trend), StringComparison.Ordinal)
            && !ViewModel.IsLoading)
        {
            PlayViewTransitionEntry(ReportDataContent, ReportDataTransitionTransform);
        }
    }

    private void PrepareViewTransitionExit(
        FrameworkElement element,
        CompositeTransform transform)
    {
        if (!MotionSettings.AreAnimationsEnabled())
        {
            ResetTransition(element, transform);
            return;
        }

        StartTransition(
            element,
            transform,
            Math.Clamp(element.Opacity, 0, 1),
            GetTransitionMinimumOpacity(element),
            transform.TranslateX,
            ReferenceEquals(element, ReportDataContent)
                ? 0
                : -MotionSettings.ReportSwitchOffset,
            MotionSettings.ReportSwitchExitDuration,
            EasingMode.EaseInOut,
            resetOnComplete: false);
    }

    private void PlayViewTransition(
        FrameworkElement element,
        CompositeTransform transform,
        Action commit,
        FrameworkElement? entryElement = null,
        CompositeTransform? entryTransform = null)
    {
        FrameworkElement destinationElement = entryElement ?? element;
        CompositeTransform destinationTransform = entryTransform ?? transform;
        StopTransition(element);
        if (!MotionSettings.AreAnimationsEnabled())
        {
            ExecuteTransitionCommit(commit);
            ResetTransition(element, transform);
            if (!ReferenceEquals(destinationElement, element))
            {
                ResetTransition(destinationElement, destinationTransform);
            }
            return;
        }

        double exitOffset = ReferenceEquals(element, ReportDataContent)
            ? 0
            : MotionSettings.ReportSwitchOffset;
        double minimumOpacity = GetTransitionMinimumOpacity(element);
        StartTransition(
            element,
            transform,
            Math.Clamp(element.Opacity, 0, 1),
            minimumOpacity,
            transform.TranslateX,
            -exitOffset,
            MotionSettings.ReportSwitchExitDuration,
            EasingMode.EaseInOut,
            resetOnComplete: false,
            completed: () =>
            {
                ExecuteTransitionCommit(commit);
                destinationElement.UpdateLayout();
                if (!ReferenceEquals(destinationElement, element))
                {
                    ResetTransition(element, transform);
                }
                if (ReferenceEquals(element, ReportDataContent) && ViewModel.IsLoading)
                {
                    destinationElement.Opacity = GetTransitionMinimumOpacity(destinationElement);
                    destinationTransform.TranslateX = 0;
                    return;
                }

                PlayViewTransitionEntry(destinationElement, destinationTransform);
            });
    }

    private void PlayViewTransitionEntry(
        FrameworkElement element,
        CompositeTransform transform)
    {
        StopTransition(element);
        if (!MotionSettings.AreAnimationsEnabled())
        {
            ResetTransition(element, transform);
            return;
        }

        double entryOffset = ReferenceEquals(element, ReportDataContent)
            ? 0
            : MotionSettings.ReportSwitchOffset;
        double minimumOpacity = GetTransitionMinimumOpacity(element);
        element.Opacity = minimumOpacity;
        transform.TranslateX = entryOffset;
        StartTransition(
            element,
            transform,
            minimumOpacity,
            1,
            entryOffset,
            0,
            MotionSettings.ReportSwitchDuration,
            EasingMode.EaseOut,
            resetOnComplete: true);
    }

    private double GetTransitionMinimumOpacity(FrameworkElement element) =>
        ReferenceEquals(element, ReportDataContent)
            ? MotionSettings.ReportRefreshMinimumOpacity
            : MotionSettings.ReportSwitchMinimumOpacity;

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

    private void StartTransition(
        FrameworkElement element,
        CompositeTransform transform,
        double fromOpacity,
        double toOpacity,
        double fromOffset,
        double toOffset,
        TimeSpan duration,
        EasingMode easingMode,
        bool resetOnComplete,
        Action? completed = null)
    {
        StopTransition(element);
        element.Opacity = fromOpacity;
        transform.TranslateX = fromOffset;

        var opacity = new DoubleAnimation
        {
            From = fromOpacity,
            To = toOpacity,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = easingMode },
        };
        Storyboard.SetTarget(opacity, element);
        Storyboard.SetTargetProperty(opacity, nameof(Opacity));
        var translation = new DoubleAnimation
        {
            From = fromOffset,
            To = toOffset,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = easingMode },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(translation, transform);
        Storyboard.SetTargetProperty(translation, nameof(CompositeTransform.TranslateX));

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translation);
        storyboard.Completed += (_, _) =>
        {
            if (!_activeViewTransitions.TryGetValue(element, out Storyboard? current)
                || !ReferenceEquals(current, storyboard))
            {
                return;
            }

            storyboard.Stop();
            _activeViewTransitions.Remove(element);
            element.Opacity = toOpacity;
            transform.TranslateX = toOffset;
            if (resetOnComplete)
            {
                ResetTransition(element, transform);
            }

            completed?.Invoke();
        };
        _activeViewTransitions[element] = storyboard;
        storyboard.Begin();
    }

    private void StopTransition(FrameworkElement element)
    {
        if (_activeViewTransitions.Remove(element, out Storyboard? storyboard))
        {
            storyboard.Stop();
        }
    }

    private void ResetTransition(FrameworkElement element, CompositeTransform transform)
    {
        StopTransition(element);
        element.Opacity = 1;
        transform.TranslateX = 0;
        transform.TranslateY = 0;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        foreach ((FrameworkElement element, Storyboard storyboard) in _activeViewTransitions.ToArray())
        {
            storyboard.Stop();
            element.Opacity = 1;
        }
        _activeViewTransitions.Clear();
        ReportDataTransitionTransform.TranslateX = 0;
        ReportDataTransitionTransform.TranslateY = 0;
        GlobalChartTransitionTransform.TranslateX = 0;
        GlobalChartTransitionTransform.TranslateY = 0;
        ModelBreakdownRowsTransform.TranslateX = 0;
        ModelBreakdownRowsTransform.TranslateY = 0;
        SourceBreakdownRowsTransform.TranslateX = 0;
        SourceBreakdownRowsTransform.TranslateY = 0;
        DayBreakdownRowsTransform.TranslateX = 0;
        DayBreakdownRowsTransform.TranslateY = 0;
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
