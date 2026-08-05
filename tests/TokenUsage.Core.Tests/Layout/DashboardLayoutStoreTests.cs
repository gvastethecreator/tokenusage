using System.Text;
using System.Text.Json;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Tests.Layout;

public sealed class DashboardLayoutStoreTests
{
    private static readonly ProviderId ProviderA = new("provider-a");
    private static readonly ProviderId ProviderB = new("provider-b");
    private static readonly MetricId MetricX = new("metric-x");
    private static readonly MetricId MetricY = new("metric-y");

    private const string SentinelSecret = "SENTINEL_SECRET_DO_NOT_PERSIST_9f3c2a1b";

    [Fact]
    public void ConstructorCanonicalizesPathAndRequiresFileName()
    {
        using var dir = new TempDirectory();
        var relative = Path.Combine(dir.Path, "..", Path.GetFileName(dir.Path), "dashboard-layout.v1.json");
        var store = new DashboardLayoutStore(relative);

        Assert.Equal(Path.GetFullPath(relative), store.DocumentPath);
        Assert.Equal(DashboardLayoutStore.DefaultFileName, Path.GetFileName(store.DocumentPath));

        Assert.Throws<ArgumentException>(() => new DashboardLayoutStore(dir.Path));
        Assert.Throws<ArgumentException>(() => new DashboardLayoutStore("   "));
    }

