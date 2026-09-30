using TokenUsage.App.Services;
using TokenUsage.App.ViewModels;
using TokenUsage.Core.Alerts;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Session;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.Cursor;
using TokenUsage.Runtime.Windows.Attribution;
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

    // Opt-in provider APIs (Vercel AI Gateway, OpenRouter). Redirects are not followed, so a
    // saved key is only ever sent to the documented host.
    private static readonly Lazy<HttpClient> ApiHttpClient = new(() => new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseDefaultCredentials = false,
    })
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

        string usageDatabasePath = GetUsageDatabasePath(localFolderPath);
        string dashboardLayoutPath = options.DashboardLayoutPath
            ?? Path.Combine(localFolderPath, DashboardLayoutStore.DefaultFileName);
        string appearanceSettingsPath = options.AppearanceSettingsPath
            ?? Path.Combine(localFolderPath, AppearanceSettingsStore.DefaultFileName);
        var quotaResetHistory = new QuotaResetHistoryStore(
            GetQuotaResetHistoryPath(localFolderPath),
            resolvedClock, isCodexHistoryFrozen: () => new TokenUsage.Core.Providers.ProviderAccountSelectionStore(
                localFolderPath, new TokenUsage.Core.Providers.ProviderId("codex")).WasActivated);

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
                VercelCoordinator: new VercelGatewayRefreshCoordinator(
                    new SnapshotStore(
                        Path.Combine(vercelCache, SnapshotStore.DefaultFileName),
                        resolvedClock),
                    new DebugVercelCredentialStore(),
                    new DebugVercelReportClient(),
                    new DebugVercelQuotaClient(),
                    new DebugVercelCreditsClient(),
                    resolvedClock));
        }
