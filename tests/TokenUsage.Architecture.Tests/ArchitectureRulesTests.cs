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

        string presentationProject = Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.Presentation",
            "TokenUsage.Presentation.csproj");
        IReadOnlyList<string> presentationIssues = ArchitectureRules.FindPresentationIsolationViolations(
            presentationProject);

        Assert.True(
            presentationIssues.Count == 0,
            "Presentation isolation violations:" + Environment.NewLine
                + string.Join(Environment.NewLine, presentationIssues));

        string[] testProjects =
        [
            Path.Combine(repoRoot, "tests", "TokenUsage.Providers.Tests", "TokenUsage.Providers.Tests.csproj"),
            Path.Combine(repoRoot, "tests", "TokenUsage.Architecture.Tests", "TokenUsage.Architecture.Tests.csproj"),
            Path.Combine(repoRoot, "tests", "TokenUsage.Platform.Windows.Tests", "TokenUsage.Platform.Windows.Tests.csproj"),
        ];
        foreach (string testProject in testProjects)
        {
            XDocument project = XDocument.Load(testProject);
            IEnumerable<string> linkedAppSources = project
                .Descendants("Compile")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
                .Where(include => include.Contains("TokenUsage.App", StringComparison.OrdinalIgnoreCase));
            Assert.True(
                !linkedAppSources.Any(),
                $"{Path.GetFileName(testProject)} still compiles App sources:{Environment.NewLine}"
                    + string.Join(Environment.NewLine, linkedAppSources));
        }
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
    public void CliOwnsItsStableJsonWireTypes()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string automation = Path.Combine(repoRoot, "src", "TokenUsage.Core", "Automation");
        string cli = Path.Combine(repoRoot, "src", "TokenUsage.Cli");
        Assert.False(File.Exists(Path.Combine(automation, "UsageJson.cs")));
        Assert.False(File.Exists(Path.Combine(automation, "ReportJson.cs")));
        Assert.False(File.Exists(Path.Combine(automation, "LimitsDocument.cs")));
        Assert.True(File.Exists(Path.Combine(cli, "UsageJson.cs")));
        Assert.True(File.Exists(Path.Combine(cli, "ReportJson.cs")));
        Assert.True(File.Exists(Path.Combine(cli, "LimitsDocument.cs")));

        string usageCommand = File.ReadAllText(Path.Combine(
            cli,
            "UsageCommand.cs"));
        string reportCommand = File.ReadAllText(Path.Combine(
            cli,
            "ReportCommand.cs"));
        Assert.Contains("UsageJson.Serialize", usageCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record UsageDocument", usageCommand, StringComparison.Ordinal);
        Assert.Contains("ReportJson.Serialize", reportCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record ReportDocument", reportCommand, StringComparison.Ordinal);
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
        Assert.Contains("WindowsManualProviderCredentialStore", composition, StringComparison.Ordinal);
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
        Assert.Contains("new ZcodeUsageEventSource", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("new CodexRefreshCoordinator", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("CreateVercelBinding", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("ProviderModuleStage.OptIn", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("ProviderModuleStage.Prepared", providerCatalog, StringComparison.Ordinal);
        Assert.Contains("ProviderModuleStage.PolicyBlocked", providerCatalog, StringComparison.Ordinal);
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
        string providerStatusSurface = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.Presentation",
            "ViewModels",
            "Surfaces",
            "ProviderStatusSurfaceViewModel.cs"));
        Assert.DoesNotContain(".ReadAsync(", providerStatusSurface, StringComparison.Ordinal);
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
        Assert.DoesNotContain(
            "EnsureOfficialCodexLimitsOnFirstOpen",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_ = RootPage.ViewModel.Dashboard.RefreshLiveAsync();",
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
        Assert.DoesNotContain("PersonalizationOptionsView", optionsView, StringComparison.Ordinal);
        Assert.Contains("ProviderStatusView", optionsView, StringComparison.Ordinal);
        Assert.DoesNotContain("OptionsHomeView", optionsView, StringComparison.Ordinal);
        Assert.DoesNotContain("ProvidersOptionsView", optionsView, StringComparison.Ordinal);
        Assert.DoesNotContain("OptionsVercelButton", providersView, StringComparison.Ordinal);
        Assert.Contains(
            "PersonalizationOptionsView",
            File.ReadAllText(Path.Combine(
                appRoot,
                "Views",
                "Options",
                "AppearanceOptionsView.xaml")),
            StringComparison.Ordinal);

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
            Path.Combine("Options", "ProviderCredentialEditor.xaml"),
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
            "ProviderStatusView.xaml",
        ];

        string[] descriptionUids =
        [
            "GeneralSectionDescription",
            "AppearanceSectionDescription",
            "ProviderStatusSectionDescription",
        ];

        for (int index = 0; index < categoryFiles.Length; index++)
        {
            string categoryFile = categoryFiles[index];
            XDocument document = XDocument.Load(Path.Combine(optionsRoot, categoryFile));
            XElement card = Assert.Single(
                document.Root!.Elements(),
                element => element.Name.LocalName == "Border");
            Assert.Equal("Border", card.Name.LocalName);
            Assert.Contains(
                card.Attributes(),
                attribute => attribute.Name.LocalName == "Style"
                    && attribute.Value == "{StaticResource OptionsCategoryCardStyle}");
            Assert.Contains(
                card.Descendants().Where(element => element.Name.LocalName == "FontIcon"),
                icon => icon.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Style"
                    && attribute.Value == "{StaticResource OptionsCategoryIconStyle}"));
            Assert.Contains(
                card.Descendants().Where(element => element.Name.LocalName == "TextBlock"),
                title => title.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Style"
                    && attribute.Value == "{StaticResource OptionsCategoryTitleStyle}"));
            Assert.Contains(
                card.Descendants().Where(element => element.Name.LocalName == "ToolTip"),
                tooltip => tooltip.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Uid"
                    && attribute.Value == descriptionUids[index]));
        }

        XDocument optionsDocument = XDocument.Load(Path.Combine(optionsRoot, "OptionsView.xaml"));
        XElement aboutCard = optionsDocument.Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Value == "AboutSection"));
        Assert.Contains(
            aboutCard.Attributes(),
            attribute => attribute.Name.LocalName == "Style"
                && attribute.Value == "{StaticResource OptionsCategoryCardStyle}");
        Assert.DoesNotContain(
            optionsDocument.Root!.Elements().SelectMany(element => element.Descendants()),
            element => element.Name.LocalName == "PersonalizationOptionsView");

        XDocument appearanceDocument = XDocument.Load(Path.Combine(
            optionsRoot,
            "AppearanceOptionsView.xaml"));
        Assert.Contains(
            appearanceDocument.Descendants(),
            element => element.Name.LocalName == "PersonalizationOptionsView");
    }

    [Fact]
    public void OptionsBooleanSettingsUseIconToggleButtons()
    {
        string optionsRoot = Path.Combine(
            ProjectReferenceGraph.FindRepoRoot(),
            "src",
            "TokenUsage.App",
            "Views",
            "Options");
        XDocument[] documents =
        [
            XDocument.Load(Path.Combine(optionsRoot, "GeneralOptionsView.xaml")),
            XDocument.Load(Path.Combine(optionsRoot, "AppearanceOptionsView.xaml")),
        ];
        string[] requiredAutomationIds =
        [
            "CloseWhenInactiveToggle",
            "DataCollectionBackgroundToggle",
            "AppearanceTransparencyToggle",
            "AppearanceTrayPopoverEnabledToggle",
            "AppearanceTrayProviderNameToggle",
        ];

        Assert.DoesNotContain(
            documents.SelectMany(document => document.Descendants()),
            element => element.Name.LocalName == "ToggleSwitch");

        XElement[] stateButtons = documents
            .SelectMany(document => document.Descendants())
            .Where(element => element.Name.LocalName == "ToggleButton")
            .Where(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Style"
                && attribute.Value == "{StaticResource OptionsStateButtonStyle}"))
            .ToArray();
        Assert.Equal(requiredAutomationIds.Length, stateButtons.Length);

        foreach (string automationId in requiredAutomationIds)
        {
            XElement stateButton = Assert.Single(
                stateButtons,
                element => element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "AutomationProperties.AutomationId"
                    && attribute.Value == automationId));
            Assert.Contains(
                stateButton.Descendants(),
                element => element.Name.LocalName == "SymbolIcon");
        }
    }

    [Fact]
    public void ProviderStatusUsesCompactRowsWithAccessibleDetails()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string providerStatus = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Views",
            "Options",
            "ProviderStatusView.xaml"));

        Assert.Contains("MinHeight=\"40\"", providerStatus, StringComparison.Ordinal);
        Assert.Contains(
            "Text=\"{x:Bind CompactState, Mode=OneTime}\"",
            providerStatus,
            StringComparison.Ordinal);
        Assert.Contains(
            "Glyph=\"{x:Bind StatusGlyph, Mode=OneTime}\"",
            providerStatus,
            StringComparison.Ordinal);
        Assert.Contains(
            "Content=\"{x:Bind DetailsText, Mode=OneTime}\"",
            providerStatus,
            StringComparison.Ordinal);
        Assert.Contains(
            "Converter={StaticResource ProviderStatusKindToBrushConverter}",
            providerStatus,
            StringComparison.Ordinal);
        Assert.Contains("<Button.Flyout>", providerStatus, StringComparison.Ordinal);
        Assert.Contains(
            "ItemsSource=\"{x:Bind ViewModel.PrimaryProviders, Mode=OneWay}\"",
            providerStatus,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"ProviderStatusMoreButton\"",
            providerStatus,
            StringComparison.Ordinal);
        Assert.Contains(
            "ItemsSource=\"{x:Bind ViewModel.AdditionalProviders, Mode=OneWay}\"",
            providerStatus,
            StringComparison.Ordinal);
        Assert.Contains(
            "Visibility=\"{x:Bind ViewModel.IsAdditionalProvidersExpanded, Mode=OneWay}\"",
            providerStatus,
            StringComparison.Ordinal);
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

        Assert.Equal(170, matches.Count);
        Assert.Equal(155, distinctIds.Length);
        Assert.Contains("DataCollectionBackgroundToggle", distinctIds, StringComparer.Ordinal);
        Assert.Contains("DataCollectionOpenRefreshSelector", distinctIds, StringComparer.Ordinal);
        Assert.Contains("AppearanceTrayPopoverEnabledToggle", distinctIds, StringComparer.Ordinal);
        Assert.Contains("AppearanceTrayPrimarySelector", distinctIds, StringComparer.Ordinal);
        Assert.Contains("AppearanceTraySecondarySelector", distinctIds, StringComparer.Ordinal);
        Assert.Contains("AppearanceTrayProviderCountSelector", distinctIds, StringComparer.Ordinal);
        Assert.Contains("AppearanceTrayProviderNameToggle", distinctIds, StringComparer.Ordinal);
        Assert.Contains("TraySummaryEmptyState", distinctIds, StringComparer.Ordinal);
        Assert.Contains("CompactGlobalCostBreakdown", distinctIds, StringComparer.Ordinal);
        Assert.Contains("HeaderVisualizationButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("HeaderShareButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("HeaderOptionsButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UnifiedOptionsView", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportCaptureButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportPreviousProviderButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportNextProviderButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportResetCycleButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportResetCycleSelector", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportResetCycleGroupSelector", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportPreviousResetCycleButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportNextResetCycleButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportCompareCycleWarning", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportResetCount", distinctIds, StringComparer.Ordinal);
        Assert.Contains("UsageReportPeriodSelector", distinctIds, StringComparer.Ordinal);
        Assert.DoesNotContain("UsageReport1DayButton", distinctIds, StringComparer.Ordinal);
        Assert.DoesNotContain("UsageReport3DaysButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("AboutSection", distinctIds, StringComparer.Ordinal);
        Assert.Contains("AboutGitHubLink", distinctIds, StringComparer.Ordinal);
        Assert.Contains("TraySummary", distinctIds, StringComparer.Ordinal);
        Assert.Contains("TraySummaryProviders", distinctIds, StringComparer.Ordinal);
        Assert.Contains("ProviderStatusMoreButton", distinctIds, StringComparer.Ordinal);
        Assert.Contains("AdditionalProviderStatusList", distinctIds, StringComparer.Ordinal);
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

        string compactCode = ReadCsharpSources(
            Path.Combine(repoRoot, "src", "TokenUsage.App", "Views", "Dashboard"),
            "CompactUsageDashboard");
        Assert.Contains("MotionSettings.AreAnimationsEnabled()", compactCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ProviderSwitchExitDuration", compactCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ProviderSwitchDuration", compactCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ProviderSwitchMinimumOpacity", compactCode, StringComparison.Ordinal);
        Assert.Contains("PlayProviderContentTransition", compactCode, StringComparison.Ordinal);
        Assert.Contains("ProviderTabsRepeater.TryGetElement", compactCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ProviderCarouselDuration", compactCode, StringComparison.Ordinal);
        Assert.Contains("PlayProviderTabsTransition", compactCode, StringComparison.Ordinal);
        Assert.Contains("ProviderTabCarouselLayout.PageSize", compactCode, StringComparison.Ordinal);
        Assert.Contains("ProviderTabCarouselLayout.ItemWidth", compactCode, StringComparison.Ordinal);
        Assert.Contains("ProviderTabCarouselLayout.MaximumPageSize", compactCode, StringComparison.Ordinal);
        Assert.Contains("ApplyProviderTabSize(tab)", compactCode, StringComparison.Ordinal);
        Assert.Contains("tab.Width = _providerTabItemWidth", compactCode, StringComparison.Ordinal);
        Assert.Contains("tab.MaxWidth = _providerTabItemWidth", compactCode, StringComparison.Ordinal);
        Assert.Contains("tab.Margin = new Thickness(0)", compactCode, StringComparison.Ordinal);
        Assert.Contains("ProviderTabsLayout.Spacing = spacing", compactCode, StringComparison.Ordinal);
        Assert.Contains("tab.HorizontalContentAlignment = HorizontalAlignment.Center", compactCode, StringComparison.Ordinal);
        Assert.Contains("bool hasOverflow = providerCount > _providerTabPageSize", compactCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.ProviderLimitsRevealDuration", compactCode, StringComparison.Ordinal);
        Assert.Contains("LayoutAnimationProgressed", compactCode, StringComparison.Ordinal);
        Assert.Contains("PlayProviderTransitionEntry", compactCode, StringComparison.Ordinal);
        Assert.Contains("MotionSettings.VisualizationSwitchDuration", compactCode, StringComparison.Ordinal);
        Assert.Contains("CycleVisualizationWithTransition", compactCode, StringComparison.Ordinal);
        Assert.Contains("PlayVisualizationTransition", compactCode, StringComparison.Ordinal);
        Assert.Contains("GetDominantOutgoingVisualization", compactCode, StringComparison.Ordinal);
        Assert.Contains("if (activityVisibilityChanges)", compactCode, StringComparison.Ordinal);
        Assert.Contains("VisualizationTransitionHost.Height", compactCode, StringComparison.Ordinal);
        Assert.Contains("ActivitySummaryTransitionHost.Height", compactCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", compactCode, StringComparison.Ordinal);

        string compactXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Views",
            "Dashboard",
            "CompactUsageDashboard.xaml"));
        Assert.Contains("x:Name=\"VisualizationTransitionHost\"", compactXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ListVisualizationContent\"", compactXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DonutVisualizationContent\"", compactXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeatmapVisualizationContent\"", compactXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Visibility=\"{x:Bind ViewModel.IsListVisualization",
            compactXaml,
            StringComparison.Ordinal);

        string mainPageCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "MainPage.xaml.cs"));
        Assert.Contains(
            "DashboardSurfaceView.CycleVisualizationWithTransition()",
            mainPageCode,
            StringComparison.Ordinal);

        string mainWindowCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "MainWindow.xaml.cs"));
        Assert.Contains("HasShellAppearanceChanged", mainWindowCode, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "previous.DashboardVisualization != current.DashboardVisualization",
            mainWindowCode,
            StringComparison.Ordinal);
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
        Assert.Contains(
            "IsEnabled=\"{x:Bind ViewModel.HasProviderOptions, Mode=OneWay}\"",
            reportXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Visibility=\"{x:Bind ViewModel.IsProviderPickerVisible, Mode=OneWay}\"",
            reportXaml,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ReportProviderCarousel\"", reportXaml, StringComparison.Ordinal);
        Assert.Contains(
            "ProviderTabCarouselLayout.ReportMaximumPageSize",
            File.ReadAllText(Path.Combine(reportRoot, "UsageReportPage.xaml.cs")),
            StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"MinWidth\" Value=\"0\" />",
            reportXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ColumnDefinition MinWidth=\"240\" Width=\"*\" />",
            reportXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedDuration", reportXaml, StringComparison.Ordinal);
        foreach (string requiredName in new[]
        {
            "UsageReportScopeTabs",
            "UsageReportMetricTabs",
            "UsageReportValueModeTabs",
            "UsageReportChartLayoutTabs",
            "UsageReportBreakdownTabs",
            "ReportSummaryTokensValue",
            "ReportSummaryCostValue",
            "ReportSummaryCoverageValue",
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
            "UsageReportCompareButton",
            "UsageReportCompareAxisTabs",
            "ReportCompareSummary",
            "ReportCompareChart",
            "ReportCompareRows",
        })
        {
            Assert.Contains(requiredName, reportXaml, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("ReportDataTransitionTransform", reportXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportSummaryValuesRoot", reportXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportCacheValuesRoot", reportXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BreakdownContentRoot", reportXaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"UsageReportPeriodSelector\"", reportXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ReportHeaderRoot\"", reportXaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ReportScrollViewer\"\n            Grid.Row=\"1\"",
            reportXaml.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);

        string reportViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "ViewModels",
            "Reports",
            "UsageReportViewModel.cs"));
        Assert.Contains("SelectUsedProviderIds", reportViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveLocalUsageEntries", reportViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportProviderScopeFiltersOneDurableReadInMemory()
    {
        string reportViewModel = File.ReadAllText(Path.Combine(
            ProjectReferenceGraph.FindRepoRoot(),
            "src",
            "TokenUsage.App",
            "ViewModels",
            "Reports",
            "UsageReportViewModel.cs"));

        const string durableRead = "query.ReadAsync(";
        int durableReadIndex = reportViewModel.IndexOf(durableRead, StringComparison.Ordinal);
        Assert.True(durableReadIndex >= 0);
        Assert.DoesNotContain(
            durableRead,
            reportViewModel[(durableReadIndex + durableRead.Length)..],
            StringComparison.Ordinal);
        Assert.Contains(
            "UsageReportQuery.FilterByAgent",
            reportViewModel,
            StringComparison.Ordinal);
        Assert.Contains(
            "await _refreshSourceAsync()",
            reportViewModel,
            StringComparison.Ordinal);
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
            "ProviderId=\"{x:Bind ProviderId}\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "<controls:ProviderMarkImage",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "ItemsSource=\"{x:Bind VisibleProviderTabs, Mode=OneTime}\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "GroupName=\"CompactProviderTabs\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "<StackLayout",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ProviderTabsLayout\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Orientation=\"Horizontal\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ProviderTabsTransitionRoot\" Margin=\"2\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"MinWidth\" Value=\"0\" />",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"HorizontalContentAlignment\" Value=\"Center\" />",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "HorizontalContentAlignment=\"Center\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"PreviousProviderTabButton\"",
            compactXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"NextProviderTabButton\"",
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
        Assert.Contains("x:Name=\"ReportProviderCarousel\"", reportXaml, StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"UsageReportPreviousProviderButton\"",
            reportXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"UsageReportNextProviderButton\"",
            reportXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"{x:Bind Name}\"",
            reportXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "GroupName=\"UsageReportProviderTabs\"",
            reportXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ReportProviderTabsTransitionRoot\" Margin=\"2,0\"",
            reportXaml,
            StringComparison.Ordinal);

        string reportCode = ReadCsharpSources(
            Path.Combine(appRoot, "Views", "Reports"),
            "UsageReportPage");
        Assert.Contains("ProviderTabCarouselLayout.ReportMaximumPageSize", reportCode, StringComparison.Ordinal);
        Assert.Contains("PlayProviderTabsTransition", reportCode, StringComparison.Ordinal);
        Assert.Contains("tab.Width = _providerTabItemWidth", reportCode, StringComparison.Ordinal);

        string trendChartCode = File.ReadAllText(Path.Combine(
            appRoot,
            "Controls",
            "UsageTrendChart.xaml.cs"));
        Assert.Contains("AddSingleDayBars", trendChartCode, StringComparison.Ordinal);
        Assert.Contains("if (data.Days.Count == 2)", trendChartCode, StringComparison.Ordinal);
        Assert.Contains("if (data.Days.Count == 1)", trendChartCode, StringComparison.Ordinal);
        Assert.Contains("MiddleDayLabel.Text = data.Days[0].Label", trendChartCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCapturesPreserveBrandAndExcludeActionChrome()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string mainPageXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "MainPage.xaml"));
        Assert.Contains("x:Name=\"HeaderActionButtons\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"{ThemeResource TokenUsageAppIconSource}\"", mainPageXaml, StringComparison.Ordinal);

        string appXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "App.xaml"));
        Assert.Contains("ms-appx:///Assets/AppIconDark.png", appXaml, StringComparison.Ordinal);
        Assert.Contains("ms-appx:///Assets/AppIconLight.png", appXaml, StringComparison.Ordinal);

        string mainPageCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "MainPage.xaml.cs")).ReplaceLineEndings("\n");
        Assert.Contains("HeaderActionButtons.Opacity = 0", mainPageCode, StringComparison.Ordinal);
        Assert.Contains("ShareCaptureService.CaptureAsync(\n                FlyoutChrome", mainPageCode, StringComparison.Ordinal);
        Assert.Contains("HeaderActionButtons.Opacity = actionButtonsOpacity", mainPageCode, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE72D;\"", mainPageXaml, StringComparison.Ordinal);

        string reportPageXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Views",
            "Reports",
            "UsageReportPage.xaml"));
        Assert.Contains("x:Name=\"ReportCaptureBrand\"", reportPageXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"TokenUsage\"", reportPageXaml, StringComparison.Ordinal);

        string reportPageCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Views",
            "Reports",
            "UsageReportPage.xaml.cs"));
        Assert.Contains("ReportControlBar.Opacity = 0", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("ReportCoverageHintButton.Opacity = 0", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0\"", reportPageXaml, StringComparison.Ordinal);
        Assert.Contains("ReportCaptureBrand.Opacity = 1", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("ShareCaptureService.CaptureScrollableAsync", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("ReportScrollViewer", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("ReportControlBar.Opacity = controlBarOpacity", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("ReportCoverageHintButton.Opacity = coverageHintOpacity", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("ReportCaptureBrand.Opacity = captureBrandOpacity", reportPageCode, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE72D;\"", reportPageXaml, StringComparison.Ordinal);

        string shareCaptureCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Services",
            "ShareCaptureService.cs"));
        Assert.Contains("private const int CapturePadding = 10", shareCaptureCode, StringComparison.Ordinal);
        Assert.Contains("DismissTransientOverlays(captureRoot)", shareCaptureCode, StringComparison.Ordinal);
        Assert.Contains("byte[] paddedPixels = AddPadding", shareCaptureCode, StringComparison.Ordinal);
        Assert.Contains("CaptureScrollableAsync", shareCaptureCode, StringComparison.Ordinal);
        Assert.Contains("CropVertical", shareCaptureCode, StringComparison.Ordinal);

        string generalOptionsCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Views",
            "Options",
            "GeneralOptionsView.xaml.cs"));
        Assert.Contains("DispatcherQueue.TryEnqueue", generalOptionsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("private async void OnLoaded", generalOptionsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void CodexScannerIsSplitIntoPathsScanAndMap()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string codex = Path.Combine(repoRoot, "src", "TokenUsage.Providers", "Codex");
        Assert.True(File.Exists(Path.Combine(codex, "CodexUsageEventSource.cs")));
        Assert.True(File.Exists(Path.Combine(codex, "CodexUsageEventSource.Paths.cs")));
        Assert.True(File.Exists(Path.Combine(codex, "CodexUsageEventSource.Scan.cs")));
        Assert.True(File.Exists(Path.Combine(codex, "CodexUsageEventSource.Map.cs")));
        Assert.Contains(
            "public sealed partial class CodexUsageEventSource",
            File.ReadAllText(Path.Combine(codex, "CodexUsageEventSource.Paths.cs")),
            StringComparison.Ordinal);
    }

    private static string ReadCsharpSources(string directory, string filePrefix)
    {
        string[] files = Directory.GetFiles(directory, filePrefix + "*.cs");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return string.Concat(files.Select(File.ReadAllText));
    }
}
