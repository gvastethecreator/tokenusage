using System.Net;
using System.Net.Http.Headers;
using TokenUsage.Providers.OpenRouter;

namespace TokenUsage.Providers.Tests.OpenRouter;

public sealed class OpenRouterClientTests
{
    private const string ManagementSecret = "PRIVATE_OPENROUTER_MANAGEMENT_KEY";
    private const string ApiSecret = "PRIVATE_OPENROUTER_API_KEY";

    [Fact]
    public async Task ValidResponsesUseFixedRequestsAndMapReportedValues()
    {
        var handler = new StubHandler((request, _) => request.RequestUri?.AbsolutePath switch
        {
            "/api/v1/credits" => Json(HttpStatusCode.OK, CreditsJson, request),
            "/api/v1/key" => Json(HttpStatusCode.OK, KeyJson, request),
            _ => throw new InvalidOperationException("Unexpected endpoint."),
        });
        var client = new OpenRouterClient(new HttpClient(handler));

        OpenRouterCredits credits = await client.GetCreditsAsync(ManagementSecret);
        OpenRouterKeyUsage usage = await client.GetKeyUsageAsync(ApiSecret);

        Assert.Equal(100.5m, credits.TotalCredits);
        Assert.Equal(25.75m, credits.TotalUsage);
        Assert.Equal(25.5m, usage.Usage);
        Assert.Equal(2.5m, usage.DailyUsage);
        Assert.Equal(12.5m, usage.WeeklyUsage);
        Assert.Equal(20.5m, usage.MonthlyUsage);
        Assert.Equal(100m, usage.Limit);
        Assert.Equal(74.5m, usage.LimitRemaining);
        Assert.Equal(OpenRouterLimitReset.Monthly, usage.LimitReset);
        Assert.False(usage.IsFreeTier);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Collection(
            handler.Requests,
            request => AssertRequest(
                request,
                "https://openrouter.ai/api/v1/credits",
                ManagementSecret),
            request => AssertRequest(request, "https://openrouter.ai/api/v1/key", ApiSecret));
    }

    [Fact]
    public async Task ZeroUsageAndNoLimitAreMeasuredValues()
    {
        var client = CreateClient(Json(HttpStatusCode.OK, """
            {
              "data": {
                "usage": 0,
                "usage_daily": 0,
                "usage_weekly": 0,
                "usage_monthly": 0,
                "limit": null,
                "limit_remaining": null,
                "limit_reset": null,
                "is_free_tier": true,
                "future_field": "ignored"
              }
            }
            """));

        OpenRouterKeyUsage result = await client.GetKeyUsageAsync(ApiSecret);

        Assert.Equal(0m, result.Usage);
        Assert.Null(result.Limit);
        Assert.Null(result.LimitRemaining);
        Assert.Null(result.LimitReset);
        Assert.True(result.IsFreeTier);
    }

