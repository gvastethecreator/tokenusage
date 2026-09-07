using TokenUsage.Core.Updates;

namespace TokenUsage.Core.Tests.Updates;

public sealed class UpdateSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TokenUsage.UpdateSettings.Tests", Guid.NewGuid().ToString("N"));
    private string DocumentPath => Path.Combine(_directory, UpdateSettingsStore.DefaultFileName);

    [Fact]
    public async Task UpdatesRequireOptInAndPersistCheckTimeInUtc()
    {
        var store = new UpdateSettingsStore(DocumentPath);
        Assert.Equal(new UpdateSettings(), await store.LoadAsync());

        var checkedAt = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.FromHours(-3));
        await store.SaveAsync(new UpdateSettings(true, checkedAt));
        UpdateSettings actual = await new UpdateSettingsStore(DocumentPath).LoadAsync();

        Assert.True(actual.AutomaticUpdatesEnabled);
        Assert.Equal(checkedAt, actual.LastCheckUtc);
        Assert.Equal(TimeSpan.Zero, actual.LastCheckUtc!.Value.Offset);
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("{\"schemaVersion\":1,\"automaticUpdatesEnabled\":\"true\"}")]
    public async Task CorruptSettingsCannotEnableAutomaticDownloads(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(DocumentPath, content);

        Assert.Equal(new UpdateSettings(), await new UpdateSettingsStore(DocumentPath).LoadAsync());
        Assert.False(File.Exists(DocumentPath));
        Assert.Single(Directory.GetFiles(_directory, "*.corrupt-*"));
    }

    [Fact]
    public async Task FutureSettingsAreLeftUntouchedAndDoNotEnableUpdates()
    {
        Directory.CreateDirectory(_directory);
        const string content = """{"schemaVersion":99,"automaticUpdatesEnabled":true}""";
        await File.WriteAllTextAsync(DocumentPath, content);

        Assert.Equal(new UpdateSettings(), await new UpdateSettingsStore(DocumentPath).LoadAsync());
        Assert.Equal(content, await File.ReadAllTextAsync(DocumentPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
