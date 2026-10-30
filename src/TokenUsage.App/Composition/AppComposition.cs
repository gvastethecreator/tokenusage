using TokenUsage.App.Services;
using TokenUsage.App.ViewModels;
using TokenUsage.Core.Alerts;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Session;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
using TokenUsage.Runtime.Windows.Providers;
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

    public static FlyoutViewModel CreateFlyoutViewModel(
        string localFolderPath,
        TimeProvider? clock = null,
        AppCompositionOptions? options = null,
        IAlertNotificationSink? alertNotifications = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);
        TimeProvider resolvedClock = clock ?? TimeProvider.System;
        options ??= new AppCompositionOptions();

        string usageDatabasePath = GetUsageDatabasePath(localFolderPath);
        string dashboardLayoutPath = options.DashboardLayoutPath
            ?? Path.Combine(localFolderPath, DashboardLayoutStore.DefaultFileName);
        string appearanceSettingsPath = options.AppearanceSettingsPath
            ?? Path.Combine(localFolderPath, AppearanceSettingsStore.DefaultFileName);
        var quotaResetHistory = new QuotaResetHistoryStore(
            GetQuotaResetHistoryPath(localFolderPath),
            resolvedClock, isCodexHistoryFrozen: () => new TokenUsage.Core.Providers.ProviderAccountSelectionStore(
                localFolderPath, new TokenUsage.Core.Providers.ProviderId("codex")).WasActivated);

        RemoveAttributionFiles(localFolderPath);
        WindowsProviderComposition providers = WindowsProviderCatalog.CreateComposition(
            localFolderPath,
            resolvedClock);
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

        var coordinator = new LocalUsageCoordinator(
            usageDatabasePath,
            providers.LocalUsageSources,
            resolvedClock);
        return new FlyoutViewModel(
#if DEBUG
            // Sample scenarios are a local development aid (--test-use-sample); release builds
            // create no sample session or cache.
            new SampleRefreshCoordinator(Path.Combine(localFolderPath, "cache", "sample"), resolvedClock),
#else
            null,
#endif
            sessionHost,
            coordinator,
            new DashboardLayoutStore(dashboardLayoutPath, resolvedClock),
            new AppearanceSettingsStore(appearanceSettingsPath, resolvedClock),
            quotaResetHistory,
            dataCollectionSettings,
            alertSettings,
            alertNotifications ?? NullAlertNotificationSink.Instance,
            CreateUpdateOptions(localFolderPath, resolvedClock),
            new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(localFolderPath)),
            clearClaudeLimitsCache: providers.ClearClaudeLimitsCacheAsync,
            accounts: providers.Accounts,
            dataDirectory: localFolderPath);
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

    /// <summary>
    /// Session, project, and tool attribution was removed. The usage database drops its tables
    /// on migration; these app-owned settings files held the consent state and user aliases.
    /// </summary>
    private static void RemoveAttributionFiles(string localFolderPath)
    {
        foreach (string name in new[] { "attribution-consent.v1.json", "attribution-aliases.v1.json" })
        {
            try
            {
                File.Delete(Path.Combine(localFolderPath, name));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A locked file is retried on the next start.
            }
        }
    }

    public static string GetQuotaResetHistoryPath(string localFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);
        return Path.Combine(
            Path.GetFullPath(localFolderPath),
            "history",
            QuotaResetHistoryStore.DefaultFileName);
    }
}
