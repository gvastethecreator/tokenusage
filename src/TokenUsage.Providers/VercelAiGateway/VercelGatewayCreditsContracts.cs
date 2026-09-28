namespace TokenUsage.Providers.VercelAiGateway;

public interface IVercelGatewayCreditsClient
{
    Task<VercelGatewayCredits> GetCreditsAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The team's AI Gateway Credits, from the documented <c>GET /v1/credits</c> endpoint. Both
/// values are team-wide, in USD, and do not depend on the key's own spend.
/// </summary>
public sealed record VercelGatewayCredits(decimal Balance, decimal TotalUsed);

public enum VercelGatewayCreditsErrorKind
{
    Authentication,
    Unavailable,
    Throttled,
    Transient,
    Contract,
}

public sealed class VercelGatewayCreditsException : Exception
{
    public VercelGatewayCreditsException(
        VercelGatewayCreditsErrorKind kind,
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

    public VercelGatewayCreditsErrorKind Kind { get; }

    public TimeSpan? RetryAfter { get; }
}
