using System.Net;
using System.Net.Http.Headers;
using WOpenUsage.Providers.VercelAiGateway;

namespace WOpenUsage.Providers.Tests.VercelAiGateway;

public sealed class VercelGatewayReportClientTests
{
    private const string Secret = "PRIVATE_VERCEL_GATEWAY_KEY";

    [Fact]
    public async Task ValidDailyReportUsesFixedRequestAndPreservesMetrics()
    {
        var handler = new StubHandler((request, _) => Json(HttpStatusCode.OK, DailyJson, request));
        using var httpClient = new HttpClient(handler);
        var client = new VercelGatewayReportClient(httpClient);

        VercelGatewayReport report = await client.GetDailyReportAsync(
            Secret,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://ai-gateway.vercel.sh/v1/report?start_date=2026-07-01&end_date=2026-07-31&group_by=day&date_part=day",
            request.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(Secret, request.Headers.Authorization?.Parameter);

        VercelGatewayDailyReportRow row = Assert.Single(report.Results);
        Assert.Equal(new DateOnly(2026, 7, 1), row.Day);
        Assert.Equal(1.25m, row.TotalCost);
        Assert.Equal(1.50m, row.MarketCost);
        Assert.Equal(0.10m, row.SurchargeCost);
        Assert.Equal(0.05m, row.GatewayCost);
        Assert.Equal(120, row.InputTokens);
        Assert.Equal(80, row.OutputTokens);
        Assert.Equal(20, row.CachedInputTokens);
        Assert.Equal(5, row.CacheCreationInputTokens);
        Assert.Equal(10, row.ReasoningTokens);
        Assert.Equal(2, row.RequestCount);
    }

    [Fact]
    public async Task EmptyResultsAreValid()
    {
        var client = CreateClient(Json(HttpStatusCode.OK, "{\"results\":[]}"));

        VercelGatewayReport report = await client.GetDailyReportAsync(
            Secret,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 1));

        Assert.Empty(report.Results);
    }

    [Fact]
    public async Task ThirtyTwoDaysAreRejectedBeforeNetwork()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new VercelGatewayReportClient(new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.GetDailyReportAsync(
                Secret,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 8, 1)));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReversedRangeAndBlankKeyAreRejectedBeforeNetwork()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new VercelGatewayReportClient(new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.GetDailyReportAsync(
                Secret,
                new DateOnly(2026, 7, 2),
                new DateOnly(2026, 7, 1)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetDailyReportAsync(
                " ",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1)));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"results\":null}")]
    [InlineData("{\"results\":[{\"day\":\"invalid\",\"total_cost\":1}]}")]
    [InlineData("{\"results\":[{\"day\":\"2026-07-01\"}]}")]
    [InlineData("{\"results\":[{\"day\":\"2026-07-01\",\"total_cost\":-1}]}")]
    [InlineData("{\"results\":[{\"day\":\"2026-07-02\",\"total_cost\":1}]}")]
    [InlineData("{\"results\":[{\"day\":\"2026-07-01\",\"total_cost\":1},{\"day\":\"2026-07-01\",\"total_cost\":2}]}")]
    [InlineData("not-json")]
    public async Task InvalidContractIsSanitized(string body)
    {
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        VercelGatewayReportException exception = await Assert.ThrowsAsync<VercelGatewayReportException>(() =>
            client.GetDailyReportAsync(
                Secret,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1)));

        Assert.Equal(VercelGatewayReportErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, VercelGatewayReportErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, VercelGatewayReportErrorKind.UnsupportedAccount)]
    [InlineData(HttpStatusCode.InternalServerError, VercelGatewayReportErrorKind.Transient)]
    public async Task HttpErrorsAreTypedAndSanitized(
        HttpStatusCode status,
        VercelGatewayReportErrorKind expectedKind)
    {
        var client = CreateClient(Json(status, $"{{\"private\":\"{Secret}\"}}"));

        VercelGatewayReportException exception = await Assert.ThrowsAsync<VercelGatewayReportException>(() =>
            client.GetDailyReportAsync(
                Secret,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1)));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrottlingPreservesRetryAfter()
    {
        HttpResponseMessage response = Json(HttpStatusCode.TooManyRequests, "{}");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
        var client = CreateClient(response);

        VercelGatewayReportException exception = await Assert.ThrowsAsync<VercelGatewayReportException>(() =>
            client.GetDailyReportAsync(
                Secret,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1)));

        Assert.Equal(VercelGatewayReportErrorKind.Throttled, exception.Kind);
        Assert.Equal(TimeSpan.FromSeconds(45), exception.RetryAfter);
    }

    [Fact]
    public async Task NetworkFailureIsTypedAndSanitized()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException(Secret));
        var client = new VercelGatewayReportClient(new HttpClient(handler));

        VercelGatewayReportException exception = await Assert.ThrowsAsync<VercelGatewayReportException>(() =>
            client.GetDailyReportAsync(
                Secret,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1)));

        Assert.Equal(VercelGatewayReportErrorKind.Transient, exception.Kind);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InternalTimeoutIsTypedAndSanitized()
    {
        var handler = new StubHandler((_, _) => throw new TaskCanceledException(Secret));
        var client = new VercelGatewayReportClient(new HttpClient(handler));

        VercelGatewayReportException exception = await Assert.ThrowsAsync<VercelGatewayReportException>(() =>
            client.GetDailyReportAsync(
                Secret,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1)));

        Assert.Equal(VercelGatewayReportErrorKind.Transient, exception.Kind);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new VercelGatewayReportClient(new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetDailyReportAsync(
                Secret,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1),
                cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CrossOriginFinalResponseIsRejectedBeforeBodyParsing()
    {
        HttpResponseMessage response = Json(HttpStatusCode.OK, DailyJson);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.test/private");
        var client = CreateClient(response);

        VercelGatewayReportException exception = await Assert.ThrowsAsync<VercelGatewayReportException>(() =>
            client.GetDailyReportAsync(
                Secret,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1)));

        Assert.Equal(VercelGatewayReportErrorKind.Contract, exception.Kind);
    }

    [Fact]
    public async Task OversizedResponseIsRejectedBeforeParsing()
    {
        string body = new('x', (1024 * 1024) + 1);
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        VercelGatewayReportException exception = await Assert.ThrowsAsync<VercelGatewayReportException>(() =>
            client.GetDailyReportAsync(
                Secret,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1)));

        Assert.Equal(VercelGatewayReportErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    private static VercelGatewayReportClient CreateClient(HttpResponseMessage response) =>
        new(new HttpClient(new StubHandler((request, _) =>
        {
            response.RequestMessage ??= request;
            return response;
        })));

    private static HttpResponseMessage Json(
        HttpStatusCode status,
        string body,
        HttpRequestMessage? request = null) =>
        new(status)
        {
            Content = new StringContent(body),
            RequestMessage = request,
        };

    private const string DailyJson = """
        {
          "results": [
            {
              "day": "2026-07-01",
              "total_cost": 1.25,
              "market_cost": 1.50,
              "surcharge_cost": 0.10,
              "gateway_cost": 0.05,
              "input_tokens": 120,
              "output_tokens": 80,
              "cached_input_tokens": 20,
              "cache_creation_input_tokens": 5,
              "reasoning_tokens": 10,
              "request_count": 2
            }
          ]
        }
        """;

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(send(request, cancellationToken));
        }
    }
}
