using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenUsage.Providers.Copilot;

public sealed class CopilotClient : ICopilotClient
{
    public const string ApiVersion = "2026-03-10";
    public const string UserAgentProduct = "TokenUsage";

    private const int MaximumResponseBytes = 64 * 1024;
    private const string GitHubJsonAccept = "application/vnd.github+json";

    private static readonly Uri ApiOrigin = new("https://api.github.com", UriKind.Absolute);
    private static readonly Uri AuthenticatedUserEndpoint =
        new("https://api.github.com/user", UriKind.Absolute);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly HttpClient _httpClient;

    public CopilotClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<CopilotAuthenticatedUser> GetAuthenticatedUserAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        byte[] content = await GetRequiredAsync(
            token,
            AuthenticatedUserEndpoint,
            "GitHub account",
            cancellationToken).ConfigureAwait(false);
        try
        {
            UserDocument? document = JsonSerializer.Deserialize<UserDocument>(content, JsonOptions);
            if (document?.Login is null || !CopilotAccountName.IsValid(document.Login))
            {
                throw ContractFailure("GitHub account");
            }

            return new CopilotAuthenticatedUser(document.Login);
        }
        catch (JsonException)
        {
            throw ContractFailure("GitHub account");
        }
    }

    public async Task<CopilotPersonalAccount> GetPersonalAccountAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        CopilotAuthenticatedUser user = await GetAuthenticatedUserAsync(token, cancellationToken)
            .ConfigureAwait(false);
        CopilotAiCreditLookupResult usage = await GetPersonalAiCreditUsageAsync(
            token,
            user.Login,
            cancellationToken).ConfigureAwait(false);
        return new CopilotPersonalAccount(user.Login, usage);
    }

    public Task<CopilotAiCreditLookupResult> GetPersonalAiCreditUsageAsync(
        string token,
        string username,
        CancellationToken cancellationToken = default)
    {
        string login = CopilotAccountName.Validate(username, nameof(username));
        return GetAiCreditUsageAsync(
            token,
            BuildUserAiCreditUsageEndpoint(login),
            CopilotBillingScope.Personal,
            cancellationToken);
    }

    public async Task<CopilotOrganizationSubscriptionLookupResult> GetOrganizationSubscriptionAsync(
        string token,
        string organization,
        CancellationToken cancellationToken = default)
    {
        string login = CopilotAccountName.Validate(organization, nameof(organization));
        byte[]? content = await GetOptionalAsync(
            token,
            BuildOrganizationSubscriptionEndpoint(login),
            "Copilot organization subscription",
            cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return CopilotOrganizationSubscriptionLookupResult.Unsupported.Instance;
        }

        try
        {
            OrganizationSubscriptionDocument? document =
                JsonSerializer.Deserialize<OrganizationSubscriptionDocument>(content, JsonOptions);
            if (document?.SeatBreakdown is null)
            {
                throw ContractFailure("Copilot organization subscription");
            }

            return new CopilotOrganizationSubscriptionLookupResult.Found(
                new CopilotOrganizationSubscription(
                    login,
                    ParsePlan(document.PlanType),
                    ParseNonNegativeInt(document.SeatBreakdown.Total),
                    ParseNonNegativeInt(document.SeatBreakdown.ActiveThisCycle)));
        }
        catch (JsonException)
        {
            throw ContractFailure("Copilot organization subscription");
        }
    }

    public Task<CopilotAiCreditLookupResult> GetOrganizationAiCreditUsageAsync(
        string token,
        string organization,
        CancellationToken cancellationToken = default)
    {
        string login = CopilotAccountName.Validate(organization, nameof(organization));
        return GetAiCreditUsageAsync(
            token,
            BuildOrganizationAiCreditUsageEndpoint(login),
            CopilotBillingScope.Organization,
            cancellationToken);
    }

    private async Task<CopilotAiCreditLookupResult> GetAiCreditUsageAsync(
        string token,
        Uri endpoint,
        CopilotBillingScope scope,
        CancellationToken cancellationToken)
    {
        byte[]? content = await GetOptionalAsync(
            token,
            endpoint,
            "AI credit usage",
            cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return CopilotAiCreditLookupResult.Unsupported.Instance;
        }

        try
        {
            UsageDocument? document = JsonSerializer.Deserialize<UsageDocument>(content, JsonOptions);
            string? account = scope == CopilotBillingScope.Personal
                ? document?.User
                : document?.Organization;
            if (document?.TimePeriod is null
                || document.UsageItems is null
                || account is null
                || !CopilotAccountName.IsValid(account))
            {
                throw ContractFailure("AI credit usage");
            }

            CopilotTimePeriod period = MapPeriod(document.TimePeriod);
            CopilotUsageItem[] items = document.UsageItems.Select(MapItem).ToArray();
            return new CopilotAiCreditLookupResult.Found(
                new CopilotAiCreditUsage(
                    scope,
                    account,
                    period,
                    items,
                    items.Sum(item => item.GrossQuantity),
                    items.Sum(item => item.GrossAmount),
                    items.Sum(item => item.DiscountQuantity),
                    items.Sum(item => item.DiscountAmount),
                    items.Sum(item => item.NetQuantity),
                    items.Sum(item => item.NetAmount)));
        }
        catch (JsonException)
        {
            throw ContractFailure("AI credit usage");
        }
    }

    private async Task<byte[]> GetRequiredAsync(
        string token,
        Uri endpoint,
        string operation,
        CancellationToken cancellationToken)
    {
        byte[]? content = await GetAsync(
            token,
            endpoint,
            operation,
            allowNotFound: false,
            cancellationToken).ConfigureAwait(false);
        return content ?? throw ContractFailure(operation);
    }

    private Task<byte[]?> GetOptionalAsync(
        string token,
        Uri endpoint,
        string operation,
        CancellationToken cancellationToken) =>
        GetAsync(token, endpoint, operation, allowNotFound: true, cancellationToken);

    private async Task<byte[]?> GetAsync(
        string token,
        Uri endpoint,
        string operation,
        bool allowNotFound,
        CancellationToken cancellationToken)
    {
        AuthenticationHeaderValue authorization = CreateAuthorization(token);
        cancellationToken.ThrowIfCancellationRequested();

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = authorization;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubJsonAccept));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgentProduct, "1.0"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            ValidateFinalEndpoint(response.RequestMessage?.RequestUri, endpoint);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw CreateStatusException(response, operation);
            }

            return await ReadBoundedContentAsync(response.Content, operation, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CopilotClientException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new CopilotClientException(
                CopilotClientErrorKind.Transient,
                $"GitHub could not return the {operation}.");
        }
        catch (IOException)
        {
            throw new CopilotClientException(
                CopilotClientErrorKind.Transient,
                $"GitHub could not read the {operation} response.");
        }
        catch (OperationCanceledException)
        {
            throw new CopilotClientException(
                CopilotClientErrorKind.Transient,
                $"GitHub timed out while reading the {operation}.");
        }
    }

    private static AuthenticationHeaderValue CreateAuthorization(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("The GitHub token must not be empty.", nameof(token));
        }

        if (!AuthenticationHeaderValue.TryParse($"Bearer {token}", out AuthenticationHeaderValue? authorization)
            || !string.Equals(authorization.Parameter, token, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The GitHub token contains characters that cannot be sent in an authorization header.",
                nameof(token));
        }

        return authorization;
    }

    private static void ValidateFinalEndpoint(Uri? finalUri, Uri expectedEndpoint)
    {
        if (finalUri is null
            || !string.Equals(finalUri.Scheme, ApiOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(finalUri.Host, ApiOrigin.Host, StringComparison.OrdinalIgnoreCase)
            || finalUri.Port != ApiOrigin.Port
            || !string.Equals(finalUri.AbsolutePath, expectedEndpoint.AbsolutePath, StringComparison.Ordinal)
            || !string.Equals(finalUri.Query, expectedEndpoint.Query, StringComparison.Ordinal))
        {
            throw new CopilotClientException(
                CopilotClientErrorKind.Contract,
                "GitHub returned a response from an unexpected endpoint.");
        }
    }

    private static CopilotClientException CreateStatusException(
        HttpResponseMessage response,
        string operation) => response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new(
                CopilotClientErrorKind.Authentication,
                "The GitHub token is invalid or revoked."),
            HttpStatusCode.Forbidden => new(
                CopilotClientErrorKind.InsufficientPermission,
                "This GitHub token lacks permission for this request."),
            HttpStatusCode.NotFound => new(
                CopilotClientErrorKind.UnsupportedScope,
                $"GitHub does not expose the {operation} for this account."),
            HttpStatusCode.TooManyRequests => new(
                CopilotClientErrorKind.Throttled,
                "GitHub asked TokenUsage to retry later.",
                ReadRetryAfter(response.Headers.RetryAfter)),
            _ => new(
                CopilotClientErrorKind.Transient,
                $"GitHub could not return the {operation}."),
        };

    private static TimeSpan? ReadRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is TimeSpan delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter?.Date is DateTimeOffset date)
        {
            TimeSpan remaining = date - DateTimeOffset.UtcNow;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        return null;
    }

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        string operation,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw ContractFailure(operation);
        }

        await using Stream source = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > MaximumResponseBytes)
            {
                throw ContractFailure(operation);
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static Uri BuildUserAiCreditUsageEndpoint(string username) =>
        new(
            $"https://api.github.com/users/{Uri.EscapeDataString(username)}/settings/billing/ai_credit/usage",
            UriKind.Absolute);

    private static Uri BuildOrganizationAiCreditUsageEndpoint(string organization) =>
        new(
            $"https://api.github.com/organizations/{Uri.EscapeDataString(organization)}/settings/billing/ai_credit/usage",
            UriKind.Absolute);

    private static Uri BuildOrganizationSubscriptionEndpoint(string organization) =>
        new(
            $"https://api.github.com/orgs/{Uri.EscapeDataString(organization)}/copilot/billing",
            UriKind.Absolute);

    private static CopilotTimePeriod MapPeriod(TimePeriodDocument period)
    {
        if (period.Year is not int year || year is < 2000 or > 2100)
        {
            throw ContractFailure("AI credit usage");
        }

        if (period.Month is int month && month is < 1 or > 12)
        {
            throw ContractFailure("AI credit usage");
        }

        if (period.Day is int day && day is < 1 or > 31)
        {
            throw ContractFailure("AI credit usage");
        }

        return new CopilotTimePeriod(year, period.Month, period.Day);
    }

    private static CopilotUsageItem MapItem(UsageItemDocument item)
    {
        if (string.IsNullOrWhiteSpace(item.Product)
            || string.IsNullOrWhiteSpace(item.Sku)
            || string.IsNullOrWhiteSpace(item.Model)
            || string.IsNullOrWhiteSpace(item.UnitType)
            || item.PricePerUnit is not decimal pricePerUnit
            || item.GrossQuantity is not decimal grossQuantity
            || item.GrossAmount is not decimal grossAmount
            || item.DiscountQuantity is not decimal discountQuantity
            || item.DiscountAmount is not decimal discountAmount
            || item.NetQuantity is not decimal netQuantity
            || item.NetAmount is not decimal netAmount
            || pricePerUnit < 0m
            || grossQuantity < 0m
            || grossAmount < 0m
            || discountQuantity < 0m
            || discountAmount < 0m
            || netQuantity < 0m
            || netAmount < 0m)
        {
            throw ContractFailure("AI credit usage");
        }

        return new CopilotUsageItem(
            item.Product,
            item.Sku,
            item.Model,
            item.UnitType,
            pricePerUnit,
            grossQuantity,
            grossAmount,
            discountQuantity,
            discountAmount,
            netQuantity,
            netAmount);
    }

    private static CopilotOrganizationPlan? ParsePlan(string? value) => value switch
    {
        null => null,
        "business" => CopilotOrganizationPlan.Business,
        "enterprise" => CopilotOrganizationPlan.Enterprise,
        _ => throw ContractFailure("Copilot organization subscription"),
    };

    private static int? ParseNonNegativeInt(int? value)
    {
        if (value < 0)
        {
            throw ContractFailure("Copilot organization subscription");
        }

        return value;
    }

    private static CopilotClientException ContractFailure(string operation) => new(
        CopilotClientErrorKind.Contract,
        $"GitHub returned an unsupported {operation} response.");

    private sealed class UserDocument
    {
        [JsonPropertyName("login")]
        public string? Login { get; init; }
    }

    private sealed class UsageDocument
    {
        [JsonPropertyName("timePeriod")]
        public TimePeriodDocument? TimePeriod { get; init; }

        [JsonPropertyName("user")]
        public string? User { get; init; }

        [JsonPropertyName("organization")]
        public string? Organization { get; init; }

        [JsonPropertyName("usageItems")]
        public UsageItemDocument[]? UsageItems { get; init; }
    }

    private sealed class TimePeriodDocument
    {
        [JsonPropertyName("year")]
        public int? Year { get; init; }

        [JsonPropertyName("month")]
        public int? Month { get; init; }

        [JsonPropertyName("day")]
        public int? Day { get; init; }
    }

    private sealed class UsageItemDocument
    {
        [JsonPropertyName("product")]
        public string? Product { get; init; }

        [JsonPropertyName("sku")]
        public string? Sku { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("unitType")]
        public string? UnitType { get; init; }

        [JsonPropertyName("pricePerUnit")]
        public decimal? PricePerUnit { get; init; }

        [JsonPropertyName("grossQuantity")]
        public decimal? GrossQuantity { get; init; }

        [JsonPropertyName("grossAmount")]
        public decimal? GrossAmount { get; init; }

        [JsonPropertyName("discountQuantity")]
        public decimal? DiscountQuantity { get; init; }

        [JsonPropertyName("discountAmount")]
        public decimal? DiscountAmount { get; init; }

        [JsonPropertyName("netQuantity")]
        public decimal? NetQuantity { get; init; }

        [JsonPropertyName("netAmount")]
        public decimal? NetAmount { get; init; }
    }

    private sealed class OrganizationSubscriptionDocument
    {
        [JsonPropertyName("plan_type")]
        public string? PlanType { get; init; }

        [JsonPropertyName("seat_breakdown")]
        public SeatBreakdownDocument? SeatBreakdown { get; init; }
    }

    private sealed class SeatBreakdownDocument
    {
        [JsonPropertyName("total")]
        public int? Total { get; init; }

        [JsonPropertyName("active_this_cycle")]
        public int? ActiveThisCycle { get; init; }
    }
}
