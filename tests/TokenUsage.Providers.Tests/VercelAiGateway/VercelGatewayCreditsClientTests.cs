using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using TokenUsage.Providers.VercelAiGateway;

namespace TokenUsage.Providers.Tests.VercelAiGateway;

public sealed class VercelGatewayCreditsClientTests
{
    private const string Secret = "PRIVATE_VERCEL_GATEWAY_KEY";

    [Fact]
    public async Task DocumentedStringAmountsUseTheFixedRequest()
    {
        var handler = new StubHandler((request, _) => Json(
            HttpStatusCode.OK,
            "{\"balance\":\"95.50\",\"total_used\":\"4.50\"}",
            request));
        var client = new VercelGatewayCreditsClient(new HttpClient(handler));

        VercelGatewayCredits credits = await client.GetCreditsAsync(Secret);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://ai-gateway.vercel.sh/v1/credits", request.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(Secret, request.Headers.Authorization?.Parameter);
        Assert.Equal(95.50m, credits.Balance);
        Assert.Equal(4.50m, credits.TotalUsed);
    }

    [Fact]
    public async Task AmountsParseIndependentlyOfTheCurrentCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-ES");
        try
        {
            var client = CreateClient(Json(
                HttpStatusCode.OK,
                "{\"balance\":\"1234.5\",\"total_used\":0.25}"));

            VercelGatewayCredits credits = await client.GetCreditsAsync(Secret);

            Assert.Equal(1234.5m, credits.Balance);
            Assert.Equal(0.25m, credits.TotalUsed);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"balance\":\"95.50\"}")]
    [InlineData("{\"balance\":\"ninety\",\"total_used\":\"4.50\"}")]
    [InlineData("{\"balance\":\"1,000.00\",\"total_used\":\"4.50\"}")]
    [InlineData("{\"balance\":\"95.50\",\"total_used\":\"-1\"}")]
    [InlineData("{\"balance\":true,\"total_used\":\"4.50\"}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public async Task InvalidBodiesAreSanitizedContractErrors(string body)
    {
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        VercelGatewayCreditsException exception =
            await Assert.ThrowsAsync<VercelGatewayCreditsException>(() =>
                client.GetCreditsAsync(Secret));

        Assert.Equal(VercelGatewayCreditsErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, VercelGatewayCreditsErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, VercelGatewayCreditsErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, VercelGatewayCreditsErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, VercelGatewayCreditsErrorKind.Transient)]
    public async Task HttpErrorsAreTypedAndSanitized(
        HttpStatusCode status,
        VercelGatewayCreditsErrorKind expectedKind)
    {
        var client = CreateClient(Json(status, $"{{\"error\":{{\"message\":\"{Secret}\"}}}}"));

        VercelGatewayCreditsException exception =
            await Assert.ThrowsAsync<VercelGatewayCreditsException>(() =>
                client.GetCreditsAsync(Secret));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrottlingPreservesRetryAfter()
    {
        HttpResponseMessage response = Json(HttpStatusCode.TooManyRequests, "{}");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        var client = CreateClient(response);

        VercelGatewayCreditsException exception =
            await Assert.ThrowsAsync<VercelGatewayCreditsException>(() =>
                client.GetCreditsAsync(Secret));

        Assert.Equal(VercelGatewayCreditsErrorKind.Throttled, exception.Kind);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task OversizedResponseIsRejectedBeforeParsing()
    {
        var client = CreateClient(Json(HttpStatusCode.OK, new string('x', (16 * 1024) + 1)));

        VercelGatewayCreditsException exception =
            await Assert.ThrowsAsync<VercelGatewayCreditsException>(() =>
                client.GetCreditsAsync(Secret));

        Assert.Equal(VercelGatewayCreditsErrorKind.Contract, exception.Kind);
    }

    [Fact]
    public async Task CrossOriginFinalResponseIsRejected()
    {
        HttpResponseMessage response = Json(
            HttpStatusCode.OK,
            "{\"balance\":\"1\",\"total_used\":\"0\"}");
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.test/credits");
        var client = CreateClient(response);

        VercelGatewayCreditsException exception =
            await Assert.ThrowsAsync<VercelGatewayCreditsException>(() =>
                client.GetCreditsAsync(Secret));

        Assert.Equal(VercelGatewayCreditsErrorKind.Contract, exception.Kind);
    }

    [Fact]
    public async Task NetworkFailureIsTransientAndSanitized()
    {
        var client = new VercelGatewayCreditsClient(new HttpClient(
            new StubHandler((_, _) => throw new HttpRequestException(Secret))));

        VercelGatewayCreditsException exception =
            await Assert.ThrowsAsync<VercelGatewayCreditsException>(() =>
                client.GetCreditsAsync(Secret));

        Assert.Equal(VercelGatewayCreditsErrorKind.Transient, exception.Kind);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }

    private static VercelGatewayCreditsClient CreateClient(HttpResponseMessage response) =>
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
