using TokenUsage.App.Services;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.App.ViewModels.Surfaces;
using TokenUsage.Core.Alerts;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Session;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Catalog;

namespace TokenUsage.Providers.Tests.Sessions;

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
        surface.SelectedDashboardVisualization = surface.DashboardVisualizationOptions.Single(
            option => option.Value == DashboardVisualizationMode.Heatmap);
        await surface.WaitForPendingSaveAsync();

        Assert.Equal(AppThemeMode.Dark, surface.Settings.Theme);
        Assert.Equal(AppDensityMode.Compact, surface.Settings.Density);
        Assert.Equal(
            DashboardVisualizationMode.Heatmap,
            surface.Settings.DashboardVisualization);
        Assert.True(surface.IsEditable);
        var reloaded = new AppearanceSession(
            new AppearanceSettingsStore(path, TimeProvider.System));
        await reloaded.InitializeAsync();
        Assert.Equal(AppThemeMode.Dark, reloaded.Settings.Theme);
        Assert.Equal(AppDensityMode.Compact, reloaded.Settings.Density);
        Assert.Equal(
            DashboardVisualizationMode.Heatmap,
            reloaded.Settings.DashboardVisualization);
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

        string[] expectedProviderIds =
        [
            "codex",
            "grok",
            .. ProviderModuleCatalog.Entries
                .Select(entry => entry.Id.Value)
                .Where(id => id is not "codex" and not "grok"),
        ];
        Assert.Equal(expectedProviderIds, surface.Providers.Select(provider => provider.ProviderId));
        string[] primaryProviderIds =
        [
            "codex",
            "claude",
            "grok",
            "opencode",
            "antigravity",
            "cursor",
            "copilot",
        ];
        Assert.Equal(
            primaryProviderIds,
            surface.PrimaryProviders.Select(provider => provider.ProviderId));
        Assert.Equal(
            surface.Providers.Count - primaryProviderIds.Length,
            surface.AdditionalProviders.Count);
        Assert.DoesNotContain(
            surface.AdditionalProviders,
            provider => primaryProviderIds.Contains(provider.ProviderId, StringComparer.Ordinal));
        Assert.True(surface.HasAdditionalProviders);
        Assert.False(surface.IsAdditionalProvidersExpanded);
        surface.IsAdditionalProvidersExpanded = true;
        Assert.Equal("ProviderStatusShowLessFormat", surface.AdditionalProvidersToggleLabel);
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
        Assert.Equal(
            "ProviderStatusPrepared",
            surface.Providers.Single(provider => provider.ProviderId == "openrouter").RootState);
        ProviderStatusRow claude = surface.Providers.Single(provider =>
            provider.ProviderId == "claude");
        Assert.Equal("ProviderStatusUnavailable", claude.RootState);
        Assert.Equal(ProviderStatusKind.Missing, claude.StatusKind);
        Assert.Equal("ProviderStatusSummaryMissing", claude.CompactState);
        Assert.Equal(
            "ProviderStatusUnavailable",
            claude.Capabilities.Single(capability =>
                capability.AutomationId == "ProviderStatus.claude.Usage").Value);
        Assert.Equal(
            "ProviderStatusBlocked",
            surface.Providers.Single(provider => provider.ProviderId == "zai").RootState);
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

        DashboardProviderLayoutRow codex = personalization.Providers.Single(provider =>
            provider.ProviderId == "codex");
        foreach (DashboardMetricLayoutRow metric in codex.Metrics)
        {
            await personalization.SetMetricVisibleAsync("codex", metric.MetricId, false);
        }

        Assert.Empty(surface.ActiveSample.Providers.Single(provider =>
            provider.ProviderId == "codex").Windows);
        Assert.NotEmpty(surface.GetProviderLimits("codex"));
    }

    [Fact]
    public async Task CompactProviderSummariesDistinguishMissingAndPartialData()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));
        var general = new GeneralOptionsViewModel(key => key, "en-US", _ => false);
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
                new SingleCodexUsageSource(clock),
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
            key => key switch
            {
                "CodexUsageMissing" => "No data",
                "ProviderStatusNoData" => "No data yet",
                "UsageReportCoveragePartial" => "Partial read",
                "UsageReportCoverageUnpriced" => "Unpriced usage",
                _ => key,
            },
            synchronizationContext: null);

        await surface.RefreshCommand.ExecuteAsync(null);

        DashboardProviderSummary codex = Assert.Single(
            surface.ProviderSummaries,
            summary => summary.ProviderId == "codex");
        Assert.True(codex.HasData);
        Assert.True(codex.IsPartial);
        Assert.True(codex.HasUnpricedData);
        Assert.NotEqual("No data", codex.CostText);

        DashboardProviderSummary openCode = Assert.Single(
            surface.ProviderSummaries,
            summary => summary.ProviderId == "opencode");
        Assert.False(openCode.HasData);
        Assert.Equal("No data", openCode.CostText);
        Assert.Equal("No data", openCode.TokensText);

        surface.SelectProvider("opencode");
        Assert.False(surface.SelectedProviderHasData);
        Assert.StartsWith("No data yet.", surface.SelectedProviderCoverageHintText);
        Assert.Equal("No data", surface.SelectedProviderCostText);
        Assert.Empty(surface.SelectedProviderTrend.Days);
        Assert.Empty(surface.SelectedProviderTrend.Series);

        surface.SelectProvider("codex");
        Assert.Contains("Partial read", surface.SelectedProviderCoverageHintText);
        Assert.Contains("Unpriced usage", surface.SelectedProviderCoverageHintText);
    }

    [Fact]
    public async Task LiveDashboardPublishesOfficialCodexLimitsWithLocalUsage()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 9, 18, 0, 0, TimeSpan.Zero));
        var general = new GeneralOptionsViewModel(key => key, "en-US", _ => false);
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
        await using AppSessionHost appSession = CreateCodexAppSession(folder.Root, clock);
        var live = new LiveDashboardSession(
            appSession,
            new LocalUsageCoordinator(
                Path.Combine(folder.Root, "usage.db"),
                new SingleCodexUsageSource(clock),
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

        await surface.RefreshCommand.ExecuteAsync(null);
        surface.SelectProvider("codex");

        Assert.Collection(
            surface.GlobalCodexLimits,
            weekly => Assert.Equal("SampleWindowWeekly", weekly.Title),
            spark => Assert.Equal("CodexWindowSpark", spark.Title));
        Assert.Equal(surface.GlobalCodexLimits, surface.SelectedProviderLimits);
        Assert.True(surface.HasGlobalCodexLimits);
        Assert.True(surface.SelectedProviderHasLimits);
    }

    [Fact]
    public async Task LiveDashboardStartPublishesOfficialCodexLimitsWithoutManualRefresh()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 9, 18, 0, 0, TimeSpan.Zero));
        var general = new GeneralOptionsViewModel(key => key, "en-US", _ => false);
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
        var codexProvider = new CodexLimitsProvider(clock, requireForceRefresh: true);
        await using AppSessionHost appSession = CreateCodexAppSession(
            folder.Root,
            clock,
            codexProvider);
        var live = new LiveDashboardSession(
            appSession,
            new LocalUsageCoordinator(
                Path.Combine(folder.Root, "usage.db"),
                new SingleCodexUsageSource(clock),
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

        await surface.StartAsync();
        surface.SelectProvider("codex");

        Assert.Collection(
            surface.GlobalCodexLimits,
            weekly => Assert.Equal("SampleWindowWeekly", weekly.Title),
            spark => Assert.Equal("CodexWindowSpark", spark.Title));
        Assert.True(surface.SelectedProviderHasLimits);
        Assert.True(codexProvider.SawForcedRefresh);
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

    private static AppSessionHost CreateCodexAppSession(
        string root,
        TimeProvider clock,
        IProviderRuntime? provider = null)
    {
        var refresh = new ProviderRefreshHost(
        [
            new ProviderRefreshRegistration(
                provider ?? new CodexLimitsProvider(clock),
                new SnapshotStore(Path.Combine(root, "codex-cache.json"), clock)),
        ], clock);
        var alerts = new AlertHost(
            new AlertDecisionStore(Path.Combine(root, "codex-alert-decisions.json"), clock),
            new AlertSettingsStore(Path.Combine(root, "codex-alert-settings.json"), clock));
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

    private sealed class CodexLimitsProvider(
        TimeProvider clock,
        bool requireForceRefresh = false) : IProviderRuntime
    {
        private static readonly DataProvenance Provenance = new(
            SourceKind.OfficialLocalApi,
            MeasurementKind.ProviderReported,
            "test-codex/1");

        public ProviderDescriptor Descriptor { get; } =
            new(new ProviderId("codex"), "Codex");

        public bool SawForcedRefresh { get; private set; }

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken)
        {
            SawForcedRefresh |= context.ForceRefresh;
            if (requireForceRefresh && !context.ForceRefresh)
            {
                return Task.FromResult<ProviderOutcome>(
                    new ProviderOutcome.NotConfigured("Force refresh required by the test provider."));
            }

            DateTimeOffset now = clock.GetUtcNow();
            var snapshot = new ProviderSnapshot(
                new ProviderId("codex"),
                "Codex",
                "Pro",
                now,
                now,
                "UTC",
                [
                    new ProgressMetricSnapshot(
                        new MetricId("quota.primary"),
                        97m,
                        100m,
                        now.AddDays(6),
                        Provenance),
                    new ScalarMetricSnapshot(
                        new MetricId("quota.primary.window-minutes"),
                        10080m,
                        "minutes",
                        Provenance),
                    new ProgressMetricSnapshot(
                        new MetricId("quota.codex-bengalfox.primary"),
                        0m,
                        100m,
                        now.AddDays(7),
                        Provenance),
                    new ScalarMetricSnapshot(
                        new MetricId("quota.codex-bengalfox.primary.window-minutes"),
                        10080m,
                        "minutes",
                        Provenance),
                ],
                CoverageKind.Complete,
                1);
            return Task.FromResult<ProviderOutcome>(new ProviderOutcome.Success(snapshot));
        }
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

    private sealed class SingleCodexUsageSource(TimeProvider clock) : IUsageEventSource
    {
        public AgentId AgentId { get; } = new("codex");

        public SourceKind SourceKind => SourceKind.Synthetic;

        public Task<UsageSourceReadResult> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UsageSourceReadResult(
                [new UsageEvent(
                    new UsageEventKey(new string('a', 64)),
                    AgentId,
                    new ModelProviderId("openai"),
                    new ModelId("gpt-5"),
                    clock.GetUtcNow(),
                    "UTC",
                    new TokenBreakdown(100, 10, 0, 0, 0),
                    CostObservation.ProviderReported(1m),
                    "test-v1",
                    CoverageKind.Partial),
                new UsageEvent(
                    new UsageEventKey(new string('b', 64)),
                    AgentId,
                    new ModelProviderId("openai"),
                    new ModelId("gpt-5-mini"),
                    clock.GetUtcNow(),
                    "UTC",
                    new TokenBreakdown(50, 5, 0, 0, 0),
                    CostObservation.Unavailable(),
                    "test-v1",
                    CoverageKind.Unpriced)],
                UsageSourceReadStatus.Complete));
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
