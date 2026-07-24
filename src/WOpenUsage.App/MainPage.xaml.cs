using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Input;
using Microsoft.Windows.AppLifecycle;
using Windows.Storage;
using Windows.System;
using Windows.UI.ViewManagement;
using Windows.UI.Core;
using WOpenUsage.App.Composition;
using WOpenUsage.App.Controls;
using WOpenUsage.App.Localization;
using WOpenUsage.App.ViewModels;
using WOpenUsage.Providers.VercelAiGateway;
using WOpenUsage.Core.Appearance;
using WOpenUsage.Core.Layout;
using WOpenUsage.Runtime.Windows.VercelAiGateway;

namespace WOpenUsage.App;

public sealed partial class MainPage : Page
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _relativeTimeTimer;
    private int _detailRevealToken;
    private Storyboard? _viewTransitionStoryboard;
    private int _viewTransitionToken;
    private FlyoutSurfaceState _lastSurfaceState;
    private OptionsSection _lastOptionsSection;

    public MainPage()
    {
        TimeProvider clock = TimeProvider.System;
        string localFolderPath = ApplicationData.Current.LocalFolder.Path;
        string vercelCacheDirectory = Path.Combine(
            localFolderPath,
            "cache",
            "providers",
            "vercel-ai-gateway");
        var options = new AppCompositionOptions(
            DashboardLayoutPath: GetDashboardLayoutPath(localFolderPath),
            AppearanceSettingsPath: GetAppearanceSettingsPath(localFolderPath),
            VercelCoordinator: TryCreateDebugVercelCoordinator(vercelCacheDirectory, clock));
        ViewModel = AppComposition.CreateFlyoutViewModel(localFolderPath, clock, options);
        InitializeComponent();
        _lastSurfaceState = ViewModel.SurfaceState;
        _lastOptionsSection = ViewModel.ActiveOptionsSection;
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

    public void ApplyAppearance(
        AppearanceSettings settings,
        bool transparencyActive)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RequestedTheme = settings.Theme switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        OpaqueSurface.Visibility = transparencyActive
            ? Visibility.Collapsed
            : Visibility.Visible;
        _ = VisualStateManager.GoToState(
            this,
            settings.Density == AppDensityMode.Compact
                ? "CompactDensity"
                : "RegularDensity",
            useTransitions: false);
    }

    private void OnFlyoutChromeSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = VisualStateManager.GoToState(
            this,
            e.NewSize.Width >= 360d
                ? "WideAppearanceLayout"
                : "NarrowAppearanceLayout",
            useTransitions: false);
    }

    public void FocusPrimaryAction()
    {
        UIElement target = ViewModel.SurfaceState switch
        {
            FlyoutSurfaceState.Options => GetOptionsPrimaryAction(),
            FlyoutSurfaceState.Loading => FooterOptionsButton,
            FlyoutSurfaceState.Sample => HeaderRefreshButton,
            FlyoutSurfaceState.SampleUnavailable => SampleRetryButton,
            _ => EmptyOpenOptionsButton,
        };

        _ = target.Focus(FocusState.Programmatic);
    }

    private UIElement GetOptionsPrimaryAction() => ViewModel.ActiveOptionsSection switch
    {
        OptionsSection.General => CloseWhenInactiveToggle,
        OptionsSection.Appearance => AppearanceThemeSelector,
        OptionsSection.Personalization => DashboardLayoutExpander,
        OptionsSection.Providers => OptionsVercelButton,
        OptionsSection.Vercel => ViewModel.Vercel.IsConnectFormVisible
            ? VercelApiKeyBox
            : VercelDisconnectButton,
        OptionsSection.ProviderStatus => ProviderStatusRefreshButton,
        _ => OptionsGeneralButton,
    };

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool isControlDown = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        object? focusedElement = FocusManager.GetFocusedElement(XamlRoot);
        bool isEditingText = focusedElement is TextBox or PasswordBox or RichEditBox;
        if (e.Key == VirtualKey.Z
            && isControlDown
            && ViewModel.IsOptions
            && ViewModel.CanUndoDashboardLayout
            && !isEditingText)
        {
            _ = ViewModel.UndoDashboardLayoutAsync();
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        if (ViewModel.IsOptions)
        {
            ViewModel.NavigateBackOptionsCommand.Execute(null);
        }
        else
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        bool surfaceChanged = string.Equals(
            e.PropertyName,
            nameof(ViewModel.SurfaceState),
            StringComparison.Ordinal);
        bool optionsSectionChanged = string.Equals(
            e.PropertyName,
            nameof(ViewModel.ActiveOptionsSection),
            StringComparison.Ordinal);
        if (surfaceChanged || optionsSectionChanged)
        {
            FlyoutSurfaceState previousSurface = _lastSurfaceState;
            OptionsSection previousSection = _lastOptionsSection;
            FlyoutSurfaceState nextSurface = ViewModel.SurfaceState;
            OptionsSection nextSection = ViewModel.ActiveOptionsSection;
            _lastSurfaceState = nextSurface;
            _lastOptionsSection = nextSection;
            bool isNavigationChange = optionsSectionChanged
                || (surfaceChanged
                    && (previousSurface == FlyoutSurfaceState.Options
                        || nextSurface == FlyoutSurfaceState.Options));
            double transitionOffset = GetViewTransitionOffset(
                previousSurface,
                previousSection,
                nextSurface,
                nextSection);
            int transitionToken = 0;
            if (isNavigationChange)
            {
                transitionToken = ++_viewTransitionToken;
                PrepareViewTransition(transitionOffset);
            }
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                BodyScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
                if (isNavigationChange && transitionToken == _viewTransitionToken)
                {
                    PlayViewTransition(transitionOffset);
                }
                if (ViewModel.IsOptions)
                {
                    FocusPrimaryAction();
                }
            });

            if (surfaceChanged && ViewModel.IsSample)
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

    private void PrepareViewTransition(double startOffset)
    {
        _viewTransitionStoryboard?.Stop();
        _viewTransitionStoryboard = null;
        if (!MotionSettings.AreAnimationsEnabled())
        {
            BodyScrollViewer.Opacity = 1;
            BodyTransitionTransform.TranslateX = 0;
            return;
        }

        BodyScrollViewer.Opacity = 0.84;
        BodyTransitionTransform.TranslateX = startOffset;
    }

    private void PlayViewTransition(double startOffset)
    {
        _viewTransitionStoryboard?.Stop();
        _viewTransitionStoryboard = null;
        if (!MotionSettings.AreAnimationsEnabled())
        {
            BodyScrollViewer.Opacity = 1;
            BodyTransitionTransform.TranslateX = 0;
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var opacity = new DoubleAnimation
        {
            From = 0.84,
            To = 1,
            Duration = MotionSettings.ViewTransitionDuration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(opacity, BodyScrollViewer);
        Storyboard.SetTargetProperty(opacity, nameof(Opacity));
        var translation = new DoubleAnimation
        {
            From = startOffset,
            To = 0,
            Duration = MotionSettings.ViewTransitionDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(translation, BodyTransitionTransform);
        Storyboard.SetTargetProperty(translation, nameof(CompositeTransform.TranslateX));
        var storyboard = new Storyboard();
        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translation);
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_viewTransitionStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            BodyScrollViewer.Opacity = 1;
            BodyTransitionTransform.TranslateX = 0;
            _viewTransitionStoryboard = null;
        };
        _viewTransitionStoryboard = storyboard;
        storyboard.Begin();
    }

    private static double GetViewTransitionOffset(
        FlyoutSurfaceState previousSurface,
        OptionsSection previousSection,
        FlyoutSurfaceState nextSurface,
        OptionsSection nextSection)
    {
        if (previousSurface != nextSurface)
        {
            return nextSurface == FlyoutSurfaceState.Options ? 12 : -12;
        }

        return GetOptionsDepth(nextSection) >= GetOptionsDepth(previousSection) ? 12 : -12;
    }

    private static int GetOptionsDepth(OptionsSection section) => section switch
    {
        OptionsSection.Home => 0,
        OptionsSection.Vercel or OptionsSection.ProviderStatus => 2,
        _ => 1,
    };

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

    private void OnRestartForLanguageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLanguageRuntime.RestartWithLanguage(
                ViewModel.PendingLanguageTag,
                GetLanguageRestartArguments());
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException)
        {
        }

        ViewModel.ReportLanguageRestartFailure();
    }

    private void OnVercelApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            ViewModel.Vercel.SetApiKeyInputPresence(passwordBox.Password);
        }
    }

    private void OnVercelKeyIdTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            ViewModel.Vercel.SetKeyIdInput(textBox.Text);
        }
    }

    private async void OnVercelConnectClicked(object sender, RoutedEventArgs e)
    {
        string apiKey = VercelApiKeyBox.Password;
        string keyId = VercelKeyIdBox.Text;
        Task connection = ViewModel.Vercel.ConnectAsync(apiKey, keyId);
        VercelApiKeyBox.Password = string.Empty;
        VercelKeyIdBox.Text = string.Empty;
        await connection;
    }

    private async void OnDashboardProviderMoveUpClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string providerId })
        {
            await ViewModel.MoveDashboardProviderAsync(providerId, -1);
        }
    }

    private async void OnDashboardLayoutUndoClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.UndoDashboardLayoutAsync();
    }

    private async void OnDashboardLayoutResetClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ViewModel.DashboardLayoutResetTitle,
            Content = ViewModel.DashboardLayoutResetBody,
            PrimaryButtonText = ViewModel.DashboardLayoutResetConfirm,
            CloseButtonText = ViewModel.DashboardLayoutResetCancel,
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(dialog, "DashboardLayoutResetDialog");
        AutomationProperties.SetName(dialog, ViewModel.DashboardLayoutResetTitle);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ResetDashboardLayoutAsync();
        }
    }

    private async void OnDashboardProviderMoveDownClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string providerId })
        {
            await ViewModel.MoveDashboardProviderAsync(providerId, 1);
        }
    }

    private async void OnDashboardProviderVisibilityClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string providerId } toggle)
        {
            await ViewModel.SetDashboardProviderVisibleAsync(
                providerId,
                toggle.IsChecked is true);
        }
    }

    private async void OnDashboardProviderHighlightClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string providerId } toggle)
        {
            await ViewModel.SetDashboardProviderHighlightedAsync(
                providerId,
                toggle.IsChecked is true);
        }
    }

    private void OnDashboardProviderColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button
            {
                Tag: DashboardProviderLayoutRow row,
                Flyout: Flyout { Content: ColorPicker picker },
            })
        {
            picker.Color = ProviderColorPalette.Parse(
                ProviderColorPalette.GetEffectiveHex(row.ProviderId, row.ColorHex));
        }
    }

    private async void OnDashboardProviderColorFlyoutClosed(object sender, object e)
    {
        if (sender is Flyout
            {
                Content: ColorPicker
                {
                    Tag: DashboardProviderLayoutRow row,
                } picker,
            })
        {
            string selectedColor = ProviderColorPalette.ToHex(picker.Color);
            string currentColor = ProviderColorPalette.GetEffectiveHex(
                row.ProviderId,
                row.ColorHex);
            if (string.Equals(selectedColor, currentColor, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await ViewModel.SetDashboardProviderColorAsync(
                row.ProviderId,
                selectedColor);
        }
    }

    private async void OnDashboardMetricMoveUpClicked(object sender, RoutedEventArgs e)
    {
        if (TryGetDashboardMetricTarget(sender, out string providerId, out string metricId))
        {
            await ViewModel.MoveDashboardMetricAsync(providerId, metricId, -1);
        }
    }

    private async void OnDashboardMetricMoveDownClicked(object sender, RoutedEventArgs e)
    {
        if (TryGetDashboardMetricTarget(sender, out string providerId, out string metricId))
        {
            await ViewModel.MoveDashboardMetricAsync(providerId, metricId, 1);
        }
    }

    private async void OnDashboardMetricVisibilityClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle
            && TryGetDashboardMetricTarget(sender, out string providerId, out string metricId))
        {
            await ViewModel.SetDashboardMetricVisibleAsync(
                providerId,
                metricId,
                toggle.IsChecked is true);
        }
    }

    private async void OnDashboardMetricHighlightClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle
            && TryGetDashboardMetricTarget(sender, out string providerId, out string metricId))
        {
            await ViewModel.SetDashboardMetricHighlightedAsync(
                providerId,
                metricId,
                toggle.IsChecked is true);
        }
    }

    private async void OnDashboardMetricSectionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle
            && TryGetDashboardMetricTarget(sender, out string providerId, out string metricId))
        {
            await ViewModel.SetDashboardMetricOnDemandAsync(
                providerId,
                metricId,
                toggle.IsChecked is true);
        }
    }

    private static bool TryGetDashboardMetricTarget(
        object sender,
        out string providerId,
        out string metricId)
    {
        if (sender is ButtonBase
            {
                Tag: string provider,
                CommandParameter: string metric,
            }
            && !string.IsNullOrWhiteSpace(provider)
            && !string.IsNullOrWhiteSpace(metric))
        {
            providerId = provider;
            metricId = metric;
            return true;
        }

        providerId = string.Empty;
        metricId = string.Empty;
        return false;
    }

    private void OnDashboardProviderMetricsExpanding(object sender, ExpanderExpandingEventArgs e)
    {
        SetDashboardProviderMetricsExpanded(sender, isExpanded: true);
        SetDashboardProviderMetricItems(sender, loadItems: true);
    }

    private void OnDashboardProviderMetricsCollapsed(object sender, ExpanderCollapsedEventArgs e)
    {
        SetDashboardProviderMetricsExpanded(sender, isExpanded: false);
        SetDashboardProviderMetricItems(sender, loadItems: false);
    }

    private void SetDashboardProviderMetricsExpanded(object sender, bool isExpanded)
    {
        if (sender is FrameworkElement { Tag: DashboardProviderLayoutRow row }
            && (isExpanded || ViewModel.DashboardLayoutProviders.Any(current =>
                ReferenceEquals(current, row))))
        {
            ViewModel.SetDashboardProviderMetricsExpanded(row.ProviderId, isExpanded);
        }
    }

    private static void SetDashboardProviderMetricItems(object sender, bool loadItems)
    {
        if (sender is not Expander
            {
                Tag: DashboardProviderLayoutRow row,
                Content: ItemsControl items,
            })
        {
            return;
        }

        items.ItemsSource = loadItems
            ? row.Metrics
            : null;
    }

    private static string GetLanguageRestartArguments()
    {
#if DEBUG || UI_TEST_FIXTURES
        return AppLanguageRestartArguments.Create(Environment.GetCommandLineArgs()[1..]);
#else
        return string.Empty;
#endif
    }

    private static VercelGatewayRefreshCoordinator? TryCreateDebugVercelCoordinator(
        string cacheDirectory,
        TimeProvider clock)
    {
#if DEBUG || UI_TEST_FIXTURES
        if (Environment.GetCommandLineArgs().Contains(
                "--test-vercel-fake",
                StringComparer.OrdinalIgnoreCase))
        {
            return AppComposition.CreateVercelCoordinator(
                cacheDirectory,
                clock,
                new DebugVercelCredentialStore(),
                new DebugVercelReportClient(),
                new DebugVercelQuotaClient());
        }
#endif
        return null;
    }

    private static string GetDashboardLayoutPath(string localFolderPath)
    {
#if DEBUG || UI_TEST_FIXTURES
        string? overrideArgument = Environment.GetCommandLineArgs().FirstOrDefault(argument =>
            argument.StartsWith("--test-layout-path=", StringComparison.OrdinalIgnoreCase));
        if (overrideArgument is not null)
        {
            return overrideArgument[(overrideArgument.IndexOf('=') + 1)..];
        }
#endif
        return Path.Combine(localFolderPath, DashboardLayoutStore.DefaultFileName);
    }

    private static string GetAppearanceSettingsPath(string localFolderPath)
    {
#if DEBUG || UI_TEST_FIXTURES
        string? overrideArgument = Environment.GetCommandLineArgs().FirstOrDefault(argument =>
            argument.StartsWith("--test-appearance-path=", StringComparison.OrdinalIgnoreCase));
        if (overrideArgument is not null)
        {
            return overrideArgument[(overrideArgument.IndexOf('=') + 1)..];
        }
#endif
        return Path.Combine(localFolderPath, AppearanceSettingsStore.DefaultFileName);
    }

    private void ScheduleSampleReveal()
    {
        int token = ViewModel.SampleRevealToken;
        _ = DispatcherQueue.TryEnqueue(() =>
            _ = DispatcherQueue.TryEnqueue(() => PlaySampleReveal(this, token)));
    }

    private void OnProviderUsageDetailsChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle)
        {
            return;
        }

        DependencyObject header = VisualTreeHelper.GetParent(toggle);
        DependencyObject provider = VisualTreeHelper.GetParent(header);
        ScheduleDetailReveal(provider);
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
            _ = DispatcherQueue.TryEnqueue(() => PlaySampleReveal(root, token)));
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
        else if (root is UsageHeatmap heatmap)
        {
            heatmap.PlayReveal(token);
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            PlaySampleReveal(VisualTreeHelper.GetChild(root, index), token);
        }
    }