#endif
        var attributionConsent = new AttributionConsentStore(
            GetAttributionConsentPath(localFolderPath),
            resolvedClock);
        IOpaqueKeyDeriver? attributionKeys = new WindowsAttributionSecretStore().TryCreateDeriver();
        // Opt-in providers are always composed: each one reads its saved key first and makes
        // no network call without one, so saving a key in the provider list is the opt-in.
        providerOptions = (providerOptions ?? new WindowsProviderCompositionOptions()) with
        {
            EnableOptInProviders = true,
            AttributionConsent = attributionConsent,
            AttributionKeys = attributionKeys,
        };

        WindowsProviderComposition providers = WindowsProviderCatalog.CreateComposition(
            localFolderPath,
            resolvedClock,
            ApiHttpClient.Value,
            providerOptions);
        var attributionAliases = new AttributionAliasStore(
            GetAttributionAliasPath(localFolderPath),
            resolvedClock);
        Task.Run(() => ResumeAttributionPurgeAsync(
            attributionConsent,
            usageDatabasePath,
            providers.LocalUsageSources.OfType<CodexUsageEventSource>().ToArray(),
            attributionAliases,
            cancellationToken: default)).GetAwaiter().GetResult();
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
            resolvedClock,
            attributionConsent);
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
            new WindowsManualProviderCredentialStore(),
            dataCollectionSettings,
            alertSettings,
            alertNotifications ?? NullAlertNotificationSink.Instance,
            CreateUpdateOptions(localFolderPath, resolvedClock),
            attributionConsent,
            usageDatabasePath,
            async (capability, token) =>
            {
                foreach (CodexUsageEventSource source in providers.LocalUsageSources.OfType<CodexUsageEventSource>())
                {
                    if (capability.Value == AttributionCapability.CodexSession.Value)
                    {
                        source.ClearStoredSessionAttribution();
                    }
                    else if (capability.Value == AttributionCapability.CodexProject.Value)
                    {
                        source.ClearStoredProjectAttribution();
                    }
                    else if (capability.Value is "codex-mcp" or "codex-skills" or "codex-commands" or "codex-files")
                    {
                        source.ClearStoredOperationAttribution(capability);
                    }
                }

                await Task.CompletedTask.ConfigureAwait(false);
            },
            attributionAliases,
            async (from, to, capability, token) =>
            {
                foreach (CodexUsageEventSource source in providers.LocalUsageSources.OfType<CodexUsageEventSource>())
                {
                    if (capability.Value == AttributionCapability.CodexSession.Value)
                    {
                        source.SessionAttributionBackfillFrom = from;
                        source.SessionAttributionBackfillTo = to;
                    }
                    else if (capability.Value == AttributionCapability.CodexProject.Value)
                    {
                        source.ProjectAttributionBackfillFrom = from;
                        source.ProjectAttributionBackfillTo = to;
                    }
                    else if (capability.Value == AttributionCapability.CodexMcp.Value)
                    {
                        source.McpAttributionBackfillFrom = from;
                        source.McpAttributionBackfillTo = to;
                    }
                    else if (capability.Value == AttributionCapability.CodexSkills.Value)
                    {
                        source.SkillsAttributionBackfillFrom = from;
                        source.SkillsAttributionBackfillTo = to;
                    }
                    else if (capability.Value == AttributionCapability.CodexCommands.Value)
                    {
                        source.CommandsAttributionBackfillFrom = from;
                        source.CommandsAttributionBackfillTo = to;
                    }
                    else if (capability.Value == AttributionCapability.CodexFiles.Value)
                    {
                        source.FilesAttributionBackfillFrom = from;
                        source.FilesAttributionBackfillTo = to;
                    }
                }

                foreach (CursorUsageEventSource source in providers.LocalUsageSources.OfType<CursorUsageEventSource>())
                {
                    if (capability.Value == AttributionCapability.CursorSession.Value)
                    {
                        source.AttributionBackfillFrom = from;
                        source.AttributionBackfillTo = to;
                    }
                }

                try
                {
                    await coordinator.RefreshDashboardAsync(_ => string.Empty, token).ConfigureAwait(false);
                }
                finally
                {
                    foreach (CodexUsageEventSource source in providers.LocalUsageSources.OfType<CodexUsageEventSource>())
                    {
                        source.SessionAttributionBackfillFrom = null;
                        source.SessionAttributionBackfillTo = null;
                        source.ProjectAttributionBackfillFrom = null;
                        source.ProjectAttributionBackfillTo = null;
                        source.McpAttributionBackfillFrom = null;
                        source.McpAttributionBackfillTo = null;
                        source.SkillsAttributionBackfillFrom = null;
                        source.SkillsAttributionBackfillTo = null;
                        source.CommandsAttributionBackfillFrom = null;
                        source.CommandsAttributionBackfillTo = null;
                        source.FilesAttributionBackfillFrom = null;
                        source.FilesAttributionBackfillTo = null;
                    }

                    foreach (CursorUsageEventSource source in providers.LocalUsageSources.OfType<CursorUsageEventSource>())
                    {
                        source.AttributionBackfillFrom = null;
                        source.AttributionBackfillTo = null;
                    }
                }
            },
            new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(localFolderPath)),
            credentialChanged: providers.ResetProviderCacheAsync,
            accounts: providers.Accounts);
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

    public static string GetAttributionConsentPath(string localFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);
        return Path.Combine(Path.GetFullPath(localFolderPath), AttributionConsentStore.DefaultFileName);
    }

    public static string GetAttributionAliasPath(string localFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);
        return Path.Combine(Path.GetFullPath(localFolderPath), AttributionAliasStore.DefaultFileName);
    }

    public static async Task ResumeAttributionPurgeAsync(
        AttributionConsentStore consent,
        string usageDatabasePath,
        IReadOnlyList<CodexUsageEventSource>? sources = null,
        AttributionAliasStore? aliases = null,
        CancellationToken cancellationToken = default)
    {
        await ResumeCapabilityPurgeAsync(
            consent,
            AttributionCapability.CodexSession,
            usageDatabasePath,
            async (repository, token) =>
            {
                await repository.PurgeSessionLinksAsync(AttributionCapability.CodexSession, token)
                    .ConfigureAwait(false);
                if (sources is null)
                {
                    return;
                }

                foreach (CodexUsageEventSource source in sources)
                {
                    source.ClearStoredSessionAttribution();
                }
            },
            cancellationToken).ConfigureAwait(false);
        await ResumeCapabilityPurgeAsync(
            consent,
            AttributionCapability.CodexProject,
            usageDatabasePath,
            async (repository, token) =>
            {
                await repository.PurgeProjectLinksAsync(token).ConfigureAwait(false);
                if (aliases is not null)
                {
                    await aliases.ClearAsync(token).ConfigureAwait(false);
                }

                if (sources is null)
                {
                    return;
                }

                foreach (CodexUsageEventSource source in sources)
                {
                    source.ClearStoredProjectAttribution();
                }
            },
            cancellationToken).ConfigureAwait(false);
        await ResumeCapabilityPurgeAsync(
            consent,
            AttributionCapability.CursorSession,
            usageDatabasePath,
            (repository, token) => repository.PurgeSessionLinksAsync(AttributionCapability.CursorSession, token),
            cancellationToken).ConfigureAwait(false);
        await ResumeOperationPurgeAsync(
            consent, AttributionCapability.CodexMcp, usageDatabasePath, sources, cancellationToken)
            .ConfigureAwait(false);
        await ResumeOperationPurgeAsync(
            consent, AttributionCapability.CodexSkills, usageDatabasePath, sources, cancellationToken)
            .ConfigureAwait(false);
        await ResumeOperationPurgeAsync(
            consent, AttributionCapability.CodexCommands, usageDatabasePath, sources, cancellationToken)
            .ConfigureAwait(false);
        await ResumeOperationPurgeAsync(
            consent, AttributionCapability.CodexFiles, usageDatabasePath, sources, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ResumeOperationPurgeAsync(
        AttributionConsentStore consent,
        AttributionCapability capability,
        string usageDatabasePath,
        IReadOnlyList<CodexUsageEventSource>? sources,
        CancellationToken cancellationToken) =>
        await ResumeCapabilityPurgeAsync(
            consent,
            capability,
            usageDatabasePath,
            async (repository, token) =>
            {
                await repository.PurgeOperationFactsAsync(capability, token).ConfigureAwait(false);
                if (sources is null)
                {
                    return;
                }

                foreach (CodexUsageEventSource source in sources)
                {
                    source.ClearStoredOperationAttribution(capability);
                }
            },
            cancellationToken).ConfigureAwait(false);

    private static async Task ResumeCapabilityPurgeAsync(
        AttributionConsentStore consent,
        AttributionCapability capability,
        string usageDatabasePath,
        Func<UsageRepository, CancellationToken, Task> purge,
        CancellationToken cancellationToken)
    {
        AttributionConsent state = await consent.LoadAsync(capability, cancellationToken)
            .ConfigureAwait(false);
        if (state.State != AttributionConsentState.PurgePending)
        {
            return;
        }

        if (File.Exists(usageDatabasePath))
        {
            UsageRepository repository = await UsageRepository.OpenAsync(usageDatabasePath, cancellationToken)
                .ConfigureAwait(false);
            await purge(repository, cancellationToken).ConfigureAwait(false);
        }

        await consent.CompletePurgeAsync(capability, cancellationToken).ConfigureAwait(false);
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
