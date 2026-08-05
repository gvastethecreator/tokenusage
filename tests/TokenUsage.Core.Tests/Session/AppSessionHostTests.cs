using TokenUsage.Core.Alerts;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Session;

namespace TokenUsage.Core.Tests.Session;

public sealed class AppSessionHostTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartRunsInitialAndPeriodicRefreshThenStopEndsLifetime()
    {
        using var folder = new TemporaryFolder();
        var provider = new RecordingProvider("codex", CreateSnapshot("codex", 20m));
        var delay = new ControlledDelay();
        await using AppSessionHost host = CreateHost(folder, [provider], delay.DelayAsync);
        var reasons = new List<AppSessionRefreshReason>();
        var periodicCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        host.Updated += (_, update) =>
        {
            if (update.IsFinal)
            {
                reasons.Add(update.Reason);
                if (update.Reason == AppSessionRefreshReason.Periodic)
                {
                    periodicCompleted.TrySetResult();
                }
            }
        };

        await host.StartAsync();
        Assert.Equal(1, provider.RefreshCalls);
        await delay.Requested.Task.WaitAsync(TimeSpan.FromSeconds(2));

        delay.Release();
        await periodicCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await host.RefreshAsync(AppSessionRefreshReason.Manual, forceRefresh: true);
        await host.StopAsync();

        Assert.Equal(3, provider.RefreshCalls);
        Assert.Equal(
            [
                AppSessionRefreshReason.Initial,
                AppSessionRefreshReason.Periodic,
                AppSessionRefreshReason.Manual,
            ],
            reasons);
        Assert.Equal(AppSessionStatus.Stopped, host.Current.Status);
    }

    [Fact]
    public async Task NewManualRefreshCancelsBlockedRefreshAndPublishesNextResult()
    {
        using var folder = new TemporaryFolder();
        var provider = new FirstCallBlockingProvider(CreateSnapshot("codex", 70m));
        await using AppSessionHost host = CreateHost(folder, [provider]);

        Task first = host.RefreshAsync(AppSessionRefreshReason.Manual, forceRefresh: true);
        await provider.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task replacement = host.RefreshAsync(
            AppSessionRefreshReason.Manual,
            forceRefresh: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await replacement;

        Assert.Equal(2, provider.RefreshCalls);
        Assert.Equal(AppSessionStatus.Ready, host.Current.Status);
        Assert.Equal(70m, ProgressUsed(Assert.Single(host.Current.Snapshots)));
    }

    [Fact]
    public async Task StopWaitsForCanceledRefreshCleanup()
    {
        using var folder = new TemporaryFolder();
        var provider = new SlowCancellationProvider();
        await using AppSessionHost host = CreateHost(folder, [provider]);

        Task refresh = host.RefreshAsync(AppSessionRefreshReason.Manual, forceRefresh: true);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task stop = host.StopAsync();
        await provider.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(stop.IsCompleted);
        provider.ReleaseCleanup();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        await stop;

        Assert.True(provider.CleanupCompleted);
        Assert.Equal(AppSessionStatus.Stopped, host.Current.Status);
    }

    [Fact]
    public async Task ManualAndSelectedRefreshCallEachRequestedProviderOnce()
    {
        using var folder = new TemporaryFolder();
        var codex = new RecordingProvider("codex", CreateSnapshot("codex", 25m));
        var vercel = new RecordingProvider(
            "vercel-ai-gateway",
            CreateSnapshot("vercel-ai-gateway", 50m));
        await using AppSessionHost host = CreateHost(folder, [codex, vercel]);

        await host.RefreshAsync(AppSessionRefreshReason.Manual, forceRefresh: true);
        await host.RefreshAsync(
            AppSessionRefreshReason.ProviderAction,
            forceRefresh: true,
            vercel.Descriptor.Id);

        Assert.Equal(1, codex.RefreshCalls);
        Assert.Equal(2, vercel.RefreshCalls);
        Assert.Equal(2, host.Current.Snapshots.Count);
    }

    [Fact]
    public async Task ProviderResultRunsAlertHostAndPublishesIntent()
    {
        using var folder = new TemporaryFolder();
        var provider = new RecordingProvider("codex", CreateSnapshot("codex", 95m));
        await SaveEnabledAlertSettingsAsync(folder);
        await using AppSessionHost host = CreateHost(folder, [provider]);
        var alerts = new List<AlertNotificationIntent>();
        host.Updated += (_, update) => alerts.AddRange(update.Alerts);

        await host.RefreshAsync(AppSessionRefreshReason.Manual, forceRefresh: true);

        AlertNotificationIntent intent = Assert.Single(alerts);
        Assert.Equal(AlertKind.QuotaThreshold, intent.Kind);
        Assert.Equal("codex", intent.ProviderId);
    }

    private static AppSessionHost CreateHost(
        TemporaryFolder folder,
        IReadOnlyList<IProviderRuntime> providers,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        var clock = new FixedTimeProvider(Now);
        ProviderRefreshRegistration[] registrations = providers
            .Select(provider => new ProviderRefreshRegistration(
                provider,
                new SnapshotStore(
                    Path.Combine(
                        folder.Root,
                        provider.Descriptor.Id.Value,
                        SnapshotStore.DefaultFileName),
                    clock)))
            .ToArray();
        var alertHost = new AlertHost(
            new AlertDecisionStore(
                Path.Combine(folder.Root, AlertDecisionStore.DefaultFileName),
                clock),
            new AlertSettingsStore(
                Path.Combine(folder.Root, AlertSettingsStore.DefaultFileName),
                clock));
        return new AppSessionHost(
            new ProviderRefreshHost(registrations, clock),
            alertHost,
            clock,
            TimeSpan.FromMinutes(5),
            delayAsync);
    }

    private static Task SaveEnabledAlertSettingsAsync(TemporaryFolder folder) =>
        new AlertSettingsStore(Path.Combine(folder.Root, AlertSettingsStore.DefaultFileName))
            .SaveAsync(new AlertSettings(
                enabled: true,
                quotaThresholdPercent: 10,
                quotaThresholdEnabled: true,
                exhaustionForecastEnabled: false,
                staleDataEnabled: false,
                credentialFailureEnabled: false));

    private static ProviderSnapshot CreateSnapshot(string providerId, decimal used) =>
        new(
            new ProviderId(providerId),
            "Provider " + providerId,
            "Test",
            Now,
            Now,
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId("session"),
                    used,
                    100m,
                    Now.AddHours(1),
                    new DataProvenance(
                        SourceKind.Synthetic,
                        MeasurementKind.ProviderReported,
                        "test/1")),
            ],
            CoverageKind.Complete,
            1);

    private static decimal ProgressUsed(ProviderSnapshot snapshot) =>
        Assert.IsType<ProgressMetricSnapshot>(snapshot.Metrics[0]).Used;

    private sealed class RecordingProvider : IProviderRuntime
    {
        private readonly ProviderSnapshot _snapshot;

        public RecordingProvider(string providerId, ProviderSnapshot snapshot)
        {
            Descriptor = new ProviderDescriptor(new ProviderId(providerId), "Provider " + providerId);
            _snapshot = snapshot;
        }

        public ProviderDescriptor Descriptor { get; }

        public int RefreshCalls { get; private set; }

        public TaskCompletionSource SecondCall { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCalls++;
            if (RefreshCalls >= 2)
            {
                SecondCall.TrySetResult();
            }

            return Task.FromResult<ProviderOutcome>(new ProviderOutcome.Success(_snapshot));
        }
    }

    private sealed class FirstCallBlockingProvider(ProviderSnapshot snapshot) : IProviderRuntime
    {
        public ProviderDescriptor Descriptor { get; } =
            new(new ProviderId("codex"), "Codex");

        public int RefreshCalls { get; private set; }

        public TaskCompletionSource FirstCallStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public async Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken)
        {
            RefreshCalls++;
            if (RefreshCalls == 1)
            {
                FirstCallStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new ProviderOutcome.Success(snapshot);
        }
    }

    private sealed class SlowCancellationProvider : IProviderRuntime
    {
        private readonly TaskCompletionSource _cleanupRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ProviderDescriptor Descriptor { get; } =
            new(new ProviderId("codex"), "Codex");

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CleanupStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CleanupCompleted { get; private set; }

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public async Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The blocked provider returned unexpectedly.");
            }
            finally
            {
                CleanupStarted.TrySetResult();
                await _cleanupRelease.Task;
                CleanupCompleted = true;
            }
        }

        public void ReleaseCleanup() => _cleanupRelease.TrySetResult();
    }

    private sealed class ControlledDelay
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Requested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Requested.TrySetResult();
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await _release.Task.WaitAsync(cancellationToken);
                return;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Release() => _release.TrySetResult();

        private int _calls;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-app-session-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
