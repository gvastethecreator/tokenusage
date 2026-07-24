using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Appearance;
using WOpenUsage.Core.Layout;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Providers.Tests.Sessions;

public sealed class SessionModuleTests
{
    [Fact]
    public async Task AppearanceSessionInitializeAndSaveRoundTripsSettings()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Root, AppearanceSettingsStore.DefaultFileName);
        var store = new AppearanceSettingsStore(path, TimeProvider.System);
        var session = new AppearanceSession(store);

        await session.InitializeAsync();
        Assert.Equal(AppearanceSessionLoadKind.Defaults, session.LastLoadKind);
        Assert.True(session.IsEditable);

        var settings = new AppearanceSettings(
            AppThemeMode.Dark,
            AppDensityMode.Compact,
            increaseTransparency: true,
            UsageDisplayMode.Used,
            ResetTimeDisplayMode.Exact);
        AppearanceSessionSaveKind save = await session.SaveAsync(settings);
        Assert.Equal(AppearanceSessionSaveKind.Saved, save);
        Assert.Equal(AppThemeMode.Dark, session.Settings.Theme);

        var reloaded = new AppearanceSession(store);
        await reloaded.InitializeAsync();
        Assert.Equal(AppearanceSessionLoadKind.Loaded, reloaded.LastLoadKind);
        Assert.Equal(AppThemeMode.Dark, reloaded.Settings.Theme);
        Assert.Equal(AppDensityMode.Compact, reloaded.Settings.Density);
        Assert.True(reloaded.Settings.IncreaseTransparency);
    }

    [Fact]
    public async Task DashboardLayoutEditorMutateAndUndoUsesEditorAsSolePath()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Root, DashboardLayoutStore.DefaultFileName);
        var store = new DashboardLayoutStore(path, TimeProvider.System);
        var editor = new DashboardLayoutEditor(store);

        await editor.InitializeAsync();
        Assert.Equal(DashboardLayoutEditorLoadKind.Empty, editor.LastLoadKind);

        DashboardLayout next = new(
        [
            new ProviderLayoutPreference(
                new ProviderId("codex"),
                isVisible: true,
                isHighlighted: false,
                metrics:
                [
                    new MetricLayoutPreference(
                        new MetricId("session"),
                        isVisible: true,
                        isHighlighted: false,
                        isOnDemand: false),
                ]),
        ]);
        DashboardLayoutEditorSaveKind save = await editor.MutateAsync(_ => next);
        Assert.Equal(DashboardLayoutEditorSaveKind.Saved, save);
        Assert.Single(editor.Layout.Providers);
        Assert.True(editor.CanUndo);

        DashboardLayoutEditorSaveKind undo = await editor.UndoAsync();
        Assert.Equal(DashboardLayoutEditorSaveKind.Saved, undo);
        Assert.Empty(editor.Layout.Providers);
    }

    [Fact]
    public async Task SampleDashboardSessionRunAsyncPublishesScenarioDashboard()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero));
        var coordinator = new SampleRefreshCoordinator(
            folder.Root,
            clock,
            providerDelay: TimeSpan.Zero);
        var session = new SampleDashboardSession(coordinator);
        int changes = 0;
        await session.RunAsync(
            SampleScenario.Normal,
            forceRefresh: true,
            key => key,
            _ => changes++,
            CancellationToken.None);

        Assert.True(session.HasPublished);
        Assert.NotNull(session.LastDashboard);
        Assert.Equal(SampleScenario.Normal, session.ActiveScenario);
        Assert.True(changes > 0);
        Assert.NotEqual(SampleDataState.Idle, session.DataState);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "wou-session-" + Guid.NewGuid().ToString("N"));
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
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
