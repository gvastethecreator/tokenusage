using WOpenUsage.App.Controls;

namespace WOpenUsage.Architecture.Tests;

public sealed class SpendDonutGeometryTests
{
    [Fact]
    public void NormalSampleClosesRingAndPreservesOrder()
    {
        SpendDonutArc[] arcs = SpendDonutGeometry.CreateArcs(
            [
                new("claude", 22.40),
                new("codex", 12.30),
                new("grok", 7.10),
                new("opencode", 5.92),
                new("antigravity", 0.40),
            ]).ToArray();

        Assert.Equal(
            ["claude", "codex", "grok", "opencode", "antigravity"],
            arcs.Select(arc => arc.ProviderId));
        Assert.Equal(0, arcs[0].StartFraction);
        Assert.Equal(1, arcs[^1].EndFraction);
        Assert.All(
            arcs.Zip(arcs.Skip(1)),
            pair => Assert.Equal(pair.First.EndFraction, pair.Second.StartFraction, 12));
        Assert.Equal(1, arcs.Sum(arc => arc.TrueShare), 12);
    }

    [Fact]
    public void NonPositiveAndNonFiniteValuesProduceNoArcs()
    {
        IReadOnlyList<SpendDonutArc> arcs = SpendDonutGeometry.CreateArcs(
            [
                new("zero", 0),
                new("negative", -1),
                new("nan", double.NaN),
                new("infinity", double.PositiveInfinity),
            ]);

        Assert.Empty(arcs);
    }

    [Fact]
    public void OnePositiveValueFillsTheRing()
    {
        SpendDonutArc arc = Assert.Single(
            SpendDonutGeometry.CreateArcs([new("codex", 8)]));

        Assert.Equal(0, arc.StartFraction);
        Assert.Equal(1, arc.EndFraction);
        Assert.Equal(1, arc.TrueShare);
    }

    [Fact]
    public void InvalidValuesAreOmittedWithoutChangingPositiveOrder()
    {
        SpendDonutArc[] arcs = SpendDonutGeometry.CreateArcs(
            [
                new("first", 3),
                new("negative", -4),
                new("second", 2),
                new("nan", double.NaN),
            ]).ToArray();

        Assert.Equal(["first", "second"], arcs.Select(arc => arc.ProviderId));
        Assert.Equal(0.6, arcs[0].TrueShare, 12);
        Assert.Equal(0.4, arcs[1].TrueShare, 12);
    }

    [Fact]
    public void TinyPositiveValueGetsAVisibleDisplayFloorBeforeNormalization()
    {
        SpendDonutArc[] arcs = SpendDonutGeometry.CreateArcs(
            [new("large", 999), new("tiny", 1)]).ToArray();

        double tinyDisplayShare = arcs[1].EndFraction - arcs[1].StartFraction;

        Assert.True(tinyDisplayShare > arcs[1].TrueShare);
        Assert.Equal(
            SpendDonutGeometry.MinimumDisplayShare / 1.024,
            tinyDisplayShare,
            12);
        Assert.Equal(1, arcs[^1].EndFraction);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(62, 62)]
    [InlineData(120, 100)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void PercentClampIsSafe(double value, double expected)
    {
        Assert.Equal(expected, SpendDonutGeometry.ClampPercent(value));
    }

    [Fact]
    public void NullInputIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SpendDonutGeometry.CreateArcs(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankProviderIdIsRejected(string providerId)
    {
        Assert.Throws<ArgumentException>(() =>
            SpendDonutGeometry.CreateArcs([new(providerId, 1)]));
    }
}
