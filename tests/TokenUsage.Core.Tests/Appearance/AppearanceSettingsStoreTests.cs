using System.Text;
using System.Text.Json;
using TokenUsage.Core.Appearance;

namespace TokenUsage.Core.Tests.Appearance;

public sealed class AppearanceSettingsStoreTests
{
    [Fact]
    public void ConstructorCanonicalizesPathAndRequiresFileName()
    {
        using var directory = new TempDirectory();
        string relative = Path.Combine(
            directory.Path,
            "..",
            Path.GetFileName(directory.Path),
            AppearanceSettingsStore.DefaultFileName);
        var store = new AppearanceSettingsStore(relative);

        Assert.Equal(Path.GetFullPath(relative), store.DocumentPath);
        Assert.Throws<ArgumentException>(() => new AppearanceSettingsStore(directory.Path));
        Assert.Throws<ArgumentException>(() => new AppearanceSettingsStore("   "));
    }

    [Fact]
    public async Task MissingDocumentReturnsDefaultsWithoutWriting()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);

        AppearanceSettingsLoadResult result = await store.LoadAsync();

        Assert.IsType<AppearanceSettingsLoadResult.Defaults>(result);
        Assert.False(File.Exists(store.DocumentPath));
    }

    [Fact]
    public async Task SaveAndLoadRoundTripUsesVersionOneContract()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        var expected = new AppearanceSettings(
            AppThemeMode.Dark,
            AppDensityMode.Compact,
            increaseTransparency: true,
            UsageDisplayMode.Used,
            ResetTimeDisplayMode.Exact);

        Assert.IsType<AppearanceSettingsSaveResult.Saved>(await store.SaveAsync(expected));
        var loaded = Assert.IsType<AppearanceSettingsLoadResult.Loaded>(await store.LoadAsync());

        Assert.Equal(expected, loaded.Settings);
        Assert.False(loaded.RequiresMigration);
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(store.DocumentPath));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("dark", json.RootElement.GetProperty("theme").GetString());
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task LegacyOpenUsageShapePreservesEveryExistingChoice()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        await File.WriteAllTextAsync(
            store.DocumentPath,
            """
            {
              "appearance": "light",
              "density": "compact",
              "increaseTransparency": true,
              "meterStyle": "used",
              "resetDisplayMode": "exact"
            }
            """,
            new UTF8Encoding(false));

        var loaded = Assert.IsType<AppearanceSettingsLoadResult.Loaded>(await store.LoadAsync());

        Assert.True(loaded.RequiresMigration);
        Assert.Equal(
            new AppearanceSettings(
                AppThemeMode.Light,
                AppDensityMode.Compact,
                true,
                UsageDisplayMode.Used,
                ResetTimeDisplayMode.Exact),
            loaded.Settings);
    }

    [Fact]
    public async Task LegacyDocumentDefaultsOnlyMissingChoices()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        await File.WriteAllTextAsync(
            store.DocumentPath,
            """{"appearance":"dark"}""",
            new UTF8Encoding(false));

        var loaded = Assert.IsType<AppearanceSettingsLoadResult.Loaded>(await store.LoadAsync());

        Assert.True(loaded.RequiresMigration);
        Assert.Equal(AppThemeMode.Dark, loaded.Settings.Theme);
        Assert.Equal(AppDensityMode.Regular, loaded.Settings.Density);
        Assert.False(loaded.Settings.IncreaseTransparency);
        Assert.Equal(UsageDisplayMode.Remaining, loaded.Settings.UsageDisplay);
        Assert.Equal(ResetTimeDisplayMode.Relative, loaded.Settings.ResetTimeDisplay);
    }

    [Fact]
    public async Task SavingMigratedSettingsRewritesVersionOneWithoutValueLoss()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        await File.WriteAllTextAsync(
            store.DocumentPath,
            """{"appearance":"dark","density":"compact","meterStyle":"used"}""",
            new UTF8Encoding(false));
        var legacy = Assert.IsType<AppearanceSettingsLoadResult.Loaded>(await store.LoadAsync());

        await store.SaveAsync(legacy.Settings);
        var migrated = Assert.IsType<AppearanceSettingsLoadResult.Loaded>(await store.LoadAsync());

        Assert.False(migrated.RequiresMigration);
        Assert.Equal(legacy.Settings, migrated.Settings);
    }

    [Fact]
    public async Task FutureVersionIsPreservedAndCannotBeOverwritten()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        byte[] original = Encoding.UTF8.GetBytes("""{"schemaVersion":99,"future":true}""");
        await File.WriteAllBytesAsync(store.DocumentPath, original);

        var load = Assert.IsType<AppearanceSettingsLoadResult.UnsupportedVersion>(
            await store.LoadAsync());
        var save = Assert.IsType<AppearanceSettingsSaveResult.RefusedUnsupportedVersion>(
            await store.SaveAsync(AppearanceSettings.Default));

        Assert.Equal(99, load.SchemaVersion);
        Assert.Equal(99, save.SchemaVersion);
        Assert.Equal(original, await File.ReadAllBytesAsync(store.DocumentPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.corrupt-*"));
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"schemaVersion\":1,\"theme\":\"unknown\",\"density\":\"regular\",\"increaseTransparency\":false,\"usageDisplay\":\"remaining\",\"resetTimeDisplay\":\"relative\"}")]
    [InlineData("{\"schemaVersion\":1,\"theme\":\"system\"}")]
    public async Task InvalidDocumentIsQuarantinedWithoutByteLoss(string json)
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        byte[] original = Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(store.DocumentPath, original);

        var corrupt = Assert.IsType<AppearanceSettingsLoadResult.Corrupt>(await store.LoadAsync());

        Assert.False(File.Exists(store.DocumentPath));
        Assert.DoesNotContain(Path.DirectorySeparatorChar, corrupt.QuarantineFileName);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, corrupt.QuarantineFileName);
        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(Path.Combine(directory.Path, corrupt.QuarantineFileName)));
    }

    [Fact]
    public async Task OversizedDocumentIsQuarantinedWithoutByteLoss()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        byte[] original = new byte[AppearanceSettingsStore.MaxDocumentBytes + 1];
        Random.Shared.NextBytes(original);
        await File.WriteAllBytesAsync(store.DocumentPath, original);

        var corrupt = Assert.IsType<AppearanceSettingsLoadResult.Corrupt>(await store.LoadAsync());

        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(Path.Combine(directory.Path, corrupt.QuarantineFileName)));
    }

    [Fact]
    public async Task SaveRefusesToReplaceAnInvalidExistingDocument()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        byte[] original = Encoding.UTF8.GetBytes("{broken");
        await File.WriteAllBytesAsync(store.DocumentPath, original);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(AppearanceSettings.Default));

        Assert.Equal(original, await File.ReadAllBytesAsync(store.DocumentPath));
    }

    [Fact]
    public async Task PreCancelledOperationsDoNotTouchDisk()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.LoadAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(AppearanceSettings.Default, cancellation.Token));

        Assert.False(File.Exists(store.DocumentPath));
    }

    private static AppearanceSettingsStore CreateStore(TempDirectory directory) =>
        new(Path.Combine(directory.Path, AppearanceSettingsStore.DefaultFileName));

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "TokenUsage.Appearance.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
