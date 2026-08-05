using TokenUsage.Core.Providers;
using TokenUsage.Providers.Codex;

namespace TokenUsage.Providers.Tests.Codex;

public sealed class CodexProviderRuntimeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 16, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CodexClientAvailability.Available, typeof(ProviderDetection.Available))]
    [InlineData(CodexClientAvailability.MissingCli, typeof(ProviderDetection.Unavailable))]
    [InlineData(CodexClientAvailability.UnsupportedVersion, typeof(ProviderDetection.Unavailable))]
    [InlineData(CodexClientAvailability.Unavailable, typeof(ProviderDetection.Unavailable))]
    public async Task DetectionMapsLocalAvailability(
        CodexClientAvailability availability,
        Type expectedType)
    {
        var factory = new StubFactory(availability, CreateReadyClient());
        var runtime = new CodexProviderRuntime(factory, "UTC");

        ProviderDetection result = await runtime.DetectAsync(CancellationToken.None);

        Assert.IsType(expectedType, result);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task ChatGptQuotaProducesARealCodexSnapshotAndUsesAccountPlanFallback()
    {
        StubClient client = CreateReadyClient(ratePlan: null, accountPlan: "plus");
        var factory = new StubFactory(CodexClientAvailability.Available, client);
        var runtime = new CodexProviderRuntime(factory, "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);

        ProviderOutcome.Success success = Assert.IsType<ProviderOutcome.Success>(result);
        Assert.Equal("codex", success.Snapshot.ProviderId.Value);
        Assert.Equal("Plus", success.Snapshot.PlanLabel);
        Assert.Equal(Now, success.Snapshot.FetchedAtUtc);
        Assert.Equal(58m, Assert.IsType<ProgressMetricSnapshot>(success.Snapshot.Metrics[0]).RemainingPercent);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(1, client.HandshakeCount);
        Assert.Equal(1, client.AccountReadCount);
        Assert.Equal(1, client.RateLimitReadCount);
        Assert.Equal(1, client.UsageReadCount);
        Assert.True(client.IsDisposed);
    }

    [Fact]
    public async Task DailyUsageAddsOnlyObservedLocalPeriodMetrics()
    {
        StubClient client = CreateReadyClient();
        client.TokenUsage = new CodexTokenUsageSnapshot(
            new CodexUsageSummary(null, null, null, null, null),
            [
                new CodexUsageDailyBucket(new DateOnly(2026, 7, 22), 800),
                new CodexUsageDailyBucket(new DateOnly(2026, 7, 21), 400),
            ]);
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);

        ProviderSnapshot snapshot = Assert.IsType<ProviderOutcome.Success>(result).Snapshot;
        Assert.Equal(CoverageKind.Complete, snapshot.Coverage);
        Assert.Equal(
            [
                "quota.primary",
                "quota.primary.window-minutes",
                "usage.tokens.today",
                "usage.tokens.yesterday",
                "usage.tokens.7d",
                "usage.tokens.30d",
            ],
            snapshot.Metrics.Select(metric => metric.Id.Value));
        Assert.Equal(
            [800m, 400m, 1200m, 1200m],
            snapshot.Metrics
                .OfType<ScalarMetricSnapshot>()
                .Where(metric => metric.Unit == "tokens")
                .Select(metric => metric.Value));
    }

    [Fact]
    public async Task SuccessfulEmptyUsageDoesNotInventZeroMetrics()
    {
        StubClient client = CreateReadyClient();
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);

        ProviderSnapshot snapshot = Assert.IsType<ProviderOutcome.Success>(result).Snapshot;
        Assert.DoesNotContain(
            snapshot.Metrics,
            metric => metric.Id.Value.StartsWith("usage.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("rpc")]
    [InlineData("missing-method")]
    [InlineData("protocol")]
    public async Task UsageFailureKeepsFreshQuotaAsPartialSuccess(string failure)
    {
        StubClient client = CreateReadyClient();
        client.UsageException = failure switch
        {
            "timeout" => new CodexRequestTimeoutException(),
            "rpc" => new CodexRpcException(-32001),
            "missing-method" => new CodexRpcException(-32601),
            "protocol" => new CodexProtocolException("PRIVATE_USAGE_SENTINEL"),
            _ => throw new InvalidOperationException(),
        };
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);

        ProviderOutcome.PartialSuccess partial =
            Assert.IsType<ProviderOutcome.PartialSuccess>(result);
        Assert.Equal(CoverageKind.Partial, partial.Snapshot.Coverage);
        Assert.Contains(
            partial.Snapshot.Metrics,
            metric => metric.Id.Value == "quota.primary");
        Assert.DoesNotContain(
            partial.Snapshot.Metrics,
            metric => metric.Id.Value.StartsWith("usage.", StringComparison.Ordinal));
        ProviderWarning warning = Assert.Single(partial.Warnings);
        Assert.Equal(ProviderWarningCode.MissingMetric, warning.Code);
        Assert.DoesNotContain("PRIVATE_", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateUsageDayKeepsFreshQuotaAsPartialSuccess()
    {
        StubClient client = CreateReadyClient();
        client.TokenUsage = new CodexTokenUsageSnapshot(
            new CodexUsageSummary(null, null, null, null, null),
            [
                new CodexUsageDailyBucket(new DateOnly(2026, 7, 22), 1),
                new CodexUsageDailyBucket(new DateOnly(2026, 7, 22), 2),
            ]);
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);

        ProviderOutcome.PartialSuccess partial =
            Assert.IsType<ProviderOutcome.PartialSuccess>(result);
        Assert.Equal(CoverageKind.Partial, partial.Snapshot.Coverage);
        Assert.Contains(partial.Snapshot.Metrics, metric => metric.Id.Value == "quota.primary");
        Assert.DoesNotContain(partial.Snapshot.Metrics, metric => metric.Id.Value.StartsWith("usage.", StringComparison.Ordinal));
        Assert.Equal(
            ProviderWarningCode.MissingMetric,
            Assert.Single(partial.Warnings).Code);
    }

    [Fact]
    public async Task CancellationDuringUsageIsNeverPublishedAsPartialSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        StubClient client = CreateReadyClient();
        client.BeforeUsageRead = cancellation.Cancel;
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.RefreshAsync(
                new RefreshContext(new FixedTimeProvider(Now)),
                cancellation.Token));

        Assert.True(client.IsDisposed);
    }

    [Fact]
    public async Task MissingCliReturnsNotConfiguredWithoutCreatingAClient()
    {
        var factory = new StubFactory(CodexClientAvailability.MissingCli, CreateReadyClient());
        var runtime = new CodexProviderRuntime(factory, "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);

        ProviderOutcome.NotConfigured missing = Assert.IsType<ProviderOutcome.NotConfigured>(result);
        Assert.Equal("Codex CLI is not installed or could not be found.", missing.Reason);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task MissingCliAfterSuccessPreservesLastGoodAsTransientFailure()
    {
        ProviderSnapshot lastGood = CreateLastGood();
        var factory = new StubFactory(CodexClientAvailability.MissingCli, CreateReadyClient());
        var runtime = new CodexProviderRuntime(factory, "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now), lastGood),
            CancellationToken.None);

        ProviderOutcome.TransientFailure failure =
            Assert.IsType<ProviderOutcome.TransientFailure>(result);
        Assert.Equal("Codex CLI is not installed or could not be found.", failure.Error.Message);
        Assert.Same(lastGood, failure.LastGood);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task MissingChatGptLoginIsExplicitAndSkipsQuotaRead()
    {
        var client = new StubClient(
            new CodexAccountStatus(CodexAccountKind.None, requiresOpenAiAuth: true, planType: null),
            CreateRateLimits());
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);

        ProviderOutcome.NotConfigured missing = Assert.IsType<ProviderOutcome.NotConfigured>(result);
        Assert.Equal("Sign in with ChatGPT through Codex to read quota.", missing.Reason);
        Assert.Equal(0, client.RateLimitReadCount);
        Assert.True(client.IsDisposed);
    }

    [Theory]
    [InlineData(CodexAccountKind.ApiKey)]
    [InlineData(CodexAccountKind.AmazonBedrock)]
    [InlineData(CodexAccountKind.Other)]
    public async Task NonChatGptAuthIsAnExplicitUnsupportedAccount(CodexAccountKind kind)
    {
        var client = new StubClient(
            new CodexAccountStatus(kind, requiresOpenAiAuth: true, planType: null),
            CreateRateLimits());
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);

        ProviderOutcome.UnsupportedAccount unsupported =
            Assert.IsType<ProviderOutcome.UnsupportedAccount>(result);
        Assert.Equal(
            "The active Codex authentication does not provide ChatGPT quota.",
            unsupported.Reason);
        Assert.Equal(0, client.RateLimitReadCount);
    }

    [Fact]
    public async Task ChatGptAccountWithoutWindowsIsExplicitlyUnsupported()
    {
        var client = new StubClient(
            new CodexAccountStatus(CodexAccountKind.ChatGpt, true, "plus"),
            new CodexRateLimitsSnapshot(
                new CodexRateLimitBucket("plus", null, null),
                new Dictionary<string, CodexRateLimitBucket>()));
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);

        ProviderOutcome.UnsupportedAccount unsupported =
            Assert.IsType<ProviderOutcome.UnsupportedAccount>(result);
        Assert.Equal(
            "The active ChatGPT account did not report Codex quota windows.",
            unsupported.Reason);
    }

    [Fact]
    public async Task TimeoutIsTransientAndPreservesLastGood()
    {
        ProviderSnapshot lastGood = CreateLastGood();
        StubClient client = CreateReadyClient();
        client.AccountException = new CodexRequestTimeoutException();
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now), lastGood),
            CancellationToken.None);

        ProviderOutcome.TransientFailure failure =
            Assert.IsType<ProviderOutcome.TransientFailure>(result);
        Assert.Equal(ProviderErrorCode.TransientSourceFailure, failure.Error.Code);
        Assert.Equal("Codex app-server timed out while reading quota.", failure.Error.Message);
        Assert.Same(lastGood, failure.LastGood);
        Assert.True(client.IsDisposed);
    }

    [Fact]
    public async Task ClientStartupFailureIsTransientAndPreservesLastGood()
    {
        ProviderSnapshot lastGood = CreateLastGood();
        var runtime = new CodexProviderRuntime(new UnavailableFactory(), "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now), lastGood),
            CancellationToken.None);

        ProviderOutcome.TransientFailure failure =
            Assert.IsType<ProviderOutcome.TransientFailure>(result);
        Assert.Equal(ProviderErrorCode.TransientSourceFailure, failure.Error.Code);
        Assert.Equal("Codex app-server is unavailable.", failure.Error.Message);
        Assert.Same(lastGood, failure.LastGood);
    }

    [Fact]
    public async Task RpcRejectionIsTransientAndDoesNotExposeServerData()
    {
        StubClient client = CreateReadyClient();
        client.RateLimitException = new CodexRpcException(-32001);
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now)),
            CancellationToken.None);

        ProviderOutcome.TransientFailure failure =
            Assert.IsType<ProviderOutcome.TransientFailure>(result);
        Assert.Equal("Codex app-server could not return quota right now.", failure.Error.Message);
        Assert.DoesNotContain("-32001", failure.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingAccountMethodMarksTheCliContractUnsupported()
    {
        ProviderSnapshot lastGood = CreateLastGood();
        StubClient client = CreateReadyClient();
        client.AccountException = new CodexRpcException(-32601);
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now), lastGood),
            CancellationToken.None);

        ProviderOutcome.ContractFailure failure =
            Assert.IsType<ProviderOutcome.ContractFailure>(result);
        Assert.Equal(
            "The installed Codex CLI does not support the required account methods.",
            failure.Error.Message);
        Assert.Same(lastGood, failure.LastGood);
    }

    [Fact]
    public async Task ProtocolFailureIsAContractFailureAndPreservesLastGood()
    {
        ProviderSnapshot lastGood = CreateLastGood();
        StubClient client = CreateReadyClient();
        client.HandshakeException = new CodexProtocolException("PRIVATE_SERVER_SENTINEL");
        var runtime = new CodexProviderRuntime(
            new StubFactory(CodexClientAvailability.Available, client),
            "UTC");

        ProviderOutcome result = await runtime.RefreshAsync(
            new RefreshContext(new FixedTimeProvider(Now), lastGood),
            CancellationToken.None);

        ProviderOutcome.ContractFailure failure =
            Assert.IsType<ProviderOutcome.ContractFailure>(result);
        Assert.Equal(ProviderErrorCode.ContractViolation, failure.Error.Code);
        Assert.Equal("Codex app-server returned an unsupported response.", failure.Error.Message);
        Assert.DoesNotContain("PRIVATE_SERVER_SENTINEL", failure.Error.Message, StringComparison.Ordinal);
        Assert.Same(lastGood, failure.LastGood);
    }

    [Fact]
    public async Task PreCanceledRefreshDoesNotCreateOrDisposeAClient()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        StubClient client = CreateReadyClient();
        var factory = new StubFactory(CodexClientAvailability.Available, client);
        var runtime = new CodexProviderRuntime(
            factory,
            "UTC");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.RefreshAsync(
                new RefreshContext(new FixedTimeProvider(Now)),
                cancellation.Token));

        Assert.False(client.IsDisposed);
        Assert.Equal(0, factory.CreateCount);
    }

    private static StubClient CreateReadyClient(
        string? ratePlan = "plus",
        string? accountPlan = "plus") =>
        new(
            new CodexAccountStatus(CodexAccountKind.ChatGpt, true, accountPlan),
            CreateRateLimits(ratePlan));

    private static CodexRateLimitsSnapshot CreateRateLimits(string? planType = "plus") =>
        new(
            new CodexRateLimitBucket(
                planType,
                new CodexRateLimitWindow(42, Now.AddHours(4), 300),
                null),
            new Dictionary<string, CodexRateLimitBucket>());

    private static ProviderSnapshot CreateLastGood()
    {
        CodexSnapshotMappingResult result =
            CodexRateLimitsSnapshotMapper.Map(CreateRateLimits(), Now.AddMinutes(-1), "UTC");
        return Assert.IsType<CodexSnapshotMappingResult.Available>(result).Snapshot;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubFactory(
        CodexClientAvailability availability,
        ICodexQuotaClient client) : ICodexQuotaClientFactory
    {
        public int CreateCount { get; private set; }

        public ValueTask<CodexClientAvailability> DetectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(availability);
        }

        public Task<ICodexQuotaClient> CreateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCount++;
            return Task.FromResult(client);
        }
    }

    private sealed class UnavailableFactory : ICodexQuotaClientFactory
    {
        public ValueTask<CodexClientAvailability> DetectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CodexClientAvailability.Available);
        }

        public Task<ICodexQuotaClient> CreateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new CodexClientUnavailableException();
        }
    }

    private sealed class StubClient(
        CodexAccountStatus accountStatus,
        CodexRateLimitsSnapshot rateLimits) : ICodexQuotaClient
    {
        public Exception? HandshakeException { get; set; }

        public Exception? AccountException { get; set; }

        public Exception? RateLimitException { get; set; }

        public Exception? UsageException { get; set; }

        public Action? BeforeUsageRead { get; set; }

        public CodexTokenUsageSnapshot TokenUsage { get; set; } =
            new(new CodexUsageSummary(null, null, null, null, null), []);

        public int HandshakeCount { get; private set; }

        public int AccountReadCount { get; private set; }

        public int RateLimitReadCount { get; private set; }

        public int UsageReadCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task HandshakeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HandshakeCount++;
            return HandshakeException is null
                ? Task.CompletedTask
                : Task.FromException(HandshakeException);
        }

        public Task<CodexAccountStatus> ReadAccountStatusAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AccountReadCount++;
            return AccountException is null
                ? Task.FromResult(accountStatus)
                : Task.FromException<CodexAccountStatus>(AccountException);
        }

        public Task<CodexRateLimitsSnapshot> ReadRateLimitsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RateLimitReadCount++;
            return RateLimitException is null
                ? Task.FromResult(rateLimits)
                : Task.FromException<CodexRateLimitsSnapshot>(RateLimitException);
        }

        public Task<CodexTokenUsageSnapshot> ReadTokenUsageAsync(
            CancellationToken cancellationToken)
        {
            UsageReadCount++;
            BeforeUsageRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return UsageException is null
                ? Task.FromResult(TokenUsage)
                : Task.FromException<CodexTokenUsageSnapshot>(UsageException);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
