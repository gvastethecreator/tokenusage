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

    private static ProviderLayoutPreference Preference(
        string providerId,
        bool isVisible,
        bool isHighlighted) =>
        new(new ProviderId(providerId), isVisible, isHighlighted, []);

    private static string Id(ProviderLayoutPreference preference) =>
        preference.ProviderId.Value;

    private static string CardId(SampleProviderCard card) => card.ProviderId;

    private static string RowId(DashboardProviderLayoutRow row) => row.ProviderId;
}
