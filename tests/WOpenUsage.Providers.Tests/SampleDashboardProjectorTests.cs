using System.Globalization;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.Fakes;

namespace WOpenUsage.Providers.Tests;

public sealed class SampleDashboardProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProviderDescriptor Codex =
        new(new ProviderId("codex"), "Codex", isExperimental: true);

    [Theory]
    [InlineData(FakeProviderScenario.Success, SampleScenario.Normal, "$48.12", 58d)]
    [InlineData(FakeProviderScenario.NearLimit, SampleScenario.NearLimit, "$96.40", 8d)]
    [InlineData(FakeProviderScenario.Partial, SampleScenario.Partial, "$31.05", 58d)]
    public async Task CodexSnapshotOverlaysSpendAndSessionWithoutChangingTheFiveCardShell(
        FakeProviderScenario providerScenario,
        SampleScenario sampleScenario,
        string expectedTotal,
        double expectedRemaining)
    {
        using var culture = new CultureScope("en-US");
        var runtime = new FakeProviderRuntime(providerScenario, descriptor: Codex);
        ProviderOutcome outcome = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);
        ProviderSnapshot snapshot = outcome switch
        {
            ProviderOutcome.Success success => success.Snapshot,
            ProviderOutcome.PartialSuccess partial => partial.Snapshot,
            _ => throw new InvalidOperationException("The test scenario did not return a snapshot."),
        };

        SampleDashboardSnapshot dashboard = SampleDashboardProjector.Create(
            sampleScenario,
            snapshot,
            GetString);

        Assert.Equal(expectedTotal, dashboard.TotalSpendAmount);
        Assert.Equal(5, dashboard.SpendSlices.Count);
        Assert.Equal(5, dashboard.Providers.Count);
        SampleProviderCard codex = dashboard.Providers.Single(provider => provider.ProviderId == "codex");
        Assert.Equal(expectedRemaining, codex.Windows[0].RemainingPercent);
        Assert.Contains("Codex", codex.Windows[0].AutomationName, StringComparison.Ordinal);
        Assert.Contains(codex.Windows[0].ResetText, codex.Windows[0].AutomationName, StringComparison.Ordinal);
        Assert.True(codex.HasDetails);
        Assert.Equal("Sample data", codex.SourceValue);
        Assert.Empty(codex.Metrics);
        Assert.True(codex.HasSecondaryMetrics);
    }

    [Fact]
    public async Task ProjectorRejectsASnapshotForAnotherProvider()
    {
        var runtime = new FakeProviderRuntime(FakeProviderScenario.Success);
        ProviderOutcome.Success outcome = Assert.IsType<ProviderOutcome.Success>(
            await runtime.RefreshAsync(
                new RefreshContext(new FixedTimeProvider(Now)),
                CancellationToken.None));

        Assert.Throws<ArgumentException>(() => SampleDashboardProjector.Create(
            SampleScenario.Normal,
            outcome.Snapshot,
            GetString));
    }

    [Theory]
    [InlineData("en-US", "$48.12", "1.24M", "$7.10", "185K tokens")]
    [InlineData("es-ES", "48,12 US$", "1,24 M", "7,10 US$", "185 mil tokens")]
    public void SampleCatalogFormatsMoneyAndCompactTokensForCulture(
        string cultureName,
        string expectedTotal,
        string expectedGrokTokens,
        string expectedGrokSpend,
        string expectedCodexToday)
    {
        using var culture = new CultureScope(cultureName);

        SampleDashboardSnapshot dashboard = SampleDashboardCatalog.Create(
            SampleScenario.Normal,
            GetString);
        SampleProviderCard grok = dashboard.Providers.Single(provider => provider.ProviderId == "grok");
        SampleProviderCard codex = dashboard.Providers.Single(provider => provider.ProviderId == "codex");

        Assert.Equal(expectedTotal, dashboard.TotalSpendAmount);
        Assert.Equal(expectedGrokTokens, grok.Metrics[0].Value);
        Assert.Equal(expectedGrokSpend, grok.Metrics[1].Value);
        Assert.Equal(expectedCodexToday, codex.SecondaryMetricItems[0].Value);
    }

    [Theory]
    [InlineData(SampleScenario.Normal)]
    [InlineData(SampleScenario.NearLimit)]
    [InlineData(SampleScenario.Partial)]
    public void ProviderMetricsExposeStableUniqueLayoutIds(SampleScenario scenario)
    {
        SampleDashboardSnapshot dashboard = SampleDashboardCatalog.Create(scenario, GetString);

        foreach (SampleProviderCard provider in dashboard.Providers)
        {
            string[] ids = provider.Windows.Select(item => item.LayoutMetricId)
                .Concat(provider.Metrics.Select(item => item.LayoutMetricId))
                .Concat(provider.SecondaryMetricItems.Select(item => item.LayoutMetricId))
                .Concat(provider.SecondaryWindowItems.Select(item => item.LayoutMetricId))
                .ToArray();

            Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        }
    }

    private static string GetString(string key) =>
        key switch
        {
            "SampleSpendAccessibleNameFormat" => "Total {0}, providers {1}. {2}",
            "SampleRemainingFormat" => "{0}% remaining",
            "SampleResetHoursFormat" => "Resets in {0} h",
            "SampleResetDaysFormat" => "Resets in {0} d",
            "SampleResetDaysHoursFormat" => "Resets in {0} d {1} h",
            "ProviderSourceLabel" => "Source",
            "ProviderSourceSample" => "Sample data",
            "ProviderObservedLabel" => "Updated",
            "ProviderObservedNow" => "Now",
            "ProviderDetailsTooltipFormat" => "Source: {0}. Updated: {1}.",
            "ProviderDetailsAutomationNameFormat" => "Details for {0}",
            "SampleUsdFormat" when CultureInfo.CurrentCulture.Name == "es-ES" => "{0:N2} US$",
            "SampleUsdFormat" => "${0:N2}",
            "SampleUsdCompactFormat" when CultureInfo.CurrentCulture.Name == "es-ES" => "{0:N2}$",
            "SampleUsdCompactFormat" => "${0:N2}",
            "SampleCompactThousandsFormat" when CultureInfo.CurrentCulture.Name == "es-ES" => "{0:0.##} mil",
            "SampleCompactThousandsFormat" => "{0:0.##}K",
            "SampleCompactMillionsFormat" when CultureInfo.CurrentCulture.Name == "es-ES" => "{0:0.##} M",
            "SampleCompactMillionsFormat" => "{0:0.##}M",
            "SampleTokenThousandsFormat" when CultureInfo.CurrentCulture.Name == "es-ES" => "{0:0.##} mil tokens",
            "SampleTokenThousandsFormat" => "{0:0.##}K tokens",
            "SampleTokenMillionsFormat" when CultureInfo.CurrentCulture.Name == "es-ES" => "{0:0.##} M tokens",
            "SampleTokenMillionsFormat" => "{0:0.##}M tokens",
            "SampleTokenExactFormat" => "{0:N0} tokens",
            _ => key,
        };

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string cultureName)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
