using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WOpenUsage.Providers.VercelAiGateway;

public sealed class VercelGatewayReportClient
{
    private const int MaximumResponseBytes = 1024 * 1024;

    private static readonly Uri ReportEndpoint =
        new("https://ai-gateway.vercel.sh/v1/report", UriKind.Absolute);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly HttpClient _httpClient;

    public VercelGatewayReportClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<VercelGatewayReport> GetDailyReportAsync(
        string apiKey,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ValidateRange(startDate, endDate);
        cancellationToken.ThrowIfCancellationRequested();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildRequestUri(startDate, endDate));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

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

            byte[] content = await ReadBoundedContentAsync(
                response.Content,
                cancellationToken).ConfigureAwait(false);
            ReportDocument? document = JsonSerializer.Deserialize<ReportDocument>(
                content,
                JsonOptions);
            return Map(document, startDate, endDate);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (VercelGatewayReportException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw ContractFailure();
        }
        catch (HttpRequestException)
        {
            throw new VercelGatewayReportException(
                VercelGatewayReportErrorKind.Transient,
                "Vercel AI Gateway could not return the report.");
        }
        catch (OperationCanceledException)
        {
            throw new VercelGatewayReportException(
                VercelGatewayReportErrorKind.Transient,
                "Vercel AI Gateway timed out while reading the report.");
        }
    }

    private static void ValidateRange(DateOnly startDate, DateOnly endDate)
    {
        int inclusiveDays = endDate.DayNumber - startDate.DayNumber + 1;
        if (inclusiveDays <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endDate),
                "The report end date must not precede its start date.");
        }

        if (inclusiveDays > 31)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endDate),
                "A report request cannot cover more than 31 days.");
        }
    }

    private static Uri BuildRequestUri(DateOnly startDate, DateOnly endDate)
    {
        string start = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string end = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var builder = new UriBuilder(ReportEndpoint)
        {
            Query = $"start_date={start}&end_date={end}&group_by=day&date_part=day",
        };
        return builder.Uri;
    }

    private static void ValidateFinalOrigin(Uri? finalUri)
    {
        if (finalUri is null
            || !string.Equals(finalUri.Scheme, ReportEndpoint.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(finalUri.Host, ReportEndpoint.Host, StringComparison.OrdinalIgnoreCase)
            || finalUri.Port != ReportEndpoint.Port)
        {
            throw new VercelGatewayReportException(
                VercelGatewayReportErrorKind.Contract,
                "Vercel AI Gateway returned a response from an unexpected origin.");
        }
    }

    private static VercelGatewayReportException CreateStatusException(
        HttpResponseMessage response) =>
        response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new(
                VercelGatewayReportErrorKind.Authentication,
                "The Vercel AI Gateway key is invalid or revoked."),
            HttpStatusCode.Forbidden => new(
                VercelGatewayReportErrorKind.UnsupportedAccount,
                "This Vercel account cannot use Custom Reporting."),
            HttpStatusCode.TooManyRequests => new(
                VercelGatewayReportErrorKind.Throttled,
                "Vercel AI Gateway asked TokenUsage to retry later.",
                ReadRetryAfter(response.Headers.RetryAfter)),
            _ => new(
                VercelGatewayReportErrorKind.Transient,
                "Vercel AI Gateway could not return the report."),
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
                throw ContractFailure();
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static VercelGatewayReport Map(
        ReportDocument? document,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (document?.Results is null)
        {
            throw ContractFailure();
        }

        var results = new List<VercelGatewayDailyReportRow>(document.Results.Length);
        var observedDays = new HashSet<DateOnly>();
        foreach (ReportRow? row in document.Results)
        {
            if (row is null
                || !DateOnly.TryParseExact(
                    row.Day,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly day)
                || day < startDate
                || day > endDate
                || !observedDays.Add(day)
                || !HasMetric(row)
                || HasNegativeMetric(row))
            {
                throw ContractFailure();
            }

            results.Add(new VercelGatewayDailyReportRow(
                day,
                row.TotalCost,
                row.MarketCost,
                row.SurchargeCost,
                row.GatewayCost,
                row.InputTokens,
                row.OutputTokens,
                row.CachedInputTokens,
                row.CacheCreationInputTokens,
                row.ReasoningTokens,
                row.RequestCount));
        }

        return new VercelGatewayReport(results);
    }

    private static bool HasMetric(ReportRow row) =>
        row.TotalCost is not null
        || row.MarketCost is not null
        || row.SurchargeCost is not null
        || row.GatewayCost is not null
        || row.InputTokens is not null
        || row.OutputTokens is not null
        || row.CachedInputTokens is not null
        || row.CacheCreationInputTokens is not null
        || row.ReasoningTokens is not null
        || row.RequestCount is not null;

    private static bool HasNegativeMetric(ReportRow row) =>
        row.TotalCost < 0
        || row.MarketCost < 0
        || row.SurchargeCost < 0
        || row.GatewayCost < 0
        || row.InputTokens < 0
        || row.OutputTokens < 0
        || row.CachedInputTokens < 0
        || row.CacheCreationInputTokens < 0
        || row.ReasoningTokens < 0
        || row.RequestCount < 0;

    private static VercelGatewayReportException ContractFailure() =>
        new(
            VercelGatewayReportErrorKind.Contract,
            "Vercel AI Gateway returned an unsupported report response.");

    private sealed class ReportDocument
    {
        [JsonPropertyName("results")]
        public ReportRow?[]? Results { get; init; }
    }

    private sealed class ReportRow
    {
        [JsonPropertyName("day")]
        public string? Day { get; init; }

        [JsonPropertyName("total_cost")]
        public decimal? TotalCost { get; init; }

        [JsonPropertyName("market_cost")]
        public decimal? MarketCost { get; init; }

        [JsonPropertyName("surcharge_cost")]
        public decimal? SurchargeCost { get; init; }

        [JsonPropertyName("gateway_cost")]
        public decimal? GatewayCost { get; init; }

        [JsonPropertyName("input_tokens")]
        public long? InputTokens { get; init; }

        [JsonPropertyName("output_tokens")]
        public long? OutputTokens { get; init; }

        [JsonPropertyName("cached_input_tokens")]
        public long? CachedInputTokens { get; init; }

        [JsonPropertyName("cache_creation_input_tokens")]
        public long? CacheCreationInputTokens { get; init; }

        [JsonPropertyName("reasoning_tokens")]
        public long? ReasoningTokens { get; init; }

        [JsonPropertyName("request_count")]
        public long? RequestCount { get; init; }
    }
}