#if DEBUG || UI_TEST_FIXTURES
    private sealed class DebugVercelCredentialStore : IVercelGatewayCredentialStore
    {
        private string? _apiKey;
        private string? _keyId;

        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_apiKey is not null);
        }

        public Task<VercelGatewayConnection?> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _apiKey is null ? null : new VercelGatewayConnection(_apiKey, _keyId));
        }

        public Task SaveAsync(string apiKey, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
            cancellationToken.ThrowIfCancellationRequested();
            _apiKey = apiKey;
            _keyId = null;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            string apiKey,
            string keyId,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
            cancellationToken.ThrowIfCancellationRequested();
            _apiKey = apiKey;
            _keyId = keyId;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool removed = _apiKey is not null;
            _apiKey = null;
            _keyId = null;
            return Task.FromResult(removed);
        }
    }

    private sealed class DebugVercelReportClient : IVercelGatewayReportClient
    {
        public Task<VercelGatewayReport> GetDailyReportAsync(
            string apiKey,
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new VercelGatewayReport(
            [
                new VercelGatewayDailyReportRow(
                    endDate,
                    TotalCost: 12.5m,
                    MarketCost: 11m,
                    SurchargeCost: 1m,
                    GatewayCost: 0.5m,
                    InputTokens: 1000,
                    OutputTokens: 250,
                    CachedInputTokens: 100,
                    CacheCreationInputTokens: 50,
                    ReasoningTokens: 25,
                    RequestCount: 7),
            ]));
        }
    }

    private sealed class DebugVercelQuotaClient : IVercelGatewayQuotaClient
    {
        public Task<VercelGatewayQuotaLookupResult> GetQuotaAsync(
            string apiKey,
            string keyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<VercelGatewayQuotaLookupResult>(
                new VercelGatewayQuotaLookupResult.Found(
                    new VercelGatewayQuota(
                        "api_key_id_" + keyId,
                        "tokenusage-ui-test",
                        10m,
                        3.5m,
                        6.5m,
                        VercelGatewayQuotaRefreshPeriod.Monthly,
                        Active: true)));
        }
    }
#endif
}
