using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageDerivedActivityTests
{
    [Fact]
    public void ClassifiesApprovedFactsAndLeavesAmbiguousUnknown()
    {
        UsageDerivedActivitySummary summary = UsageDerivedActivity.Summarize(
        [
            (UsageOperationKind.File, "patch", 2),
            (UsageOperationKind.Tool, "Edit", 1),
            (UsageOperationKind.Tool, "Read", 1),
            (UsageOperationKind.Command, "search", 1),
            (UsageOperationKind.Command, "test", 1),
            (UsageOperationKind.Spawn, "reviewer", 1),
            (UsageOperationKind.Command, "git", 1),
            (UsageOperationKind.Mcp, "list_things", 1),
            (UsageOperationKind.Skill, "invented", 1),
        ]);
        Assert.Equal(UsageDerivedActivity.MethodVersion, summary.MethodVersion);
        Assert.Equal(10, summary.Eligible);
        Assert.Equal(3, summary.Edit);
        Assert.Equal(2, summary.Read);
        Assert.Equal(1, summary.Test);
        Assert.Equal(1, summary.Delegate);
        Assert.Equal(3, summary.Unknown);
        Assert.Equal(UsageDerivedActivity.CostUnavailable, summary.CostAvailability);
        Assert.Equal(UsageDerivedActivityCategory.Unknown, UsageDerivedActivity.Classify(UsageOperationKind.Command, "dotnet"));
        Assert.Equal(UsageDerivedActivityCategory.Unknown, UsageDerivedActivity.Classify(UsageOperationKind.Skill, "any"));
    }

    [Fact]
    public void PartitionCountsMatchEligiblePopulation()
    {
        UsageDerivedActivitySummary summary = UsageDerivedActivity.Summarize(
        [
            (UsageOperationKind.File, "patch", 1),
            (UsageOperationKind.Command, "unknown", 2),
        ]);
        Assert.Equal(summary.Eligible, summary.Edit + summary.Read + summary.Test + summary.Delegate + summary.Unknown);
    }

    [Fact]
    public void CategoryCountsMatchFilteredRankingInvocationCounts()
    {
        UsageOperationRankedRow[] rows =
        [
            Ranked(UsageOperationKind.File, "patch", 2),
            Ranked(UsageOperationKind.Tool, "Edit", 1),
            Ranked(UsageOperationKind.Tool, "Read", 1),
            Ranked(UsageOperationKind.Command, "search", 1),
            Ranked(UsageOperationKind.Command, "test", 1),
            Ranked(UsageOperationKind.Spawn, "reviewer", 1),
            Ranked(UsageOperationKind.Mcp, "list_things", 2),
            Ranked(UsageOperationKind.Command, "git", 1),
        ];
        UsageDerivedActivitySummary summary = UsageDerivedActivity.Summarize(rows);
        Assert.Equal(Sum(rows, UsageDerivedActivityCategory.Edit), summary.Edit);
        Assert.Equal(Sum(rows, UsageDerivedActivityCategory.Read), summary.Read);
        Assert.Equal(Sum(rows, UsageDerivedActivityCategory.Test), summary.Test);
        Assert.Equal(Sum(rows, UsageDerivedActivityCategory.Delegate), summary.Delegate);
        Assert.Equal(Sum(rows, UsageDerivedActivityCategory.Unknown), summary.Unknown);
        Assert.Equal(rows.Sum(row => row.InvocationCount), summary.Eligible);
        Assert.Equal(3, summary.Edit);
        Assert.Equal(2, summary.Read);
        Assert.Equal(1, summary.Test);
        Assert.Equal(1, summary.Delegate);
        Assert.Equal(3, summary.Unknown);
    }

    private static int Sum(IEnumerable<UsageOperationRankedRow> rows, UsageDerivedActivityCategory category) =>
        rows.Where(row => UsageDerivedActivity.Classify(row.Kind, row.Tool) == category)
            .Sum(row => row.InvocationCount);

    private static UsageOperationRankedRow Ranked(UsageOperationKind kind, string tool, int count) =>
        new(
            kind + ":" + tool,
            kind,
            tool,
            null,
            count,
            0,
            0,
            0,
            OutcomesAvailable: false);
}
