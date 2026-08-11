using System.Xml.Linq;

namespace TokenUsage.Architecture.Tests;

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
            "TokenUsage.Core",
            "TokenUsage.Core.csproj");
        IReadOnlyList<string> coreIssues = ArchitectureRules.FindCoreIsolationViolations(coreProject);

        Assert.True(
            coreIssues.Count == 0,
            "Core isolation violations:" + Environment.NewLine + string.Join(Environment.NewLine, coreIssues));
    }

    [Fact]
    public void RuleDetectsInvertedCoreToProvidersEdge()
    {
        var invalid = new ProjectReferenceGraph(
            ["TokenUsage.Core", "TokenUsage.Providers"],
            [("TokenUsage.Core", "TokenUsage.Providers")]);

        IReadOnlyList<string> forbidden = ArchitectureRules.FindForbiddenEdges(invalid);

        Assert.Contains(
            forbidden,
            violation => string.Equals(
                violation,
                "TokenUsage.Core -> TokenUsage.Providers",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuleDetectsMissingProductProject()
    {
        var incomplete = new ProjectReferenceGraph(
            [
                "TokenUsage.Core",
                "TokenUsage.Platform.Windows",
                "TokenUsage.Providers",
                "TokenUsage.App",
            ],
            []);

        IReadOnlyList<string> forbidden = ArchitectureRules.FindForbiddenEdges(incomplete);

        Assert.Contains(
            "Missing product project: TokenUsage.Cli",
            forbidden,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuleBlocksFutureLocalApiFromReferencingCli()
    {
        var invalid = new ProjectReferenceGraph(
            ArchitectureRules.AllowedReferences.Keys,
            [("TokenUsage.LocalApi", "TokenUsage.Cli")]);

        IReadOnlyList<string> forbidden = ArchitectureRules.FindForbiddenEdges(invalid);

        Assert.Contains(
            "TokenUsage.LocalApi -> TokenUsage.Cli",
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
            "TokenUsage.App",
            "Composition",
            "AppComposition.cs"));

        Assert.Contains("WindowsProviderCatalog.CreateComposition", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("SyntheticUsageEventSource", composition, StringComparison.Ordinal);

        string providerCatalog = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.Runtime.Windows",
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
            "TokenUsage.Cli",
            "LocalLimitsCliAccess.cs"));
        string cliDiagnostics = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.Cli",
            "LocalProviderDiagnosticsAccess.cs"));
        string diagnosticsQuery = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.Runtime.Windows",
            "Automation",
            "WindowsProviderDiagnosticsQuery.cs"));
        Assert.Contains("WindowsProviderCatalog", cliLimits, StringComparison.Ordinal);
        Assert.Contains("WindowsProviderCatalog", diagnosticsQuery, StringComparison.Ordinal);

        string mainPage = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "MainPage.xaml.cs"));
        Assert.Contains("AppComposition.CreateFlyoutViewModel", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("new FlyoutViewModel(", mainPage, StringComparison.Ordinal);

        string flyoutViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
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
            "TokenUsage.Core",
            "Session",
            "AppSessionHost.cs"));
        Assert.DoesNotContain("Microsoft.UI", sessionHost, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.UI", sessionHost, StringComparison.Ordinal);

        string mainWindow = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "MainWindow.xaml.cs"));
        Assert.Contains("RootPage.SessionHost.RefreshAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RootPage.ViewModel.RefreshCommand",
            mainWindow,
            StringComparison.Ordinal);

        string cliApplication = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.Cli",
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
            "TokenUsage.App",
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
        string appRoot = Path.Combine(repoRoot, "src", "TokenUsage.App");
        string mainPagePath = Path.Combine(appRoot, "MainPage.xaml");
        string mainPageCodePath = Path.Combine(appRoot, "MainPage.xaml.cs");
        string flyoutPath = Path.Combine(appRoot, "ViewModels", "FlyoutViewModel.cs");
        string mainPage = File.ReadAllText(mainPagePath);
        string mainPageCode = File.ReadAllText(mainPageCodePath);
        string flyout = File.ReadAllText(flyoutPath);

        Assert.InRange(File.ReadLines(mainPagePath).Count(), 1, 400);
        Assert.InRange(File.ReadLines(flyoutPath).Count(), 1, 350);
        Assert.Contains("<optionViews:OptionsView", mainPage, StringComparison.Ordinal);
        Assert.Contains(
            "<dashboardViews:CompactUsageDashboard",
            mainPage,
            StringComparison.Ordinal);
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
        Assert.Contains("UnifiedOptionsView", optionsView, StringComparison.Ordinal);
        Assert.Contains("GeneralOptionsView", optionsView, StringComparison.Ordinal);
        Assert.Contains("AppearanceOptionsView", optionsView, StringComparison.Ordinal);
        Assert.Contains("PersonalizationOptionsView", optionsView, StringComparison.Ordinal);
        Assert.Contains("ProviderStatusView", optionsView, StringComparison.Ordinal);
        Assert.DoesNotContain("OptionsHomeView", optionsView, StringComparison.Ordinal);
        Assert.DoesNotContain("ProvidersOptionsView", optionsView, StringComparison.Ordinal);
        Assert.DoesNotContain("OptionsVercelButton", providersView, StringComparison.Ordinal);

        string[] requiredViews =
        [
            Path.Combine("Dashboard", "DashboardView.xaml"),
            Path.Combine("Dashboard", "CompactUsageDashboard.xaml"),
            Path.Combine("Options", "AppearanceOptionsView.xaml"),
            Path.Combine("Options", "GeneralOptionsView.xaml"),
            Path.Combine("Options", "OptionsHomeView.xaml"),
            Path.Combine("Options", "OptionsView.xaml"),
            Path.Combine("Options", "PersonalizationOptionsView.xaml"),
            Path.Combine("Options", "ProvidersOptionsView.xaml"),
            Path.Combine("Options", "ProviderStatusView.xaml"),
            Path.Combine("Options", "VercelConnectionView.xaml"),
            Path.Combine("Reports", "UsageReportPage.xaml"),
        ];

        foreach (string relativePath in requiredViews)
        {
            Assert.True(
                File.Exists(Path.Combine(appRoot, "Views", relativePath)),
                $"Missing feature view: {relativePath}");
        }
    }

    [Fact]
    public void UnifiedOptionsCategoriesUseOneConsistentOuterCard()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string optionsRoot = Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Views",
            "Options");
        string[] categoryFiles =
        [
            "GeneralOptionsView.xaml",
            "AppearanceOptionsView.xaml",
            "PersonalizationOptionsView.xaml",
            "ProviderStatusView.xaml",
        ];

        foreach (string categoryFile in categoryFiles)
        {
            XDocument document = XDocument.Load(Path.Combine(optionsRoot, categoryFile));
            XElement card = Assert.Single(document.Root!.Elements());
            Assert.Equal("Border", card.Name.LocalName);
            Assert.Contains(
                card.Attributes(),
                attribute => attribute.Name.LocalName == "Style"
                    && attribute.Value == "{StaticResource OptionsCategoryCardStyle}");
        }

        XDocument optionsDocument = XDocument.Load(Path.Combine(optionsRoot, "OptionsView.xaml"));
        XElement aboutCard = optionsDocument.Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Value == "AboutSection"));
        Assert.Contains(
            aboutCard.Attributes(),
            attribute => attribute.Name.LocalName == "Style"
                && attribute.Value == "{StaticResource OptionsCategoryCardStyle}");
    }

    [Fact]
    public void QuotaSurfacesDoNotRenderProjectedPaceText()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string appRoot = Path.Combine(repoRoot, "src", "TokenUsage.App");
        string[] quotaSurfaces =
        [
            Path.Combine(appRoot, "App.xaml"),
            Path.Combine(appRoot, "Views", "Dashboard", "DashboardView.xaml"),
            Path.Combine(appRoot, "Views", "Reports", "UsageReportPage.xaml"),
        ];

        foreach (string quotaSurface in quotaSurfaces)
        {
            string xaml = File.ReadAllText(quotaSurface);
            Assert.DoesNotContain("CompactPaceText", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Text=\"{x:Bind PaceText}\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Text=\"{Binding PaceText}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FeatureViewSplitPreservesTheAutomationIdContract()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string appRoot = Path.Combine(repoRoot, "src", "TokenUsage.App");
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

        Assert.Equal(146, matches.Count);
        Assert.Equal(132, distinctIds.Length);
        Assert.Contains("HeaderVisualizationButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("HeaderShareButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("HeaderOptionsButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UnifiedOptionsView", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportCaptureButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportResetCycleButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportResetCycleSelector", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportPreviousResetCycleButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportNextResetCycleButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportResetCount", distinctIds, StringComparer.Ordinal);
        Assert.Contains("AboutSection", distinctIds, StringComparer.Ordinal);
        Assert.Contains("AboutGitHubLink", distinctIds, StringComparer.Ordinal);
        Assert.DoesNotContain("SampleModeToggle", distinctIds, StringComparer.Ordinal);
        Assert.DoesNotContain("SampleScenarioCombo", distinctIds, StringComparer.Ordinal);
        Assert.DoesNotContain("CompactGlobalOptionsButton", distinctIds, StringComparer.Ordinal);
        Assert.DoesNotContain("CompactProviderOptionsButton", distinctIds, StringComparer.Ordinal);
    }

    [Fact]
    public void ViewTransitionsUseTheSharedReducedMotionGate()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string pageCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "MainPage.xaml.cs"));

        Assert.Contains("MotionSettings.AreAnimationsEnabled()", pageCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ViewTransitionDuration", pageCode, StringComparison.Ordinal);
        Assert.Contains("BodyTransitionTransform", pageCode, StringComparison.Ordinal);

        string compactCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Views",
            "Dashboard",
            "CompactUsageDashboard.xaml.cs"));
        Assert.Contains("MotionSettings.AreAnimationsEnabled()", compactCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ProviderSwitchExitDuration", compactCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ProviderSwitchDuration", compactCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ProviderSwitchMinimumOpacity", compactCode, StringComparison.Ordinal);
        Assert.Contains("PlayProviderContentTransition", compactCode, StringComparison.Ordinal);
        Assert.Contains("ProviderTabsRepeater.TryGetElement", compactCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ProviderLimitsRevealDuration", compactCode, StringComparison.Ordinal);
        Assert.Contains("LayoutAnimationProgressed", compactCode, StringComparison.Ordinal);
        Assert.Contains("PlayProviderTransitionEntry", compactCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", compactCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportMotionUsesExclusiveTabsAndLeafTransitionChannels()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string reportRoot = Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Views",
            "Reports");
        string reportXaml = File.ReadAllText(Path.Combine(reportRoot, "UsageReportPage.xaml"));
        Assert.Contains(
            "x:Key=\"ReportToolbarToggleButtonStyle\" TargetType=\"RadioButton\"",
            reportXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedDuration", reportXaml, StringComparison.Ordinal);
        foreach (string requiredName in new[]
        {
            "UsageReportScopeTabs",
            "UsageReportPeriodTabs",
            "UsageReportMetricTabs",
            "UsageReportValueModeTabs",
            "UsageReportChartLayoutTabs",
            "UsageReportBreakdownTabs",
            "ReportSummaryTokensValue",
            "ReportSummaryCostValue",
            "ReportSummaryCoverageValue",
            "ReportSummaryQualityProgress",
            "ReportSummaryQualityValue",
            "ReportCompositionLegendRoot",
            "ReportCompositionBar",
            "GlobalChartTransitionRoot",
            "ProviderChartContentRoot",
            "ReportCachedInputValue",
            "ReportUncachedInputValue",
            "ReportOutputTokensValue",
            "ReportProviderLimitsContentRoot",
            "ModelBreakdownRows",
            "SourceBreakdownRows",
            "DayBreakdownRows",
        })
        {
            Assert.Contains(requiredName, reportXaml, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("ReportDataTransitionTransform", reportXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportSummaryValuesRoot", reportXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportCacheValuesRoot", reportXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BreakdownContentRoot", reportXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChartsAndProviderSelectorsExposeKeyboardNamesAndDetails()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string appRoot = Path.Combine(repoRoot, "src", "TokenUsage.App");
        string heatmapCode = File.ReadAllText(Path.Combine(
            appRoot,
            "Controls",
            "UsageHeatmap.xaml.cs"));
        Assert.Contains("element.GotFocus +=", heatmapCode, StringComparison.Ordinal);
        Assert.Contains("element.LostFocus +=", heatmapCode, StringComparison.Ordinal);

        string compactXaml = File.ReadAllText(Path.Combine(
            appRoot,
            "Views",
            "Dashboard",
            "CompactUsageDashboard.xaml"));
        Assert.Contains(
            "AutomationProperties.Name=\"{x:Bind Name}\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Style=\"{StaticResource CompactProviderTabStyle}\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "ItemsSource=\"{x:Bind ViewModel.ProviderSummaries, Mode=OneWay}\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "GroupName=\"CompactProviderTabs\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "YAxisWidth=\"30\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "YAxisGap=\"4\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "VisualTransition GeneratedDuration",
            compactXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ItemsSource=\"{x:Bind ViewModel.ProviderOptions, Mode=OneWay}\"",
            compactXaml,
            StringComparison.Ordinal);

        string reportXaml = File.ReadAllText(Path.Combine(
            appRoot,
            "Views",
            "Reports",
            "UsageReportPage.xaml"));
        Assert.Contains("ContainerContentChanging=\"OnProviderContainerContentChanging\"", reportXaml, StringComparison.Ordinal);

        string reportCode = File.ReadAllText(Path.Combine(
            appRoot,
            "Views",
            "Reports",
            "UsageReportPage.xaml.cs"));
        Assert.Contains("AutomationProperties.SetName(container, option.Name)", reportCode, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAutomationId(container, option.ProviderId)", reportCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCapturesExcludeWindowActionChrome()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string mainPageCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "MainPage.xaml.cs")).ReplaceLineEndings("\n");
        Assert.Contains("ShareCaptureService.CaptureAsync(\n                CompactCaptureRoot", mainPageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ShareCaptureService.CaptureAsync(\n                FlyoutChrome", mainPageCode, StringComparison.Ordinal);

        string reportPageCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Views",
            "Reports",
            "UsageReportPage.xaml.cs"));
        Assert.Contains("ReportControlBar.Visibility = Visibility.Collapsed", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("ReportCoverageHintButton.Visibility = Visibility.Collapsed", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("ReportControlBar.Visibility = controlBarVisibility", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("ReportCoverageHintButton.Visibility = coverageHintVisibility", reportPageCode, StringComparison.Ordinal);

        string shareCaptureCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Services",
            "ShareCaptureService.cs"));
        Assert.Contains("private const int CapturePadding = 10", shareCaptureCode, StringComparison.Ordinal);
        Assert.Contains("DismissTransientOverlays(captureRoot)", shareCaptureCode, StringComparison.Ordinal);
        Assert.Contains("byte[] paddedPixels = AddPadding", shareCaptureCode, StringComparison.Ordinal);
    }
}
