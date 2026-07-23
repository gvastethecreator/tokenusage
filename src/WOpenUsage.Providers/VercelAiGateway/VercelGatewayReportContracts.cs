namespace WOpenUsage.Providers.VercelAiGateway;

public enum VercelGatewayReportErrorKind
{
    Authentication,
    UnsupportedAccount,
    Throttled,
    Transient,
    Contract,
}

public sealed class VercelGatewayReportException : Exception
{
    public VercelGatewayReportException(
        VercelGatewayReportErrorKind kind,
        string message,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (retryAfter < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfter));
        }

        Kind = kind;
        RetryAfter = retryAfter;
    }

    public VercelGatewayReportErrorKind Kind { get; }

    public TimeSpan? RetryAfter { get; }
}

public sealed record VercelGatewayDailyReportRow(
    DateOnly Day,
    decimal? TotalCost,
    decimal? MarketCost,
    decimal? SurchargeCost,
    decimal? GatewayCost,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? CacheCreationInputTokens,
    long? ReasoningTokens,
    long? RequestCount);

public sealed class VercelGatewayReport
{
    public VercelGatewayReport(IEnumerable<VercelGatewayDailyReportRow> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        VercelGatewayDailyReportRow[] rows = results.ToArray();
        if (rows.Any(row => row is null))
        {
            throw new ArgumentException("Report rows cannot contain null values.", nameof(results));
        }

        Results = Array.AsReadOnly(rows);
    }

    public IReadOnlyList<VercelGatewayDailyReportRow> Results { get; }
}
