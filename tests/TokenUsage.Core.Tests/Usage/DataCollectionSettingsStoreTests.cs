using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class DataCollectionSettingsStoreTests
{
    [Fact]
    public async Task MissingDocumentReturnsDefaults()
    {
        using var folder = new TemporaryFolder();
        var store = new DataCollectionSettingsStore(Path.Combine(folder.Root, "datacollection.v1.json"));

        DataCollectionSettings settings = await store.LoadAsync();

        Assert.True(settings.BackgroundCollection);
        Assert.Equal(0, settings.OpenRefreshMinutes);
    }

    [Fact]
    public async Task SaveAndLoadRoundTrips()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Root, "datacollection.v1.json");
        var store = new DataCollectionSettingsStore(path);

        await store.SaveAsync(new DataCollectionSettings(
            BackgroundCollection: false,
            OpenRefreshMinutes: 30));
        DataCollectionSettings settings = await new DataCollectionSettingsStore(path).LoadAsync();

        Assert.False(settings.BackgroundCollection);
        Assert.Equal(30, settings.OpenRefreshMinutes);
    }

    [Fact]
    public async Task CorruptDocumentIsQuarantinedAndIgnored()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Root, "datacollection.v1.json");
        await File.WriteAllTextAsync(path, "{ not json");
        var store = new DataCollectionSettingsStore(path);

        DataCollectionSettings settings = await store.LoadAsync();

        Assert.True(settings.BackgroundCollection);
        Assert.Equal(0, settings.OpenRefreshMinutes);
    }

    [Fact]
    public async Task UnsupportedIntervalFallsBackToManual()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Root, "datacollection.v1.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":1,"backgroundCollection":true,"openRefreshMinutes":7}""");
        var store = new DataCollectionSettingsStore(path);

        DataCollectionSettings settings = await store.LoadAsync();

        Assert.Equal(0, settings.OpenRefreshMinutes);
    }

    [Fact]
    public async Task NewerSchemaKeepsDefaults()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Root, "datacollection.v1.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":99,"backgroundCollection":false,"openRefreshMinutes":60}""");
        var store = new DataCollectionSettingsStore(path);

        DataCollectionSettings settings = await store.LoadAsync();

        Assert.True(settings.BackgroundCollection);
        Assert.Equal(0, settings.OpenRefreshMinutes);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-data-collection-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
