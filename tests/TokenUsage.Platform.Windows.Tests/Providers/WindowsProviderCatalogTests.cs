using TokenUsage.Core.Cache;
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
        Assert.False(ProviderModuleCatalog.IsActiveLocalUsageProvider("unknown"));
    }

    [Fact]
    public void CatalogRecordsWhichProvidersKeepTheirQuotaOutOfReach()
    {
        Assert.Equal(
            ["antigravity", "grok"],
            ProviderModuleCatalog.Entries
                .Where(entry => entry.IsQuotaBlocked)
                .Select(entry => entry.Id.Value)
                .Order());
        Assert.False(ProviderModuleCatalog.Get("codex").IsQuotaBlocked);
        Assert.Throws<ArgumentException>(() => new ProviderModuleDefinition(
            "quota-contradiction",
            "Quota contradiction",
            [ProviderCapability.Limits],
            aliases: null,
            isQuotaBlocked: true));
    }

    [Fact]
    public void CatalogHasEveryProviderOnceAndResolvesAliases()
    {
        ProviderModuleDefinition[] entries = ProviderModuleCatalog.Entries.ToArray();

        Assert.Equal(12, entries.Length);
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
            ["claude", "codex"],
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
    public async Task TurningClaudeLimitsOffClearsOnlyItsCachedReading()
    {
        using var folder = new TemporaryFolder();
        WindowsProviderComposition composition = WindowsProviderCatalog.CreateComposition(
            folder.Path,
            TimeProvider.System,
            new WindowsProviderCompositionOptions(TimeZoneId: "UTC"));
        ProviderRefreshRegistration registration = composition.RefreshHost.Registrations
            .Single(candidate => candidate.Provider.Descriptor.Id.Value == "claude");
        await registration.Store.UpsertLastGoodAsync(Snapshot("claude"));

        Assert.True(await composition.ClearClaudeLimitsCacheAsync());

        SnapshotCacheReadResult cache = await registration.Store.LoadAsync();
        Assert.DoesNotContain(
            cache is SnapshotCacheReadResult.Loaded loaded ? loaded.Snapshots : [],
            snapshot => snapshot.ProviderId.Value == "claude");
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
