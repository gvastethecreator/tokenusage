using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Layout;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Providers.Tests;

public sealed class DashboardLayoutProjectorTests
{
    [Fact]
    public void EmptyLayoutUsesCatalogOrderAndVisibleDefaults()
    {
        SampleDashboardSnapshot source = Dashboard(
            Card("codex", "Codex"),
            Card("grok", "Grok Build"));

        DashboardLayoutProjection result = DashboardLayoutProjector.Apply(
            source,
            DashboardLayout.Empty,
            "Highlighted");

        Assert.Equal(["codex", "grok"], result.Layout.Providers.Select(Id));
        Assert.Equal(["codex", "grok"], result.Dashboard.Providers.Select(CardId));
        Assert.Equal(["codex", "grok"], result.Providers.Select(RowId));
        Assert.False(result.Providers[0].CanMoveUp);
        Assert.True(result.Providers[0].CanMoveDown);
        Assert.True(result.Providers[1].CanMoveUp);
        Assert.False(result.Providers[1].CanMoveDown);
        Assert.Equal("Move Codex up", result.Providers[0].MoveUpAutomationName);
        Assert.Equal("Show or hide Grok Build", result.Providers[1].VisibilityAutomationName);
    }

    [Fact]
    public void SavedOrderVisibilityAndHighlightAreApplied()
    {
        SampleDashboardSnapshot source = Dashboard(
            Card("codex", "Codex"),
            Card("grok", "Grok Build"));
        var saved = new DashboardLayout(
        [
            Preference("grok", isVisible: false, isHighlighted: false),
            Preference("codex", isVisible: true, isHighlighted: true),
        ]);

        DashboardLayoutProjection result = DashboardLayoutProjector.Apply(
            source,
            saved,
            "Highlighted");

        Assert.Equal(["grok", "codex"], result.Providers.Select(RowId));
        SampleProviderCard card = Assert.Single(result.Dashboard.Providers);
        Assert.Equal("codex", card.ProviderId);
        Assert.True(card.IsHighlighted);
        Assert.Equal("Highlighted", card.HighlightLabel);
        Assert.Equal("Codex. Highlighted", card.CardAutomationName);
    }

    [Fact]
    public void SavedProviderColorFlowsToCardRowAndSpendSlice()
    {
        SampleDashboardSnapshot source = Dashboard(Card("codex", "Codex")) with
        {
            SpendSlices = [new SampleSpendSlice("codex", "Codex", 12.3, "$12.30")],
        };
        var saved = new DashboardLayout(
        [
            Preference("codex", isVisible: true, isHighlighted: false, colorHex: "#123ABC"),
        ]);

        DashboardLayoutProjection result = DashboardLayoutProjector.Apply(
            source,
            saved,
            "Highlighted");

        Assert.Equal("#123ABC", Assert.Single(result.Providers).ColorHex);
        Assert.Equal("#123ABC", Assert.Single(result.Dashboard.Providers).ProviderColorHex);
        Assert.Equal("#123ABC", Assert.Single(result.Dashboard.SpendSlices).ColorHex);
    }

    [Fact]
    public void UnknownSavedProviderStaysInLayoutOnly()
    {
        SampleDashboardSnapshot source = Dashboard(Card("codex", "Codex"));
        var saved = new DashboardLayout(
        [
            Preference("legacy", isVisible: true, isHighlighted: true),
            Preference("codex", isVisible: true, isHighlighted: false),
        ]);

        DashboardLayoutProjection result = DashboardLayoutProjector.Apply(
            source,
            saved,
            "Highlighted");

        Assert.Equal(["legacy", "codex"], result.Layout.Providers.Select(Id));
        Assert.Equal("codex", Assert.Single(result.Providers).ProviderId);
        Assert.Equal("codex", Assert.Single(result.Dashboard.Providers).ProviderId);
    }

    [Fact]
    public void NewCatalogProviderAppendsAfterSavedProviders()
    {
        SampleDashboardSnapshot source = Dashboard(
            Card("codex", "Codex"),
            Card("grok", "Grok Build"));
        var saved = new DashboardLayout(
        [
            Preference("codex", isVisible: true, isHighlighted: false),
        ]);

        DashboardLayoutProjection result = DashboardLayoutProjector.Apply(
            source,
            saved,
            "Highlighted");

        Assert.Equal(["codex", "grok"], result.Layout.Providers.Select(Id));
        Assert.True(result.Layout.Providers[1].IsVisible);
        Assert.False(result.Layout.Providers[1].IsHighlighted);
    }

