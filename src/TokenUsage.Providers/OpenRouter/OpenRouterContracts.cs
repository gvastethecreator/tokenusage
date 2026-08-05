namespace TokenUsage.Providers.OpenRouter;

public interface IOpenRouterClient
{
    Task<OpenRouterCredits> GetCreditsAsync(
        string managementKey,
        CancellationToken cancellationToken = default);

    Task<OpenRouterKeyUsage> GetKeyUsageAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}

public sealed record OpenRouterCredits(
    decimal TotalCredits,
    decimal TotalUsage);

public sealed record OpenRouterKeyUsage(
    decimal Usage,
    decimal DailyUsage,
    decimal WeeklyUsage,
    decimal MonthlyUsage,
    decimal? Limit,
    decimal? LimitRemaining,
    OpenRouterLimitReset? LimitReset,
    bool IsFreeTier);

public enum OpenRouterLimitReset
{
    Daily,
    Weekly,
    Monthly,
}

public enum OpenRouterClientErrorKind
{
    Authentication,
    InsufficientPermission,
    Throttled,
    Transient,
    Contract,
}

public sealed class OpenRouterClientException : Exception
{
    public OpenRouterClientException(
        OpenRouterClientErrorKind kind,
        string message,
        TimeSpan? retryAfter = null)
        : base(message)
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

    public OpenRouterClientErrorKind Kind { get; }

    public TimeSpan? RetryAfter { get; }
}
