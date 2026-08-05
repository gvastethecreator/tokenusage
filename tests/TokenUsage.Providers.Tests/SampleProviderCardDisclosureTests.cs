using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;

namespace TokenUsage.Providers.Tests;

public sealed class ProviderCardDisclosureTests
{
    [Fact]
    public void OnDemandMetricsDisclosureStartsClosedAndNotifiesOnlyOnChange()
    {
        var card = new ProviderCard(
            "codex",
            "SampleProvider.Codex",
            "Codex",
            "Plus",
            "Quota and local usage",
            NoticeText: null,
            Windows: [],
            Metrics: []);
        var changedProperties = new List<string?>();
        card.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        Assert.False(card.IsOnDemandMetricsExpanded);

        card.IsOnDemandMetricsExpanded = true;
        card.IsOnDemandMetricsExpanded = true;
        card.IsOnDemandMetricsExpanded = false;

        Assert.Equal(
            [nameof(ProviderCard.IsOnDemandMetricsExpanded), nameof(ProviderCard.IsOnDemandMetricsExpanded)],
            changedProperties);
    }
}
