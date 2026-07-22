using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.Fakes;

namespace WOpenUsage.Providers.Tests;

public sealed class FakeProviderRuntimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SuccessIsDeterministicAndFresh()
    {
        var clock = new AdjustableTimeProvider(Now);
        var runtime = new FakeProviderRuntime(FakeProviderScenario.Success);
        var context = new RefreshContext(clock);

        ProviderOutcome result = await runtime.RefreshAsync(context, CancellationToken.None);

        ProviderOutcome.Success success = Assert.IsType<ProviderOutcome.Success>(result);
        Assert.Equal("fake", success.Snapshot.ProviderId.Value);
        Assert.True(runtime.Descriptor.IsExperimental);
        Assert.Equal(Now, success.Snapshot.FetchedAtUtc);
        Assert.Equal(CoverageKind.Complete, success.Snapshot.Coverage);
        Assert.Collection(
            success.Snapshot.Metrics,
            metric => Assert.IsType<ProgressMetricSnapshot>(metric),
            metric => Assert.IsType<ScalarMetricSnapshot>(metric));
        Assert.False(SnapshotFreshness.IsStale(success.Snapshot, clock));
        Assert.All(success.Snapshot.Metrics, metric =>
        {
            Assert.Equal(SourceKind.Synthetic, metric.Provenance.SourceKind);
            Assert.Equal("fake/1", metric.Provenance.AdapterVersion);
        });
    }

    [Fact]
    public async Task PartialCarriesTypedWarningAndPartialCoverage()
    {
        var runtime = new FakeProviderRuntime(FakeProviderScenario.Partial);
        var context = new RefreshContext(new AdjustableTimeProvider(Now));

        ProviderOutcome result = await runtime.RefreshAsync(context, CancellationToken.None);

        ProviderOutcome.PartialSuccess partial = Assert.IsType<ProviderOutcome.PartialSuccess>(result);
        Assert.Equal(CoverageKind.Partial, partial.Snapshot.Coverage);
        Assert.Contains(
            partial.Warnings,
            warning => warning.Code == ProviderWarningCode.PartialCoverage);
    }

    [Fact]
    public async Task StaleScenarioRemainsSuccessAndUsesTheInjectedClock()
    {
        var clock = new AdjustableTimeProvider(Now);
        var runtime = new FakeProviderRuntime(FakeProviderScenario.Stale);
        var context = new RefreshContext(clock, staleAfter: TimeSpan.FromMinutes(10));

        ProviderOutcome result = await runtime.RefreshAsync(context, CancellationToken.None);

        ProviderOutcome.Success success = Assert.IsType<ProviderOutcome.Success>(result);
        Assert.Equal(Now, success.Snapshot.FetchedAtUtc);
        Assert.True(SnapshotFreshness.IsStale(success.Snapshot, clock, context.StaleAfter));
        Assert.Equal(
            Now.Subtract(context.StaleAfter).AddTicks(-1),
            success.Snapshot.SourceObservedAtUtc);
    }

    [Fact]
    public async Task ErrorCarriesTypedFailureAndPreservesLastGood()
    {
        ProviderSnapshot lastGood = FakeProviderRuntime.CreateSnapshot(
            Now.AddMinutes(-1),
            Now.AddMinutes(-2),
            CoverageKind.Complete);
        var runtime = new FakeProviderRuntime(FakeProviderScenario.Error);
        var context = new RefreshContext(new AdjustableTimeProvider(Now), lastGood);

        ProviderOutcome result = await runtime.RefreshAsync(context, CancellationToken.None);

        ProviderOutcome.TransientFailure failure =
            Assert.IsType<ProviderOutcome.TransientFailure>(result);
        Assert.Equal(ProviderErrorCode.TransientSourceFailure, failure.Error.Code);
        Assert.Same(lastGood, failure.LastGood);
    }

    [Fact]
    public async Task ConfiguredDescriptorIsUsedByTheSnapshotAndCacheContract()
    {
        var descriptor = new ProviderDescriptor(
            new ProviderId("codex"),
            "Codex",
            isExperimental: true);
        var runtime = new FakeProviderRuntime(
            FakeProviderScenario.Success,
            descriptor: descriptor);

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new AdjustableTimeProvider(Now)),
            CancellationToken.None);

        ProviderOutcome.Success success = Assert.IsType<ProviderOutcome.Success>(result);
        Assert.Same(descriptor, runtime.Descriptor);
        Assert.Equal(descriptor.Id, success.Snapshot.ProviderId);
        Assert.Equal(descriptor.DisplayName, success.Snapshot.DisplayName);
    }

    [Fact]
    public async Task NearLimitProducesAVisibleLowRemainingValue()
    {
        var runtime = new FakeProviderRuntime(FakeProviderScenario.NearLimit);

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new AdjustableTimeProvider(Now)),
            CancellationToken.None);

        ProviderOutcome.Success success = Assert.IsType<ProviderOutcome.Success>(result);
        ProgressMetricSnapshot progress = Assert.IsType<ProgressMetricSnapshot>(success.Snapshot.Metrics[0]);
        Assert.Equal(8m, progress.RemainingPercent);
    }

    [Fact]
    public async Task OptionalDelayCanBeCanceledWithoutPublishingAnOutcome()
    {
        var runtime = new FakeProviderRuntime(
            FakeProviderScenario.Success,
            delay: TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.RefreshAsync(
                new RefreshContext(new AdjustableTimeProvider(Now)),
                cancellation.Token));
    }

    [Fact]
    public async Task DetectionIsLocalAndAvailable()
    {
        var runtime = new FakeProviderRuntime(FakeProviderScenario.Success);

        ProviderDetection result = await runtime.DetectAsync(CancellationToken.None);

        Assert.IsType<ProviderDetection.Available>(result);
    }

    [Fact]
    public async Task CanceledDetectionStopsBeforePublishingAResult()
    {
        var runtime = new FakeProviderRuntime(FakeProviderScenario.Success);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await runtime.DetectAsync(cancellation.Token));
    }

    [Fact]
    public async Task CanceledRefreshStopsBeforePublishingAnOutcome()
    {
        var runtime = new FakeProviderRuntime(FakeProviderScenario.Success);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.RefreshAsync(
                new RefreshContext(new AdjustableTimeProvider(Now)),
                cancellation.Token));
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
