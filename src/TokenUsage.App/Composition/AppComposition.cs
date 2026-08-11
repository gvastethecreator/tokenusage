using TokenUsage.App.Services;
using TokenUsage.App.ViewModels;
using TokenUsage.Core.Alerts;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Session;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.VercelAiGateway;
using TokenUsage.Runtime.Windows.Providers;
using TokenUsage.Runtime.Windows.VercelAiGateway;

namespace TokenUsage.App.Composition;

public sealed record AppCompositionOptions(
    string? DashboardLayoutPath = null,
    string? AppearanceSettingsPath = null);

/// <summary>
/// App composition root: builds the flyout product graph outside the page.
/// </summary>
public static class AppComposition
{
    private static readonly Lazy<HttpClient> VercelHttpClient = new(() => new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30),
    });

    public static FlyoutViewModel CreateFlyoutViewModel(
        string localFolderPath,
        TimeProvider? clock = null,
        AppCompositionOptions? options = null)
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

        WindowsProviderComposition providers = WindowsProviderCatalog.CreateComposition(
            localFolderPath,
            resolvedClock);
        var sessionHost = new AppSessionHost(
            providers.RefreshHost,
            new AlertHost(
                new AlertDecisionStore(
                    Path.Combine(localFolderPath, AlertDecisionStore.DefaultFileName),
                    resolvedClock),
                new AlertSettingsStore(
                    Path.Combine(localFolderPath, AlertSettingsStore.DefaultFileName),
                    resolvedClock)),
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
            quotaResetHistory);
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
