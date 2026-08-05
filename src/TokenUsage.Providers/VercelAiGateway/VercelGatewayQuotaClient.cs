using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WOpenUsage.Providers.VercelAiGateway;

public sealed class VercelGatewayQuotaClient : IVercelGatewayQuotaClient
{
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly Uri QuotaEndpoint =
        new("https://ai-gateway.vercel.sh/v1/quotas", UriKind.Absolute);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly HttpClient _httpClient;

    public VercelGatewayQuotaClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<VercelGatewayQuotaLookupResult> GetQuotaAsync(
        string apiKey,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        VercelGatewayKeyIdValidation.Validate(keyId, nameof(keyId));
        cancellationToken.ThrowIfCancellationRequested();

        string entityId = VercelGatewayKeyIdValidation.EntityPrefix + keyId;
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(entityId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            ValidateFinalOrigin(response.RequestMessage?.RequestUri);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                byte[] notFoundContent = await ReadBoundedContentAsync(
                    response.Content,
                    cancellationToken).ConfigureAwait(false);
                return ParseNotFound(notFoundContent);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw CreateStatusException(response);
            }

            byte[] content = await ReadBoundedContentAsync(
                response.Content,
                cancellationToken).ConfigureAwait(false);
            QuotaDocument? document = JsonSerializer.Deserialize<QuotaDocument>(content, JsonOptions);
            return new VercelGatewayQuotaLookupResult.Found(Map(document, entityId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (VercelGatewayQuotaException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw ContractFailure();
        }
        catch (HttpRequestException)
        {
            throw new VercelGatewayQuotaException(
                VercelGatewayQuotaErrorKind.Transient,
                "Vercel AI Gateway could not return the API key budget.");
        }
        catch (OperationCanceledException)
        {
            throw new VercelGatewayQuotaException(
                VercelGatewayQuotaErrorKind.Transient,
                "Vercel AI Gateway timed out while reading the API key budget.");
        }
    }

    private static Uri BuildRequestUri(string entityId)
    {
        var builder = new UriBuilder(QuotaEndpoint)
        {
            Query = "quotaEntityId=" + Uri.EscapeDataString(entityId),
        };
        return builder.Uri;
    }

    private static void ValidateFinalOrigin(Uri? finalUri)
    {
        if (finalUri is null
            || !string.Equals(finalUri.Scheme, QuotaEndpoint.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(finalUri.Host, QuotaEndpoint.Host, StringComparison.OrdinalIgnoreCase)
            || finalUri.Port != QuotaEndpoint.Port)
        {
            throw new VercelGatewayQuotaException(
                VercelGatewayQuotaErrorKind.Contract,
                "Vercel AI Gateway returned a response from an unexpected origin.");
        }
    }

    private static VercelGatewayQuotaLookupResult.NoBudget ParseNotFound(byte[] content)
    {
        ErrorDocument? document = JsonSerializer.Deserialize<ErrorDocument>(content, JsonOptions);
        if (!string.Equals(document?.Error, "Quota not found", StringComparison.Ordinal))
        {
            throw ContractFailure();
        }

        return VercelGatewayQuotaLookupResult.NoBudget.Instance;
    }

    private static VercelGatewayQuota Map(QuotaDocument? document, string expectedEntityId)
    {
        if (document is null
            || !string.Equals(document.QuotaEntityId, expectedEntityId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.ApiKeyName)
            || document.LimitAmount is null or < 1
            || document.CurrentSpend is null or < 0
            || document.Active is null
            || !TryMapRefreshPeriod(document.RefreshPeriod, out VercelGatewayQuotaRefreshPeriod period))
        {
            throw ContractFailure();
        }

        decimal limit = document.LimitAmount.Value;
        decimal spend = document.CurrentSpend.Value;
        return new VercelGatewayQuota(
            expectedEntityId,
            document.ApiKeyName,
            limit,
            spend,
            Math.Max(0, limit - spend),
            period,
            document.Active.Value);
    }

    private static bool TryMapRefreshPeriod(
        string? value,
        out VercelGatewayQuotaRefreshPeriod period)
    {
        period = value switch
        {
            "daily" => VercelGatewayQuotaRefreshPeriod.Daily,
            "weekly" => VercelGatewayQuotaRefreshPeriod.Weekly,
            "monthly" => VercelGatewayQuotaRefreshPeriod.Monthly,
            "none" => VercelGatewayQuotaRefreshPeriod.None,
            _ => default,
        };
        return value is "daily" or "weekly" or "monthly" or "none";
    }

    private static VercelGatewayQuotaException CreateStatusException(
        HttpResponseMessage response) =>
        response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new(
                VercelGatewayQuotaErrorKind.Authentication,
                "The Vercel AI Gateway key is invalid or revoked."),
            HttpStatusCode.Forbidden => new(
                VercelGatewayQuotaErrorKind.UnsupportedAccount,
                "This Vercel account cannot return API key budgets."),
            HttpStatusCode.TooManyRequests => new(
                VercelGatewayQuotaErrorKind.Throttled,
                "Vercel AI Gateway asked TokenUsage to retry later.",
                ReadRetryAfter(response.Headers.RetryAfter)),
            _ => new(
                VercelGatewayQuotaErrorKind.Transient,
                "Vercel AI Gateway could not return the API key budget."),
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
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw ContractFailure();
        }

        await using Stream source = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > MaximumResponseBytes)
            {
                throw ContractFailure();
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static VercelGatewayQuotaException ContractFailure() =>
        new(
            VercelGatewayQuotaErrorKind.Contract,
            "Vercel AI Gateway returned an unsupported API key budget response.");

    private sealed class QuotaDocument
    {
        [JsonPropertyName("quotaEntityId")]
        public string? QuotaEntityId { get; init; }

        [JsonPropertyName("apiKeyName")]
        public string? ApiKeyName { get; init; }

        [JsonPropertyName("limitAmount")]
        public decimal? LimitAmount { get; init; }

        [JsonPropertyName("currentSpend")]
        public decimal? CurrentSpend { get; init; }

        [JsonPropertyName("refreshPeriod")]
        public string? RefreshPeriod { get; init; }

        [JsonPropertyName("active")]
        public bool? Active { get; init; }
    }

    private sealed class ErrorDocument
    {
        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }
}
