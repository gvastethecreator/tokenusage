namespace WOpenUsage.Architecture.Tests;

public sealed class ArchitectureRulesTests
{
    [Fact]
    public void RepositoryProjectGraphMatchesAdrAllowList()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        ProjectReferenceGraph graph = ProjectReferenceGraph.LoadProductProjects(repoRoot);
        IReadOnlyList<string> forbidden = ArchitectureRules.FindForbiddenEdges(graph);

        Assert.True(
            forbidden.Count == 0,
            "Forbidden project references:" + Environment.NewLine + string.Join(Environment.NewLine, forbidden));

        string coreProject = Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.Core",
            "WOpenUsage.Core.csproj");
        IReadOnlyList<string> coreIssues = ArchitectureRules.FindCoreIsolationViolations(coreProject);

        Assert.True(
            coreIssues.Count == 0,
            "Core isolation violations:" + Environment.NewLine + string.Join(Environment.NewLine, coreIssues));
    }

    [Fact]
    public void RuleDetectsInvertedCoreToProvidersEdge()
    {
        var invalid = new ProjectReferenceGraph(
            ["WOpenUsage.Core", "WOpenUsage.Providers"],
            [("WOpenUsage.Core", "WOpenUsage.Providers")]);

        IReadOnlyList<string> forbidden = ArchitectureRules.FindForbiddenEdges(invalid);

        Assert.Contains(
            forbidden,
            violation => string.Equals(
                violation,
                "WOpenUsage.Core -> WOpenUsage.Providers",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuleDetectsMissingProductProject()
    {
        var incomplete = new ProjectReferenceGraph(
            [
                "WOpenUsage.Core",
                "WOpenUsage.Platform.Windows",
                "WOpenUsage.Providers",
                "WOpenUsage.App",
            ],
            []);

        IReadOnlyList<string> forbidden = ArchitectureRules.FindForbiddenEdges(incomplete);

        Assert.Contains(
            "Missing product project: WOpenUsage.Cli",
            forbidden,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuleBlocksFutureLocalApiFromReferencingCli()
    {
        var invalid = new ProjectReferenceGraph(
            ArchitectureRules.AllowedReferences.Keys,
            [("WOpenUsage.LocalApi", "WOpenUsage.Cli")]);

        IReadOnlyList<string> forbidden = ArchitectureRules.FindForbiddenEdges(invalid);

        Assert.Contains(
            "WOpenUsage.LocalApi -> WOpenUsage.Cli",
            forbidden,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionCompositionUsesCanonicalWindowsProviderCatalog()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string composition = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.App",
            "Composition",
            "AppComposition.cs"));

        Assert.Contains("WindowsProviderCatalog.CreateComposition", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("SyntheticUsageEventSource", composition, StringComparison.Ordinal);

        string providerCatalog = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.Runtime.Windows",
            "Providers",
            "WindowsProviderCatalog.cs"));
        Assert.Contains("new ClaudeUsageEventSource", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("new CodexUsageEventSource", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("new GrokUsageEventSource", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("new OpenCodeUsageEventSource", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("new CodexRefreshCoordinator", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("CreateVercelBinding", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("isEnabledByDefault: false", providerCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("SyntheticUsageEventSource", providerCatalog, StringComparison.Ordinal);

        string cliLimits = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.Cli",
            "LocalLimitsCliAccess.cs"));
        string cliDiagnostics = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.Cli",
            "LocalProviderDiagnosticsAccess.cs"));
        string diagnosticsQuery = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.Runtime.Windows",
            "Automation",
            "WindowsProviderDiagnosticsQuery.cs"));
        Assert.Contains("WindowsProviderCatalog", cliLimits, StringComparison.Ordinal);
        Assert.Contains("WindowsProviderCatalog", diagnosticsQuery, StringComparison.Ordinal);

        string mainPage = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.App",
            "MainPage.xaml.cs"));
        Assert.Contains("AppComposition.CreateFlyoutViewModel", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("new FlyoutViewModel(", mainPage, StringComparison.Ordinal);

        string flyoutViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.App",
            "ViewModels",
            "FlyoutViewModel.cs"));
        Assert.DoesNotContain("Vercel.RefreshAsync", flyoutViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("VercelGatewaySettingsViewModel", flyoutViewModel, StringComparison.Ordinal);
        string flyoutConstructor = flyoutViewModel[..flyoutViewModel.IndexOf(
            "[ObservableProperty]",
            StringComparison.Ordinal)];
        Assert.DoesNotContain(
            "_ = RefreshDashboardAsync(scenario: null, forceRefresh: false)",
            flyoutConstructor,
            StringComparison.Ordinal);

        string sessionHost = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.Core",
            "Session",
            "AppSessionHost.cs"));
        Assert.DoesNotContain("Microsoft.UI", sessionHost, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.UI", sessionHost, StringComparison.Ordinal);

        string mainWindow = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.App",
            "MainWindow.xaml.cs"));
        Assert.Contains("RootPage.SessionHost.RefreshAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RootPage.ViewModel.RefreshCommand",
            mainWindow,
            StringComparison.Ordinal);

        string cliApplication = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.Cli",
            "CliApplication.cs"));
        Assert.Contains("new UsageQuery", cliApplication, StringComparison.Ordinal);
        Assert.Contains("new LimitsQuery", cliLimits, StringComparison.Ordinal);
        Assert.Contains(
            "new WindowsProviderDiagnosticsQuery",
            cliDiagnostics,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UsageHeatmapIsVisibleInsideDetailsWithoutANestedExpander()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.App",
            "Views",
            "Dashboard",
            "DashboardView.xaml"));
        System.Xml.Linq.XElement heatmap = Assert.Single(
            document.Descendants(),
            element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && attribute.Value == "UsageProductCard.Heatmap"));

        Assert.DoesNotContain(
            heatmap.Ancestors(),
            element => element.Name.LocalName == "Expander");
        Assert.Contains(
            heatmap.Ancestors(),
            element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "UsageProductDetailsPanel"));
    }

    [Fact]
    public void MainPageAndFlyoutComposeBoundedFeatureSurfaces()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string appRoot = Path.Combine(repoRoot, "src", "WOpenUsage.App");
        string mainPagePath = Path.Combine(appRoot, "MainPage.xaml");
        string mainPageCodePath = Path.Combine(appRoot, "MainPage.xaml.cs");
        string flyoutPath = Path.Combine(appRoot, "ViewModels", "FlyoutViewModel.cs");
        string mainPage = File.ReadAllText(mainPagePath);
        string mainPageCode = File.ReadAllText(mainPageCodePath);
        string flyout = File.ReadAllText(flyoutPath);

        Assert.InRange(File.ReadLines(mainPagePath).Count(), 1, 400);
        Assert.InRange(File.ReadLines(flyoutPath).Count(), 1, 350);
        Assert.Contains("<optionViews:OptionsView", mainPage, StringComparison.Ordinal);
        Assert.Contains("<dashboardViews:DashboardView", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("VercelApiKeyBox", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardLayoutProviderList", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("UsageProductCard", mainPage, StringComparison.Ordinal);
        Assert.Contains("OptionsSurfaceViewModel Options", flyout, StringComparison.Ordinal);
        Assert.Contains("DashboardSurfaceViewModel Dashboard", flyout, StringComparison.Ordinal);
        Assert.DoesNotContain("SampleDashboardCatalog.Build", flyout, StringComparison.Ordinal);
        Assert.Contains("_ = ViewModel.StartAsync();", mainPageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = SessionHost.StartAsync();", mainPageCode, StringComparison.Ordinal);

        string optionsView = File.ReadAllText(Path.Combine(
            appRoot,
            "Views",
            "Options",
            "OptionsView.xaml"));
        string providersView = File.ReadAllText(Path.Combine(
            appRoot,
            "Views",
            "Options",
            "ProvidersOptionsView.xaml"));
        Assert.DoesNotContain("VercelConnectionView", optionsView, StringComparison.Ordinal);
        Assert.DoesNotContain("OptionsVercelButton", providersView, StringComparison.Ordinal);

        string[] requiredViews =
        [
            Path.Combine("Dashboard", "DashboardView.xaml"),
            Path.Combine("Options", "AppearanceOptionsView.xaml"),
            Path.Combine("Options", "GeneralOptionsView.xaml"),
            Path.Combine("Options", "OptionsHomeView.xaml"),
            Path.Combine("Options", "OptionsView.xaml"),
            Path.Combine("Options", "PersonalizationOptionsView.xaml"),
            Path.Combine("Options", "ProvidersOptionsView.xaml"),
            Path.Combine("Options", "ProviderStatusView.xaml"),
            Path.Combine("Options", "VercelConnectionView.xaml"),
        ];

        foreach (string relativePath in requiredViews)
        {
            Assert.True(
                File.Exists(Path.Combine(appRoot, "Views", relativePath)),
                $"Missing feature view: {relativePath}");
        }
    }

    [Fact]
    public void FeatureViewSplitPreservesTheAutomationIdContract()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string appRoot = Path.Combine(repoRoot, "src", "WOpenUsage.App");
        IEnumerable<string> viewPaths =
        [
            Path.Combine(appRoot, "MainPage.xaml"),
            .. Directory.EnumerateFiles(
                Path.Combine(appRoot, "Views"),
                "*.xaml",
                SearchOption.AllDirectories),
        ];
        string xaml = string.Join(Environment.NewLine, viewPaths.Select(File.ReadAllText));
        var matches = System.Text.RegularExpressions.Regex.Matches(
            xaml,
            "AutomationProperties\\.AutomationId=\"([^\"]+)\"");
        string[] distinctIds = matches
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(96, matches.Count);
        Assert.Equal(84, distinctIds.Length);
    }

    [Fact]
    public void ViewTransitionsUseTheSharedReducedMotionGate()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string pageCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.App",
            "MainPage.xaml.cs"));

        Assert.Contains("MotionSettings.AreAnimationsEnabled()", pageCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ViewTransitionDuration", pageCode, StringComparison.Ordinal);
        Assert.Contains("BodyTransitionTransform", pageCode, StringComparison.Ordinal);
    }
}