    [Theory]
    [InlineData("daily", OpenRouterLimitReset.Daily)]
    [InlineData("weekly", OpenRouterLimitReset.Weekly)]
    [InlineData("monthly", OpenRouterLimitReset.Monthly)]
    public async Task SupportedLimitCadencesMapExactly(
        string wireValue,
        OpenRouterLimitReset expected)
    {
        var client = CreateClient(Json(HttpStatusCode.OK, $$"""
            {
              "data": {
                "usage": 1,
                "usage_daily": 1,
                "usage_weekly": 1,
                "usage_monthly": 1,
                "limit_reset": "{{wireValue}}",
                "is_free_tier": false
              }
            }
            """));

        OpenRouterKeyUsage result = await client.GetKeyUsageAsync(ApiSecret);

        Assert.Equal(expected, result.LimitReset);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"total_credits\":1}}")]
    [InlineData("{\"data\":{\"total_credits\":-1,\"total_usage\":0}}")]
    [InlineData("{\"data\":{\"total_credits\":1,\"total_usage\":-1}}")]
    [InlineData("not-json")]
    public async Task InvalidCreditsContractIsSanitized(string body)
    {
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        OpenRouterClientException exception = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => client.GetCreditsAsync(ManagementSecret));

        Assert.Equal(OpenRouterClientErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(ManagementSecret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":{\"usage\":-1,\"usage_daily\":0,\"usage_weekly\":0,\"usage_monthly\":0,\"is_free_tier\":false}}")]
    [InlineData("{\"data\":{\"usage\":1,\"usage_weekly\":0,\"usage_monthly\":0,\"is_free_tier\":false}}")]
    [InlineData("{\"data\":{\"usage\":1,\"usage_daily\":0,\"usage_weekly\":0,\"usage_monthly\":0}}")]
    [InlineData("{\"data\":{\"usage\":1,\"usage_daily\":0,\"usage_weekly\":0,\"usage_monthly\":0,\"limit_reset\":\"yearly\",\"is_free_tier\":false}}")]
    [InlineData("{\"data\":{\"usage\":1,\"usage_daily\":0,\"usage_weekly\":0,\"usage_monthly\":0,\"limit_reset\":\"Monthly\",\"is_free_tier\":false}}")]
    [InlineData("{\"data\":{\"usage\":1,\"usage_daily\":0,\"usage_weekly\":0,\"usage_monthly\":0,\"limit\":-1,\"is_free_tier\":false}}")]
    [InlineData("{\"data\":{\"usage\":1,\"usage_daily\":0,\"usage_weekly\":0,\"usage_monthly\":0,\"limit_remaining\":-1,\"is_free_tier\":false}}")]
    [InlineData("not-json")]
    public async Task InvalidKeyContractIsSanitized(string body)
    {
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        OpenRouterClientException exception = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => client.GetKeyUsageAsync(ApiSecret));

        Assert.Equal(OpenRouterClientErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(ApiSecret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, OpenRouterClientErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, OpenRouterClientErrorKind.InsufficientPermission)]
    [InlineData(HttpStatusCode.InternalServerError, OpenRouterClientErrorKind.Transient)]
    public async Task HttpErrorsAreTypedWithoutReadingPrivateBodies(
        HttpStatusCode status,
        OpenRouterClientErrorKind expectedKind)
    {
        string body = new string('x', (64 * 1024) + 1) + ManagementSecret;
        var client = CreateClient(Json(status, body));

        OpenRouterClientException exception = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => client.GetCreditsAsync(ManagementSecret));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.DoesNotContain(ManagementSecret, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, OpenRouterClientErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, OpenRouterClientErrorKind.InsufficientPermission)]
    [InlineData(HttpStatusCode.InternalServerError, OpenRouterClientErrorKind.Transient)]
    public async Task KeyUsageHttpErrorsUseTheSharedTypedMapping(
        HttpStatusCode status,
        OpenRouterClientErrorKind expectedKind)
    {
        var client = CreateClient(Json(status, $"{{\"private\":\"{ApiSecret}\"}}"));

        OpenRouterClientException exception = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => client.GetKeyUsageAsync(ApiSecret));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.DoesNotContain(ApiSecret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrottlingPreservesRetryAfter()
    {
        HttpResponseMessage response = Json(HttpStatusCode.TooManyRequests, "{}");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
        var client = CreateClient(response);

        OpenRouterClientException exception = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => client.GetKeyUsageAsync(ApiSecret));

        Assert.Equal(OpenRouterClientErrorKind.Throttled, exception.Kind);
        Assert.Equal(TimeSpan.FromSeconds(45), exception.RetryAfter);
    }

    [Fact]
    public async Task ThrottlingAcceptsAnHttpDateRetryAfter()
    {
        DateTimeOffset retryAt = DateTimeOffset.UtcNow.AddMinutes(2);
        HttpResponseMessage response = Json(HttpStatusCode.TooManyRequests, "{}");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAt);
        var client = CreateClient(response);

        OpenRouterClientException exception = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => client.GetCreditsAsync(ManagementSecret));

        Assert.Equal(OpenRouterClientErrorKind.Throttled, exception.Kind);
        TimeSpan retryAfter = Assert.IsType<TimeSpan>(exception.RetryAfter);
        Assert.InRange(
            retryAfter,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task NetworkFailureAndInternalTimeoutAreSanitized()
    {
        var networkClient = new OpenRouterClient(new HttpClient(
            new StubHandler((_, _) => throw new HttpRequestException(ManagementSecret))));
        var timeoutClient = new OpenRouterClient(new HttpClient(
            new StubHandler((_, _) => throw new TaskCanceledException(ApiSecret))));

        OpenRouterClientException network = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => networkClient.GetCreditsAsync(ManagementSecret));
        OpenRouterClientException timeout = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => timeoutClient.GetKeyUsageAsync(ApiSecret));

        Assert.Equal(OpenRouterClientErrorKind.Transient, network.Kind);
        Assert.Equal(OpenRouterClientErrorKind.Transient, timeout.Kind);
        Assert.DoesNotContain(ManagementSecret, network.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiSecret, timeout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlankKeyAndCallerCancellationStopBeforeNetwork()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new OpenRouterClient(new HttpClient(handler));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ArgumentException creditsBlank = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetCreditsAsync(" "));
        ArgumentException keyBlank = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetKeyUsageAsync(" "));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetKeyUsageAsync(ApiSecret, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetCreditsAsync(ManagementSecret, cancellation.Token));

        Assert.Equal("managementKey", creditsBlank.ParamName);
        Assert.Equal("apiKey", keyBlank.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task InvalidAuthorizationCharactersAreRejectedBeforeNetwork()
    {
        const string invalidKey = "private\r\nInjected: value";
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new OpenRouterClient(new HttpClient(handler));

        ArgumentException apiException = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetKeyUsageAsync(invalidKey));
        ArgumentException managementException = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetCreditsAsync(invalidKey));

        Assert.Equal("apiKey", apiException.ParamName);
        Assert.Equal("managementKey", managementException.ParamName);
        Assert.DoesNotContain(invalidKey, apiException.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(invalidKey, managementException.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ResponseStreamFailureIsTypedAndSanitized()
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingReadStream()),
        };
        var client = CreateClient(response);

        OpenRouterClientException exception = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => client.GetKeyUsageAsync(ApiSecret));

        Assert.Equal(OpenRouterClientErrorKind.Transient, exception.Kind);
        Assert.DoesNotContain(ApiSecret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossOriginFinalResponseIsRejectedBeforeParsing()
    {
        HttpResponseMessage response = Json(HttpStatusCode.OK, CreditsJson);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.test/private");
        var client = CreateClient(response);

        OpenRouterClientException exception = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => client.GetCreditsAsync(ManagementSecret));

        Assert.Equal(OpenRouterClientErrorKind.Contract, exception.Kind);
    }

    [Fact]
    public async Task SameOriginWrongEndpointIsRejectedBeforeParsing()
    {
        HttpResponseMessage response = Json(HttpStatusCode.OK, CreditsJson);
        response.RequestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            "https://openrouter.ai/api/v1/key");
        var client = CreateClient(response);

        OpenRouterClientException exception = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => client.GetCreditsAsync(ManagementSecret));

        Assert.Equal(OpenRouterClientErrorKind.Contract, exception.Kind);
    }

    [Fact]
    public async Task KeyUsageWrongEndpointAndUnexpectedQueryAreRejected()
    {
        HttpResponseMessage wrongPath = Json(HttpStatusCode.OK, KeyJson);
        wrongPath.RequestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            "https://openrouter.ai/api/v1/credits");
        HttpResponseMessage unexpectedQuery = Json(HttpStatusCode.OK, KeyJson);
        unexpectedQuery.RequestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            "https://openrouter.ai/api/v1/key?private=1");

        OpenRouterClientException pathException = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => CreateClient(wrongPath).GetKeyUsageAsync(ApiSecret));
        OpenRouterClientException queryException = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => CreateClient(unexpectedQuery).GetKeyUsageAsync(ApiSecret));

        Assert.Equal(OpenRouterClientErrorKind.Contract, pathException.Kind);
        Assert.Equal(OpenRouterClientErrorKind.Contract, queryException.Kind);
    }

    [Fact]
    public async Task OversizedSuccessResponseIsRejectedBeforeParsing()
    {
        string body = new('x', (64 * 1024) + 1);
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        OpenRouterClientException exception = await Assert.ThrowsAsync<OpenRouterClientException>(
            () => client.GetCreditsAsync(ManagementSecret));

        Assert.Equal(OpenRouterClientErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertRequest(
        HttpRequestMessage request,
        string expectedUri,
        string expectedSecret)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(expectedUri, request.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(expectedSecret, request.Headers.Authorization?.Parameter);
        Assert.Contains(
            request.Headers.Accept,
            value => string.Equals(value.MediaType, "application/json", StringComparison.Ordinal));
    }

    private static OpenRouterClient CreateClient(HttpResponseMessage response) =>
        new(new HttpClient(new StubHandler((request, _) =>
        {
            response.RequestMessage ??= request;
            return response;
        })));

    private static HttpResponseMessage Json(
        HttpStatusCode status,
        string body,
        HttpRequestMessage? request = null) => new(status)
        {
            Content = new StringContent(body),
            RequestMessage = request,
        };

    private const string CreditsJson = """
        {
          "data": {
            "total_credits": 100.5,
            "total_usage": 25.75,
            "future_field": "ignored"
          }
        }
        """;

    private const string KeyJson = """
        {
          "data": {
            "usage": 25.5,
            "usage_daily": 2.5,
            "usage_weekly": 12.5,
            "usage_monthly": 20.5,
            "limit": 100,
            "limit_remaining": 74.5,
            "limit_reset": "monthly",
            "is_free_tier": false
          }
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

    private sealed class FailingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException(ApiSecret);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException(ApiSecret));

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
