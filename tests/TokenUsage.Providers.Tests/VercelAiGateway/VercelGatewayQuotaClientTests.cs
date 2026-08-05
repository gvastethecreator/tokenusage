using System.Net;
using System.Net.Http.Headers;
using TokenUsage.Providers.VercelAiGateway;

namespace TokenUsage.Providers.Tests.VercelAiGateway;

public sealed class VercelGatewayQuotaClientTests
{
    private const string Secret = "PRIVATE_VERCEL_GATEWAY_KEY";
    private const string KeyId = "key_abc-123";

    [Fact]
    public async Task ValidBudgetUsesFixedRequestAndMapsRemainingAmount()
    {
        var handler = new StubHandler((request, _) => Json(HttpStatusCode.OK, ValidJson, request));
        using var httpClient = new HttpClient(handler);
        var client = new VercelGatewayQuotaClient(httpClient);

        VercelGatewayQuotaLookupResult result = await client.GetQuotaAsync(Secret, KeyId);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://ai-gateway.vercel.sh/v1/quotas?quotaEntityId=api_key_id_key_abc-123",
            request.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(Secret, request.Headers.Authorization?.Parameter);

        VercelGatewayQuota quota = Assert.IsType<VercelGatewayQuotaLookupResult.Found>(result).Quota;
        Assert.Equal("api_key_id_key_abc-123", quota.QuotaEntityId);
        Assert.Equal("desktop-key", quota.ApiKeyName);
        Assert.Equal(10m, quota.LimitAmount);
        Assert.Equal(1.04m, quota.CurrentSpend);
        Assert.Equal(8.96m, quota.RemainingAmount);
        Assert.Equal(VercelGatewayQuotaRefreshPeriod.Monthly, quota.RefreshPeriod);
        Assert.True(quota.Active);
    }

    [Fact]
    public async Task SpendAboveLimitClampsRemainingAmountToZero()
    {
        var client = CreateClient(Json(HttpStatusCode.OK, ValidJson.Replace("1.04", "12.5")));

        VercelGatewayQuotaLookupResult result = await client.GetQuotaAsync(Secret, KeyId);

        Assert.Equal(0m, Assert.IsType<VercelGatewayQuotaLookupResult.Found>(result).Quota.RemainingAmount);
    }

    [Theory]
    [InlineData("daily", VercelGatewayQuotaRefreshPeriod.Daily)]
    [InlineData("weekly", VercelGatewayQuotaRefreshPeriod.Weekly)]
    [InlineData("monthly", VercelGatewayQuotaRefreshPeriod.Monthly)]
    [InlineData("none", VercelGatewayQuotaRefreshPeriod.None)]
    public async Task SupportedRefreshPeriodsAreMapped(
        string source,
        VercelGatewayQuotaRefreshPeriod expected)
    {
        var client = CreateClient(Json(
            HttpStatusCode.OK,
            ValidJson.Replace("monthly", source)));

        VercelGatewayQuotaLookupResult result = await client.GetQuotaAsync(Secret, KeyId);

        Assert.Equal(expected, Assert.IsType<VercelGatewayQuotaLookupResult.Found>(result).Quota.RefreshPeriod);
    }

