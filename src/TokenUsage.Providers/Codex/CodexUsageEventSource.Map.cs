using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Providers.Codex;

public sealed partial class CodexUsageEventSource
{
    private static bool ProcessLine(
        ReadOnlyMemory<byte> utf8,
        ref string? currentModel,
        ref Candidate? latest,
        LocalScanState state,
        bool markSchemaFailures,
        bool captureResumeCarry,
        ref TokenBreakdown? resumeCarry)
    {
        if (utf8.Length == 0)
        {
            return true;
        }

        if (utf8.Length > state.MaximumLineBytes)
        {
            state.MarkPartial();
            return false;
        }

        ReadOnlySpan<byte> bytes = utf8.Span;
        if (bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF)
        {
            utf8 = utf8[3..];
            bytes = utf8.Span;
        }

        bool mightBeContext = bytes.IndexOf("turn_context"u8) >= 0;
        bool mightBeUsage = bytes.IndexOf("token_count"u8) >= 0;
        if (!mightBeContext && !mightBeUsage)
        {
            return true;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8);
            JsonElement root = document.RootElement;
            if (!TryGetString(root, "type", out string? recordType)
                || !root.TryGetProperty("payload", out JsonElement payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return MarkSchemaFailure(state, markSchemaFailures);
            }

            if (string.Equals(recordType, "turn_context", StringComparison.Ordinal))
            {
                if (!TryGetString(payload, "model", out string? model))
                {
                    return MarkSchemaFailure(state, markSchemaFailures);
                }

                currentModel = NormalizeModel(model);
                return true;
            }

            if (!string.Equals(recordType, "event_msg", StringComparison.Ordinal)
                || !TryGetString(payload, "type", out string? eventType)
                || !string.Equals(eventType, "token_count", StringComparison.Ordinal))
            {
                return true;
            }

            if (!payload.TryGetProperty("info", out JsonElement info)
                || info.ValueKind is JsonValueKind.Null)
            {
                return true;
            }

            if (info.ValueKind != JsonValueKind.Object)
            {
                return MarkSchemaFailure(state, markSchemaFailures);
            }

            bool hasCumulative = info.TryGetProperty(
                                     "total_token_usage",
                                     out JsonElement cumulativeElement)
                                 && cumulativeElement.ValueKind == JsonValueKind.Object;
            bool hasLast = info.TryGetProperty(
                               "last_token_usage",
                               out JsonElement lastElement)
                           && lastElement.ValueKind == JsonValueKind.Object;
            if (!hasCumulative && !hasLast)
            {
                return info.TryGetProperty("total_token_usage", out _)
                       || info.TryGetProperty("last_token_usage", out _)
                    ? MarkSchemaFailure(state, markSchemaFailures)
                    : true;
            }

            if (!TryGetUtcTimestamp(root, "timestamp", out DateTimeOffset timestamp))
            {
                return MarkSchemaFailure(state, markSchemaFailures);
            }

            TokenBreakdown? cumulative = null;
            TokenBreakdown? last = null;
            bool cumulativeIsValid = hasCumulative
                && TryReadTokenBreakdown(cumulativeElement, out cumulative);
            bool lastIsValid = hasLast
                && TryReadTokenBreakdown(lastElement, out last);
            if (!cumulativeIsValid && !lastIsValid)
            {
                return MarkSchemaFailure(state, markSchemaFailures);
            }

            if ((hasCumulative && !cumulativeIsValid)
                || (hasLast && !lastIsValid))
            {
                state.MarkPartial();
            }

            TokenBreakdown current = cumulative ?? last!;
            if (captureResumeCarry && resumeCarry is null && cumulativeIsValid && lastIsValid)
            {
                resumeCarry = CanSubtract(cumulative!, last!)
                    ? Difference(cumulative!, last!)
                    : new TokenBreakdown(0, 0, 0, 0, 0);
            }

            TokenBreakdown total = resumeCarry is not null && CanSubtract(current, resumeCarry)
                ? Difference(current, resumeCarry)
                : current;
            TokenBreakdown sample = last ?? total;

            latest = new Candidate(
                timestamp,
                NormalizeModel(currentModel) ?? "unknown",
                total,
                sample);
            if (!hasCumulative)
            {
                state.MarkPartial();
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException
                                           or ArgumentException
                                           or InvalidOperationException
                                           or OverflowException)
        {
            return MarkSchemaFailure(state, markSchemaFailures);
        }
    }

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

    private static bool MarkSchemaFailure(LocalScanState state, bool mark)
    {
        if (mark)
        {
            state.UnsupportedSchema = true;
        }

        return false;
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
