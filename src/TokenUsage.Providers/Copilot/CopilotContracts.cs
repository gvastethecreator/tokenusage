namespace TokenUsage.Providers.Copilot;

public interface ICopilotClient
{
    Task<CopilotAuthenticatedUser> GetAuthenticatedUserAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task<CopilotPersonalAccount> GetPersonalAccountAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task<CopilotAiCreditLookupResult> GetPersonalAiCreditUsageAsync(
        string token,
        string username,
        CancellationToken cancellationToken = default);

    Task<CopilotOrganizationSubscriptionLookupResult> GetOrganizationSubscriptionAsync(
        string token,
        string organization,
        CancellationToken cancellationToken = default);

    Task<CopilotAiCreditLookupResult> GetOrganizationAiCreditUsageAsync(
        string token,
        string organization,
        CancellationToken cancellationToken = default);
}

public enum CopilotClientErrorKind
{
    Authentication,
    InsufficientPermission,
    UnsupportedScope,
    Throttled,
    Transient,
    Contract,
}

public sealed class CopilotClientException : Exception
{
    public CopilotClientException(
        CopilotClientErrorKind kind,
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

    public CopilotClientErrorKind Kind { get; }

    public TimeSpan? RetryAfter { get; }
}

public sealed record CopilotAuthenticatedUser(string Login);

public sealed record CopilotPersonalAccount(
    string Login,
    CopilotAiCreditLookupResult Usage);

public enum CopilotBillingScope
{
    Personal,
    Organization,
}

public enum CopilotOrganizationPlan
{
    Business,
    Enterprise,
}

public sealed record CopilotTimePeriod(int Year, int? Month, int? Day);

public sealed record CopilotUsageItem(
    string Product,
    string Sku,
    string Model,
    string UnitType,
    decimal PricePerUnit,
    decimal GrossQuantity,
    decimal GrossAmount,
    decimal DiscountQuantity,
    decimal DiscountAmount,
    decimal NetQuantity,
    decimal NetAmount);

public sealed record CopilotAiCreditUsage(
    CopilotBillingScope Scope,
    string AccountLogin,
    CopilotTimePeriod Period,
    IReadOnlyList<CopilotUsageItem> Items,
    decimal GrossQuantity,
    decimal GrossAmount,
    decimal DiscountQuantity,
    decimal DiscountAmount,
    decimal NetQuantity,
    decimal NetAmount);

public abstract record CopilotAiCreditLookupResult
{
    private CopilotAiCreditLookupResult()
    {
    }

    public sealed record Found(CopilotAiCreditUsage Usage) : CopilotAiCreditLookupResult;

    public sealed record Unsupported : CopilotAiCreditLookupResult
    {
        private Unsupported()
        {
        }

        public static Unsupported Instance { get; } = new();
    }
}

public sealed record CopilotOrganizationSubscription(
    string Organization,
    CopilotOrganizationPlan? Plan,
    int? SeatTotal,
    int? ActiveThisCycle);

public abstract record CopilotOrganizationSubscriptionLookupResult
{
    private CopilotOrganizationSubscriptionLookupResult()
    {
    }

    public sealed record Found(CopilotOrganizationSubscription Subscription)
        : CopilotOrganizationSubscriptionLookupResult;

    public sealed record Unsupported : CopilotOrganizationSubscriptionLookupResult
    {
        private Unsupported()
        {
        }

        public static Unsupported Instance { get; } = new();
    }
}
