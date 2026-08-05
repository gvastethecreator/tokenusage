using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Tests;

public sealed class ResilientProviderRuntimeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentRefreshesNeverEnterProviderTogether()
    {
        var provider = new BlockingProvider();
        var runtime = new ResilientProviderRuntime(
            provider,
            new ProviderBackoffOptions(jitterRatio: 0));
        var context = new RefreshContext(
            new MutableTimeProvider(Now),
            lastGood: null,
            forceRefresh: true);

        Task<ProviderOutcome> first = runtime.RefreshAsync(context, CancellationToken.None);
        await provider.FirstEntry.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<ProviderOutcome> second = runtime.RefreshAsync(context, CancellationToken.None);

        Assert.False(second.IsCompleted);
        Assert.Equal(1, provider.MaximumConcurrentCalls);

        provider.ReleaseFirst.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, provider.MaximumConcurrentCalls);
        Assert.Equal(2, provider.RefreshCalls);
    }

    [Fact]
    public async Task FailureBacksOffAutomaticRefreshWhileManualRefreshBypassesAndResetsIt()
    {
        ProviderSnapshot lastGood = CreateSnapshot();
        var clock = new MutableTimeProvider(Now);
        var provider = new SequenceProvider(
            new ProviderOutcome.TransientFailure(
                new ProviderError(
                    ProviderErrorCode.TransientSourceFailure,
                    "Synthetic transient failure."),
                lastGood),
            new ProviderOutcome.Success(lastGood),
            new ProviderOutcome.Success(lastGood));
        var runtime = new ResilientProviderRuntime(
            provider,
            new ProviderBackoffOptions(
                initialDelay: TimeSpan.FromSeconds(15),
                maximumDelay: TimeSpan.FromMinutes(5),
                jitterRatio: 0));

        ProviderOutcome first = await runtime.RefreshAsync(
            new RefreshContext(clock, lastGood, forceRefresh: false),
            CancellationToken.None);
        ProviderOutcome throttled = await runtime.RefreshAsync(
            new RefreshContext(clock, lastGood, forceRefresh: false),
            CancellationToken.None);
        ProviderOutcome forced = await runtime.RefreshAsync(
            new RefreshContext(clock, lastGood, forceRefresh: true),
            CancellationToken.None);
        ProviderOutcome afterReset = await runtime.RefreshAsync(
            new RefreshContext(clock, lastGood, forceRefresh: false),
            CancellationToken.None);

        ProviderOutcome.TransientFailure failure =
            Assert.IsType<ProviderOutcome.TransientFailure>(first);
        Assert.Equal(Now.AddSeconds(15), failure.RetryAtUtc);
        ProviderOutcome.Throttled retry = Assert.IsType<ProviderOutcome.Throttled>(throttled);
        Assert.Equal(Now.AddSeconds(15), retry.RetryAtUtc);
        Assert.Same(lastGood, retry.LastGood);
        Assert.IsType<ProviderOutcome.Success>(forced);
        Assert.IsType<ProviderOutcome.Success>(afterReset);
        Assert.Equal(3, provider.RefreshCalls);
    }

    [Fact]
    public async Task RepeatedContractFailuresUseCappedExponentialBackoff()
    {
        var clock = new MutableTimeProvider(Now);
        var provider = new SequenceProvider(
            ContractFailure(),
            ContractFailure(),
            ContractFailure());
        var runtime = new ResilientProviderRuntime(
            provider,
            new ProviderBackoffOptions(
                initialDelay: TimeSpan.FromSeconds(10),
                maximumDelay: TimeSpan.FromSeconds(15),
                jitterRatio: 0));

        await runtime.RefreshAsync(
            new RefreshContext(clock, null, forceRefresh: true),
            CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(10));
        await runtime.RefreshAsync(
            new RefreshContext(clock, null, forceRefresh: true),
            CancellationToken.None);
        ProviderOutcome throttled = await runtime.RefreshAsync(
            new RefreshContext(clock, null, forceRefresh: false),
            CancellationToken.None);

        ProviderOutcome.Throttled retry = Assert.IsType<ProviderOutcome.Throttled>(throttled);
        Assert.Equal(clock.GetUtcNow().AddSeconds(15), retry.RetryAtUtc);
        Assert.Equal(2, provider.RefreshCalls);
    }

    private static ProviderOutcome.ContractFailure ContractFailure() =>
        new(
            new ProviderError(ProviderErrorCode.ContractViolation, "Synthetic contract failure."),
            lastGood: null);

    private static ProviderSnapshot CreateSnapshot() =>
        new(
            new ProviderId("fake"),
            "Fake provider",
            "Sample",
            Now,
            Now,
            "UTC",
            [],
            CoverageKind.Complete,
            1);

    private sealed class BlockingProvider : IProviderRuntime
    {
        private int _activeCalls;
        private int _refreshCalls;

        public ProviderDescriptor Descriptor { get; } =
            new(new ProviderId("fake"), "Fake provider");

        public TaskCompletionSource FirstEntry { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaximumConcurrentCalls { get; private set; }

        public int RefreshCalls => _refreshCalls;

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public async Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref _activeCalls);
            MaximumConcurrentCalls = Math.Max(MaximumConcurrentCalls, active);
            int call = Interlocked.Increment(ref _refreshCalls);
            try
            {
                if (call == 1)
                {
                    FirstEntry.TrySetResult();
                    await ReleaseFirst.Task.WaitAsync(cancellationToken);
                }

                return new ProviderOutcome.Success(CreateSnapshot());
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }

    private sealed class SequenceProvider(params ProviderOutcome[] outcomes) : IProviderRuntime
    {
        private readonly Queue<ProviderOutcome> _outcomes = new(outcomes);

        public ProviderDescriptor Descriptor { get; } =
            new(new ProviderId("fake"), "Fake provider");

        public int RefreshCalls { get; private set; }

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCalls++;
            return Task.FromResult(_outcomes.Dequeue());
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
