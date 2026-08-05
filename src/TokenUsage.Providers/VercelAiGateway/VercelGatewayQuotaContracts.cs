namespace WOpenUsage.Providers.VercelAiGateway;

public interface IVercelGatewayQuotaClient
{
    Task<VercelGatewayQuotaLookupResult> GetQuotaAsync(
        string apiKey,
        string keyId,
        CancellationToken cancellationToken = default);
}

public enum VercelGatewayQuotaErrorKind
{
    Authentication,
    UnsupportedAccount,
    Throttled,
    Transient,
    Contract,
}

public sealed class VercelGatewayQuotaException : Exception
{
    public VercelGatewayQuotaException(
        VercelGatewayQuotaErrorKind kind,
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

    public VercelGatewayQuotaErrorKind Kind { get; }

    public TimeSpan? RetryAfter { get; }
}

public enum VercelGatewayQuotaRefreshPeriod
{
    Daily,
    Weekly,
    Monthly,
    None,
}

public sealed record VercelGatewayQuota(
    string QuotaEntityId,
    string ApiKeyName,
    decimal LimitAmount,
    decimal CurrentSpend,
    decimal RemainingAmount,
    VercelGatewayQuotaRefreshPeriod RefreshPeriod,
    bool Active);

public abstract record VercelGatewayQuotaLookupResult
{
    private VercelGatewayQuotaLookupResult()
    {
    }

    public sealed record Found(VercelGatewayQuota Quota) : VercelGatewayQuotaLookupResult;

    public sealed record NoBudget : VercelGatewayQuotaLookupResult
    {
        private NoBudget()
        {
        }

        public static NoBudget Instance { get; } = new();
    }
}

internal static class VercelGatewayKeyIdValidation
{
    public const string EntityPrefix = "api_key_id_";

    public static void Validate(string keyId, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId, paramName);
        if (keyId.Length > 256
            || keyId.StartsWith(EntityPrefix, StringComparison.Ordinal)
            || keyId.Any(character =>
                !IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            throw new ArgumentException(
                "The Vercel API key ID has an unsupported format.",
                paramName);
        }
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z'
        || value is >= 'A' and <= 'Z'
        || value is >= '0' and <= '9';
}