    [Fact]
    public void DuplicateDashboardProviderIdsFail()
    {
        SampleDashboardSnapshot source = Dashboard(
            Card("codex", "Codex"),
            Card("codex", "Codex duplicate"));

        Assert.Throws<ArgumentException>(() => DashboardLayoutProjector.Apply(
            source,
            DashboardLayout.Empty,
            "Highlighted"));
    }

    [Fact]
    public void NullDashboardProviderFails()
    {
        SampleDashboardSnapshot source = Dashboard([null!]);

        Assert.Throws<ArgumentException>(() => DashboardLayoutProjector.Apply(
            source,
            DashboardLayout.Empty,
            "Highlighted"));
    }

    [Fact]
    public void NullDashboardProviderCollectionFails()
    {
        var source = new SampleDashboardSnapshot(
            SampleScenario.Normal,
            "$10.00",
            "30 days",
            "Total spend: $10.00",
            [],
            null!);

        Assert.Throws<ArgumentException>(() => DashboardLayoutProjector.Apply(
            source,
            DashboardLayout.Empty,
            "Highlighted"));
    }

    [Fact]
    public void ProjectionLeavesSourceUnchangedAndHidesMutableLists()
    {
        SampleProviderCard codex = Card("codex", "Codex");
        SampleProviderCard grok = Card("grok", "Grok Build");
        SampleDashboardSnapshot source = Dashboard(codex, grok);
        var saved = new DashboardLayout(
        [
            Preference("grok", isVisible: false, isHighlighted: false),
            Preference("codex", isVisible: true, isHighlighted: true),
        ]);

        DashboardLayoutProjection result = DashboardLayoutProjector.Apply(
            source,
            saved,
            "Highlighted");

        Assert.Equal([codex, grok], source.Providers);
        Assert.False(codex.IsHighlighted);
        Assert.IsNotType<List<DashboardProviderLayoutRow>>(result.Providers);
        Assert.IsNotType<List<SampleProviderCard>>(result.Dashboard.Providers);
    }

    [Fact]
    public void MetricPreferencesApplyAcrossQuotaPrimaryAndOnDemandSections()
    {
        SampleProviderCard sourceCard = CardWithMetrics();
        SampleDashboardSnapshot source = Dashboard(sourceCard);
        var saved = new DashboardLayout(
        [
            new ProviderLayoutPreference(
                new ProviderId("codex"),
                isVisible: true,
                isHighlighted: false,
                [
                    Metric("usage.secondary", isVisible: true, isHighlighted: false, isOnDemand: false),
                    Metric("quota.session", isVisible: true, isHighlighted: true, isOnDemand: false),
                    Metric("usage.primary", isVisible: false, isHighlighted: false, isOnDemand: false),
                ]),
        ]);

        DashboardLayoutProjection result = DashboardLayoutProjector.Apply(
            source,
            saved,
            "Highlighted",
            actionNameFormats: null,
            new DashboardMetricActionNameFormats(
                "Up {0}",
                "Down {0}",
                "Visible {0}",
                "Star {0}",
                "Always",
                "Demand",
                "Always action {0}",
                "Demand action {0}"));

        SampleProviderCard card = Assert.Single(result.Dashboard.Providers);
        Assert.Equal("usage.secondary", Assert.Single(card.Metrics).LayoutMetricId);
        Assert.Empty(card.SecondaryMetricItems);
        Assert.Empty(card.SecondaryWindowItems);
        SampleQuotaWindow quota = Assert.Single(card.Windows);
        Assert.Equal("quota.session", quota.LayoutMetricId);
        Assert.True(quota.IsHighlighted);
        Assert.Equal("Session: 50% remaining. Highlighted", quota.DisplayAutomationName);
        Assert.Collection(
            card.PrimaryMetricItems,
            item => Assert.Equal("usage.secondary", Assert.IsType<SampleMetric>(item.Metric).LayoutMetricId),
            item => Assert.Equal("quota.session", Assert.IsType<SampleQuotaWindow>(item.Window).LayoutMetricId));

        DashboardProviderLayoutRow providerRow = Assert.Single(result.Providers);
        Assert.Equal(
            ["usage.secondary", "quota.session", "usage.primary"],
            providerRow.Metrics.Select(metric => metric.MetricId));
        Assert.False(providerRow.Metrics[0].CanMoveUp);
        Assert.True(providerRow.Metrics[0].CanMoveDown);
        Assert.False(providerRow.Metrics[2].CanMoveDown);
        Assert.Equal("Always", providerRow.Metrics[0].SectionLabel);
        Assert.Equal("Up Secondary", providerRow.Metrics[0].MoveUpAutomationName);
        Assert.Equal("Demand action Secondary", providerRow.Metrics[0].SectionAutomationName);
        Assert.Equal(
            "DashboardLayout.Provider.codex.Metric.usage.secondary.Section",
            providerRow.Metrics[0].SectionAutomationId);
    }

