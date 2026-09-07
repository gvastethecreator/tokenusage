using TokenUsage.App.Services;
using TokenUsage.App.ViewModels;
using TokenUsage.Core.Alerts;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Session;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.VercelAiGateway;
using TokenUsage.Runtime.Windows.Credentials;
using TokenUsage.Runtime.Windows.Providers;
using TokenUsage.Runtime.Windows.VercelAiGateway;
using TokenUsage.Runtime.Windows;
using TokenUsage.App.ViewModels.Surfaces;
using TokenUsage.Core.Updates;
using TokenUsage.Runtime.Windows.Updates;
using Microsoft.Windows.ApplicationModel.Resources;

namespace TokenUsage.App.Composition;

public sealed record AppCompositionOptions(
    string? DashboardLayoutPath = null,
    string? AppearanceSettingsPath = null);

/// <summary>
/// App composition root: builds the flyout product graph outside the page.
/// </summary>
public static class AppComposition
{
    private static readonly Lazy<HttpClient> UpdateHttpClient = new(() => new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseDefaultCredentials = false,
    })
    {
        Timeout = TimeSpan.FromMinutes(2),
    });

    private static readonly Lazy<HttpClient> VercelHttpClient = new(() => new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30),
    });

    public static FlyoutViewModel CreateFlyoutViewModel(
        string localFolderPath,
        TimeProvider? clock = null,
        AppCompositionOptions? options = null,
        IAlertNotificationSink? alertNotifications = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);
        TimeProvider resolvedClock = clock ?? TimeProvider.System;
        options ??= new AppCompositionOptions();

        string sampleCacheDirectory = Path.Combine(localFolderPath, "cache", "sample");
        string usageDatabasePath = GetUsageDatabasePath(localFolderPath);
        string dashboardLayoutPath = options.DashboardLayoutPath
            ?? Path.Combine(localFolderPath, DashboardLayoutStore.DefaultFileName);
        string appearanceSettingsPath = options.AppearanceSettingsPath
            ?? Path.Combine(localFolderPath, AppearanceSettingsStore.DefaultFileName);
        var quotaResetHistory = new QuotaResetHistoryStore(
            GetQuotaResetHistoryPath(localFolderPath),
            resolvedClock);

        WindowsProviderCompositionOptions? providerOptions = null;
#if DEBUG || UI_TEST_FIXTURES
        if (Environment.GetCommandLineArgs().Contains(
                "--test-vercel-fake",
                StringComparer.OrdinalIgnoreCase))
        {
            string vercelCache = Path.Combine(
                localFolderPath,
                "cache",
                "providers",
                "vercel-ai-gateway");
            providerOptions = new WindowsProviderCompositionOptions(
                VercelCoordinator: CreateVercelCoordinator(
                    vercelCache,
                    resolvedClock,
                    new DebugVercelCredentialStore(),
                    new DebugVercelReportClient(),
                    new DebugVercelQuotaClient()),
                EnableVercelGateway: true);
        }
#endif

        WindowsProviderComposition providers = WindowsProviderCatalog.CreateComposition(
            localFolderPath,
            resolvedClock,
            vercelHttpClient: null,
            providerOptions);
        var dataCollectionSettings = new DataCollectionSettingsStore(
            Path.Combine(localFolderPath, DataCollectionSettingsStore.DefaultFileName),
            resolvedClock);
        DataCollectionSettings collectionSettings = Task.Run(
            () => dataCollectionSettings.LoadAsync()).GetAwaiter().GetResult();
        RefreshHookAutoSetup.EnsureInstalled(
            backgroundCollection: collectionSettings.BackgroundCollection);
        var alertSettings = new AlertSettingsStore(
            Path.Combine(localFolderPath, AlertSettingsStore.DefaultFileName),
            resolvedClock);
        var sessionHost = new AppSessionHost(
            providers.RefreshHost,
            new AlertHost(
                new AlertDecisionStore(
                    Path.Combine(localFolderPath, AlertDecisionStore.DefaultFileName),
                    resolvedClock),
                alertSettings),
            resolvedClock);

        return new FlyoutViewModel(
            new SampleRefreshCoordinator(sampleCacheDirectory, resolvedClock),
            sessionHost,
            new LocalUsageCoordinator(
                usageDatabasePath,
                providers.LocalUsageSources,
                resolvedClock),
            new DashboardLayoutStore(dashboardLayoutPath, resolvedClock),
            new AppearanceSettingsStore(appearanceSettingsPath, resolvedClock),
            quotaResetHistory,
            new WindowsManualProviderCredentialStore(),
            dataCollectionSettings,
            alertSettings,
            alertNotifications ?? NullAlertNotificationSink.Instance,
            CreateUpdateOptions(localFolderPath, resolvedClock));
    }

    private static UpdateOptionsViewModel CreateUpdateOptions(string localFolderPath, TimeProvider clock)
    {
        var resources = new ResourceLoader();
        Version version = typeof(AppComposition).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return new UpdateOptionsViewModel(
            UpdateEnvironment.Detect(version),
            new UpdateSettingsStore(Path.Combine(localFolderPath, UpdateSettingsStore.DefaultFileName)),
            new GitHubUpdateClient(UpdateHttpClient.Value),
            Path.Combine(localFolderPath, "updates"),
            resources.GetString,
            clock);
    }

    public static string GetUsageDatabasePath(string localFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);
        return Path.Combine(Path.GetFullPath(localFolderPath), "scanner", "usage.v1.db");
    }

    public static string GetQuotaResetHistoryPath(string localFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);
        return Path.Combine(
            Path.GetFullPath(localFolderPath),
            "history",
            QuotaResetHistoryStore.DefaultFileName);
    }

    public static VercelGatewayRefreshCoordinator CreateVercelCoordinator(
        string cacheDirectory,
        TimeProvider clock,
        IVercelGatewayCredentialStore? credentialStore = null,
        IVercelGatewayReportClient? reportClient = null,
        IVercelGatewayQuotaClient? quotaClient = null)
    {
        if (credentialStore is not null
            && reportClient is not null
            && quotaClient is not null)
        {
            return new VercelGatewayRefreshCoordinator(
                new SnapshotStore(Path.Combine(cacheDirectory, SnapshotStore.DefaultFileName), clock),
                credentialStore,
                reportClient,
                quotaClient,
                clock);
        }

        return new VercelGatewayRefreshCoordinator(
            cacheDirectory,
            clock,
            VercelHttpClient.Value);
    }
}
