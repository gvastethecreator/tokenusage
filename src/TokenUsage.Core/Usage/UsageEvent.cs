using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public enum CostKind
{
    ProviderReported,
    CatalogEstimated,
    Unavailable,
}

public sealed record UsageEventKey
{
    public UsageEventKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "Usage event keys must be lowercase SHA-256 hex values.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record AgentId
{
    public AgentId(string value) => Value = StableId.Validate(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record ModelProviderId
{
    public ModelProviderId(string value) => Value = StableId.Validate(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record ModelId
{
    public ModelId(string value) => Value = StableId.Validate(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record TokenBreakdown
{
    public TokenBreakdown(
        long input,
        long output,
        long reasoning,
        long cacheRead,
        long cacheWrite)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(input);
        ArgumentOutOfRangeException.ThrowIfNegative(output);
        ArgumentOutOfRangeException.ThrowIfNegative(reasoning);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheRead);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheWrite);

        Input = input;
        Output = output;
        Reasoning = reasoning;
        CacheRead = cacheRead;
        CacheWrite = cacheWrite;
    }

    public long Input { get; }

    public long Output { get; }

    public long Reasoning { get; }

    public long CacheRead { get; }

    public long CacheWrite { get; }

    public long Total => checked(Input + Output + Reasoning + CacheRead + CacheWrite);
}

public abstract record CostObservation
{
    private CostObservation(
        CostKind kind,
        decimal? reportedCostUsd,
        decimal? estimatedCostUsd,
        string? catalogVersion,
        string? exactPriceMatch)
    {
        Kind = kind;
        ReportedCostUsd = reportedCostUsd;
        EstimatedCostUsd = estimatedCostUsd;
        CatalogVersion = catalogVersion;
        ExactPriceMatch = exactPriceMatch;
    }

    public CostKind Kind { get; }

    public decimal? ReportedCostUsd { get; }

    public decimal? EstimatedCostUsd { get; }

    public string? CatalogVersion { get; }

    public string? ExactPriceMatch { get; }

    public static CostObservation ProviderReported(decimal amountUsd)
    {
        ValidateAmount(amountUsd);
        return new ProviderReportedCost(amountUsd);
    }

    public static CostObservation CatalogEstimated(
        decimal amountUsd,
        string catalogVersion,
        string exactPriceMatch)
    {
        ValidateAmount(amountUsd);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(exactPriceMatch);
        return new CatalogEstimatedCost(amountUsd, catalogVersion, exactPriceMatch);
    }

    public static CostObservation Unavailable() => new UnavailableCost();

    private static void ValidateAmount(decimal amountUsd) =>
        ValidateUsdAmount(amountUsd);

    private static void ValidateUsdAmount(decimal amountUsd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amountUsd);
        if (decimal.Round(amountUsd, 6) != amountUsd)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amountUsd),
                "USD amounts support at most six decimal places.");
        }

        if (amountUsd > long.MaxValue / 1_000_000m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amountUsd),
                "The USD amount exceeds the supported range.");
        }
    }

    private sealed record ProviderReportedCost(decimal AmountUsd)
        : CostObservation(CostKind.ProviderReported, AmountUsd, null, null, null);

    private sealed record CatalogEstimatedCost(
        decimal AmountUsd,
        string Version,
        string PriceMatch)
        : CostObservation(
            CostKind.CatalogEstimated,
            null,
            AmountUsd,
            Version,
            PriceMatch);

    private sealed record UnavailableCost()
        : CostObservation(CostKind.Unavailable, null, null, null, null);
}

public sealed record UsageEvent
{
    public UsageEvent(
        UsageEventKey eventKey,
        AgentId agentId,
        ModelProviderId? modelProviderId,
        ModelId modelId,
        DateTimeOffset occurredAtUtc,
        string groupingTimeZoneId,
        TokenBreakdown tokens,
        CostObservation cost,
        string parserVersion,
        CoverageKind coverage)
    {
        UtcTimestamp.Require(occurredAtUtc, nameof(occurredAtUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parserVersion);
        if (!string.Equals(groupingTimeZoneId, groupingTimeZoneId.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The grouping time-zone ID cannot start or end with whitespace.",
                nameof(groupingTimeZoneId));
        }

        if (!Enum.IsDefined(coverage))
        {
            throw new ArgumentOutOfRangeException(nameof(coverage));
        }

        if (coverage == CoverageKind.Unpriced && cost.Kind != CostKind.Unavailable)
        {
            throw new ArgumentException(
                "Unpriced events cannot contain a reported or estimated cost.",
                nameof(coverage));
        }

        EventKey = eventKey ?? throw new ArgumentNullException(nameof(eventKey));
        AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        ModelProviderId = modelProviderId;
        ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
        OccurredAtUtc = occurredAtUtc;
        GroupingTimeZoneId = groupingTimeZoneId;
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        Cost = cost ?? throw new ArgumentNullException(nameof(cost));
        ParserVersion = parserVersion;
        Coverage = coverage;
    }

    public UsageEventKey EventKey { get; }

    public AgentId AgentId { get; }

    public ModelProviderId? ModelProviderId { get; }

    public ModelId ModelId { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public string GroupingTimeZoneId { get; }

    public TokenBreakdown Tokens { get; }

    public CostObservation Cost { get; }

    public string ParserVersion { get; }

    public CoverageKind Coverage { get; }
}