    [Fact]
    public void UnknownSavedMetricStaysInLayoutWithoutAConfigurationRow()
    {
        var saved = new DashboardLayout(
        [
            new ProviderLayoutPreference(
                new ProviderId("codex"),
                isVisible: true,
                isHighlighted: false,
                [
                    Metric("legacy.metric", true, false, false),
                    Metric("quota.session", true, false, false),
                ]),
        ]);

        DashboardLayoutProjection result = DashboardLayoutProjector.Apply(
            Dashboard(CardWithMetrics()),
            saved,
            "Highlighted");

        Assert.Contains(result.Layout.Providers[0].Metrics, metric => metric.MetricId.Value == "legacy.metric");
        DashboardMetricLayoutRow row = Assert.Single(
            result.Providers[0].Metrics,
            metric => metric.MetricId == "quota.session");
        Assert.False(row.CanMoveUp);
    }

    [Fact]
    public void MissingOrDuplicateLayoutMetricIdsFailClosed()
    {
        SampleProviderCard missing = Card("codex", "Codex") with
        {
            Metrics = [new SampleMetric("Usage", "10")],
        };
        Assert.Throws<ArgumentException>(() => DashboardLayoutProjector.Apply(
            Dashboard(missing),
            DashboardLayout.Empty,
            "Highlighted"));

        SampleProviderCard duplicate = Card("codex", "Codex") with
        {
            Windows = [Window("quota.same")],
            Metrics = [new SampleMetric("Usage", "10", LayoutMetricId: "quota.same")],
        };
        Assert.Throws<ArgumentException>(() => DashboardLayoutProjector.Apply(
            Dashboard(duplicate),
            DashboardLayout.Empty,
            "Highlighted"));
    }

    private static SampleDashboardSnapshot Dashboard(params SampleProviderCard[] providers) =>
        new(
            SampleScenario.Normal,
            "$10.00",
            "30 days",
            "Total spend: $10.00",
            [],
            providers);

    private static SampleProviderCard Card(string providerId, string name) =>
        new(
            providerId,
            $"Provider.{name.Replace(" ", string.Empty, StringComparison.Ordinal)}",
            name,
            "Plan",
            "Capability",
            null,
            [],
            []);

    private static SampleProviderCard CardWithMetrics() =>
        Card("codex", "Codex") with
        {
            Windows = [Window("quota.session")],
            Metrics = [new SampleMetric("Primary", "10", LayoutMetricId: "usage.primary")],
            SecondaryMetrics = [new SampleMetric("Secondary", "20", LayoutMetricId: "usage.secondary")],
        };

    private static SampleQuotaWindow Window(string metricId) =>
        new(
            "Session",
            50,
            "50% remaining",
            "Resets in 1 h",
            "Session: 50% remaining",
            IsNearLimit: false,
            LayoutMetricId: metricId);

    private static MetricLayoutPreference Metric(
        string metricId,
        bool isVisible,
        bool isHighlighted,
        bool isOnDemand) =>
        new(new MetricId(metricId), isVisible, isHighlighted, isOnDemand);

    private static ProviderLayoutPreference Preference(
        string providerId,
        bool isVisible,
        bool isHighlighted,
        string? colorHex = null) =>
        new(new ProviderId(providerId), isVisible, isHighlighted, [], colorHex);

    private static string Id(ProviderLayoutPreference preference) =>
        preference.ProviderId.Value;

    private static string CardId(SampleProviderCard card) => card.ProviderId;

    private static string RowId(DashboardProviderLayoutRow row) => row.ProviderId;
}
