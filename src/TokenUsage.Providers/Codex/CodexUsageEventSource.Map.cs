using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Codex;

public sealed partial class CodexUsageEventSource
{
    private static bool TryReadTokenBreakdown(
        JsonElement usage,
        out TokenBreakdown? tokens)
    {
        tokens = null;
        if (!TryGetNonNegativeInt64(usage, "input_tokens", out long input)
            || !TryGetNonNegativeInt64(usage, "output_tokens", out long output)
            || !TryGetOptionalNonNegativeInt64(
                usage,
                "cached_input_tokens",
                out long cacheRead)
            || !TryGetOptionalNonNegativeInt64(
                usage,
                "cache_write_input_tokens",
                out long cacheWrite)
            || !TryGetOptionalNonNegativeInt64(
                usage,
                "reasoning_output_tokens",
                out long reasoning)
            || checked(cacheRead + cacheWrite) > input
            || reasoning > output)
        {
            return false;
        }

        if (usage.TryGetProperty("total_tokens", out JsonElement totalElement)
            && (!totalElement.TryGetInt64(out long total)
                || total < 0
                || total != checked(input + output)))
        {
            return false;
        }

        tokens = new TokenBreakdown(
            input - cacheRead - cacheWrite,
            output - reasoning,
            reasoning,
            cacheRead,
            cacheWrite);
        return true;
    }

    private async Task<UsageSourceReadResult> ReadOfficialUsageAsync(
        ScanResult scan,
        CancellationToken cancellationToken)
    {
        try
        {
            CodexClientAvailability availability = await _clientFactory!
                .DetectAsync(cancellationToken)
                .ConfigureAwait(false);
            if (availability != CodexClientAvailability.Available)
            {
                return CreateFallbackResult(scan);
            }

            await using ICodexQuotaClient client = await _clientFactory
                .CreateAsync(cancellationToken)
                .ConfigureAwait(false);
            await client.HandshakeAsync(cancellationToken).ConfigureAwait(false);
            CodexTokenUsageSnapshot usage = await client
                .ReadTokenUsageAsync(cancellationToken)
                .ConfigureAwait(false);
            UsageSourceReadResult local = CreateFallbackResult(scan);
            return local with
            {
                AccountAggregates = usage.DailyUsageBuckets
                    .GroupBy(bucket => bucket.StartDate)
                    .Select(group => new AccountUsageAggregate(AgentId, group.Key,
                        group.Sum(bucket => bucket.Tokens), _clock.GetUtcNow().ToUniversalTime())).ToArray(),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is CodexClientUnavailableException
                                           or CodexProtocolException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or ObjectDisposedException)
        {
            return CreateFallbackResult(scan);
        }
    }

    private static UsageSourceReadResult CreateFallbackResult(ScanResult scan) =>
        new(
            scan.Observations,
            scan.Status,
            scan.Issue);

    private UsageEvent CreateObservationEvent(CodexNumericObservation observation)
    {
        CostObservation cost = observation.Precision == UsageTimePrecision.Timestamp && observation.Tier is null or "standard"
            ? CodexPricingCatalog.Resolve(observation.Model, observation.Tokens, observation.Timestamp)
            : CostObservation.Unavailable();
        return new UsageEvent(new UsageEventKey(observation.Key), AgentId, new ModelProviderId("openai"),
            CreateModelId(observation.Model), observation.Timestamp, _groupingTimeZoneId,
            observation.Tokens, cost, ParserVersion,
            cost.Kind == CostKind.Unavailable ? CoverageKind.Unpriced : CoverageKind.Partial,
            observation.Precision, observation.IntervalStart,
            observation.ObservedModel is null ? null : new ModelId(observation.ObservedModel), observation.Effort, observation.Tier);
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out JsonElement property)
               && property.ValueKind == JsonValueKind.String
               && !string.IsNullOrWhiteSpace(value = property.GetString());
    }

    private static bool TryGetUtcTimestamp(
        JsonElement element,
        string propertyName,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        return TryGetString(element, propertyName, out string? text)
               && DateTimeOffset.TryParse(
                   text,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out timestamp)
               && timestamp.Offset == TimeSpan.Zero;
    }

    private static bool TryGetNonNegativeInt64(
        JsonElement element,
        string propertyName,
        out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out JsonElement property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt64(out value)
               && value >= 0;
    }

    private static bool TryGetOptionalNonNegativeInt64(
        JsonElement element,
        string propertyName,
        out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.Number
               && property.TryGetInt64(out value)
               && value >= 0;
    }

    private static string? NormalizeModel(string? model) =>
        string.IsNullOrWhiteSpace(model) ? null : ModelIdentity.ForStorage(model);

    private static ModelId CreateModelId(string model) => ModelIdentity.ToModelId(model);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