    [Fact]
    public async Task LoadAsyncMissingFileReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var store = new DashboardLayoutStore(Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName));

        var result = await store.LoadAsync();

        Assert.IsType<DashboardLayoutLoadResult.Empty>(result);
        Assert.False(File.Exists(store.DocumentPath));
    }

    [Fact]
    public async Task SaveAndLoadRoundTripPreservesOrderAndFlags()
    {
        using var dir = new TempDirectory();
        var store = new DashboardLayoutStore(Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName));
        var layout = CreateSampleLayout();

        var save = await store.SaveAsync(layout);
        Assert.IsType<DashboardLayoutSaveResult.Saved>(save);

        var load = await store.LoadAsync();
        var loaded = Assert.IsType<DashboardLayoutLoadResult.Loaded>(load);
        Assert.Equal(layout, loaded.Layout);
    }

    [Fact]
    public async Task LoadAsyncIndependentValidJsonLoadsWithoutPriorSave()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var json =
            """
            {"schemaVersion":1,"providers":[{"providerId":"provider-a","isVisible":true,"isHighlighted":false,"metrics":[{"metricId":"metric-x","isVisible":true,"isHighlighted":false}]},{"providerId":"provider-b","isVisible":false,"isHighlighted":true,"metrics":[{"metricId":"metric-y","isVisible":false,"isHighlighted":true}]}]}
            """;
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));

        var store = new DashboardLayoutStore(path);
        var load = await store.LoadAsync();
        var loaded = Assert.IsType<DashboardLayoutLoadResult.Loaded>(load);

        Assert.Equal(2, loaded.Layout.Providers.Count);
        Assert.Equal(ProviderA, loaded.Layout.Providers[0].ProviderId);
        Assert.Equal(ProviderB, loaded.Layout.Providers[1].ProviderId);
        Assert.False(loaded.Layout.Providers[1].IsVisible);
        Assert.True(loaded.Layout.Providers[1].IsHighlighted);
        Assert.Equal(MetricY, loaded.Layout.Providers[1].Metrics[0].MetricId);
        Assert.False(loaded.Layout.Providers[1].Metrics[0].IsVisible);
        Assert.True(loaded.Layout.Providers[1].Metrics[0].IsHighlighted);
        Assert.False(loaded.Layout.Providers[1].Metrics[0].IsOnDemand);
    }

    [Fact]
    public async Task LoadAsyncLegacyOverHighlightLimitPreservesVersionOneDocument()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var json =
            """
            {"schemaVersion":1,"providers":[{"providerId":"provider-a","isVisible":true,"isHighlighted":false,"metrics":[{"metricId":"metric-x","isVisible":true,"isHighlighted":true},{"metricId":"metric-y","isVisible":true,"isHighlighted":true},{"metricId":"metric-z","isVisible":true,"isHighlighted":true}]}]}
            """;
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));

        var store = new DashboardLayoutStore(path);
        DashboardLayoutLoadResult load = await store.LoadAsync();
        DashboardLayout loaded = Assert.IsType<DashboardLayoutLoadResult.Loaded>(load).Layout;

        Assert.Equal(3, loaded.Providers[0].Metrics.Count(metric => metric.IsHighlighted));
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.corrupt-*"));

        DashboardLayout repaired = loaded.SetMetricHighlighted(ProviderA, MetricX, false);
        Assert.Equal(2, repaired.Providers[0].Metrics.Count(metric => metric.IsHighlighted));
    }

    [Fact]
    public async Task LoadAsyncEmptyFileQuarantinesPreservingBytes()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var original = Array.Empty<byte>();
        await File.WriteAllBytesAsync(path, original);

        var store = new DashboardLayoutStore(path);
        var result = await store.LoadAsync();
        var corrupt = Assert.IsType<DashboardLayoutLoadResult.Corrupt>(result);

        Assert.False(corrupt.QuarantineFileName.Contains(Path.DirectorySeparatorChar));
        Assert.False(corrupt.QuarantineFileName.Contains(Path.AltDirectorySeparatorChar));
        Assert.DoesNotContain(":", corrupt.QuarantineFileName);
        Assert.False(File.Exists(path));

        var quarantinePath = Path.Combine(dir.Path, corrupt.QuarantineFileName);
        Assert.True(File.Exists(quarantinePath));
        Assert.Equal(original, await File.ReadAllBytesAsync(quarantinePath));
    }

    [Fact]
    public async Task LoadAsyncCorruptJsonQuarantinesPreservingBytes()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var original = Encoding.UTF8.GetBytes("{not-json");
        await File.WriteAllBytesAsync(path, original);

        var store = new DashboardLayoutStore(path);
        var result = await store.LoadAsync();
        var corrupt = Assert.IsType<DashboardLayoutLoadResult.Corrupt>(result);

        Assert.False(File.Exists(path));
        var quarantinePath = Path.Combine(dir.Path, corrupt.QuarantineFileName);
        Assert.Equal(original, await File.ReadAllBytesAsync(quarantinePath));
    }

    [Fact]
    public async Task LoadAsyncOversizedQuarantinesPreservingBytes()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var original = Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":1,\"providers\":[],\"pad\":\"" +
            new string('x', DashboardLayoutStore.MaxDocumentBytes) +
            "\"}");
        Assert.True(original.Length > DashboardLayoutStore.MaxDocumentBytes);
        await File.WriteAllBytesAsync(path, original);

        var store = new DashboardLayoutStore(path);
        var result = await store.LoadAsync();
        var corrupt = Assert.IsType<DashboardLayoutLoadResult.Corrupt>(result);

        Assert.False(File.Exists(path));
        var quarantinePath = Path.Combine(dir.Path, corrupt.QuarantineFileName);
        Assert.Equal(original, await File.ReadAllBytesAsync(quarantinePath));
    }

    [Fact]
    public async Task LoadAsyncLargeOversizedDocumentQuarantinesAllBytes()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var original = new byte[DashboardLayoutStore.MaxDocumentBytes * 4 + 17];
        Random.Shared.NextBytes(original);
        await File.WriteAllBytesAsync(path, original);

        var store = new DashboardLayoutStore(path);
        var result = await store.LoadAsync();
        var corrupt = Assert.IsType<DashboardLayoutLoadResult.Corrupt>(result);

        Assert.False(File.Exists(path));
        var quarantinePath = Path.Combine(dir.Path, corrupt.QuarantineFileName);
        Assert.Equal(original, await File.ReadAllBytesAsync(quarantinePath));
    }

    [Fact]
    public async Task LoadAsyncDuplicateProviderIdsQuarantinesPreservingBytes()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var original = Encoding.UTF8.GetBytes(
            """
            {"schemaVersion":1,"providers":[{"providerId":"provider-a","isVisible":true,"isHighlighted":false,"metrics":[]},{"providerId":"provider-a","isVisible":true,"isHighlighted":false,"metrics":[]}]}
            """);
        await File.WriteAllBytesAsync(path, original);

        var store = new DashboardLayoutStore(path);
        var result = await store.LoadAsync();
        var corrupt = Assert.IsType<DashboardLayoutLoadResult.Corrupt>(result);

        Assert.False(File.Exists(path));
        var quarantinePath = Path.Combine(dir.Path, corrupt.QuarantineFileName);
        Assert.Equal(original, await File.ReadAllBytesAsync(quarantinePath));
    }

    [Fact]
    public async Task LoadAsyncFutureVersionDoesNotMoveOrModifyBytes()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var original = Encoding.UTF8.GetBytes(
            """
            {"schemaVersion":99,"providers":[{"providerId":"provider-a","isVisible":true,"isHighlighted":false,"metrics":[]}]}
            """);
        await File.WriteAllBytesAsync(path, original);

        var store = new DashboardLayoutStore(path);
        var result = await store.LoadAsync();
        var unsupported = Assert.IsType<DashboardLayoutLoadResult.UnsupportedVersion>(result);
        Assert.Equal(99, unsupported.SchemaVersion);

        Assert.True(File.Exists(path));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.corrupt-*"));
    }

    [Fact]
    public async Task LoadAsyncVersionOneMigratesWithoutProviderColors()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        await File.WriteAllTextAsync(
            path,
            """
            {"schemaVersion":1,"providers":[{"providerId":"provider-a","isVisible":true,"isHighlighted":false,"metrics":[{"metricId":"metric-x","isVisible":true,"isHighlighted":false,"isOnDemand":false}]}]}
            """,
            new UTF8Encoding(false));

        var store = new DashboardLayoutStore(path);
        var loaded = Assert.IsType<DashboardLayoutLoadResult.Loaded>(await store.LoadAsync());
        ProviderLayoutPreference provider = Assert.Single(loaded.Layout.Providers);

        Assert.Equal(ProviderA, provider.ProviderId);
        Assert.Null(provider.ColorHex);
        Assert.IsType<DashboardLayoutSaveResult.Saved>(await store.SaveAsync(loaded.Layout));
        using JsonDocument migrated = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(2, migrated.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task SaveAsyncFutureVersionDocumentRefusesWithoutByteChanges()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var original = Encoding.UTF8.GetBytes(
            """
            {"schemaVersion":7,"providers":[]}
            """);
        await File.WriteAllBytesAsync(path, original);

        var store = new DashboardLayoutStore(path);
        var save = await store.SaveAsync(CreateSampleLayout());
        var refused = Assert.IsType<DashboardLayoutSaveResult.RefusedUnsupportedVersion>(save);
        Assert.Equal(7, refused.SchemaVersion);

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task SaveAsyncOversizedExistingDocumentPreservesAllBytes()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var original = new byte[DashboardLayoutStore.MaxDocumentBytes + 1];
        Random.Shared.NextBytes(original);
        await File.WriteAllBytesAsync(path, original);

        var store = new DashboardLayoutStore(path);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(CreateSampleLayout()));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task SaveAsyncCorruptVersionOneDocumentPreservesAllBytes()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var original = Encoding.UTF8.GetBytes(
            """
            {"schemaVersion":1,"providers":null}
            """);
        await File.WriteAllBytesAsync(path, original);

        var store = new DashboardLayoutStore(path);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(CreateSampleLayout()));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task LoadAsyncCancellationBeforeWorkThrows()
    {
        using var dir = new TempDirectory();
        var store = new DashboardLayoutStore(Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(cts.Token));
    }

    [Fact]
    public async Task SaveAsyncCancellationBeforeWorkThrows()
    {
        using var dir = new TempDirectory();
        var store = new DashboardLayoutStore(Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(CreateSampleLayout(), cts.Token));
    }

    [Fact]
    public async Task SaveAsyncInterruptedTempDoesNotReplaceLastGoodDocument()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var store = new DashboardLayoutStore(path);

        var good = CreateSampleLayout();
        var saved = await store.SaveAsync(good);
        Assert.IsType<DashboardLayoutSaveResult.Saved>(saved);
        var goodBytes = await File.ReadAllBytesAsync(path);

        // Simulate an interrupted write: leave a temp sibling without replacing the document.
        var tempSibling = Path.Combine(
            dir.Path,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempSibling, "{\"partial\":true}", new UTF8Encoding(false));

        Assert.True(File.Exists(tempSibling));
        Assert.Equal(goodBytes, await File.ReadAllBytesAsync(path));

        var reload = await store.LoadAsync();
        var loaded = Assert.IsType<DashboardLayoutLoadResult.Loaded>(reload);
        Assert.Equal(good, loaded.Layout);
    }

    [Fact]
    public async Task SaveAsyncOutputJsonHasSchemaVersionCamelCaseAndDeterministicOrder()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var store = new DashboardLayoutStore(path);

        var layout = new DashboardLayout(
        [
            new ProviderLayoutPreference(
                ProviderB,
                isVisible: false,
                isHighlighted: true,
                [new MetricLayoutPreference(MetricY, false, true)]),
            new ProviderLayoutPreference(
                ProviderA,
                isVisible: true,
                isHighlighted: false,
                [
                    new MetricLayoutPreference(MetricX, true, false),
                    new MetricLayoutPreference(MetricY, true, false, isOnDemand: true),
                ]),
        ]);

        await store.SaveAsync(layout);
        var first = await File.ReadAllTextAsync(path, new UTF8Encoding(false));
        await store.SaveAsync(layout);
        var second = await File.ReadAllTextAsync(path, new UTF8Encoding(false));

        Assert.Equal(first, second);

        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());

        // camelCase property names only
        Assert.True(root.TryGetProperty("providers", out var providers));
        Assert.False(root.TryGetProperty("Providers", out _));
        Assert.Equal(2, providers.GetArrayLength());

        var p0 = providers[0];
        Assert.Equal("provider-b", p0.GetProperty("providerId").GetString());
        Assert.False(p0.GetProperty("isVisible").GetBoolean());
        Assert.True(p0.GetProperty("isHighlighted").GetBoolean());
        Assert.Equal("metric-y", p0.GetProperty("metrics")[0].GetProperty("metricId").GetString());

        var p1 = providers[1];
        Assert.Equal("provider-a", p1.GetProperty("providerId").GetString());
        var metrics = p1.GetProperty("metrics");
        Assert.Equal(2, metrics.GetArrayLength());
        Assert.Equal("metric-x", metrics[0].GetProperty("metricId").GetString());
        Assert.Equal("metric-y", metrics[1].GetProperty("metricId").GetString());
        Assert.False(metrics[0].GetProperty("isOnDemand").GetBoolean());
        Assert.True(metrics[1].GetProperty("isOnDemand").GetBoolean());

        // No enum-style strings for flags
        Assert.DoesNotContain("True", first, StringComparison.Ordinal);
        Assert.DoesNotContain("False", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsyncNeverPersistsSentinelSecretOutsideIds()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, DashboardLayoutStore.DefaultFileName);
        var store = new DashboardLayoutStore(path);

        // Sentinel is only present in test memory, never as an id.
        _ = SentinelSecret;
        var layout = CreateSampleLayout();
        await store.SaveAsync(layout);

        var text = await File.ReadAllTextAsync(path, new UTF8Encoding(false));
        Assert.DoesNotContain(SentinelSecret, text, StringComparison.Ordinal);
        Assert.Contains("provider-a", text, StringComparison.Ordinal);
        Assert.Contains("metric-x", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaConstantsMatchContract()
    {
        Assert.Equal(2, DashboardLayoutStore.SchemaVersion);
        Assert.Equal("dashboard-layout.v1.json", DashboardLayoutStore.DefaultFileName);
        Assert.Equal(64 * 1024, DashboardLayoutStore.MaxDocumentBytes);
        Assert.Equal(16, DashboardLayoutStore.MaxJsonDepth);
    }

    private static DashboardLayout CreateSampleLayout()
    {
        return new DashboardLayout(
        [
            new ProviderLayoutPreference(
                ProviderA,
                isVisible: true,
                isHighlighted: false,
                [
                    new MetricLayoutPreference(MetricX, true, false),
                    new MetricLayoutPreference(MetricY, false, true, isOnDemand: true),
                ],
                colorHex: "#10A37F"),
            new ProviderLayoutPreference(
                ProviderB,
                isVisible: false,
                isHighlighted: true,
                [new MetricLayoutPreference(MetricX, true, false)]),
        ]);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tokenusage-layout-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for temp test dirs.
            }
        }
    }
}
