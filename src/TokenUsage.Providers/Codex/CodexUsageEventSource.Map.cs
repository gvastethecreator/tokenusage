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
                return CreateObservedFallbackResult(scan);
            }

            await using ICodexQuotaClient client = await _clientFactory
                .CreateAsync(cancellationToken)
                .ConfigureAwait(false);
            await client.HandshakeAsync(cancellationToken).ConfigureAwait(false);
            CodexTokenUsageSnapshot usage = await client
                .ReadTokenUsageAsync(cancellationToken)
                .ConfigureAwait(false);
            UsageEvent[] events = CreateOfficialEvents(
                usage,
                scan,
                out bool usesRecentLocalTotals);
            return events.Length == 0
                ? new UsageSourceReadResult(
                    [],
                    UsageSourceReadStatus.NoData,
                    UsageSourceIssueKind.Empty)
                : usesRecentLocalTotals && scan.Status == UsageSourceReadStatus.Partial
                    ? new UsageSourceReadResult(
                        events,
                        UsageSourceReadStatus.Partial,
                        scan.Issue)
                    : new UsageSourceReadResult(events, UsageSourceReadStatus.Complete);
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
            return CreateObservedFallbackResult(scan);
        }
    }

    private UsageEvent[] CreateOfficialEvents(
        CodexTokenUsageSnapshot usage,
        ScanResult scan,
        out bool usesRecentLocalTotals)
    {
        TimeZoneInfo groupingTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            _groupingTimeZoneId);
        Dictionary<DateOnly, ModelSample[]> samplesByDate = scan.Sessions
            .Where(session => session.Candidate.SampleTokens.Total > 0)
            .GroupBy(session => DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(
                    session.Candidate.Timestamp,
                    groupingTimeZone).DateTime))
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(session => session.Candidate.Model, StringComparer.Ordinal)
                    .Select(group => CreateModelSample(group, useCumulativeTokens: false))
                    .Where(sample => sample.Tokens.Total > 0)
                    .OrderBy(sample => sample.Model, StringComparer.Ordinal)
                    .ToArray());
        Dictionary<DateOnly, ModelSample[]> localTotalsByDate = scan.UsesCheckpoints
            ? scan.RecentSamples
                .GroupBy(sample => sample.Date)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(sample => sample.Sample)
                        .Where(sample => sample.Tokens.Total > 0)
                        .OrderBy(sample => sample.Model, StringComparer.Ordinal)
                        .ToArray())
            : scan.Sessions
                .Where(session => session.Candidate.TotalTokens.Total > 0)
                .GroupBy(session => DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(
                        session.Candidate.Timestamp,
                        groupingTimeZone).DateTime))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .GroupBy(session => session.Candidate.Model, StringComparer.Ordinal)
                        .Select(group => CreateModelSample(group, useCumulativeTokens: true))
                        .Where(sample => sample.Tokens.Total > 0)
                        .OrderBy(sample => sample.Model, StringComparer.Ordinal)
                        .ToArray());

        var tokensByDate = new SortedDictionary<DateOnly, long>();
        // The account history reaches back far beyond the reconciliation window.
        // Those old aggregates carry no model split, double-count tokens the
        // session files already cover, and never leave the store once written,
        // so only the reconciliation window is emitted.
        DateOnly officialCutoff = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), groupingTimeZone).DateTime)
            .AddDays(-UsagePeriodPolicy.ReconciliationDays);
        foreach (CodexUsageDailyBucket bucket in usage.DailyUsageBuckets)
        {
            if (bucket.StartDate < officialCutoff)
            {
                continue;
            }

            tokensByDate[bucket.StartDate] = checked(
                tokensByDate.GetValueOrDefault(bucket.StartDate) + bucket.Tokens);
        }

        var events = new List<UsageEvent>();
        usesRecentLocalTotals = false;
        DateOnly? latestOfficialDate = tokensByDate.Count == 0
            ? null
            : tokensByDate.Keys.Max();
        foreach ((DateOnly date, long totalTokens) in tokensByDate)
        {
            if (date == latestOfficialDate
                && localTotalsByDate.TryGetValue(date, out ModelSample[]? latestLocalSamples)
                && SumTokens(latestLocalSamples) > totalTokens)
            {
                AddLocalEvents(events, date, latestLocalSamples, groupingTimeZone);
                usesRecentLocalTotals = true;
                continue;
            }

            if (totalTokens == 0)
            {
                continue;
            }

            if (!samplesByDate.TryGetValue(date, out ModelSample[]? samples)
                || samples.Length == 0)
            {
                events.Add(CreateOfficialEvent(
                    date,
                    "codex-account",
                    new TokenBreakdown(totalTokens, 0, 0, 0, 0),
                    groupingTimeZone,
                    CostObservation.Unavailable()));
                continue;
            }

            long[] modelTotals = Allocate(
                totalTokens,
                samples.Select(sample => sample.Tokens.Total).ToArray());
            for (int index = 0; index < samples.Length; index++)
            {
                if (modelTotals[index] == 0)
                {
                    continue;
                }

                events.Add(CreateOfficialEvent(
                    date,
                    samples[index].Model,
                    ScaleTokens(samples[index].Tokens, modelTotals[index]),
                    groupingTimeZone,
                    ScaleSampleCost(samples[index], modelTotals[index])));
            }
        }

        foreach ((DateOnly date, ModelSample[] samples) in localTotalsByDate
                     .Where(item => latestOfficialDate is null || item.Key > latestOfficialDate)
                     .OrderBy(item => item.Key))
        {
            AddLocalEvents(events, date, samples, groupingTimeZone);
            usesRecentLocalTotals = true;
        }

        return events.ToArray();
    }

    private void AddLocalEvents(
        List<UsageEvent> events,
        DateOnly date,
        IEnumerable<ModelSample> samples,
        TimeZoneInfo groupingTimeZone)
    {
        foreach (ModelSample sample in samples)
        {
            events.Add(CreateOfficialEvent(
                date,
                sample.Model,
                sample.Tokens,
                groupingTimeZone,
                sample.Cost));
        }
    }

    private UsageEvent CreateOfficialEvent(
        DateOnly date,
        string model,
        TokenBreakdown tokens,
        TimeZoneInfo groupingTimeZone,
        CostObservation cost)
    {
        DateTimeOffset observedAtUtc = _clock.GetUtcNow().ToUniversalTime();
        DateOnly observedLocalDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(observedAtUtc, groupingTimeZone).DateTime);
        DateTimeOffset timestamp;
        if (date == observedLocalDate)
        {
            // Today's provider bucket is cumulative through this refresh. Stamping it at noon
            // can put the event in the future and leave the active reset cycle empty.
            timestamp = observedAtUtc;
        }
        else
        {
            DateTime localNoon = DateTime.SpecifyKind(
                date.ToDateTime(new TimeOnly(12, 0)),
                DateTimeKind.Unspecified);
            timestamp = new DateTimeOffset(
                TimeZoneInfo.ConvertTimeToUtc(localNoon, groupingTimeZone),
                TimeSpan.Zero);
        }

        return new UsageEvent(
            new UsageEventKey(Hash(
                $"codex-account\0{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\0{model}")),
            AgentId,
            new ModelProviderId("openai"),
            CreateModelId(model),
            timestamp,
            _groupingTimeZoneId,
            tokens,
            cost,
            ParserVersion,
            cost.Kind == CostKind.CatalogEstimated
                ? CoverageKind.Partial
                : CoverageKind.Unpriced);
    }

    private static ModelSample CreateModelSample(
        IGrouping<string, ScannedSession> modelGroup,
        bool useCumulativeTokens)
    {
        ScannedSession[] sessions = modelGroup.ToArray();
        TokenBreakdown tokens = SumTokens(
            sessions.Select(value => useCumulativeTokens
                ? value.Candidate.TotalTokens
                : value.Candidate.SampleTokens));
        CostObservation[] costs = sessions
            .Select(value => CodexPricingCatalog.Resolve(
                modelGroup.Key,
                useCumulativeTokens
                    ? value.Candidate.TotalTokens
                    : value.Candidate.SampleTokens,
                value.Candidate.Timestamp))
            .ToArray();
        if (costs.Any(cost => cost.Kind != CostKind.CatalogEstimated))
        {
            return new ModelSample(modelGroup.Key, tokens, CostObservation.Unavailable());
        }

        decimal totalCost = costs.Sum(cost => cost.EstimatedCostUsd ?? 0m);
        return new ModelSample(
            modelGroup.Key,
            tokens,
            CostObservation.CatalogEstimated(
                decimal.Round(totalCost, 6, MidpointRounding.AwayFromZero),
                CodexPricingCatalog.Version,
            costs[0].ExactPriceMatch!));
    }

    private static ModelSample CreateModelSample(string model, TokenBreakdown tokens) =>
        new(model, tokens, CodexPricingCatalog.Resolve(model, tokens));

    private static long SumTokens(IEnumerable<ModelSample> samples)
    {
        long total = 0;
        foreach (ModelSample sample in samples)
        {
            total = checked(total + sample.Tokens.Total);
        }

        return total;
    }

    private static CostObservation ScaleSampleCost(ModelSample sample, long totalTokens)
    {
        if (sample.Cost.Kind != CostKind.CatalogEstimated
            || sample.Cost.EstimatedCostUsd is not decimal sampleCost
            || sample.Tokens.Total == 0)
        {
            return CostObservation.Unavailable();
        }

        decimal scaled = decimal.Round(
            sampleCost * totalTokens / sample.Tokens.Total,
            6,
            MidpointRounding.AwayFromZero);
        return CostObservation.CatalogEstimated(
            scaled,
            sample.Cost.CatalogVersion!,
            sample.Cost.ExactPriceMatch!);
    }

    private static TokenBreakdown ScaleTokens(TokenBreakdown sample, long total)
    {
        long[] values = Allocate(
            total,
            [
                sample.Input,
                sample.Output,
                sample.Reasoning,
                sample.CacheRead,
                sample.CacheWrite,
            ]);
        return new TokenBreakdown(values[0], values[1], values[2], values[3], values[4]);
    }

    private static long[] Allocate(long total, long[] weights)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        if (weights.Length == 0 || weights.Any(weight => weight < 0))
        {
            throw new ArgumentException("Allocation weights must be non-negative.", nameof(weights));
        }

        long weightTotal = 0;
        foreach (long weight in weights)
        {
            weightTotal = checked(weightTotal + weight);
        }
        if (weightTotal == 0)
        {
            var emptyWeights = new long[weights.Length];
            emptyWeights[0] = total;
            return emptyWeights;
        }

        var result = new long[weights.Length];
        var remainders = new decimal[weights.Length];
        long allocated = 0;
        for (int index = 0; index < weights.Length; index++)
        {
            decimal exact = total * ((decimal)weights[index] / weightTotal);
            result[index] = decimal.ToInt64(decimal.Truncate(exact));
            remainders[index] = exact - result[index];
            allocated = checked(allocated + result[index]);
        }

        long remaining = total - allocated;
        foreach (int index in Enumerable.Range(0, weights.Length)
                     .OrderByDescending(index => remainders[index])
                     .ThenBy(index => index)
                     .Take(checked((int)remaining)))
        {
            result[index]++;
        }

        return result;
    }

    private static TokenBreakdown SumTokens(IEnumerable<TokenBreakdown> values)
    {
        long input = 0;
        long output = 0;
        long reasoning = 0;
        long cacheRead = 0;
        long cacheWrite = 0;
        foreach (TokenBreakdown value in values)
        {
            input = checked(input + value.Input);
            output = checked(output + value.Output);
            reasoning = checked(reasoning + value.Reasoning);
            cacheRead = checked(cacheRead + value.CacheRead);
            cacheWrite = checked(cacheWrite + value.CacheWrite);
        }

        return new TokenBreakdown(input, output, reasoning, cacheRead, cacheWrite);
    }

    private UsageSourceReadResult CreateFallbackResult(ScanResult scan) =>
        new(
            scan.Sessions
                .Select(session => CreateEvent(
                    session.SessionIdentity,
                    session.Candidate,
                    session.Candidate.TotalTokens))
                .ToArray(),
            scan.Status,
            scan.Issue);

    private UsageSourceReadResult CreateObservedFallbackResult(ScanResult scan)
    {
        UsageEvent[] events = scan.Sessions
            .Select(session => CreateEvent(
                $"sample\0{session.SessionIdentity}",
                session.Candidate,
                session.Candidate.SampleTokens))
            .ToArray();
        return events.Length == 0
            ? new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                scan.Issue is UsageSourceIssueKind.RootUnavailable
                    ? UsageSourceIssueKind.RootUnavailable
                    : UsageSourceIssueKind.Empty)
            : new UsageSourceReadResult(
                events,
                UsageSourceReadStatus.Partial,
                scan.Issue is UsageSourceIssueKind.UnsupportedSchema
                    ? UsageSourceIssueKind.UnsupportedSchema
                    : UsageSourceIssueKind.PartialScan);
    }

    private UsageEvent CreateEvent(
        string sessionIdentity,
        Candidate candidate,
        TokenBreakdown tokens)
    {
        CostObservation cost = CodexPricingCatalog.Resolve(candidate.Model, tokens);
        return new UsageEvent(
            new UsageEventKey(Hash($"codex\0{sessionIdentity}")),
            AgentId,
            new ModelProviderId("openai"),
            CreateModelId(candidate.Model),
            candidate.Timestamp,
            _groupingTimeZoneId,
            tokens,
            cost,
            ParserVersion,
            cost.Kind == CostKind.CatalogEstimated
                ? CoverageKind.Partial
                : CoverageKind.Unpriced);
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
        string.IsNullOrWhiteSpace(model) ? null : model.Trim().ToLowerInvariant();

    private static ModelId CreateModelId(string model) => ModelIdentity.ToModelId(model);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
