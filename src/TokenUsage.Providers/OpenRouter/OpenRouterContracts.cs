namespace TokenUsage.Providers.OpenRouter;

public interface IOpenRouterClient
{
    /// <summary>
    /// Account credits. OpenRouter answers this only for a management key, so the key-only
    /// runtime does not call it.
    /// </summary>
    Task<OpenRouterCredits> GetCreditsAsync(
        string managementKey,
        CancellationToken cancellationToken = default);

    Task<OpenRouterKeyUsage> GetKeyUsageAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the OpenRouter key the user saved in TokenUsage. Null means no key is saved.
/// </summary>
public interface IOpenRouterKeySource
{
    Task<string?> ReadApiKeyAsync(CancellationToken cancellationToken = default);
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

/// <summary>
/// How the key limit resets. Null on <see cref="OpenRouterKeyUsage.LimitReset"/> means the
/// limit never resets. <see cref="Unknown"/> is a value OpenRouter added after this contract:
/// the limit is still shown, without a reset time.
/// </summary>
public enum OpenRouterLimitReset
{
    Daily,
    Weekly,
    Monthly,
    Unknown,
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
