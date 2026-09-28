using TokenUsage.Core.Cache;
using TokenUsage.Core.Credentials;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Catalog;
using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Platform.Windows.Tests.Providers;

public sealed class WindowsProviderCatalogTests
{
    // The tools read from disk, in catalog order. One list, so adding a provider changes one line.
    private static readonly string[] ActiveLocalProviders =
        ["amp", "antigravity", "claude", "codex", "copilot", "cursor", "goose", "grok", "hermes", "mux", "opencode", "zcode"];

    [Fact]
    public void CatalogOwnsCanonicalProviderIdentityAndCapabilities()
    {
        WindowsProviderCatalogEntry[] entries = WindowsProviderCatalog.Entries.ToArray();

        Assert.Equal(
            ActiveLocalProviders,
            entries.Select(entry => entry.Id.Value));
        Assert.Equal(entries.Length, entries.Select(entry => entry.Id.Value).Distinct().Count());
        Assert.Equal(
            [
                ProviderCapability.Limits,
                ProviderCapability.LocalUsage,
                ProviderCapability.Spend,
            ],
            entries.Single(entry => entry.Id.Value == "codex").Capabilities);
        WindowsProviderCatalogEntry[] deferredEntries =
            WindowsProviderCatalog.DeferredEntries.ToArray();
        Assert.Equal(
            ["openrouter", "vercel-ai-gateway"],
            deferredEntries.Select(entry => entry.Id.Value));
        Assert.All(deferredEntries, entry => Assert.False(entry.IsEnabledByDefault));
        Assert.All(deferredEntries, entry => Assert.Equal(
            [ProviderCapability.Limits, ProviderCapability.Spend],
            entry.Capabilities));
        Assert.Equal(
            ManualCredentialKind.ApiKey,
            ProviderModuleCatalog.Get("openrouter").ManualCredentialKind);
        Assert.Equal(
            ManualCredentialKind.ApiKeyAndOptionalKeyId,
            ProviderModuleCatalog.Get("vercel-ai-gateway").ManualCredentialKind);
        Assert.DoesNotContain(
            WindowsProviderCatalog.PreparedEntries,
            entry => entry.Id.Value == "openrouter");
        Assert.Equal(
            ["cline", "cline-cli", "gemini-cli", "kilo-code", "kimi-cli", "kimi-code", "perplexity", "zai", "zed"],
            WindowsProviderCatalog.PolicyBlockedEntries.Select(entry => entry.Id.Value));
        Assert.Equal(
            ProviderModuleCatalog.Entries.Select(entry => entry.Id.Value).Order(),
            WindowsProviderCatalog.AllEntries.Select(entry => entry.Id.Value).Order());
    }

    [Fact]
    public void CatalogNamesTheToolsReadFromDiskSoNoScreenKeepsItsOwnList()
    {
        string[] localUsageIds = ProviderModuleCatalog.ActiveLocalUsageEntries
            .Select(entry => entry.Id.Value)
            .Order()
            .ToArray();

        Assert.Equal(
            ActiveLocalProviders,
            localUsageIds);
        Assert.All(localUsageIds, id => Assert.True(
            ProviderModuleCatalog.IsActiveLocalUsageProvider(id)));
        Assert.False(ProviderModuleCatalog.IsActiveLocalUsageProvider("vercel-ai-gateway"));
        Assert.False(ProviderModuleCatalog.IsActiveLocalUsageProvider("gemini-cli"));
        Assert.False(ProviderModuleCatalog.IsActiveLocalUsageProvider("unknown"));
    }

    [Fact]
    public void CatalogRecordsWhichProvidersKeepTheirQuotaOutOfReach()
    {
        Assert.Equal(
            ["antigravity", "grok", "grok-bot"],
            ProviderModuleCatalog.Entries
                .Where(entry => entry.IsQuotaBlocked)
                .Select(entry => entry.Id.Value)
                .Order());
        Assert.False(ProviderModuleCatalog.Get("codex").IsQuotaBlocked);
        Assert.Equal(
            [
                "alibaba-cloud",
                "anthropic",
                "azure-openai",
                "copilot",
                "deepseek",
                "devin",
                "gemini-api",
                "groq",
                "mistral",
                "moonshot",
                "openai",
                "openrouter",
                "vercel-ai-gateway",
                "xai",
            ],
            ProviderModuleCatalog.ManualCredentialEntries
                .Select(entry => entry.Id.Value)
                .Order());
        Assert.All(
            WindowsProviderCatalog.PolicyBlockedEntries,
            entry => Assert.False(entry.Module.AcceptsManualCredential));
        Assert.Equal(
            ManualCredentialKind.ApiKeyAndEndpoint,
            ProviderModuleCatalog.Get("azure-openai").ManualCredentialKind);
        Assert.Equal(
            ManualCredentialKind.ApiKeyAndOrganization,
            ProviderModuleCatalog.Get("devin").ManualCredentialKind);
        Assert.Throws<ArgumentException>(() => new ProviderModuleDefinition(
            "blocked-key",
            "Blocked key",
            [ProviderCapability.Usage],
            ProviderModuleStage.PolicyBlocked,
            ProviderReference.OpenUsage,
            aliases: null,
            isQuotaBlocked: false,
            manualCredentialKind: ManualCredentialKind.ApiKey));
        Assert.Throws<ArgumentException>(() => new ProviderModuleDefinition(
            "quota-contradiction",
            "Quota contradiction",
            [ProviderCapability.Limits],
            ProviderModuleStage.Active,
            ProviderReference.CodeBurn,
            aliases: null,
            isQuotaBlocked: true));
    }

