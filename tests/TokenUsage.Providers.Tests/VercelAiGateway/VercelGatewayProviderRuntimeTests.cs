using TokenUsage.Core.Providers;
using TokenUsage.Providers.VercelAiGateway;

namespace TokenUsage.Providers.Tests.VercelAiGateway;

public sealed class VercelGatewayProviderRuntimeTests
{
    private const string SecretApiKey = "fake-vercel-api-key-for-tests";
    private static readonly DateTimeOffset FixedUtc = new DateTimeOffset(2026, 7, 23, 15, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly ExpectedEnd = new DateOnly(2026, 7, 23);
    private static readonly DateOnly ExpectedStart = new DateOnly(2026, 6, 24); // today - 29 inclusive 30 days

    [Fact]
    public void DescriptorIsExperimentalVercelAiGateway()
    {
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), new FakeReportClient());

        Assert.Equal("vercel-ai-gateway", runtime.Descriptor.Id.Value);
        Assert.Equal("Vercel AI Gateway", runtime.Descriptor.DisplayName);
        Assert.True(runtime.Descriptor.IsExperimental);
    }

    [Fact]
    public void ConnectionPreservesOptionalValidatedKeyId()
    {
        var legacy = new VercelGatewayConnection(SecretApiKey);
        var current = new VercelGatewayConnection(SecretApiKey, "key_abc-123");

        Assert.Null(legacy.KeyId);
        Assert.Equal("key_abc-123", current.KeyId);
        Assert.Throws<ArgumentException>(() =>
            new VercelGatewayConnection(SecretApiKey, "bad/key"));
    }

    [Fact]
    public async Task DetectAsyncWhenConnectionPresentReturnsAvailable()
    {
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), new FakeReportClient());

        var detection = await runtime.DetectAsync(CancellationToken.None);

        Assert.IsType<ProviderDetection.Available>(detection);
    }

    [Fact]
    public async Task DetectAsyncWhenConnectionMissingReturnsUnavailableWithSafeCopy()
    {
        var runtime = CreateRuntime(new FakeConnectionSource(null), new FakeReportClient());

        var detection = await runtime.DetectAsync(CancellationToken.None);

        var unavailable = Assert.IsType<ProviderDetection.Unavailable>(detection);
        Assert.Equal("Vercel AI Gateway is not configured.", unavailable.Reason);
        AssertNoSecret(unavailable.Reason);
    }

    [Fact]
    public async Task DetectAsyncDoesNotCallReportClient()
    {
        var client = new FakeReportClient();
        var source = new FakeConnectionSource(CreateConnection());
        var runtime = CreateRuntime(source, client);

        await runtime.DetectAsync(CancellationToken.None);

        Assert.Equal(0, client.CallCount);
        Assert.Equal(0, source.ReadCount);
        Assert.Equal(1, source.ConfigurationReadCount);
    }

    [Fact]
    public async Task RefreshAsyncWhenConnectionMissingReturnsNotConfigured()
    {
        var runtime = CreateRuntime(new FakeConnectionSource(null), new FakeReportClient());

        var outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        var notConfigured = Assert.IsType<ProviderOutcome.NotConfigured>(outcome);
        Assert.Equal("Vercel AI Gateway is not configured.", notConfigured.Reason);
        AssertNoSecret(notConfigured.Reason);
    }

    [Fact]
    public async Task RefreshAsyncUsesFreshCacheWithoutPaidReportCall()
    {
        ProviderSnapshot lastGood = CreateLastGood(TimeSpan.FromMinutes(5));
        var client = new FakeReportClient();
        var source = new FakeConnectionSource(CreateConnection());
        var runtime = CreateRuntime(source, client);

        ProviderOutcome outcome = await runtime.RefreshAsync(
            CreateContext(lastGood),
            CancellationToken.None);

        ProviderOutcome.Success success = Assert.IsType<ProviderOutcome.Success>(outcome);
        Assert.Same(lastGood, success.Snapshot);
        Assert.Equal(1, source.ReadCount);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task ForcedRefreshIgnoresFreshCacheAndCallsReport()
    {
        ProviderSnapshot lastGood = CreateLastGood(TimeSpan.FromMinutes(5));
        var client = new FakeReportClient { Report = CreateFullReport() };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        ProviderOutcome outcome = await runtime.RefreshAsync(
            CreateContext(lastGood, forceRefresh: true),
            CancellationToken.None);

        ProviderOutcome.Success success = Assert.IsType<ProviderOutcome.Success>(outcome);
        Assert.NotSame(lastGood, success.Snapshot);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task MissingConnectionDoesNotPublishFreshCache()
    {
        ProviderSnapshot lastGood = CreateLastGood(TimeSpan.FromMinutes(5));
        var client = new FakeReportClient();
        var runtime = CreateRuntime(new FakeConnectionSource(null), client);

        ProviderOutcome outcome = await runtime.RefreshAsync(
            CreateContext(lastGood),
            CancellationToken.None);

        Assert.IsType<ProviderOutcome.NotConfigured>(outcome);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task RefreshAsyncQueriesExactlyThirtyInclusiveUtcDays()
    {
        var client = new FakeReportClient
        {
            Report = CreateFullReport()
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(1, client.CallCount);
        Assert.Equal(ExpectedStart, client.LastStartDate);
        Assert.Equal(ExpectedEnd, client.LastEndDate);
        Assert.Equal(30, client.LastEndDate.DayNumber - client.LastStartDate.DayNumber + 1);
    }

    [Fact]
    public async Task RefreshAsyncFullReportAggregatesMetricsWithIdsProvenanceAndCompleteCoverage()
    {
        var client = new FakeReportClient
        {
            Report = new VercelGatewayReport(new[]
            {
                new VercelGatewayDailyReportRow(
                    new DateOnly(2026, 7, 22),
                    TotalCost: 1.5m,
                    MarketCost: 1.0m,
                    SurchargeCost: 0.25m,
                    GatewayCost: 0.25m,
                    InputTokens: 100,
                    OutputTokens: 50,
                    CachedInputTokens: 10,
                    CacheCreationInputTokens: 5,
                    ReasoningTokens: 2,
                    RequestCount: 3),
                new VercelGatewayDailyReportRow(
                    new DateOnly(2026, 7, 23),
                    TotalCost: 2.5m,
                    MarketCost: 2.0m,
                    SurchargeCost: 0.25m,
                    GatewayCost: 0.25m,
                    InputTokens: 200,
                    OutputTokens: 150,
                    CachedInputTokens: 20,
                    CacheCreationInputTokens: 15,
                    ReasoningTokens: 8,
                    RequestCount: 7)
            })
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        var success = Assert.IsType<ProviderOutcome.Success>(outcome);
        var snapshot = success.Snapshot;

        Assert.Equal("vercel-ai-gateway", snapshot.ProviderId.Value);
        Assert.Equal("Vercel AI Gateway", snapshot.DisplayName);
        Assert.Null(snapshot.PlanLabel);
        Assert.Equal(FixedUtc, snapshot.FetchedAtUtc);
        Assert.Equal(FixedUtc, snapshot.SourceObservedAtUtc);
        Assert.Equal("UTC", snapshot.TimeZoneId);
        Assert.Equal(CoverageKind.Complete, snapshot.Coverage);
        Assert.Equal(2, snapshot.AdapterContractVersion);

        var metrics = snapshot.Metrics.OfType<ScalarMetricSnapshot>().ToDictionary(m => m.Id.Value);
        Assert.Equal(10, metrics.Count);

        AssertMetric(metrics, "spend.gateway.total.30d", 4.0m, "usd");
        AssertMetric(metrics, "spend.gateway.market.30d", 3.0m, "usd");
        AssertMetric(metrics, "spend.gateway.surcharge.30d", 0.5m, "usd");
        AssertMetric(metrics, "spend.gateway.fee.30d", 0.5m, "usd");
        AssertMetric(metrics, "usage.tokens.input.30d", 300m, "tokens");
        AssertMetric(metrics, "usage.tokens.output.30d", 200m, "tokens");
        AssertMetric(metrics, "usage.tokens.cached-input.30d", 30m, "tokens");
        AssertMetric(metrics, "usage.tokens.cache-creation-input.30d", 20m, "tokens");
        AssertMetric(metrics, "usage.tokens.reasoning.30d", 10m, "tokens");
        AssertMetric(metrics, "usage.requests.30d", 10m, "requests");

        foreach (var metric in metrics.Values)
        {
            Assert.Equal(SourceKind.ManualKey, metric.Provenance.SourceKind);
            Assert.Equal(MeasurementKind.ProviderReported, metric.Provenance.MeasurementKind);
            Assert.Equal("vercel-ai-gateway-report/1", metric.Provenance.AdapterVersion);
        }
    }

    [Fact]
    public async Task RefreshAsyncEmptyReportReturnsCompleteSnapshotWithNoMetrics()
    {
        var client = new FakeReportClient
        {
            Report = new VercelGatewayReport(Array.Empty<VercelGatewayDailyReportRow>())
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        var success = Assert.IsType<ProviderOutcome.Success>(outcome);
        Assert.Equal(CoverageKind.Complete, success.Snapshot.Coverage);
        Assert.Empty(success.Snapshot.Metrics);
    }

    [Fact]
    public async Task RefreshWithKeyIdAddsTypedBudgetMetric()
    {
        var quotaClient = new FakeQuotaClient
        {
            Result = new VercelGatewayQuotaLookupResult.Found(
                new VercelGatewayQuota(
                    "api_key_id_key_abc-123",
                    "desktop-key",
                    10m,
                    1.5m,
                    8.5m,
                    VercelGatewayQuotaRefreshPeriod.Monthly,
                    Active: true)),
        };
        var runtime = CreateRuntime(
            new FakeConnectionSource(CreateConnection("key_abc-123")),
            new FakeReportClient { Report = CreateFullReport() },
            quotaClient);

        ProviderOutcome outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        ProviderSnapshot snapshot = Assert.IsType<ProviderOutcome.Success>(outcome).Snapshot;
        ProgressMetricSnapshot metric = Assert.IsType<ProgressMetricSnapshot>(
            snapshot.Metrics.Single(item => item.Id.Value == "quota.gateway.key.budget"));
        Assert.Equal(1.5m, metric.Used);
        Assert.Equal(10m, metric.Limit);
        Assert.Equal("usd", metric.Unit);
        Assert.Equal(ProgressResetCadence.Monthly, metric.ResetCadence);
        Assert.True(metric.IsActive);
        Assert.Null(metric.ResetsAtUtc);
        Assert.Equal("vercel-ai-gateway-quota/1", metric.Provenance.AdapterVersion);
        ProviderCapabilitySnapshot capability = Assert.Single(snapshot.Capabilities);
        Assert.Equal("quota.gateway.key.budget", capability.Id.Value);
        Assert.Equal(ProviderCapabilityState.Available, capability.State);
        Assert.Equal(SecretApiKey, quotaClient.LastApiKey);
        Assert.Equal("key_abc-123", quotaClient.LastKeyId);
    }

    [Fact]
    public async Task LegacyConnectionSkipsQuotaCall()
    {
        var quotaClient = new FakeQuotaClient();
        var runtime = CreateRuntime(
            new FakeConnectionSource(CreateConnection()),
            new FakeReportClient { Report = CreateFullReport() },
            quotaClient);

        ProviderOutcome outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(0, quotaClient.CallCount);
        ProviderSnapshot snapshot = Assert.IsType<ProviderOutcome.Success>(outcome).Snapshot;
        Assert.Equal(
            ProviderCapabilityState.NotRequested,
            Assert.Single(snapshot.Capabilities).State);
    }

    [Fact]
    public async Task NoBudgetKeepsReportSuccessfulWithoutQuotaMetric()
    {
        var quotaClient = new FakeQuotaClient
        {
            Result = VercelGatewayQuotaLookupResult.NoBudget.Instance,
        };
        var runtime = CreateRuntime(
            new FakeConnectionSource(CreateConnection("key_abc-123")),
            new FakeReportClient { Report = CreateFullReport() },
            quotaClient);

        ProviderOutcome outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        ProviderSnapshot snapshot = Assert.IsType<ProviderOutcome.Success>(outcome).Snapshot;
        Assert.DoesNotContain(snapshot.Metrics, metric =>
            metric.Id.Value == "quota.gateway.key.budget");
        Assert.Equal(
            ProviderCapabilityState.NotConfigured,
            Assert.Single(snapshot.Capabilities).State);
        Assert.Equal(1, quotaClient.CallCount);
    }

    [Fact]
    public async Task QuotaFailureDegradesOnlyQuotaAndKeepsReportSnapshot()
    {
        var quotaClient = new FakeQuotaClient
        {
            Exception = new VercelGatewayQuotaException(
                VercelGatewayQuotaErrorKind.Transient,
                "PRIVATE_QUOTA_BODY"),
        };
        var runtime = CreateRuntime(
            new FakeConnectionSource(CreateConnection("key_abc-123")),
            new FakeReportClient { Report = CreateFullReport() },
            quotaClient);

        ProviderOutcome outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        ProviderOutcome.PartialSuccess partial =
            Assert.IsType<ProviderOutcome.PartialSuccess>(outcome);
        Assert.Equal(CoverageKind.Complete, partial.Snapshot.Coverage);
        Assert.Contains(partial.Snapshot.Metrics, metric =>
            metric.Id.Value == "spend.gateway.total.30d");
        ProviderWarning warning = Assert.Single(partial.Warnings);
        Assert.Equal(ProviderWarningCode.SourceDegraded, warning.Code);
        Assert.DoesNotContain("PRIVATE_QUOTA_BODY", warning.Message, StringComparison.Ordinal);
        AssertNoSecret(warning.Message);
        ProviderCapabilitySnapshot capability = Assert.Single(partial.Snapshot.Capabilities);
        Assert.Equal(ProviderCapabilityState.Degraded, capability.State);
        Assert.Equal(MeasurementKind.Derived, capability.Provenance.MeasurementKind);
        Assert.Equal("vercel-ai-gateway-quota-state/1", capability.Provenance.AdapterVersion);
    }

    [Fact]
    public async Task RefreshAsyncPartialMissingFieldOmitsMetricAddsWarningAndPartialCoverage()
    {
        var client = new FakeReportClient
        {
            Report = new VercelGatewayReport(new[]
            {
                new VercelGatewayDailyReportRow(
                    new DateOnly(2026, 7, 22),
                    TotalCost: 1.0m,
                    MarketCost: 1.0m,
                    SurchargeCost: 0.1m,
                    GatewayCost: 0.1m,
                    InputTokens: 10,
                    OutputTokens: 5,
                    CachedInputTokens: null,
                    CacheCreationInputTokens: 1,
                    ReasoningTokens: 1,
                    RequestCount: 1),
                new VercelGatewayDailyReportRow(
                    new DateOnly(2026, 7, 23),
                    TotalCost: 2.0m,
                    MarketCost: 2.0m,
                    SurchargeCost: 0.2m,
                    GatewayCost: 0.2m,
                    InputTokens: 20,
                    OutputTokens: 15,
                    CachedInputTokens: 4,
                    CacheCreationInputTokens: 2,
                    ReasoningTokens: 2,
                    RequestCount: 2)
            })
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        var partial = Assert.IsType<ProviderOutcome.PartialSuccess>(outcome);
        Assert.Equal(CoverageKind.Partial, partial.Snapshot.Coverage);

        var metrics = partial.Snapshot.Metrics.OfType<ScalarMetricSnapshot>().ToDictionary(m => m.Id.Value);
        Assert.False(metrics.ContainsKey("usage.tokens.cached-input.30d"));
        Assert.True(metrics.ContainsKey("spend.gateway.total.30d"));
        Assert.Equal(3.0m, metrics["spend.gateway.total.30d"].Value);

        var warning = Assert.Single(partial.Warnings);
        Assert.Equal(ProviderWarningCode.MissingMetric, warning.Code);
        Assert.Contains("usage.tokens.cached-input.30d", warning.Message, StringComparison.Ordinal);
        AssertNoSecret(warning.Message);
    }

    [Fact]
    public async Task RefreshAsyncAuthenticationReturnsNotConfiguredWithoutLastGood()
    {
        var lastGood = CreateLastGood();
        var client = new FakeReportClient
        {
            Exception = CreateReportException(VercelGatewayReportErrorKind.Authentication)
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(lastGood), CancellationToken.None);

        var notConfigured = Assert.IsType<ProviderOutcome.NotConfigured>(outcome);
        Assert.Equal("Vercel AI Gateway credentials were rejected.", notConfigured.Reason);
        AssertNoSecret(notConfigured.Reason);
    }

    [Fact]
    public async Task RefreshAsyncUnsupportedAccountReturnsUnsupportedAccount()
    {
        var client = new FakeReportClient
        {
            Exception = CreateReportException(VercelGatewayReportErrorKind.UnsupportedAccount)
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        var unsupported = Assert.IsType<ProviderOutcome.UnsupportedAccount>(outcome);
        Assert.Equal("Vercel AI Gateway does not support this account.", unsupported.Reason);
        AssertNoSecret(unsupported.Reason);
    }

    [Fact]
    public async Task RefreshAsyncThrottledWithRetryAfterUsesProvidedDelayAndLastGood()
    {
        var lastGood = CreateLastGood();
        var retryAfter = TimeSpan.FromMinutes(12);
        var client = new FakeReportClient
        {
            Exception = CreateReportException(VercelGatewayReportErrorKind.Throttled, retryAfter)
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(lastGood), CancellationToken.None);

        var throttled = Assert.IsType<ProviderOutcome.Throttled>(outcome);
        Assert.Equal(FixedUtc + retryAfter, throttled.RetryAtUtc);
        Assert.Same(lastGood, throttled.LastGood);
    }

    [Fact]
    public async Task RefreshAsyncThrottledWithoutRetryAfterFallsBackToFiveMinutes()
    {
        var lastGood = CreateLastGood();
        var client = new FakeReportClient
        {
            Exception = CreateReportException(VercelGatewayReportErrorKind.Throttled, retryAfter: null)
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(lastGood), CancellationToken.None);

        var throttled = Assert.IsType<ProviderOutcome.Throttled>(outcome);
        Assert.Equal(FixedUtc + TimeSpan.FromMinutes(5), throttled.RetryAtUtc);
        Assert.Same(lastGood, throttled.LastGood);
    }

    [Fact]
    public async Task RefreshAsyncThrottledClampsUnrepresentableRetryDate()
    {
        var client = new FakeReportClient
        {
            Exception = CreateReportException(
                VercelGatewayReportErrorKind.Throttled,
                TimeSpan.MaxValue)
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);

        var throttled = Assert.IsType<ProviderOutcome.Throttled>(outcome);
        Assert.Equal(DateTimeOffset.MaxValue, throttled.RetryAtUtc);
    }

    [Fact]
    public async Task RefreshAsyncTransientReturnsTransientFailureWithLastGood()
    {
        var lastGood = CreateLastGood();
        var client = new FakeReportClient
        {
            Exception = CreateReportException(VercelGatewayReportErrorKind.Transient)
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(lastGood), CancellationToken.None);

        var failure = Assert.IsType<ProviderOutcome.TransientFailure>(outcome);
        Assert.Equal(ProviderErrorCode.TransientSourceFailure, failure.Error.Code);
        Assert.Equal("Vercel AI Gateway temporarily failed.", failure.Error.Message);
        Assert.Same(lastGood, failure.LastGood);
        AssertNoSecret(failure.Error.Message);
    }

    [Fact]
    public async Task RefreshAsyncContractReturnsContractFailureWithLastGood()
    {
        var lastGood = CreateLastGood();
        var client = new FakeReportClient
        {
            Exception = CreateReportException(VercelGatewayReportErrorKind.Contract)
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(lastGood), CancellationToken.None);

        var failure = Assert.IsType<ProviderOutcome.ContractFailure>(outcome);
        Assert.Equal(ProviderErrorCode.ContractViolation, failure.Error.Code);
        Assert.Equal("Vercel AI Gateway returned an unexpected response.", failure.Error.Message);
        Assert.Same(lastGood, failure.LastGood);
        AssertNoSecret(failure.Error.Message);
    }

    [Fact]
    public async Task RefreshAsyncAggregationOverflowReturnsContractFailureWithLastGood()
    {
        var lastGood = CreateLastGood();
        var client = new FakeReportClient
        {
            Report = new VercelGatewayReport(new[]
            {
                FullRow(new DateOnly(2026, 7, 22), inputTokens: long.MaxValue),
                FullRow(new DateOnly(2026, 7, 23), inputTokens: 1)
            })
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(lastGood), CancellationToken.None);

        var failure = Assert.IsType<ProviderOutcome.ContractFailure>(outcome);
        Assert.Equal(ProviderErrorCode.ContractViolation, failure.Error.Code);
        Assert.Equal("Vercel AI Gateway report aggregation overflowed.", failure.Error.Message);
        Assert.Same(lastGood, failure.LastGood);
        AssertNoSecret(failure.Error.Message);
    }

    [Fact]
    public async Task DetectAsyncPropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runtime = CreateRuntime(
            new FakeConnectionSource(CreateConnection(), respectCancellation: true),
            new FakeReportClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.DetectAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task RefreshAsyncPropagatesCancellationFromConnectionSource()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runtime = CreateRuntime(
            new FakeConnectionSource(CreateConnection(), respectCancellation: true),
            new FakeReportClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.RefreshAsync(CreateContext(), cts.Token));
    }

    [Fact]
    public async Task RefreshAsyncPropagatesCancellationFromReportClient()
    {
        var client = new FakeReportClient { ThrowCanceled = true };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.RefreshAsync(CreateContext(), CancellationToken.None));
    }

    [Fact]
    public async Task DetectAsyncPropagatesCancellationIgnoredByConnectionSource()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new FakeConnectionSource(CreateConnection())
        {
            BeforeConfigurationReturn = cancellation.Cancel
        };
        var runtime = CreateRuntime(source, new FakeReportClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.DetectAsync(cancellation.Token).AsTask());
    }

    [Fact]
    public async Task RefreshAsyncPropagatesCancellationIgnoredByMissingConnectionSource()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new FakeConnectionSource(null)
        {
            BeforeReturn = cancellation.Cancel
        };
        var runtime = CreateRuntime(source, new FakeReportClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.RefreshAsync(CreateContext(), cancellation.Token));
    }

    [Fact]
    public async Task RefreshAsyncPropagatesCancellationIgnoredByReportClient()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new FakeReportClient
        {
            BeforeReturn = cancellation.Cancel
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.RefreshAsync(CreateContext(), cancellation.Token));
    }

    [Fact]
    public void ConnectionRejectsBlankApiKeyWithoutEchoingValue()
    {
        var blank = "   ";
        var ex = Assert.Throws<ArgumentException>(() => new VercelGatewayConnection(blank));
        Assert.Equal("apiKey", ex.ParamName);
        AssertNoSecret(ex.Message);
    }

    [Fact]
    public async Task RefreshAsyncOutcomeMessagesNeverContainApiKey()
    {
        var client = new FakeReportClient
        {
            Exception = CreateReportException(VercelGatewayReportErrorKind.Authentication)
        };
        var runtime = CreateRuntime(new FakeConnectionSource(CreateConnection()), client);

        var outcome = await runtime.RefreshAsync(CreateContext(), CancellationToken.None);
        var notConfigured = Assert.IsType<ProviderOutcome.NotConfigured>(outcome);
        AssertNoSecret(notConfigured.Reason);
    }

    private static VercelGatewayProviderRuntime CreateRuntime(
        IVercelGatewayConnectionSource connectionSource,
        IVercelGatewayReportClient reportClient,
        IVercelGatewayQuotaClient? quotaClient = null)
    {
        return new VercelGatewayProviderRuntime(
            connectionSource,
            reportClient,
            quotaClient ?? new FakeQuotaClient());
    }

    private static RefreshContext CreateContext(
        ProviderSnapshot? lastGood = null,
        bool forceRefresh = false)
    {
        return new RefreshContext(
            new FixedTimeProvider(FixedUtc),
            lastGood,
            forceRefresh);
    }

    private static VercelGatewayConnection CreateConnection(string? keyId = null)
    {
        return new VercelGatewayConnection(SecretApiKey, keyId);
    }

    private static ProviderSnapshot CreateLastGood(TimeSpan? age = null)
    {
        TimeSpan effectiveAge = age ?? TimeSpan.FromHours(1);
        return new ProviderSnapshot(
            new ProviderId("vercel-ai-gateway"),
            "Vercel AI Gateway",
            planLabel: null,
            fetchedAtUtc: FixedUtc - effectiveAge,
            sourceObservedAtUtc: FixedUtc - effectiveAge,
            timeZoneId: "UTC",
            metrics: Array.Empty<MetricSnapshot>(),
            coverage: CoverageKind.Complete,
            adapterContractVersion: 1);
    }

    private static VercelGatewayReport CreateFullReport()
    {
        return new VercelGatewayReport(new[]
        {
            FullRow(new DateOnly(2026, 7, 23))
        });
    }

    private static VercelGatewayDailyReportRow FullRow(
        DateOnly day,
        decimal totalCost = 1m,
        long inputTokens = 1)
    {
        return new VercelGatewayDailyReportRow(
            day,
            TotalCost: totalCost,
            MarketCost: 1m,
            SurchargeCost: 0m,
            GatewayCost: 0m,
            InputTokens: inputTokens,
            OutputTokens: 1,
            CachedInputTokens: 0,
            CacheCreationInputTokens: 0,
            ReasoningTokens: 0,
            RequestCount: 1);
    }

    private static VercelGatewayReportException CreateReportException(
        VercelGatewayReportErrorKind kind,
        TimeSpan? retryAfter = null)
    {
        return new VercelGatewayReportException(
            kind,
            "PRIVATE_UPSTREAM_BODY",
            retryAfter);
    }

    private static void AssertMetric(
        Dictionary<string, ScalarMetricSnapshot> metrics,
        string id,
        decimal value,
        string unit)
    {
        Assert.True(metrics.ContainsKey(id), $"Missing metric '{id}'.");
        Assert.Equal(value, metrics[id].Value);
        Assert.Equal(unit, metrics[id].Unit);
    }

    private static void AssertNoSecret(string? text)
    {
        Assert.NotNull(text);
        Assert.DoesNotContain(SecretApiKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test", text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class FakeConnectionSource : IVercelGatewayConnectionSource
    {
        private readonly VercelGatewayConnection? _connection;
        private readonly bool _respectCancellation;

        public FakeConnectionSource(VercelGatewayConnection? connection, bool respectCancellation = false)
        {
            _connection = connection;
            _respectCancellation = respectCancellation;
        }

        public Action? BeforeConfigurationReturn { get; init; }

        public Action? BeforeReturn { get; init; }

        public int ConfigurationReadCount { get; private set; }

        public int ReadCount { get; private set; }

        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
        {
            ConfigurationReadCount++;
            if (_respectCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            BeforeConfigurationReturn?.Invoke();
            return Task.FromResult(_connection is not null);
        }

        public Task<VercelGatewayConnection?> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (_respectCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            BeforeReturn?.Invoke();
            return Task.FromResult(_connection);
        }
    }

    private sealed class FakeReportClient : IVercelGatewayReportClient
    {
        public VercelGatewayReport Report { get; set; } =
            new VercelGatewayReport(Array.Empty<VercelGatewayDailyReportRow>());

        public VercelGatewayReportException? Exception { get; set; }
        public bool ThrowCanceled { get; set; }
        public Action? BeforeReturn { get; init; }
        public int CallCount { get; private set; }
        public DateOnly LastStartDate { get; private set; }
        public DateOnly LastEndDate { get; private set; }
        public string? LastApiKey { get; private set; }

        public Task<VercelGatewayReport> GetDailyReportAsync(
            string apiKey,
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastApiKey = apiKey;
            LastStartDate = startDate;
            LastEndDate = endDate;

            if (ThrowCanceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (Exception is not null)
            {
                throw Exception;
            }

            BeforeReturn?.Invoke();
            return Task.FromResult(Report);
        }
    }

    private sealed class FakeQuotaClient : IVercelGatewayQuotaClient
    {
        public VercelGatewayQuotaLookupResult Result { get; set; } =
            VercelGatewayQuotaLookupResult.NoBudget.Instance;

        public VercelGatewayQuotaException? Exception { get; set; }

        public int CallCount { get; private set; }

        public string? LastApiKey { get; private set; }

        public string? LastKeyId { get; private set; }

        public Task<VercelGatewayQuotaLookupResult> GetQuotaAsync(
            string apiKey,
            string keyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastApiKey = apiKey;
            LastKeyId = keyId;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result);
        }
    }
}