    [Fact]
    public async Task ExactNotFoundResponseReturnsNoBudget()
    {
        var client = CreateClient(Json(HttpStatusCode.NotFound, "{\"error\":\"Quota not found\"}"));

        VercelGatewayQuotaLookupResult result = await client.GetQuotaAsync(Secret, KeyId);

        Assert.Same(VercelGatewayQuotaLookupResult.NoBudget.Instance, result);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"quotaEntityId\":\"wrong\",\"apiKeyName\":\"desktop-key\",\"limitAmount\":10,\"currentSpend\":1,\"refreshPeriod\":\"monthly\",\"active\":true}")]
    [InlineData("{\"quotaEntityId\":\"api_key_id_key_abc-123\",\"apiKeyName\":\"desktop-key\",\"limitAmount\":0,\"currentSpend\":1,\"refreshPeriod\":\"monthly\",\"active\":true}")]
    [InlineData("{\"quotaEntityId\":\"api_key_id_key_abc-123\",\"apiKeyName\":\"desktop-key\",\"limitAmount\":10,\"currentSpend\":-1,\"refreshPeriod\":\"monthly\",\"active\":true}")]
    [InlineData("{\"quotaEntityId\":\"api_key_id_key_abc-123\",\"apiKeyName\":\"desktop-key\",\"limitAmount\":10,\"currentSpend\":1,\"refreshPeriod\":\"yearly\",\"active\":true}")]
    [InlineData("not-json")]
    public async Task InvalidSuccessContractIsSanitized(string body)
    {
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        VercelGatewayQuotaException exception = await Assert.ThrowsAsync<VercelGatewayQuotaException>(() =>
            client.GetQuotaAsync(Secret, KeyId));

        Assert.Equal(VercelGatewayQuotaErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(KeyId, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HarmlessExtraFieldsRemainForwardCompatible()
    {
        var client = CreateClient(Json(
            HttpStatusCode.OK,
            ValidJson.Replace("\"active\": true", "\"active\": true, \"futureField\": 42")));

        VercelGatewayQuotaLookupResult result = await client.GetQuotaAsync(Secret, KeyId);

        Assert.IsType<VercelGatewayQuotaLookupResult.Found>(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("api_key_id_abc")]
    [InlineData("abc/def")]
    [InlineData("ábc")]
    public async Task InvalidKeyIdsAreRejectedBeforeNetwork(string keyId)
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new VercelGatewayQuotaClient(new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetQuotaAsync(Secret, keyId));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LongKeyIdIsRejectedBeforeNetwork()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new VercelGatewayQuotaClient(new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetQuotaAsync(Secret, new string('a', 257)));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, VercelGatewayQuotaErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, VercelGatewayQuotaErrorKind.UnsupportedAccount)]
    [InlineData(HttpStatusCode.InternalServerError, VercelGatewayQuotaErrorKind.Transient)]
    public async Task HttpErrorsAreTypedAndSanitized(
        HttpStatusCode status,
        VercelGatewayQuotaErrorKind expectedKind)
    {
        var client = CreateClient(Json(status, $"{{\"private\":\"{Secret}\"}}"));

        VercelGatewayQuotaException exception = await Assert.ThrowsAsync<VercelGatewayQuotaException>(() =>
            client.GetQuotaAsync(Secret, KeyId));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(KeyId, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticationFailureDoesNotReadAnOversizedBody()
    {
        var client = CreateClient(Json(
            HttpStatusCode.Unauthorized,
            new string('x', (64 * 1024) + 1)));

        VercelGatewayQuotaException exception = await Assert.ThrowsAsync<VercelGatewayQuotaException>(() =>
            client.GetQuotaAsync(Secret, KeyId));

        Assert.Equal(VercelGatewayQuotaErrorKind.Authentication, exception.Kind);
    }

    [Fact]
    public async Task UnexpectedNotFoundBodyIsAContractError()
    {
        var client = CreateClient(Json(HttpStatusCode.NotFound, "{\"error\":\"private detail\"}"));

        VercelGatewayQuotaException exception = await Assert.ThrowsAsync<VercelGatewayQuotaException>(() =>
            client.GetQuotaAsync(Secret, KeyId));

        Assert.Equal(VercelGatewayQuotaErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain("private detail", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrottlingPreservesRetryAfter()
    {
        HttpResponseMessage response = Json(HttpStatusCode.TooManyRequests, "{}");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
        var client = CreateClient(response);

        VercelGatewayQuotaException exception = await Assert.ThrowsAsync<VercelGatewayQuotaException>(() =>
            client.GetQuotaAsync(Secret, KeyId));

        Assert.Equal(VercelGatewayQuotaErrorKind.Throttled, exception.Kind);
        Assert.Equal(TimeSpan.FromSeconds(45), exception.RetryAfter);
    }

    [Fact]
    public async Task NetworkFailureAndInternalTimeoutAreTyped()
    {
        var networkClient = new VercelGatewayQuotaClient(new HttpClient(
            new StubHandler((_, _) => throw new HttpRequestException(Secret))));
        var timeoutClient = new VercelGatewayQuotaClient(new HttpClient(
            new StubHandler((_, _) => throw new TaskCanceledException(Secret))));

        VercelGatewayQuotaException network = await Assert.ThrowsAsync<VercelGatewayQuotaException>(() =>
            networkClient.GetQuotaAsync(Secret, KeyId));
        VercelGatewayQuotaException timeout = await Assert.ThrowsAsync<VercelGatewayQuotaException>(() =>
            timeoutClient.GetQuotaAsync(Secret, KeyId));

        Assert.Equal(VercelGatewayQuotaErrorKind.Transient, network.Kind);
        Assert.Equal(VercelGatewayQuotaErrorKind.Transient, timeout.Kind);
        Assert.DoesNotContain(Secret, network.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, timeout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationPropagatesBeforeNetwork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new VercelGatewayQuotaClient(new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetQuotaAsync(Secret, KeyId, cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CrossOriginFinalResponseIsRejectedBeforeBodyParsing()
    {
        HttpResponseMessage response = Json(HttpStatusCode.OK, ValidJson);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.test/private");
        var client = CreateClient(response);

        VercelGatewayQuotaException exception = await Assert.ThrowsAsync<VercelGatewayQuotaException>(() =>
            client.GetQuotaAsync(Secret, KeyId));

        Assert.Equal(VercelGatewayQuotaErrorKind.Contract, exception.Kind);
    }

    [Fact]
    public async Task OversizedResponseIsRejectedBeforeParsing()
    {
        string body = new('x', (64 * 1024) + 1);
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        VercelGatewayQuotaException exception = await Assert.ThrowsAsync<VercelGatewayQuotaException>(() =>
            client.GetQuotaAsync(Secret, KeyId));

        Assert.Equal(VercelGatewayQuotaErrorKind.Contract, exception.Kind);
    }

    private static VercelGatewayQuotaClient CreateClient(HttpResponseMessage response) =>
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

    private const string ValidJson = """
        {
          "quotaEntityId": "api_key_id_key_abc-123",
          "apiKeyName": "desktop-key",
          "limitAmount": 10,
          "currentSpend": 1.04,
          "refreshPeriod": "monthly",
          "active": true
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
