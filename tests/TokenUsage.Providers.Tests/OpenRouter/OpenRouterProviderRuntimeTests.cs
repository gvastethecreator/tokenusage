using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.OpenRouter;

namespace TokenUsage.Providers.Tests.OpenRouter;

/// <summary>
/// The runtime runs against the real client and a stub HTTP handler, so each case covers
/// the wire response, the error mapping, and the snapshot the dashboard reads.
/// </summary>
public sealed class OpenRouterProviderRuntimeTests
{
    private const string ApiSecret = "PRIVATE_OPENROUTER_KEY";

    // Thursday 2026-07-23 15:30 UTC.
    private static readonly DateTimeOffset FixedUtc = new(2026, 7, 23, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingKeyReturnsNotConfiguredWithoutARequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException());
        var runtime = CreateRuntime(handler, apiKey: null);

        ProviderOutcome outcome = await runtime.RefreshAsync(Context(forceRefresh: true), CancellationToken.None);

        Assert.IsType<ProviderOutcome.NotConfigured>(outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task KeyUsageMapsSpendScalarsAndAMonthlyLimitWindow()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, KeyJson(
            limit: "20",
            limitRemaining: "12.5",
            limitReset: "\"monthly\"")));
        var runtime = CreateRuntime(handler);

        ProviderOutcome outcome = await runtime.RefreshAsync(Context(), CancellationToken.None);

        ProviderSnapshot snapshot = Assert.IsType<ProviderOutcome.Success>(outcome).Snapshot;
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("https://openrouter.ai/api/v1/key", request.RequestUri?.AbsoluteUri);
        Assert.Equal(ApiSecret, request.Headers.Authorization?.Parameter);
        Assert.Equal("openrouter", snapshot.ProviderId.Value);
        Assert.Equal("paid", snapshot.PlanLabel);
        var scalars = snapshot.Metrics.OfType<ScalarMetricSnapshot>().ToDictionary(m => m.Id.Value);
        Assert.Equal(0.5m, scalars["spend.openrouter.day"].Value);
        Assert.Equal(2m, scalars["spend.openrouter.week"].Value);
        Assert.Equal(7.5m, scalars["spend.openrouter.month"].Value);
        Assert.Equal(30m, scalars["spend.openrouter.total"].Value);
        Assert.All(scalars.Values, metric => Assert.Equal("usd", metric.Unit));
        ProgressMetricSnapshot limit = Assert.Single(snapshot.Metrics.OfType<ProgressMetricSnapshot>());
        Assert.Equal("quota.openrouter.key.limit", limit.Id.Value);
        Assert.Equal(7.5m, limit.Used);
        Assert.Equal(20m, limit.Limit);
        Assert.Equal(ProgressResetCadence.Monthly, limit.ResetCadence);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), limit.ResetsAtUtc);
        Assert.Equal(
            ProviderCapabilityState.Available,
            Assert.Single(snapshot.Capabilities).State);
    }

    [Theory]
    [InlineData("\"daily\"", ProgressResetCadence.Daily, "2026-07-24T00:00:00Z")]
    [InlineData("\"weekly\"", ProgressResetCadence.Weekly, "2026-07-27T00:00:00Z")]
    [InlineData("null", ProgressResetCadence.Never, null)]
    public async Task LimitResetMapsToCadenceAndNextUtcBoundary(
        string limitReset,
        ProgressResetCadence cadence,
        string? resetsAt)
    {
        var runtime = CreateRuntime(new StubHandler(_ => Json(HttpStatusCode.OK, KeyJson(
            limit: "10",
            limitRemaining: "4",
            limitReset: limitReset))));

        ProviderOutcome outcome = await runtime.RefreshAsync(Context(), CancellationToken.None);

        ProgressMetricSnapshot limit = Assert.Single(
            Assert.IsType<ProviderOutcome.Success>(outcome).Snapshot.Metrics
                .OfType<ProgressMetricSnapshot>());
        Assert.Equal(6m, limit.Used);
        Assert.Equal(cadence, limit.ResetCadence);
        Assert.Equal(
            resetsAt is null
                ? null
                : DateTimeOffset.Parse(resetsAt, CultureInfo.InvariantCulture),
            limit.ResetsAtUtc);
    }

    [Fact]
    public async Task UnknownLimitResetKeepsTheLimitWithoutACadence()
    {
        var runtime = CreateRuntime(new StubHandler(_ => Json(HttpStatusCode.OK, KeyJson(
            limit: "10",
            limitRemaining: "4",
            limitReset: "\"quarterly\""))));

        ProviderOutcome outcome = await runtime.RefreshAsync(Context(), CancellationToken.None);

        ProgressMetricSnapshot limit = Assert.Single(
            Assert.IsType<ProviderOutcome.Success>(outcome).Snapshot.Metrics
                .OfType<ProgressMetricSnapshot>());
        Assert.Null(limit.ResetCadence);
        Assert.Null(limit.ResetsAtUtc);
    }

    [Fact]
    public async Task KeyWithoutLimitHasNoWindow()
    {
        var runtime = CreateRuntime(new StubHandler(_ => Json(HttpStatusCode.OK, KeyJson(
            limit: "null",
            limitRemaining: "null",
            limitReset: "null",
            isFreeTier: true))));

        ProviderOutcome outcome = await runtime.RefreshAsync(Context(), CancellationToken.None);

        ProviderSnapshot snapshot = Assert.IsType<ProviderOutcome.Success>(outcome).Snapshot;
        Assert.Empty(snapshot.Metrics.OfType<ProgressMetricSnapshot>());
        Assert.Equal("free", snapshot.PlanLabel);
        Assert.Equal(
            ProviderCapabilityState.NotConfigured,
            Assert.Single(snapshot.Capabilities).State);
    }

    [Fact]
    public async Task RejectedKeyReturnsNotConfiguredWithoutTheSecret()
    {
        var runtime = CreateRuntime(new StubHandler(_ => Json(
            HttpStatusCode.Unauthorized,
            $"{{\"error\":\"{ApiSecret}\"}}")));

        ProviderOutcome outcome = await runtime.RefreshAsync(Context(), CancellationToken.None);

        ProviderOutcome.NotConfigured notConfigured =
            Assert.IsType<ProviderOutcome.NotConfigured>(outcome);
        Assert.DoesNotContain(ApiSecret, notConfigured.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForbiddenKeyReturnsUnsupportedAccount()
    {
        var runtime = CreateRuntime(new StubHandler(_ => Json(HttpStatusCode.Forbidden, "{}")));

        ProviderOutcome outcome = await runtime.RefreshAsync(Context(), CancellationToken.None);

        Assert.IsType<ProviderOutcome.UnsupportedAccount>(outcome);
    }

    [Fact]
    public async Task ThrottlingUsesRetryAfterAndKeepsTheLastGoodReading()
    {
        var runtime = CreateRuntime(new StubHandler(_ =>
        {
            HttpResponseMessage response = Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        }));
        ProviderSnapshot lastGood = LastGood(TimeSpan.FromHours(2));

        ProviderOutcome outcome = await runtime.RefreshAsync(
            Context(lastGood, forceRefresh: true),
            CancellationToken.None);

        ProviderOutcome.Throttled throttled = Assert.IsType<ProviderOutcome.Throttled>(outcome);
        Assert.Equal(FixedUtc.AddSeconds(90), throttled.RetryAtUtc);
        Assert.Same(lastGood, throttled.LastGood);
    }

    [Fact]
    public async Task FreshReadingIsReusedWithoutARequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException());
        var runtime = CreateRuntime(handler);
        ProviderSnapshot lastGood = LastGood(TimeSpan.FromMinutes(2));

        ProviderOutcome outcome = await runtime.RefreshAsync(Context(lastGood), CancellationToken.None);

        Assert.Same(lastGood, Assert.IsType<ProviderOutcome.Success>(outcome).Snapshot);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CardAndSummaryShowMonthSpendAndTheLimitWindow()
    {
        var runtime = CreateRuntime(new StubHandler(_ => Json(HttpStatusCode.OK, KeyJson(
            limit: "20",
            limitRemaining: "12.5",
            limitReset: "\"monthly\""))));
        ProviderSnapshot snapshot = Assert.IsType<ProviderOutcome.Success>(
            await runtime.RefreshAsync(Context(), CancellationToken.None)).Snapshot;

        ProviderCard card = OpenRouterCardProjector.Create(snapshot, Strings);
        DashboardProviderSummary summary = OpenRouterCardProjector.CreateSummary(snapshot, Strings);

        Assert.Equal("Paid credits", card.PlanLabel);
        QuotaWindow window = Assert.Single(card.Windows);
        Assert.Equal("Key limit", window.Title);
        Assert.Equal(62.5d, window.RemainingPercent);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "${0:N2} left of ${1:N2}", 12.5m, 20m),
            window.RemainingText);
        Assert.Equal("Resets monthly (UTC)", window.ResetText);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), window.ResetAtUtc);
        Assert.Equal(7.5m, summary.CostUsd);
        Assert.Equal("—", summary.TokensText);
        Assert.True(summary.HasData);
    }

    private static OpenRouterProviderRuntime CreateRuntime(
        StubHandler handler,
        string? apiKey = ApiSecret) =>
        new(new FakeKeySource(apiKey), new OpenRouterClient(new HttpClient(handler)));

    private static RefreshContext Context(
        ProviderSnapshot? lastGood = null,
        bool forceRefresh = false) =>
        new(new FixedTimeProvider(FixedUtc), lastGood, forceRefresh);

    private static ProviderSnapshot LastGood(TimeSpan age) => new(
        new ProviderId("openrouter"),
        "OpenRouter",
        planLabel: null,
        FixedUtc - age,
        FixedUtc - age,
        "UTC",
        [],
        CoverageKind.Complete,
        1);

    private static string KeyJson(
        string limit,
        string limitRemaining,
        string limitReset,
        bool isFreeTier = false) => $$"""
        {
          "data": {
            "label": "sk-or-v1-abc...xyz",
            "limit": {{limit}},
            "limit_reset": {{limitReset}},
            "limit_remaining": {{limitRemaining}},
            "include_byok_in_limit": false,
            "usage": 30,
            "usage_daily": 0.5,
            "usage_weekly": 2,
            "usage_monthly": 7.5,
            "byok_usage": 0,
            "byok_usage_daily": 0,
            "byok_usage_weekly": 0,
            "byok_usage_monthly": 0,
            "is_free_tier": {{(isFreeTier ? "true" : "false")}}
          }
        }
        """;

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private static string Strings(string key) => key switch
    {
        "CodexUsageMissing" => "Missing",
        "OpenRouterPlanFree" => "Free tier",
        "OpenRouterPlanPaid" => "Paid credits",
        "OpenRouterCapability" => "Spend for this key",
        "OpenRouterPeriodNotice" => "UTC calendar periods",
        "OpenRouterSourceValue" => "OpenRouter API",
        "OpenRouterMetricToday" => "Today",
        "OpenRouterMetricWeek" => "This week",
        "OpenRouterMetricMonth" => "This month",
        "OpenRouterMetricTotal" => "All time",
        "OpenRouterMetricKeyLimit" => "Key limit",
        "OpenRouterLimitTitle" => "Key limit",
        "OpenRouterLimitNone" => "No limit set",
        "ApiLimitRemainingFormat" => "${0:N2} left of ${1:N2}",
        "ApiLimitUsedFormat" => "${0:N2} of ${1:N2} used",
        "ApiLimitAutomationFormat" => "{0}: {1}. {2}",
        "ApiLimitResetMonthly" => "Resets monthly (UTC)",
        "ProviderSourceLabel" => "Source",
        "ProviderObservedLabel" => "Observed",
        "ProviderObservedValueFormat" => "{0}",
        "ProviderDetailsTooltipFormat" => "{0}; {1}",
        "ProviderDetailsAutomationNameFormat" => "Details for {0}",
        "LocalUsageUsdFormat" => "${0:N2}",
        "LocalUsageUsdTinyFormat" => "{0}",
        _ => throw new KeyNotFoundException(key),
    };

    private sealed class FakeKeySource(string? apiKey) : IOpenRouterKeySource
    {
        public Task<string?> ReadApiKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(apiKey);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            HttpResponseMessage response = send(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
