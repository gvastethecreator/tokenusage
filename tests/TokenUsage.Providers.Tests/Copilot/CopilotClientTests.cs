using System.Net;
using System.Net.Http.Headers;
using TokenUsage.Providers.Copilot;

namespace TokenUsage.Providers.Tests.Copilot;

public sealed class CopilotClientTests
{
    private const string Token = "github_pat_PRIVATE_TEST_TOKEN";
    private const string PrivateEmail = "private-user@example.test";

    [Fact]
    public async Task PersonalAccountDetectsLoginAndUsedCreditsWithoutRemainingQuota()
    {
        var handler = new StubHandler((request, _) => request.RequestUri?.AbsolutePath switch
        {
            "/user" => Json(HttpStatusCode.OK, UserJson, request),
            "/users/octocat/settings/billing/ai_credit/usage" => Json(
                HttpStatusCode.OK,
                PersonalUsageJson,
                request),
            _ => throw new InvalidOperationException(request.RequestUri?.AbsolutePath),
        });
        var client = new CopilotClient(new HttpClient(handler));

        CopilotPersonalAccount account = await client.GetPersonalAccountAsync(Token);

        Assert.Equal("octocat", account.Login);
        CopilotAiCreditUsage usage = Assert.IsType<CopilotAiCreditLookupResult.Found>(account.Usage).Usage;
        Assert.Equal(CopilotBillingScope.Personal, usage.Scope);
        Assert.Equal("octocat", usage.AccountLogin);
        Assert.Equal(2026, usage.Period.Year);
        Assert.Equal(8, usage.Period.Month);
        Assert.Equal(12.5m, usage.GrossQuantity);
        Assert.Equal(0.125m, usage.GrossAmount);
        Assert.Equal(10m, usage.DiscountQuantity);
        Assert.Equal(0.10m, usage.DiscountAmount);
        Assert.Equal(2.5m, usage.NetQuantity);
        Assert.Equal(0.025m, usage.NetAmount);
        CopilotUsageItem item = Assert.Single(usage.Items);
        Assert.Equal("copilot", item.Product);
        Assert.Equal("copilot_premium_request", item.Sku);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Collection(
            handler.Requests,
            request => AssertPublicGitHubRequest(request, "https://api.github.com/user"),
            request => AssertPublicGitHubRequest(
                request,
                "https://api.github.com/users/octocat/settings/billing/ai_credit/usage"));
        Assert.DoesNotContain(
            handler.Requests,
            request => request.RequestUri?.AbsoluteUri.Contains("copilot_internal", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(PrivateEmail, account.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersonalBillingNotFoundIsUnsupportedAfterLoginDetection()
    {
        var handler = new StubHandler((request, _) => request.RequestUri?.AbsolutePath switch
        {
            "/user" => Json(HttpStatusCode.OK, UserJson, request),
            "/users/octocat/settings/billing/ai_credit/usage" => Json(HttpStatusCode.NotFound, "{}", request),
            _ => throw new InvalidOperationException(request.RequestUri?.AbsolutePath),
        });
        var client = new CopilotClient(new HttpClient(handler));

        CopilotPersonalAccount account = await client.GetPersonalAccountAsync(Token);

        Assert.Equal("octocat", account.Login);
        Assert.Same(CopilotAiCreditLookupResult.Unsupported.Instance, account.Usage);
    }

    [Fact]
    public async Task EmptyUsageItemsAreMeasuredZeros()
    {
        var client = CreateClient(Json(HttpStatusCode.OK, """
            {
              "timePeriod": { "year": 2026, "month": 8 },
              "user": "octocat",
              "usageItems": [],
              "future_field": "ignored"
            }
            """));

        CopilotAiCreditLookupResult result = await client.GetPersonalAiCreditUsageAsync(Token, "octocat");

        CopilotAiCreditUsage usage = Assert.IsType<CopilotAiCreditLookupResult.Found>(result).Usage;
        Assert.Empty(usage.Items);
        Assert.Equal(0m, usage.NetAmount);
        Assert.Equal(0m, usage.GrossQuantity);
    }

    [Fact]
    public async Task OrganizationSubscriptionMapsPublicPlanAndSeats()
    {
        var handler = new StubHandler((request, _) => request.RequestUri?.AbsolutePath switch
        {
            "/orgs/github/copilot/billing" => Json(HttpStatusCode.OK, OrganizationSubscriptionJson, request),
            "/organizations/github/settings/billing/ai_credit/usage" => Json(
                HttpStatusCode.OK,
                OrganizationUsageJson,
                request),
            _ => throw new InvalidOperationException(request.RequestUri?.AbsolutePath),
        });
        var client = new CopilotClient(new HttpClient(handler));

        CopilotOrganizationSubscriptionLookupResult subscriptionResult =
            await client.GetOrganizationSubscriptionAsync(Token, "github");
        CopilotAiCreditLookupResult usageResult =
            await client.GetOrganizationAiCreditUsageAsync(Token, "github");

        CopilotOrganizationSubscription subscription =
            Assert.IsType<CopilotOrganizationSubscriptionLookupResult.Found>(subscriptionResult).Subscription;
        Assert.Equal("github", subscription.Organization);
        Assert.Equal(CopilotOrganizationPlan.Business, subscription.Plan);
        Assert.Equal(12, subscription.SeatTotal);
        Assert.Equal(9, subscription.ActiveThisCycle);
        CopilotAiCreditUsage usage = Assert.IsType<CopilotAiCreditLookupResult.Found>(usageResult).Usage;
        Assert.Equal(CopilotBillingScope.Organization, usage.Scope);
        Assert.Equal("github", usage.AccountLogin);
        Assert.Equal(1.50m, usage.NetAmount);
        Assert.Collection(
            handler.Requests,
            request => AssertPublicGitHubRequest(
                request,
                "https://api.github.com/orgs/github/copilot/billing"),
            request => AssertPublicGitHubRequest(
                request,
                "https://api.github.com/organizations/github/settings/billing/ai_credit/usage"));
    }

    [Theory]
    [InlineData("business", CopilotOrganizationPlan.Business)]
    [InlineData("enterprise", CopilotOrganizationPlan.Enterprise)]
    public async Task SupportedOrganizationPlansMapExactly(
        string wireValue,
        CopilotOrganizationPlan expected)
    {
        var client = CreateClient(Json(HttpStatusCode.OK, $$"""
            {
              "plan_type": "{{wireValue}}",
              "seat_breakdown": { "total": 1 }
            }
            """));

        CopilotOrganizationSubscriptionLookupResult result =
            await client.GetOrganizationSubscriptionAsync(Token, "github");

        Assert.Equal(
            expected,
            Assert.IsType<CopilotOrganizationSubscriptionLookupResult.Found>(result).Subscription.Plan);
    }

    [Fact]
    public async Task OrganizationSubscriptionNotFoundIsUnsupported()
    {
        var client = CreateClient(Json(HttpStatusCode.NotFound, "{}"));

        CopilotOrganizationSubscriptionLookupResult result =
            await client.GetOrganizationSubscriptionAsync(Token, "github");

        Assert.Same(CopilotOrganizationSubscriptionLookupResult.Unsupported.Instance, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("octocat/admin")]
    [InlineData("../user")]
    [InlineData("octocat_internal")]
    [InlineData("-bot")]
    [InlineData("bot-")]
    [InlineData("has space")]
    public async Task UnsafeAccountNamesAreRejectedBeforeNetwork(string account)
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new CopilotClient(new HttpClient(handler));

        ArgumentException personal = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetPersonalAiCreditUsageAsync(Token, account));
        ArgumentException organization = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetOrganizationSubscriptionAsync(Token, account));

        Assert.Equal("username", personal.ParamName);
        Assert.Equal("organization", organization.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void PublicGitHubLoginsAreAccepted()
    {
        Assert.Equal("octocat", CopilotAccountName.Validate(" octocat ", "username"));
        Assert.True(CopilotAccountName.IsValid("a"));
        Assert.True(CopilotAccountName.IsValid(new string('a', CopilotAccountName.MaximumLength)));
        Assert.False(CopilotAccountName.IsValid(new string('a', CopilotAccountName.MaximumLength + 1)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"login\":null}")]
    [InlineData("{\"login\":\"octocat/admin\"}")]
    [InlineData("not-json")]
    public async Task InvalidUserContractIsSanitized(string body)
    {
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        CopilotClientException exception = await Assert.ThrowsAsync<CopilotClientException>(
            () => client.GetAuthenticatedUserAsync(Token));

        Assert.Equal(CopilotClientErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(Token, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"timePeriod\":{\"year\":2026},\"user\":\"octocat\"}")]
    [InlineData("{\"timePeriod\":{\"year\":1999,\"month\":8},\"user\":\"octocat\",\"usageItems\":[]}")]
    [InlineData("{\"timePeriod\":{\"year\":2026,\"month\":13},\"user\":\"octocat\",\"usageItems\":[]}")]
    [InlineData("""{"timePeriod":{"year":2026},"user":"octocat","usageItems":[{"product":"copilot","sku":"s","model":"m","unitType":"credits","pricePerUnit":1,"grossQuantity":-1,"grossAmount":0,"discountQuantity":0,"discountAmount":0,"netQuantity":0,"netAmount":0}]}""")]
    [InlineData("not-json")]
    public async Task InvalidUsageContractIsSanitized(string body)
    {
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        CopilotClientException exception = await Assert.ThrowsAsync<CopilotClientException>(
            () => client.GetPersonalAiCreditUsageAsync(Token, "octocat"));

        Assert.Equal(CopilotClientErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(Token, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"seat_breakdown\":{\"total\":1},\"plan_type\":\"pro\"}")]
    [InlineData("{\"plan_type\":\"business\"}")]
    [InlineData("{\"seat_breakdown\":{\"total\":-1}}")]
    public async Task InvalidOrganizationSubscriptionContractIsSanitized(string body)
    {
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        CopilotClientException exception = await Assert.ThrowsAsync<CopilotClientException>(
            () => client.GetOrganizationSubscriptionAsync(Token, "github"));

        Assert.Equal(CopilotClientErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CopilotClientErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, CopilotClientErrorKind.InsufficientPermission)]
    [InlineData(HttpStatusCode.NotFound, CopilotClientErrorKind.UnsupportedScope)]
    [InlineData(HttpStatusCode.InternalServerError, CopilotClientErrorKind.Transient)]
    public async Task HttpErrorsAreTypedWithoutReadingPrivateBodies(
        HttpStatusCode status,
        CopilotClientErrorKind expectedKind)
    {
        string body = new string('x', (64 * 1024) + 1) + Token + PrivateEmail;
        var client = CreateClient(Json(status, body));

        CopilotClientException exception = await Assert.ThrowsAsync<CopilotClientException>(
            () => client.GetAuthenticatedUserAsync(Token));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.DoesNotContain(Token, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateEmail, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrottlingPreservesRetryAfter()
    {
        HttpResponseMessage response = Json(HttpStatusCode.TooManyRequests, "{}");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        var client = CreateClient(response);

        CopilotClientException exception = await Assert.ThrowsAsync<CopilotClientException>(
            () => client.GetAuthenticatedUserAsync(Token));

        Assert.Equal(CopilotClientErrorKind.Throttled, exception.Kind);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task BlankTokenAndCallerCancellationStopBeforeNetwork()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new CopilotClient(new HttpClient(handler));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ArgumentException blank = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetAuthenticatedUserAsync(" "));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAuthenticatedUserAsync(Token, cancellation.Token));

        Assert.Equal("token", blank.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task InvalidAuthorizationCharactersAreRejectedBeforeNetwork()
    {
        const string invalidToken = "github_pat_\r\nInjected: value";
        var handler = new StubHandler((_, _) => throw new InvalidOperationException());
        var client = new CopilotClient(new HttpClient(handler));

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetAuthenticatedUserAsync(invalidToken));

        Assert.Equal("token", exception.ParamName);
        Assert.DoesNotContain(invalidToken, exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CrossOriginAndInternalCopilotEndpointsAreRejected()
    {
        HttpResponseMessage crossOrigin = Json(HttpStatusCode.OK, UserJson);
        crossOrigin.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.test/user");
        HttpResponseMessage internalEndpoint = Json(HttpStatusCode.OK, UserJson);
        internalEndpoint.RequestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.github.com/copilot_internal/user");

        CopilotClientException origin = await Assert.ThrowsAsync<CopilotClientException>(
            () => CreateClient(crossOrigin).GetAuthenticatedUserAsync(Token));
        CopilotClientException internalUser = await Assert.ThrowsAsync<CopilotClientException>(
            () => CreateClient(internalEndpoint).GetAuthenticatedUserAsync(Token));

        Assert.Equal(CopilotClientErrorKind.Contract, origin.Kind);
        Assert.Equal(CopilotClientErrorKind.Contract, internalUser.Kind);
    }

    [Fact]
    public async Task OversizedSuccessResponseIsRejectedBeforeParsing()
    {
        string body = new('x', (64 * 1024) + 1);
        var client = CreateClient(Json(HttpStatusCode.OK, body));

        CopilotClientException exception = await Assert.ThrowsAsync<CopilotClientException>(
            () => client.GetAuthenticatedUserAsync(Token));

        Assert.Equal(CopilotClientErrorKind.Contract, exception.Kind);
        Assert.DoesNotContain(body, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NetworkFailureIsTypedAndSanitized()
    {
        var client = new CopilotClient(new HttpClient(
            new StubHandler((_, _) => throw new HttpRequestException(Token))));

        CopilotClientException exception = await Assert.ThrowsAsync<CopilotClientException>(
            () => client.GetAuthenticatedUserAsync(Token));

        Assert.Equal(CopilotClientErrorKind.Transient, exception.Kind);
        Assert.DoesNotContain(Token, exception.ToString(), StringComparison.Ordinal);
    }

    private static void AssertPublicGitHubRequest(HttpRequestMessage request, string expectedUri)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(expectedUri, request.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(Token, request.Headers.Authorization?.Parameter);
        Assert.Contains(
            request.Headers.Accept,
            value => string.Equals(value.MediaType, "application/vnd.github+json", StringComparison.Ordinal));
        Assert.Contains(
            request.Headers.UserAgent,
            value => string.Equals(value.Product?.Name, CopilotClient.UserAgentProduct, StringComparison.Ordinal));
        Assert.True(request.Headers.TryGetValues("X-GitHub-Api-Version", out IEnumerable<string>? versions));
        Assert.Equal(CopilotClient.ApiVersion, Assert.Single(versions));
        Assert.False(request.Headers.Contains("Editor-Version"));
        Assert.False(request.Headers.Contains("Copilot-Session"));
        Assert.False(request.Headers.Contains("Openai-Organization"));
        Assert.DoesNotContain(
            "copilot_internal",
            request.RequestUri?.AbsoluteUri,
            StringComparison.Ordinal);
    }

    private static CopilotClient CreateClient(HttpResponseMessage response) =>
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

    private const string UserJson = """
        {
          "login": "octocat",
          "id": 1,
          "email": "private-user@example.test",
          "name": "Private Name"
        }
        """;

    private const string PersonalUsageJson = """
        {
          "timePeriod": { "year": 2026, "month": 8 },
          "user": "octocat",
          "usageItems": [
            {
              "product": "copilot",
              "sku": "copilot_premium_request",
              "model": "gpt-4.1",
              "unitType": "credits",
              "pricePerUnit": 0.01,
              "grossQuantity": 12.5,
              "grossAmount": 0.125,
              "discountQuantity": 10,
              "discountAmount": 0.10,
              "netQuantity": 2.5,
              "netAmount": 0.025
            }
          ]
        }
        """;

    private const string OrganizationSubscriptionJson = """
        {
          "plan_type": "business",
          "seat_breakdown": {
            "total": 12,
            "active_this_cycle": 9
          },
          "public_code_suggestions": "block"
        }
        """;

    private const string OrganizationUsageJson = """
        {
          "timePeriod": { "year": 2026, "month": 8 },
          "organization": "github",
          "usageItems": [
            {
              "product": "copilot",
              "sku": "copilot_business",
              "model": "gpt-4.1",
              "unitType": "credits",
              "pricePerUnit": 0.01,
              "grossQuantity": 200,
              "grossAmount": 2.00,
              "discountQuantity": 50,
              "discountAmount": 0.50,
              "netQuantity": 150,
              "netAmount": 1.50
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