    [Fact]
    public void OpenUsageParityCatalogHasEveryCurrentProviderOnce()
    {
        ProviderModuleDefinition[] entries = ProviderModuleCatalog.OpenUsageEntries.ToArray();

        Assert.Equal(38, entries.Length);
        Assert.Contains(entries, entry => entry.Id.Value == "claude");
        Assert.Contains(entries, entry => entry.Id.Value == "cursor");
        Assert.Contains(entries, entry => entry.Id.Value == "ollama");
        Assert.Contains(entries, entry => entry.Id.Value == "qwen-cli");
        Assert.Equal(entries.Length, entries.Select(entry => entry.Id.Value).Distinct().Count());
        Assert.All(entries, entry => Assert.NotEmpty(entry.Capabilities));
        Assert.Contains("claude-code", ProviderModuleCatalog.Get("claude").Aliases);
        Assert.Equal("claude", ProviderModuleCatalog.Resolve("claude-code").Id.Value);
    }

    [Fact]
    public void CompositionDerivesRefreshAndLocalUsageSetsFromCatalog()
    {
        using var folder = new TemporaryFolder();
        WindowsProviderComposition composition = WindowsProviderCatalog.CreateComposition(
            folder.Path,
            TimeProvider.System,
            options: new WindowsProviderCompositionOptions(TimeZoneId: "UTC"));

        Assert.Equal(
            ["codex"],
            composition.RefreshHost.Registrations.Select(
                registration => registration.Provider.Descriptor.Id.Value));
        Assert.Equal(
            ActiveLocalProviders,
            composition.LocalUsageSources.Select(source => source.AgentId.Value));
        Assert.Equal(
            SourceKind.OfficialLocalApi,
            composition.LocalUsageSources.Single(source =>
                source.AgentId.Value == "codex").SourceKind);
    }

    [Fact]
    public void OptInProvidersAreComposedOnlyWhenEnabledAndNeedAnHttpClient()
    {
        using var folder = new TemporaryFolder();
        using var httpClient = new HttpClient();

        WindowsProviderComposition composition = WindowsProviderCatalog.CreateComposition(
            folder.Path,
            TimeProvider.System,
            httpClient,
            new WindowsProviderCompositionOptions(
                TimeZoneId: "UTC",
                EnableOptInProviders: true));

        Assert.Equal(
            ["codex", "openrouter", "vercel-ai-gateway"],
            composition.RefreshHost.Registrations.Select(
                registration => registration.Provider.Descriptor.Id.Value));
        Assert.Throws<ArgumentNullException>(() => WindowsProviderCatalog.CreateComposition(
            folder.Path,
            TimeProvider.System,
            apiHttpClient: null,
            new WindowsProviderCompositionOptions(EnableOptInProviders: true)));
    }

    [Theory]
    [InlineData("openrouter")]
    [InlineData("vercel-ai-gateway")]
    public async Task CredentialChangeClearsOnlyThatProvidersCachedReading(string providerId)
    {
        using var folder = new TemporaryFolder();
        using var httpClient = new HttpClient();
        WindowsProviderComposition composition = WindowsProviderCatalog.CreateComposition(
            folder.Path,
            TimeProvider.System,
            httpClient,
            new WindowsProviderCompositionOptions(
                TimeZoneId: "UTC",
                EnableOptInProviders: true));
        ProviderRefreshRegistration registration = composition.RefreshHost.Registrations
            .Single(candidate => candidate.Provider.Descriptor.Id.Value == providerId);
        await registration.Store.UpsertLastGoodAsync(Snapshot(providerId));

        ManualCredentialChangeResult result = await composition.ResetProviderCacheAsync(providerId);

        Assert.Equal(ManualCredentialChangeResult.CacheCleared, result);
        SnapshotCacheReadResult cache = await registration.Store.LoadAsync();
        Assert.DoesNotContain(
            cache is SnapshotCacheReadResult.Loaded loaded ? loaded.Snapshots : [],
            snapshot => snapshot.ProviderId.Value == providerId);
        Assert.Equal(
            ManualCredentialChangeResult.NoLiveSource,
            await composition.ResetProviderCacheAsync("codex"));
        Assert.Equal(
            ManualCredentialChangeResult.NoLiveSource,
            await composition.ResetProviderCacheAsync("openai"));
    }

    private static ProviderSnapshot Snapshot(string providerId) => new(
        new ProviderId(providerId),
        providerId,
        planLabel: null,
        new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero),
        "UTC",
        [
            new ScalarMetricSnapshot(
                new MetricId("spend.test"),
                1m,
                "usd",
                new DataProvenance(SourceKind.ManualKey, MeasurementKind.ProviderReported, "test")),
        ],
        CoverageKind.Complete,
        1);

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tokenusage-provider-catalog-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
