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

    private static string GetString(string key) =>
        key switch
        {
            "SampleSpendAccessibleNameFormat" => "Total {0}, providers {1}. {2}",
            "SampleRemainingFormat" => "{0}% remaining",
            "SampleResetHoursFormat" => "Resets in {0} h",
            "SampleResetDaysFormat" => "Resets in {0} d",
            "SampleResetDaysHoursFormat" => "Resets in {0} d {1} h",
            _ => key,
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
