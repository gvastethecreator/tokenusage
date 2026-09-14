using System.Security.Cryptography;
using System.Text;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class SessionLineageAccountingTests
{
    [Fact]
    public void ExclusiveAccountingAddsDistinctChildrenWithoutDoubleCounting()
    {
        OpaqueAttributionKey parent = Key("parent");
        OpaqueAttributionKey child = Key("child");
        IReadOnlyList<UsageSessionLineageNode> nodes = SessionLineageAccounting.Build(
            [
                Session(parent, parentSessionKey: null, tokens: 100),
                Session(child, parent, tokens: 50),
            ],
            ParentChildAccountingKind.Exclusive);

        UsageSessionLineageNode parentNode = Assert.Single(nodes, row => row.SessionKey == parent);
        UsageSessionLineageNode childNode = Assert.Single(nodes, row => row.SessionKey == child);
        Assert.Equal(100, parentNode.ExclusiveTokens.Total);
        Assert.Equal(150, parentNode.InclusiveTokens!.Total);
        Assert.Equal(child, Assert.Single(parentNode.Children));
        Assert.Equal(50, childNode.ExclusiveTokens.Total);
        Assert.Equal(50, childNode.InclusiveTokens!.Total);
    }

    [Fact]
    public void InclusiveAccountingKeepsTheParentStoredTotal()
    {
        OpaqueAttributionKey parent = Key("inclusive-parent");
        OpaqueAttributionKey child = Key("inclusive-child");
        UsageSessionLineageNode parentNode = Assert.Single(
            SessionLineageAccounting.Build(
                [
                    Session(parent, parentSessionKey: null, tokens: 150),
                    Session(child, parent, tokens: 50),
                ],
                ParentChildAccountingKind.Inclusive),
            row => row.SessionKey == parent);

        Assert.Equal(150, parentNode.ExclusiveTokens.Total);
        Assert.Equal(150, parentNode.InclusiveTokens!.Total);
    }

    [Fact]
    public void UnknownAccountingLeavesInclusiveTotalsUnavailable()
    {
        OpaqueAttributionKey parent = Key("unknown-parent");
        UsageSessionLineageNode node = Assert.Single(SessionLineageAccounting.Build(
            [Session(parent, parentSessionKey: null, tokens: 80)],
            ParentChildAccountingKind.Unknown));

        Assert.Equal(80, node.ExclusiveTokens.Total);
        Assert.Null(node.InclusiveTokens);
    }

    [Fact]
    public void CycleEdgesAreDroppedWithoutDeletingNodes()
    {
        OpaqueAttributionKey first = Key("cycle-a");
        OpaqueAttributionKey second = Key("cycle-b");
        IReadOnlyList<UsageSessionLineageNode> nodes = SessionLineageAccounting.Build(
            [
                Session(first, second, tokens: 40),
                Session(second, first, tokens: 60),
            ],
            ParentChildAccountingKind.Exclusive);

        Assert.Equal(2, nodes.Count);
        Assert.Equal(100, nodes.Sum(row => row.ExclusiveTokens.Total));
        Assert.Contains(nodes, row => row.ParentSessionKey is null);
        Assert.DoesNotContain(nodes, row =>
            row.ParentSessionKey is not null
            && nodes.Any(other => other.SessionKey == row.ParentSessionKey
                && other.ParentSessionKey == row.SessionKey));
    }

    private static UsageSessionContribution Session(
        OpaqueAttributionKey sessionKey,
        OpaqueAttributionKey? parentSessionKey,
        long tokens)
    {
        var breakdown = new TokenBreakdown(tokens, 0, 0, 0, 0);
        return new UsageSessionContribution(
            sessionKey,
            parentSessionKey,
            breakdown,
            breakdown,
            SelectedEventCount: 1,
            SessionEventCount: 1,
            FirstOccurredAtUtc: new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            LastOccurredAtUtc: new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            IsUnassigned: false);
    }

    private static OpaqueAttributionKey Key(string seed) =>
        new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant());
}
