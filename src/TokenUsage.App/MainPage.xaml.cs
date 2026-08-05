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
using TokenUsage.App.Composition;
using TokenUsage.App.Controls;
using TokenUsage.App.Localization;
using TokenUsage.App.ViewModels;
using TokenUsage.Providers.VercelAiGateway;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Session;
using TokenUsage.Runtime.Windows.VercelAiGateway;

namespace TokenUsage.App;

public sealed partial class MainPage : Page, IDisposable
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _relativeTimeTimer;
    private Storyboard? _viewTransitionStoryboard;
    private int _viewTransitionToken;
    private FlyoutSurfaceState _lastSurfaceState;
    private OptionsSection _lastOptionsSection;
    private bool _disposed;

    public MainPage()
    {
        TimeProvider clock = TimeProvider.System;
        string localFolderPath = ApplicationData.Current.LocalFolder.Path;
        var options = new AppCompositionOptions(
            DashboardLayoutPath: GetDashboardLayoutPath(localFolderPath),
            AppearanceSettingsPath: GetAppearanceSettingsPath(localFolderPath));
        ViewModel = AppComposition.CreateFlyoutViewModel(localFolderPath, clock, options);
        SessionHost = ViewModel.SessionHost;
        InitializeComponent();
        _lastSurfaceState = ViewModel.SurfaceState;
        _lastOptionsSection = ViewModel.ActiveOptionsSection;
        ApplyTextScaleLayout();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        KeyDown += OnKeyDown;
        _relativeTimeTimer = DispatcherQueue.CreateTimer();
        _relativeTimeTimer.Interval = TimeSpan.FromSeconds(30);
        _relativeTimeTimer.Tick += OnRelativeTimeTimerElapsed;
        _relativeTimeTimer.Start();
        _ = ViewModel.StartAsync();
    }

    public event EventHandler? HideRequested;

    public FlyoutViewModel ViewModel { get; }

    public AppSessionHost SessionHost { get; }

    public FrameworkElement MeasureRoot => FlyoutChrome;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _relativeTimeTimer.Stop();
        _relativeTimeTimer.Tick -= OnRelativeTimeTimerElapsed;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        KeyDown -= OnKeyDown;
        _viewTransitionStoryboard?.Stop();
        ViewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnRelativeTimeTimerElapsed(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args) => ViewModel.RefreshRelativeTime();

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
        OptionsSurfaceView.ApplyAppearance(settings, FlyoutChrome.ActualWidth);
        DashboardSurfaceView.ApplyAppearance(settings);
    }

    private void OnFlyoutChromeSizeChanged(object sender, SizeChangedEventArgs e)
    {
        OptionsSurfaceView.ApplyAppearance(ViewModel.Appearance, e.NewSize.Width);
    }

    public void FocusPrimaryAction() => FocusPrimaryAction(remainingAttempts: 2);

    private void FocusPrimaryAction(int remainingAttempts)
    {
        UIElement target = ViewModel.SurfaceState switch
        {
            FlyoutSurfaceState.Options => OptionsSurfaceView.GetPrimaryAction(
                ViewModel.ActiveOptionsSection),
            FlyoutSurfaceState.Loading => FooterOptionsButton,
            FlyoutSurfaceState.Sample => HeaderRefreshButton,
            FlyoutSurfaceState.SampleUnavailable => SampleRetryButton,
            _ => EmptyOpenOptionsButton,
        };

        if (!target.Focus(FocusState.Programmatic) && remainingAttempts > 0)
        {
            _ = DispatcherQueue.TryEnqueue(
                () => FocusPrimaryAction(remainingAttempts - 1));
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool isControlDown = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        object? focusedElement = FocusManager.GetFocusedElement(XamlRoot);
        bool isEditingText = focusedElement is TextBox or PasswordBox or RichEditBox;
        if (e.Key == VirtualKey.Z
            && isControlDown
            && ViewModel.IsOptions
            && ViewModel.Personalization.CanUndo
            && !isEditingText)
        {
            _ = ViewModel.Personalization.UndoAsync();
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        if (ViewModel.IsOptions)
        {
            ViewModel.OptionsNavigation.NavigateBackCommand.Execute(null);
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
                if (isNavigationChange)
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
        OptionsSection.ProviderStatus => 2,
        _ => 1,
    };

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
        => DashboardSurfaceView.ScheduleReveal();

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
