using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WOpenUsage.Providers.OpenRouter;

public sealed class OpenRouterClient : IOpenRouterClient
{
    private const int MaximumResponseBytes = 64 * 1024;

    private static readonly Uri CreditsEndpoint =
        new("https://openrouter.ai/api/v1/credits", UriKind.Absolute);
    private static readonly Uri KeyEndpoint =
        new("https://openrouter.ai/api/v1/key", UriKind.Absolute);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly HttpClient _httpClient;

    public OpenRouterClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<OpenRouterCredits> GetCreditsAsync(
        string managementKey,
        CancellationToken cancellationToken = default)
    {
        byte[] content = await GetAsync(
            managementKey,
            nameof(managementKey),
            CreditsEndpoint,
            "credits",
            cancellationToken).ConfigureAwait(false);
        try
        {
            CreditsDocument? document = JsonSerializer.Deserialize<CreditsDocument>(content, JsonOptions);
            if (document?.Data?.TotalCredits is not decimal totalCredits
                || document.Data.TotalUsage is not decimal totalUsage
                || totalCredits < 0m
                || totalUsage < 0m)
            {
                throw ContractFailure("credits");
            }

            return new OpenRouterCredits(totalCredits, totalUsage);
        }
        catch (JsonException)
        {
            throw ContractFailure("credits");
        }
    }

    public async Task<OpenRouterKeyUsage> GetKeyUsageAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        byte[] content = await GetAsync(
            apiKey,
            nameof(apiKey),
            KeyEndpoint,
            "key usage",
            cancellationToken).ConfigureAwait(false);
        try
        {
            KeyDocument? document = JsonSerializer.Deserialize<KeyDocument>(content, JsonOptions);
            KeyData? data = document?.Data;
            if (data?.Usage is not decimal usage
                || data.DailyUsage is not decimal dailyUsage
                || data.WeeklyUsage is not decimal weeklyUsage
                || data.MonthlyUsage is not decimal monthlyUsage
                || data.IsFreeTier is not bool isFreeTier
                || usage < 0m
                || dailyUsage < 0m
                || weeklyUsage < 0m
                || monthlyUsage < 0m
                || data.Limit is < 0m
                || data.LimitRemaining is < 0m)
            {
                throw ContractFailure("key usage");
            }

            return new OpenRouterKeyUsage(
                usage,
                dailyUsage,
                weeklyUsage,
                monthlyUsage,
                data.Limit,
                data.LimitRemaining,
                ParseLimitReset(data.LimitReset),
                isFreeTier);
        }
        catch (JsonException)
        {
            throw ContractFailure("key usage");
        }
    }

    private async Task<byte[]> GetAsync(
        string apiKey,
        string apiKeyParameterName,
        Uri endpoint,
        string operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException(
                "The OpenRouter key must not be empty.",
                apiKeyParameterName);
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (!AuthenticationHeaderValue.TryParse($"Bearer {apiKey}", out var authorization)
            || !string.Equals(authorization.Parameter, apiKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The OpenRouter key contains characters that cannot be sent in an authorization header.",
                apiKeyParameterName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = authorization;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            ValidateFinalEndpoint(response.RequestMessage?.RequestUri, endpoint);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateStatusException(response, operation);
            }

            return await ReadBoundedContentAsync(
                response.Content,
                operation,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OpenRouterClientException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new OpenRouterClientException(
                OpenRouterClientErrorKind.Transient,
                $"OpenRouter could not return the {operation}.");
        }
        catch (IOException)
        {
            throw new OpenRouterClientException(
                OpenRouterClientErrorKind.Transient,
                $"OpenRouter could not read the {operation} response.");
        }
        catch (OperationCanceledException)
        {
            throw new OpenRouterClientException(
                OpenRouterClientErrorKind.Transient,
                $"OpenRouter timed out while reading the {operation}.");
        }
    }

    private static void ValidateFinalEndpoint(Uri? finalUri, Uri expectedEndpoint)
    {
        if (finalUri is null
            || !string.Equals(finalUri.Scheme, expectedEndpoint.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(finalUri.Host, expectedEndpoint.Host, StringComparison.OrdinalIgnoreCase)
            || finalUri.Port != expectedEndpoint.Port
            || !string.Equals(finalUri.AbsolutePath, expectedEndpoint.AbsolutePath, StringComparison.Ordinal)
            || !string.Equals(finalUri.Query, expectedEndpoint.Query, StringComparison.Ordinal))
        {
            throw new OpenRouterClientException(
                OpenRouterClientErrorKind.Contract,
                "OpenRouter returned a response from an unexpected endpoint.");
        }
    }

    private static OpenRouterClientException CreateStatusException(
        HttpResponseMessage response,
        string operation) => response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new(
                OpenRouterClientErrorKind.Authentication,
                "The OpenRouter key is invalid or revoked."),
            HttpStatusCode.Forbidden => new(
                OpenRouterClientErrorKind.InsufficientPermission,
                "This OpenRouter key lacks permission for this request."),
            HttpStatusCode.TooManyRequests => new(
                OpenRouterClientErrorKind.Throttled,
                "OpenRouter asked TokenUsage to retry later.",
                ReadRetryAfter(response.Headers.RetryAfter)),
            _ => new(
                OpenRouterClientErrorKind.Transient,
                $"OpenRouter could not return the {operation}."),
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

    private static OpenRouterLimitReset? ParseLimitReset(string? value) => value switch
    {
        null => null,
        "daily" => OpenRouterLimitReset.Daily,
        "weekly" => OpenRouterLimitReset.Weekly,
        "monthly" => OpenRouterLimitReset.Monthly,
        _ => throw ContractFailure("key usage"),
    };

    private static OpenRouterClientException ContractFailure(string operation) => new(
        OpenRouterClientErrorKind.Contract,
        $"OpenRouter returned an unsupported {operation} response.");

    private sealed class CreditsDocument
    {
        [JsonPropertyName("data")]
        public CreditsData? Data { get; init; }
    }

    private sealed class CreditsData
    {
        [JsonPropertyName("total_credits")]
        public decimal? TotalCredits { get; init; }

        [JsonPropertyName("total_usage")]
        public decimal? TotalUsage { get; init; }
    }

    private sealed class KeyDocument
    {
        [JsonPropertyName("data")]
        public KeyData? Data { get; init; }
    }

    private sealed class KeyData
    {
        [JsonPropertyName("usage")]
        public decimal? Usage { get; init; }

        [JsonPropertyName("usage_daily")]
        public decimal? DailyUsage { get; init; }

        [JsonPropertyName("usage_weekly")]
        public decimal? WeeklyUsage { get; init; }

        [JsonPropertyName("usage_monthly")]
        public decimal? MonthlyUsage { get; init; }

        [JsonPropertyName("limit")]
        public decimal? Limit { get; init; }

        [JsonPropertyName("limit_remaining")]
        public decimal? LimitRemaining { get; init; }

        [JsonPropertyName("limit_reset")]
        public string? LimitReset { get; init; }

        [JsonPropertyName("is_free_tier")]
        public bool? IsFreeTier { get; init; }
    }
}
