using TokenUsage.Core.Providers;
using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Platform.Windows.Tests.Providers;

public sealed class WindowsProviderCatalogTests
{
    [Fact]
    public void CatalogOwnsCanonicalProviderIdentityAndCapabilities()
    {
        WindowsProviderCatalogEntry[] entries = WindowsProviderCatalog.Entries.ToArray();

        Assert.Equal(
            ["claude", "codex", "grok", "opencode", "antigravity"],
            entries.Select(entry => entry.Id.Value));
        Assert.Equal(entries.Length, entries.Select(entry => entry.Id.Value).Distinct().Count());
        Assert.Equal(
            [
                ProviderCapability.Limits,
                ProviderCapability.LocalUsage,
                ProviderCapability.Spend,
            ],
            entries.Single(entry => entry.Id.Value == "codex").Capabilities);
        WindowsProviderCatalogEntry deferred = Assert.Single(
            WindowsProviderCatalog.DeferredEntries);
        Assert.Equal(
            [ProviderCapability.Limits, ProviderCapability.Spend],
            deferred.Capabilities);
        Assert.Equal("vercel-ai-gateway", deferred.Id.Value);
        Assert.False(deferred.IsEnabledByDefault);
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
            ["claude", "codex", "grok", "opencode", "antigravity"],
            composition.LocalUsageSources.Select(source => source.AgentId.Value));
        Assert.Equal(
            SourceKind.OfficialLocalApi,
            composition.LocalUsageSources.Single(source =>
                source.AgentId.Value == "codex").SourceKind);
        Assert.Null(composition.VercelCoordinator);
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
