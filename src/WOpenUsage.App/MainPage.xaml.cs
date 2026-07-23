using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using Windows.Storage;
using Windows.System;
using Windows.UI.ViewManagement;
using WOpenUsage.App.Controls;
using WOpenUsage.App.Localization;
using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels;
using WOpenUsage.Providers.Claude;
using WOpenUsage.Providers.Grok;
using WOpenUsage.Providers.OpenCode;
using WOpenUsage.Providers.VercelAiGateway;
using WOpenUsage.Core.Cache;
using WOpenUsage.Runtime.Windows.Codex;
using WOpenUsage.Runtime.Windows.VercelAiGateway;

namespace WOpenUsage.App;

public sealed partial class MainPage : Page
{
    private static readonly HttpClient VercelHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

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
        string vercelCacheDirectory = Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            "cache",
            "providers",
            "vercel-ai-gateway");
        var codexClientFactory = new CodexAppServerQuotaClientFactory(clock);
        ViewModel = new FlyoutViewModel(
            new SampleRefreshCoordinator(sampleCacheDirectory, clock),
            new CodexRefreshCoordinator(codexCacheDirectory, clock, codexClientFactory),
            new LocalUsageCoordinator(
                usageDatabasePath,
                [
                    new ClaudeUsageEventSource(TimeZoneInfo.Local.Id),
                    new GrokUsageEventSource(TimeZoneInfo.Local.Id),
                    new OpenCodeUsageEventSource(TimeZoneInfo.Local.Id),
                ],
                clock),
            CreateVercelCoordinator(vercelCacheDirectory, clock));
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

    private static string GetLanguageRestartArguments()
    {
#if DEBUG || UI_TEST_FIXTURES
        return AppLanguageRestartArguments.Create(Environment.GetCommandLineArgs()[1..]);
#else
        return string.Empty;
#endif
    }

    private static VercelGatewayRefreshCoordinator CreateVercelCoordinator(
        string cacheDirectory,
        TimeProvider clock)
    {
#if DEBUG || UI_TEST_FIXTURES
        if (Environment.GetCommandLineArgs().Contains(
                "--test-vercel-fake",
                StringComparer.OrdinalIgnoreCase))
        {
            return new VercelGatewayRefreshCoordinator(
                new SnapshotStore(
                    Path.Combine(cacheDirectory, SnapshotStore.DefaultFileName),
                    clock),
                new DebugVercelCredentialStore(),
                new DebugVercelReportClient(),
                new DebugVercelQuotaClient(),
                clock);
        }
#endif
        return new VercelGatewayRefreshCoordinator(
            cacheDirectory,
            clock,
            VercelHttpClient);
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
