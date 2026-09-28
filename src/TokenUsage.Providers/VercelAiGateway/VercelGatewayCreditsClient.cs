using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TokenUsage.Providers.VercelAiGateway;

/// <summary>
/// Reads <c>GET https://ai-gateway.vercel.sh/v1/credits</c>. The documented response is
/// <c>{"balance":"95.50","total_used":"4.50"}</c>: both amounts are decimal strings in USD.
/// </summary>
public sealed class VercelGatewayCreditsClient : IVercelGatewayCreditsClient
{
    private const int MaximumResponseBytes = 16 * 1024;

    private static readonly Uri CreditsEndpoint =
        new("https://ai-gateway.vercel.sh/v1/credits", UriKind.Absolute);

    private readonly HttpClient _httpClient;

    public VercelGatewayCreditsClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<VercelGatewayCredits> GetCreditsAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        cancellationToken.ThrowIfCancellationRequested();

        using var request = new HttpRequestMessage(HttpMethod.Get, CreditsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            ValidateFinalOrigin(response.RequestMessage?.RequestUri);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateStatusException(response);
            }

            byte[] content = await ProviderHttpResponse.ReadBoundedAsync(
                response.Content,
                MaximumResponseBytes,
                ContractFailure,
                cancellationToken).ConfigureAwait(false);
            return Parse(content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (VercelGatewayCreditsException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw ContractFailure();
        }
        catch (HttpRequestException)
        {
            throw new VercelGatewayCreditsException(
                VercelGatewayCreditsErrorKind.Transient,
                "Vercel AI Gateway could not return the credit balance.");
        }
        catch (IOException)
        {
            throw new VercelGatewayCreditsException(
                VercelGatewayCreditsErrorKind.Transient,
                "Vercel AI Gateway could not return the credit balance.");
        }
        catch (OperationCanceledException)
        {
            throw new VercelGatewayCreditsException(
                VercelGatewayCreditsErrorKind.Transient,
                "Vercel AI Gateway timed out while reading the credit balance.");
        }
    }

    private static VercelGatewayCredits Parse(byte[] content)
    {
        using JsonDocument document = JsonDocument.Parse(content);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !TryReadAmount(root, "balance", out decimal balance)
            || !TryReadAmount(root, "total_used", out decimal totalUsed)
            || totalUsed < 0m)
        {
            throw ContractFailure();
        }

        return new VercelGatewayCredits(balance, totalUsed);
    }

    /// <summary>
    /// The contract sends each amount as a decimal string. A JSON number is read the same way,
    /// so a later switch to numbers keeps working; anything else is a contract failure.
    /// </summary>
    private static bool TryReadAmount(JsonElement root, string name, out decimal value)
    {
        value = 0m;
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => decimal.TryParse(
                element.GetString(),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value),
            JsonValueKind.Number => element.TryGetDecimal(out value),
            _ => false,
        };
    }

    private static void ValidateFinalOrigin(Uri? finalUri)
    {
        if (finalUri is null
            || !string.Equals(finalUri.Scheme, CreditsEndpoint.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(finalUri.Host, CreditsEndpoint.Host, StringComparison.OrdinalIgnoreCase)
            || finalUri.Port != CreditsEndpoint.Port)
        {
            throw new VercelGatewayCreditsException(
                VercelGatewayCreditsErrorKind.Contract,
                "Vercel AI Gateway returned a response from an unexpected origin.");
        }
    }

    private static VercelGatewayCreditsException CreateStatusException(
        HttpResponseMessage response) =>
        response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new(
                VercelGatewayCreditsErrorKind.Authentication,
                "The Vercel AI Gateway key is invalid or revoked."),
            HttpStatusCode.Forbidden or HttpStatusCode.NotFound => new(
                VercelGatewayCreditsErrorKind.Unavailable,
                "Vercel AI Gateway did not return a credit balance for this key."),
            HttpStatusCode.TooManyRequests => new(
                VercelGatewayCreditsErrorKind.Throttled,
                "Vercel AI Gateway asked TokenUsage to retry later.",
                ProviderHttpResponse.ReadRetryAfter(response.Headers.RetryAfter)),
            _ => new(
                VercelGatewayCreditsErrorKind.Transient,
                "Vercel AI Gateway could not return the credit balance."),
        };

    private static VercelGatewayCreditsException ContractFailure() =>
        new(
            VercelGatewayCreditsErrorKind.Contract,
            "Vercel AI Gateway returned an unsupported credit balance response.");
}
