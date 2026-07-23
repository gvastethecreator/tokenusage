using System.Globalization;
using WOpenUsage.Core.Layout;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Tests.Layout;

public sealed class DashboardLayoutTests
{
    private static readonly ProviderId ProviderA = new("provider-a");
    private static readonly ProviderId ProviderB = new("provider-b");
    private static readonly ProviderId ProviderC = new("provider-c");
    private static readonly ProviderId ProviderUnknown = new("provider-unknown");

    private static readonly MetricId MetricX = new("metric-x");
    private static readonly MetricId MetricY = new("metric-y");
    private static readonly MetricId MetricZ = new("metric-z");
    private static readonly MetricId MetricUnknown = new("metric-unknown");

    [Fact]
    public void ConstructorRejectsNullProvidersCollection()
    {
        Assert.Throws<ArgumentNullException>(() => new DashboardLayout(null!));
    }

    [Fact]
    public void ConstructorRejectsNullProviderEntry()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new DashboardLayout([null!]));
        Assert.Contains("null", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicCollectionsCannotBeCastBackToMutableArrays()
    {
        var layout = CreateSingleProviderWithThreeMetrics();

        Assert.IsNotType<ProviderLayoutPreference[]>(layout.Providers);
        Assert.IsNotType<MetricLayoutPreference[]>(layout.Providers[0].Metrics);
    }

    [Fact]
    public void ConstructorRejectsDuplicateProviderIdsOrdinal()
    {
        var metrics = new[] { new MetricLayoutPreference(MetricX, true, false) };
        var a = new ProviderLayoutPreference(ProviderA, true, false, metrics);
        var duplicate = new ProviderLayoutPreference(new ProviderId("provider-a"), false, true, metrics);

        var ex = Assert.Throws<ArgumentException>(() => new DashboardLayout([a, duplicate]));
        Assert.Contains("Duplicate provider", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsDuplicateMetricIdsWithinProviderOrdinal()
    {
        var metrics = new[]
        {
            new MetricLayoutPreference(MetricX, true, false),
            new MetricLayoutPreference(new MetricId("metric-x"), false, true),
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            new ProviderLayoutPreference(ProviderA, true, false, metrics));
        Assert.Contains("Duplicate metric", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsNullMetricEntry()
    {
        Assert.Throws<ArgumentException>(() =>
            new ProviderLayoutPreference(ProviderA, true, false, [null!]));
    }

    [Fact]
    public void ConstructorRejectsMoreThanMaxProviders()
    {
        var providers = Enumerable.Range(0, DashboardLayout.MaxProviders + 1)
            .Select(i => new ProviderLayoutPreference(
                new ProviderId($"p-{i:D3}"),
                true,
                false,
                Array.Empty<MetricLayoutPreference>()))
            .ToArray();

        var ex = Assert.Throws<ArgumentException>(() => new DashboardLayout(providers));
        Assert.Contains(
            DashboardLayout.MaxProviders.ToString(CultureInfo.InvariantCulture),
            ex.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsMoreThanMaxMetricsPerProvider()
    {
        var metrics = Enumerable.Range(0, DashboardLayout.MaxMetricsPerProvider + 1)
            .Select(i => new MetricLayoutPreference(new MetricId($"m-{i:D3}"), true, false))
            .ToArray();

        var ex = Assert.Throws<ArgumentException>(() =>
            new ProviderLayoutPreference(ProviderA, true, false, metrics));
        Assert.Contains(
            DashboardLayout.MaxMetricsPerProvider.ToString(CultureInfo.InvariantCulture),
            ex.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MoveProviderMovesByMinusOneAndPlusOne()
    {
        var layout = CreateThreeProviderLayout();

        var up = layout.MoveProvider(ProviderC, -1);
        Assert.Equal(new[] { ProviderA, ProviderC, ProviderB }, up.Providers.Select(p => p.ProviderId));

        var down = layout.MoveProvider(ProviderA, +1);
        Assert.Equal(new[] { ProviderB, ProviderA, ProviderC }, down.Providers.Select(p => p.ProviderId));
    }

    [Fact]
    public void MoveProviderClampsAtEdges()
    {
        var layout = CreateThreeProviderLayout();

        var top = layout.MoveProvider(ProviderA, -1);
        Assert.Same(layout, top);
        Assert.Equal(new[] { ProviderA, ProviderB, ProviderC }, top.Providers.Select(p => p.ProviderId));

        var bottom = layout.MoveProvider(ProviderC, +1);
        Assert.Same(layout, bottom);
        Assert.Equal(new[] { ProviderA, ProviderB, ProviderC }, bottom.Providers.Select(p => p.ProviderId));
    }

    [Fact]
    public void SetProviderVisibleAndHighlightedReturnNewLayouts()
    {
        var layout = CreateThreeProviderLayout();

        var hidden = layout.SetProviderVisible(ProviderB, false);
        Assert.True(layout.Providers[1].IsVisible);
        Assert.False(hidden.Providers[1].IsVisible);
        Assert.NotSame(layout, hidden);

        var highlighted = hidden.SetProviderHighlighted(ProviderB, true);
        Assert.False(hidden.Providers[1].IsHighlighted);
        Assert.True(highlighted.Providers[1].IsHighlighted);
    }

    [Fact]
    public void SetProviderColorNormalizesAndPreservesOtherPreferences()
    {
        var layout = CreateThreeProviderLayout();

        DashboardLayout colored = layout.SetProviderColor(ProviderB, " #a1b2c3 ");

        Assert.Equal("#A1B2C3", colored.Providers[1].ColorHex);
        Assert.Equal(layout.Providers[1].Metrics, colored.Providers[1].Metrics);
        Assert.Throws<ArgumentException>(() =>
            layout.SetProviderColor(ProviderB, "red"));
    }

    [Fact]
    public void MoveMetricMovesByMinusOneAndPlusOneAndClamps()
    {
        var layout = CreateSingleProviderWithThreeMetrics();

        var up = layout.MoveMetric(ProviderA, MetricZ, -1);
        Assert.Equal(
            new[] { MetricX, MetricZ, MetricY },
            up.Providers[0].Metrics.Select(m => m.MetricId));

        var down = layout.MoveMetric(ProviderA, MetricX, +1);
        Assert.Equal(
            new[] { MetricY, MetricX, MetricZ },
            down.Providers[0].Metrics.Select(m => m.MetricId));

        var clampTop = layout.MoveMetric(ProviderA, MetricX, -1);
        Assert.Same(layout, clampTop);

        var clampBottom = layout.MoveMetric(ProviderA, MetricZ, +1);
        Assert.Same(layout, clampBottom);
    }

    [Fact]
    public void SetMetricVisibleAndHighlightedReturnNewLayouts()
    {
        var layout = CreateSingleProviderWithThreeMetrics();

        var hidden = layout.SetMetricVisible(ProviderA, MetricY, false);
        Assert.True(layout.Providers[0].Metrics[1].IsVisible);
        Assert.False(hidden.Providers[0].Metrics[1].IsVisible);

        var highlighted = hidden.SetMetricHighlighted(ProviderA, MetricY, true);
        Assert.False(hidden.Providers[0].Metrics[1].IsHighlighted);
        Assert.True(highlighted.Providers[0].Metrics[1].IsHighlighted);
    }

    [Fact]
    public void SetMetricOnDemandReturnsNewLayoutAndIsIdempotent()
    {
        var layout = CreateSingleProviderWithThreeMetrics();

        DashboardLayout onDemand = layout.SetMetricOnDemand(ProviderA, MetricY, true);

        Assert.False(layout.Providers[0].Metrics[1].IsOnDemand);
        Assert.True(onDemand.Providers[0].Metrics[1].IsOnDemand);
        Assert.Same(onDemand, onDemand.SetMetricOnDemand(ProviderA, MetricY, true));
        Assert.False(onDemand.SetMetricOnDemand(ProviderA, MetricY, false)
            .Providers[0].Metrics[1].IsOnDemand);
    }

    [Fact]
    public void ThirdHighlightedMetricIsRefusedWithoutEvictingExistingChoices()
    {
        DashboardLayout layout = CreateSingleProviderWithThreeMetrics()
            .SetMetricHighlighted(ProviderA, MetricX, true)
            .SetMetricHighlighted(ProviderA, MetricY, true);

        DashboardLayout refused = layout.SetMetricHighlighted(ProviderA, MetricZ, true);

        Assert.Same(layout, refused);
        Assert.Equal(
            [MetricX, MetricY],
            refused.Providers[0].Metrics
                .Where(metric => metric.IsHighlighted)
                .Select(metric => metric.MetricId));
        Assert.False(refused.Providers[0].Metrics[2].IsHighlighted);
        Assert.False(refused.SetMetricHighlighted(ProviderA, MetricY, false)
            .Providers[0].Metrics[1].IsHighlighted);
    }

    [Fact]
    public void LegacyProviderPreferenceCanRetainMoreThanTwoHighlightedMetrics()
    {
        var metrics = new[]
        {
            new MetricLayoutPreference(MetricX, true, true),
            new MetricLayoutPreference(MetricY, true, true),
            new MetricLayoutPreference(MetricZ, true, true),
        };

        var provider = new ProviderLayoutPreference(ProviderA, true, false, metrics);

        Assert.Equal(3, provider.Metrics.Count(metric => metric.IsHighlighted));
    }

    [Fact]
    public void MutationsMissingProviderOrMetricThrowKeyNotFoundException()
    {
        var layout = CreateThreeProviderLayout();

        Assert.Throws<KeyNotFoundException>(() => layout.MoveProvider(new ProviderId("missing"), +1));
        Assert.Throws<KeyNotFoundException>(() => layout.SetProviderVisible(new ProviderId("missing"), false));
        Assert.Throws<KeyNotFoundException>(() => layout.SetProviderHighlighted(new ProviderId("missing"), true));
        Assert.Throws<KeyNotFoundException>(() =>
            layout.MoveMetric(ProviderA, new MetricId("missing"), +1));
        Assert.Throws<KeyNotFoundException>(() =>
            layout.SetMetricVisible(ProviderA, new MetricId("missing"), false));
        Assert.Throws<KeyNotFoundException>(() =>
            layout.SetMetricHighlighted(new ProviderId("missing"), MetricX, true));
    }

    [Fact]
    public void MutationsRejectNullProviderAndMetricIds()
    {
        var layout = CreateSingleProviderWithThreeMetrics();

        Assert.Throws<ArgumentNullException>(() => layout.MoveProvider(null!, 1));
        Assert.Throws<ArgumentNullException>(() => layout.SetProviderVisible(null!, true));
        Assert.Throws<ArgumentNullException>(() => layout.SetProviderHighlighted(null!, true));
        Assert.Throws<ArgumentNullException>(() => layout.MoveMetric(ProviderA, null!, 1));
        Assert.Throws<ArgumentNullException>(() => layout.SetMetricVisible(null!, MetricX, true));
        Assert.Throws<ArgumentNullException>(() => layout.SetMetricHighlighted(ProviderA, null!, true));
        Assert.Throws<ArgumentNullException>(() => layout.SetMetricOnDemand(null!, MetricX, true));
        Assert.Throws<ArgumentNullException>(() => layout.SetMetricOnDemand(ProviderA, null!, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-2)]
    [InlineData(100)]
    public void MutationsInvalidOffsetThrowArgumentOutOfRangeException(int offset)
    {
        var layout = CreateThreeProviderLayout();

        Assert.Throws<ArgumentOutOfRangeException>(() => layout.MoveProvider(ProviderA, offset));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.MoveMetric(ProviderA, MetricX, offset));
    }

    [Fact]
    public void ReconcilePreservesSavedAndUnknownOrderAppendsNewCatalogItems()
    {
        var saved = new DashboardLayout(
        [
            new ProviderLayoutPreference(
                ProviderUnknown,
                isVisible: false,
                isHighlighted: true,
                [
                    new MetricLayoutPreference(MetricUnknown, false, true),
                    new MetricLayoutPreference(MetricX, true, false),
                ]),
            new ProviderLayoutPreference(
                ProviderB,
                isVisible: true,
                isHighlighted: false,
                [
                    new MetricLayoutPreference(MetricY, false, false),
                ]),
        ]);

        var catalogProviders = new[] { ProviderA, ProviderB, ProviderC };
        var catalogMetrics = new Dictionary<ProviderId, IReadOnlyList<MetricId>>
        {
            [ProviderA] = new[] { MetricX, MetricY },
            [ProviderB] = new[] { MetricY, MetricZ },
            [ProviderC] = new[] { MetricX },
        };

        var reconciled = saved.Reconcile(catalogProviders, catalogMetrics);

        Assert.Equal(
            new[] { ProviderUnknown, ProviderB, ProviderA, ProviderC },
            reconciled.Providers.Select(p => p.ProviderId));

        var unknown = reconciled.Providers[0];
        Assert.False(unknown.IsVisible);
        Assert.True(unknown.IsHighlighted);
        Assert.Equal(new[] { MetricUnknown, MetricX }, unknown.Metrics.Select(m => m.MetricId));
        Assert.False(unknown.Metrics[0].IsVisible);
        Assert.True(unknown.Metrics[0].IsHighlighted);

        var b = reconciled.Providers[1];
        Assert.Equal(new[] { MetricY, MetricZ }, b.Metrics.Select(m => m.MetricId));
        Assert.False(b.Metrics[0].IsVisible);
        Assert.True(b.Metrics[1].IsVisible);
        Assert.False(b.Metrics[1].IsHighlighted);

        var a = reconciled.Providers[2];
        Assert.True(a.IsVisible);
        Assert.False(a.IsHighlighted);
        Assert.Equal(new[] { MetricX, MetricY }, a.Metrics.Select(m => m.MetricId));
        Assert.All(a.Metrics, m =>
        {
            Assert.True(m.IsVisible);
            Assert.False(m.IsHighlighted);
        });

        var c = reconciled.Providers[3];
        Assert.True(c.IsVisible);
        Assert.False(c.IsHighlighted);
        Assert.Equal(new[] { MetricX }, c.Metrics.Select(m => m.MetricId));
    }

    [Fact]
    public void ReconcileIsIdempotent()
    {
        var saved = CreateThreeProviderLayout();
        var catalogProviders = new[] { ProviderA, ProviderB, ProviderC, new ProviderId("provider-d") };
        var catalogMetrics = new Dictionary<ProviderId, IReadOnlyList<MetricId>>
        {
            [ProviderA] = new[] { MetricX, MetricY, new MetricId("metric-new") },
            [ProviderB] = new[] { MetricX },
            [ProviderC] = new[] { MetricZ },
            [new ProviderId("provider-d")] = new[] { MetricX },
        };

        var once = saved.Reconcile(catalogProviders, catalogMetrics);
        var twice = once.Reconcile(catalogProviders, catalogMetrics);

        Assert.Equal(once, twice);
        Assert.Equal(
            once.Providers.Select(p => p.ProviderId.Value),
            twice.Providers.Select(p => p.ProviderId.Value));
    }

    [Fact]
    public void ReconcileCatalogEntriesUseDefaultsAndKeepSavedOnDemandMembership()
    {
        var saved = new DashboardLayout(
        [
            new ProviderLayoutPreference(
                ProviderA,
                true,
                false,
                [new MetricLayoutPreference(MetricX, true, false, isOnDemand: true)]),
        ]);
        var catalog = new Dictionary<ProviderId, IReadOnlyList<MetricLayoutCatalogEntry>>
        {
            [ProviderA] =
            [
                new MetricLayoutCatalogEntry(MetricX, isOnDemand: false),
                new MetricLayoutCatalogEntry(MetricY, isOnDemand: true),
            ],
        };

        DashboardLayout reconciled = saved.ReconcileWithMetricCatalog([ProviderA], catalog);

        Assert.Equal([MetricX, MetricY], reconciled.Providers[0].Metrics.Select(item => item.MetricId));
        Assert.True(reconciled.Providers[0].Metrics[0].IsOnDemand);
        Assert.True(reconciled.Providers[0].Metrics[1].IsOnDemand);
        Assert.True(reconciled.Providers[0].Metrics[1].IsVisible);
        Assert.False(reconciled.Providers[0].Metrics[1].IsHighlighted);
    }

    [Fact]
    public void LegacyMetricCatalogOverloadDefaultsNewMetricsToAlwaysVisible()
    {
        DashboardLayout reconciled = DashboardLayout.Empty.Reconcile(
            [ProviderA],
            new Dictionary<ProviderId, IReadOnlyList<MetricId>>
            {
                [ProviderA] = [MetricX],
            });

        Assert.False(Assert.Single(reconciled.Providers[0].Metrics).IsOnDemand);
    }

    [Fact]
    public void ReconcilePreservesSavedItemsAtPersistedLimits()
    {
        var fullMetrics = Enumerable.Range(0, DashboardLayout.MaxMetricsPerProvider)
            .Select(index => new MetricLayoutPreference(new MetricId($"metric-{index}"), true, false))
            .ToArray();
        var fullProviders = Enumerable.Range(0, DashboardLayout.MaxProviders)
            .Select(index => new ProviderLayoutPreference(
                new ProviderId($"provider-{index}"),
                true,
                false,
                index == 0 ? fullMetrics : []))
            .ToArray();
        var layout = new DashboardLayout(fullProviders);
        var addedProvider = new ProviderId("provider-new");
        var addedMetric = new MetricId("metric-new");

        var reconciled = layout.Reconcile(
            [.. fullProviders.Select(item => item.ProviderId), addedProvider],
            new Dictionary<ProviderId, IReadOnlyList<MetricId>>
            {
                [fullProviders[0].ProviderId] = [.. fullMetrics.Select(item => item.MetricId), addedMetric],
                [addedProvider] = [],
            });

        Assert.Equal(fullProviders, reconciled.Providers);
        Assert.DoesNotContain(reconciled.Providers, item => item.ProviderId == addedProvider);
        Assert.DoesNotContain(reconciled.Providers[0].Metrics, item => item.MetricId == addedMetric);
    }

    [Fact]
    public void EmptyHasNoProviders()
    {
        Assert.Empty(DashboardLayout.Empty.Providers);
    }

    private static DashboardLayout CreateThreeProviderLayout()
    {
        return new DashboardLayout(
        [
            new ProviderLayoutPreference(
                ProviderA,
                true,
                false,
                [new MetricLayoutPreference(MetricX, true, false)]),
            new ProviderLayoutPreference(
                ProviderB,
                true,
                false,
                [new MetricLayoutPreference(MetricX, true, false)]),
            new ProviderLayoutPreference(
                ProviderC,
                true,
                false,
                [new MetricLayoutPreference(MetricX, true, false)]),
        ]);
    }

    private static DashboardLayout CreateSingleProviderWithThreeMetrics()
    {
        return new DashboardLayout(
        [
            new ProviderLayoutPreference(
                ProviderA,
                true,
                false,
                [
                    new MetricLayoutPreference(MetricX, true, false),
                    new MetricLayoutPreference(MetricY, true, false),
                    new MetricLayoutPreference(MetricZ, true, false),
                ]),
        ]);
    }
}
