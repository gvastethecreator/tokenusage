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
    public void ProductionCompositionUsesTheRealClaudeUsageSource()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string composition = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.App",
            "MainPage.xaml.cs"));

        Assert.Contains("new ClaudeUsageEventSource", composition, StringComparison.Ordinal);
        Assert.Contains("new GrokUsageEventSource", composition, StringComparison.Ordinal);
        Assert.Contains("new OpenCodeUsageEventSource", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("SyntheticUsageEventSource", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageHeatmapIsVisibleInsideDetailsWithoutANestedExpander()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.App",
            "MainPage.xaml"));
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
