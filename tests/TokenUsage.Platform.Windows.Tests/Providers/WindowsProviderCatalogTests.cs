using TokenUsage.Core.Providers;
using TokenUsage.Providers.Catalog;
using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Platform.Windows.Tests.Providers;

public sealed class WindowsProviderCatalogTests
{
    [Fact]
    public void CatalogOwnsCanonicalProviderIdentityAndCapabilities()
    {
        WindowsProviderCatalogEntry[] entries = WindowsProviderCatalog.Entries.ToArray();

        Assert.Equal(
            ["amp", "antigravity", "claude", "codex", "cursor", "goose", "grok", "hermes", "mux", "opencode"],
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
            ["vercel-ai-gateway"],
            deferredEntries.Select(entry => entry.Id.Value));
        Assert.All(deferredEntries, entry => Assert.False(entry.IsEnabledByDefault));
        Assert.Equal(
            [ProviderCapability.Limits, ProviderCapability.Spend],
            deferredEntries.Single(entry => entry.Id.Value == "vercel-ai-gateway").Capabilities);
        Assert.Equal(35, WindowsProviderCatalog.PreparedEntries.Count);
        Assert.Contains(
            WindowsProviderCatalog.PreparedEntries,
            entry => entry.Id.Value == "openrouter");
        Assert.Equal(
            ["cline", "cline-cli", "kilo-code", "kimi-cli", "kimi-code", "perplexity", "zai", "zcode", "zed"],
            WindowsProviderCatalog.PolicyBlockedEntries.Select(entry => entry.Id.Value));
        Assert.Equal(55, WindowsProviderCatalog.AllEntries.Count);
        Assert.Equal(
            ProviderModuleCatalog.Entries.Select(entry => entry.Id.Value).Order(),
            WindowsProviderCatalog.AllEntries.Select(entry => entry.Id.Value).Order());
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
            ["amp", "antigravity", "claude", "codex", "cursor", "goose", "grok", "hermes", "mux", "opencode"],
            composition.LocalUsageSources.Select(source => source.AgentId.Value));
        Assert.Equal(
            SourceKind.OfficialLocalApi,
            composition.LocalUsageSources.Single(source =>
                source.AgentId.Value == "codex").SourceKind);
        Assert.Null(composition.VercelCoordinator);
    }

    [Fact]
    public void ClaudeLocalUsageIsActiveByDefault()
    {
        using var folder = new TemporaryFolder();

        WindowsProviderComposition composition = WindowsProviderCatalog.CreateComposition(
            folder.Path,
            TimeProvider.System,
            options: new WindowsProviderCompositionOptions(TimeZoneId: "UTC"));

        Assert.Contains(
            composition.LocalUsageSources,
            source => source.AgentId.Value == "claude");
    }

    [Fact]
    public void DeferredVercelBindingRequiresExplicitOptIn()
    {
        using var folder = new TemporaryFolder();
        using var httpClient = new HttpClient();

        WindowsProviderComposition composition = WindowsProviderCatalog.CreateComposition(
            folder.Path,
            TimeProvider.System,
            httpClient,
            new WindowsProviderCompositionOptions(
                TimeZoneId: "UTC",
                EnableVercelGateway: true));

        Assert.Equal(
            ["codex", "vercel-ai-gateway"],
            composition.RefreshHost.Registrations.Select(
                registration => registration.Provider.Descriptor.Id.Value));
        Assert.NotNull(composition.VercelCoordinator);
        Assert.Equal(
            "vercel-ai-gateway",
            composition.VercelCoordinator.CreateRegistration().Provider.Descriptor.Id.Value);
    }

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
