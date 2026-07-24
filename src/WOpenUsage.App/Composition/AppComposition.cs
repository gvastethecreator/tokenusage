using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels;
using WOpenUsage.Core.Appearance;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Layout;
using WOpenUsage.Providers.Claude;
using WOpenUsage.Providers.Grok;
using WOpenUsage.Providers.OpenCode;
using WOpenUsage.Providers.VercelAiGateway;
using WOpenUsage.Runtime.Windows.Codex;
using WOpenUsage.Runtime.Windows.VercelAiGateway;

namespace WOpenUsage.App.Composition;

public sealed record AppCompositionOptions(
    string? DashboardLayoutPath = null,
    string? AppearanceSettingsPath = null,
    VercelGatewayRefreshCoordinator? VercelCoordinator = null);

/// <summary>
/// App composition root: builds the flyout product graph outside the page.
/// </summary>
public static class AppComposition
{
    private static readonly HttpClient VercelHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public static FlyoutViewModel CreateFlyoutViewModel(
        string localFolderPath,
        TimeProvider? clock = null,
        AppCompositionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);
        TimeProvider resolvedClock = clock ?? TimeProvider.System;
        options ??= new AppCompositionOptions();

        string sampleCacheDirectory = Path.Combine(localFolderPath, "cache", "sample");
        string codexCacheDirectory = Path.Combine(localFolderPath, "cache", "providers", "codex");
        string usageDatabasePath = Path.Combine(localFolderPath, "scanner", "usage.v1.db");
        string vercelCacheDirectory = Path.Combine(
            localFolderPath,
            "cache",
            "providers",
            "vercel-ai-gateway");
        string dashboardLayoutPath = options.DashboardLayoutPath
            ?? Path.Combine(localFolderPath, DashboardLayoutStore.DefaultFileName);
        string appearanceSettingsPath = options.AppearanceSettingsPath
            ?? Path.Combine(localFolderPath, AppearanceSettingsStore.DefaultFileName);

        var codexCoordinator = new CodexRefreshCoordinator(
            codexCacheDirectory,
            resolvedClock,
            new CodexAppServerQuotaClientFactory(resolvedClock));
        VercelGatewayRefreshCoordinator vercelCoordinator = options.VercelCoordinator
            ?? new VercelGatewayRefreshCoordinator(
                vercelCacheDirectory,
                resolvedClock,
                VercelHttpClient);
        var liveRefreshHost = new ProviderRefreshHost(
            [
                codexCoordinator.CreateRegistration(),
                vercelCoordinator.CreateRegistration(),
            ],
            resolvedClock);

        return new FlyoutViewModel(
            new SampleRefreshCoordinator(sampleCacheDirectory, resolvedClock),
            liveRefreshHost,
            new LocalUsageCoordinator(
                usageDatabasePath,
                [
                    new ClaudeUsageEventSource(TimeZoneInfo.Local.Id),
                    new GrokUsageEventSource(TimeZoneInfo.Local.Id),
                    new OpenCodeUsageEventSource(TimeZoneInfo.Local.Id),
                ],
                resolvedClock),
            new DashboardLayoutStore(dashboardLayoutPath, resolvedClock),
            new AppearanceSettingsStore(appearanceSettingsPath, resolvedClock),
            vercelCoordinator);
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

        return new VercelGatewayRefreshCoordinator(cacheDirectory, clock, VercelHttpClient);
    }
}
