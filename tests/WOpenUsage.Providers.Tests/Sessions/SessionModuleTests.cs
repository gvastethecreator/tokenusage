using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Dashboard;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.App.ViewModels.Surfaces;
using WOpenUsage.Core.Alerts;
using WOpenUsage.Core.Appearance;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Layout;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Session;
using WOpenUsage.Core.Usage;

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
    public async Task AppearanceSurfaceOwnsBindableSelectionAndSerialSave()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Root, AppearanceSettingsStore.DefaultFileName);
        var session = new AppearanceSession(
            new AppearanceSettingsStore(path, TimeProvider.System));
        var surface = new AppearanceSurfaceViewModel(session, key => key);
        await surface.Initialization;

        surface.SelectedTheme = surface.ThemeOptions.Single(option =>
            option.Value == AppThemeMode.Dark);
        surface.SelectedDensity = surface.DensityOptions.Single(option =>
            option.Value == AppDensityMode.Compact);
        await surface.WaitForPendingSaveAsync();

        Assert.Equal(AppThemeMode.Dark, surface.Settings.Theme);
        Assert.Equal(AppDensityMode.Compact, surface.Settings.Density);
        Assert.True(surface.IsEditable);
        var reloaded = new AppearanceSession(
            new AppearanceSettingsStore(path, TimeProvider.System));
        await reloaded.InitializeAsync();
        Assert.Equal(AppThemeMode.Dark, reloaded.Settings.Theme);
        Assert.Equal(AppDensityMode.Compact, reloaded.Settings.Density);
    }

    [Fact]
    public void GeneralOptionsSurfaceOwnsLanguageAndSampleState()
    {
        var surface = new GeneralOptionsViewModel(
            key => key,
            "en-US",
            languageTag => languageTag == "es-ES");
        int modeChanges = 0;
        int scenarioChanges = 0;
        surface.SampleModeChanged += (_, _) => modeChanges++;
        surface.SampleScenarioChanged += (_, _) => scenarioChanges++;

        surface.SelectedLanguage = surface.LanguageOptions.Single(option =>
            option.LanguageTag == "es-ES");
        surface.IsSampleModeEnabled = true;
        surface.SelectedSampleScenario = surface.SampleScenarios.Single(option =>
            option.Value == SampleScenario.Partial);

        Assert.True(surface.IsLanguageRestartRequired);
        Assert.True(surface.IsSampleScenarioEnabled);
        Assert.Equal(SampleScenario.Partial, surface.SelectedSampleScenario.Value);
        Assert.Equal(1, modeChanges);
        Assert.Equal(1, scenarioChanges);
    }

    [Fact]
    public void OptionsNavigationOwnsDepthAndCloseRequest()
    {
        var navigation = new OptionsNavigationViewModel();
        int closeRequests = 0;
        navigation.CloseRequested += (_, _) => closeRequests++;

        navigation.ShowProvidersCommand.Execute(null);
        navigation.ShowProviderStatusCommand.Execute(null);
        navigation.NavigateBackCommand.Execute(null);
        Assert.Equal(OptionsSection.Providers, navigation.ActiveSection);

        navigation.NavigateBackCommand.Execute(null);
        Assert.Equal(OptionsSection.Home, navigation.ActiveSection);
        navigation.NavigateBackCommand.Execute(null);
        Assert.Equal(1, closeRequests);
    }

    [Fact]
    public async Task PersonalizationSurfaceOwnsProjectionMutationAndUndo()
    {
        using var folder = new TemporaryFolder();
        var editor = new DashboardLayoutEditor(new DashboardLayoutStore(
            Path.Combine(folder.Root, DashboardLayoutStore.DefaultFileName),
            TimeProvider.System));
        var surface = new PersonalizationSurfaceViewModel(editor, key => key);
        await surface.Initialization;
        DashboardSnapshot dashboard = SampleDashboardCatalog.Create(
            SampleScenario.Normal,
            key => key);
        _ = surface.Apply(dashboard);
        surface.LayoutChanged += (_, _) => _ = surface.Apply(dashboard);

        await surface.SetProviderVisibleAsync("codex", isVisible: false);

        Assert.False(surface.Providers.Single(provider => provider.ProviderId == "codex").IsVisible);
        Assert.True(surface.CanUndo);
        await surface.UndoAsync();
        Assert.True(surface.Providers.Single(provider => provider.ProviderId == "codex").IsVisible);
    }

    [Fact]
    public async Task ProviderStatusSurfaceOwnsRowsAndRefreshCommand()
    {
        int refreshCalls = 0;
        var surface = new ProviderStatusSurfaceViewModel(key => key);
        surface.BindRefresh(
            () =>
            {
                refreshCalls++;
                return Task.CompletedTask;
            });
        var local = new ProviderStatusRow(
            "grok",
            "Grok",
            "Detected",
            "Refresh",
            [],
            "ProviderStatus.grok");
        var localCodex = new ProviderStatusRow(
            "codex",
            "Codex",
            "Detected",
            "Refresh",
            [
                new("Quota", "Unused", "ProviderStatus.codex.Quota"),
                new("Usage", "Complete", "ProviderStatus.codex.Usage"),
                new("Spend", "Estimated", "ProviderStatus.codex.Spend"),
                new("Coverage", "100%", "ProviderStatus.codex.Coverage"),
            ],
            "ProviderStatus.codex");

        surface.Update(
            codexOutcome: null,
            hasPublishedDashboard: false,
            SampleDataState.Idle,
            [localCodex, local]);
        await surface.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["codex", "grok"],
            surface.Providers.Select(provider => provider.ProviderId));
        ProviderStatusRow codex = surface.Providers[0];
        Assert.Equal("Detected", codex.RootState);
        Assert.Equal(
            "ProviderStatusUnavailable",
            codex.Capabilities.Single(capability =>
                capability.AutomationId == "ProviderStatus.codex.Quota").Value);
        Assert.Equal(
            "Complete",
            codex.Capabilities.Single(capability =>
                capability.AutomationId == "ProviderStatus.codex.Usage").Value);
        Assert.Equal(1, refreshCalls);
    }

    [Fact]
    public async Task DashboardSurfaceOwnsSampleRefreshAndVisibleState()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));
        var general = new GeneralOptionsViewModel(
            key => key,
            "en-US",
            _ => false);
        var appearance = new AppearanceSurfaceViewModel(
            new AppearanceSession(new AppearanceSettingsStore(
                Path.Combine(folder.Root, "appearance.json"),
                clock)),
            key => key);
        await appearance.Initialization;
        var personalization = new PersonalizationSurfaceViewModel(
            new DashboardLayoutEditor(new DashboardLayoutStore(
                Path.Combine(folder.Root, "layout.json"),
                clock)),
            key => key);
        await personalization.Initialization;
        var providerStatus = new ProviderStatusSurfaceViewModel(key => key);
        await using AppSessionHost appSession = CreateAppSession(folder.Root, clock);
        var live = new LiveDashboardSession(
            appSession,
            new LocalUsageCoordinator(
                Path.Combine(folder.Root, "usage.db"),
                new EmptyUsageSource(),
                clock));
        using var surface = new DashboardSurfaceViewModel(
            new SampleDashboardSession(new SampleRefreshCoordinator(
                Path.Combine(folder.Root, "sample"),
                clock,
                providerDelay: TimeSpan.Zero)),
            live,
            general,
            appearance,
            personalization,
            providerStatus,
            key => key,
            synchronizationContext: null);
        var published = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        surface.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(surface.ResultSurface)
                && surface.ResultSurface == FlyoutSurfaceState.Sample)
            {
                published.TrySetResult();
            }
        };

        general.IsSampleModeEnabled = true;
        await published.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FlyoutSurfaceState.Sample, surface.ResultSurface);
        Assert.NotNull(surface.ActiveSample);
        Assert.False(surface.IsSessionRefreshing);
    }

    private static AppSessionHost CreateAppSession(string root, TimeProvider clock)
    {
        var provider = new EmptyProvider();
        var refresh = new ProviderRefreshHost(
        [
            new ProviderRefreshRegistration(
                provider,
                new SnapshotStore(Path.Combine(root, "provider-cache.json"), clock)),
        ], clock);
        var alerts = new AlertHost(
            new AlertDecisionStore(Path.Combine(root, "alert-decisions.json"), clock),
            new AlertSettingsStore(Path.Combine(root, "alert-settings.json"), clock));
        return new AppSessionHost(refresh, alerts, clock);
    }

    private sealed class EmptyProvider : IProviderRuntime
    {
        public ProviderDescriptor Descriptor { get; } =
            new(new ProviderId("empty"), "Empty");

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult<ProviderOutcome>(new ProviderOutcome.NotConfigured("empty"));
    }

    private sealed class EmptyUsageSource : IUsageEventSource
    {
        public AgentId AgentId { get; } = new("empty");

        public SourceKind SourceKind => SourceKind.Synthetic;

        public Task<UsageSourceReadResult> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData));
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
