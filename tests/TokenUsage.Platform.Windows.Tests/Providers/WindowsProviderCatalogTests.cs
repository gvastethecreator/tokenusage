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
            ["codex", "grok", "opencode", "antigravity", "cursor"],
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
            ["claude", "vercel-ai-gateway"],
            deferredEntries.Select(entry => entry.Id.Value));
        Assert.All(deferredEntries, entry => Assert.False(entry.IsEnabledByDefault));
        Assert.Equal(
            [ProviderCapability.Limits, ProviderCapability.Spend],
            deferredEntries.Single(entry => entry.Id.Value == "vercel-ai-gateway").Capabilities);
        Assert.Equal(
            ["copilot", "devin", "openrouter"],
            WindowsProviderCatalog.PreparedEntries.Select(entry => entry.Id.Value));
        Assert.Equal(
            ["zai"],
            WindowsProviderCatalog.PolicyBlockedEntries.Select(entry => entry.Id.Value));
        Assert.Equal(
            ProviderModuleCatalog.Entries.Select(entry => entry.Id.Value).Order(),
            WindowsProviderCatalog.AllEntries.Select(entry => entry.Id.Value).Order());
    }

    [Fact]
    public void OpenUsageParityCatalogHasEveryCurrentProviderOnce()
    {
        ProviderModuleDefinition[] entries = ProviderModuleCatalog.OpenUsageEntries.ToArray();

        Assert.Equal(
            [
                "antigravity",
                "claude",
                "codex",
                "copilot",
                "cursor",
                "devin",
                "grok",
                "opencode",
                "openrouter",
                "zai",
            ],
            entries.Select(entry => entry.Id.Value).Order());
        Assert.Equal(entries.Length, entries.Select(entry => entry.Id.Value).Distinct().Count());
        Assert.All(entries, entry => Assert.NotEmpty(entry.Capabilities));
        Assert.Equal(
            ["copilot", "devin", "openrouter"],
            entries.Where(entry => entry.Stage == ProviderModuleStage.Prepared)
                .Select(entry => entry.Id.Value));
        Assert.Equal(
            ["zai"],
            entries.Where(entry => entry.Stage == ProviderModuleStage.PolicyBlocked)
                .Select(entry => entry.Id.Value));
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
            ["codex", "grok", "opencode", "antigravity", "cursor"],
            composition.LocalUsageSources.Select(source => source.AgentId.Value));
        Assert.Equal(
            SourceKind.OfficialLocalApi,
            composition.LocalUsageSources.Single(source =>
                source.AgentId.Value == "codex").SourceKind);
        Assert.Null(composition.VercelCoordinator);
    }

    [Fact]
    public void ClaudeLocalUsageRequiresExplicitOptIn()
    {
        using var folder = new TemporaryFolder();

        WindowsProviderComposition composition = WindowsProviderCatalog.CreateComposition(
            folder.Path,
            TimeProvider.System,
            options: new WindowsProviderCompositionOptions(
                TimeZoneId: "UTC",
                EnableClaudeLocalUsage: true));

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
