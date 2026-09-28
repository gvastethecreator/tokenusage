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

    [Theory]
    [InlineData(ReportChartStyle.Area, "area")]
    [InlineData(ReportChartStyle.TwoHourBars, "twohourbars")]
    public async Task SaveAndLoadRoundTripUsesVersionFiveContract(ReportChartStyle chartStyle, string serializedStyle)
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory);
        var expected = new AppearanceSettings(
            AppThemeMode.Dark,
            AppDensityMode.Compact,
            increaseTransparency: true,
            UsageDisplayMode.Used,
            ResetTimeDisplayMode.Exact,
            DashboardVisualizationMode.Heatmap,
            new TrayPopoverSettings(
                TrayPopoverMetric.SpendLast30Days,
                TrayPopoverMetric.TokensLast30Days,
                providerCount: 2,
                showProviderName: true,
                isEnabled: false),
            chartStyle,
            ReportChartGrouping.Model);

        Assert.IsType<AppearanceSettingsSaveResult.Saved>(await store.SaveAsync(expected));
        var loaded = Assert.IsType<AppearanceSettingsLoadResult.Loaded>(await store.LoadAsync());

        Assert.Equal(expected, loaded.Settings);
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(store.DocumentPath));
        Assert.Equal(5, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(serializedStyle, json.RootElement.GetProperty("reportChartStyle").GetString());
        Assert.Equal("model", json.RootElement.GetProperty("reportChartGrouping").GetString());
        Assert.True(json.RootElement.GetProperty("markReportBestValues").GetBoolean());
        Assert.True(expected.MarkReportBestValues);
        Assert.Equal("dark", json.RootElement.GetProperty("theme").GetString());
        Assert.Equal(
            "heatmap",
            json.RootElement.GetProperty("dashboardVisualization").GetString());
        JsonElement popover = json.RootElement.GetProperty("trayPopover");
        Assert.Equal("spendlast30days", popover.GetProperty("primaryMetric").GetString());
        Assert.Equal("tokenslast30days", popover.GetProperty("secondaryMetric").GetString());
        Assert.Equal(2, popover.GetProperty("providerCount").GetInt32());
        Assert.True(popover.GetProperty("showProviderName").GetBoolean());
        Assert.False(popover.GetProperty("isEnabled").GetBoolean());
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
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
    [InlineData("{\"schemaVersion\":2,\"theme\":\"unknown\",\"density\":\"regular\",\"increaseTransparency\":false,\"usageDisplay\":\"remaining\",\"resetTimeDisplay\":\"relative\",\"dashboardVisualization\":\"list\"}")]
    [InlineData("{\"schemaVersion\":1,\"theme\":\"system\"}")]
    // An unreleased older schema is set aside like any unreadable document.
    [InlineData("{\"schemaVersion\":4,\"theme\":\"light\",\"density\":\"compact\",\"increaseTransparency\":true,\"usageDisplay\":\"used\",\"resetTimeDisplay\":\"exact\",\"dashboardVisualization\":\"donut\",\"trayPopover\":{\"primaryMetric\":\"spendlast30days\",\"secondaryMetric\":\"none\",\"providerCount\":3,\"showProviderName\":true,\"isEnabled\":false}}")]
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
