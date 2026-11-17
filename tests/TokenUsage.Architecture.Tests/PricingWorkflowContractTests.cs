namespace TokenUsage.Architecture.Tests;

public sealed class PricingWorkflowContractTests
{
    [Fact]
    public void WeeklyPricingWorkflowIsPinnedDraftOnlyAndLeastPrivilege()
    {
        string root = ProjectReferenceGraph.FindRepoRoot();
        string workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "pricing-refresh.yml"));

        Assert.Contains("cron: '17 8 * * 1'", workflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: write", workflow, StringComparison.Ordinal);
        Assert.Contains("pull-requests: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("issues: write", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("pricing refresh --update", workflow, StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", workflow, StringComparison.Ordinal);
        Assert.Contains("ref: main", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git rebase", workflow, StringComparison.Ordinal);
        Assert.Contains(":!docs/pricing-refresh.md", workflow, StringComparison.Ordinal);
        Assert.Contains("--force-with-lease=refs/heads/", workflow, StringComparison.Ordinal);
        Assert.Contains("$env:EXPECTED_HEAD", workflow, StringComparison.Ordinal);
        Assert.Contains("gh pr create --draft", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr merge", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--auto", workflow, StringComparison.Ordinal);
    }
}
