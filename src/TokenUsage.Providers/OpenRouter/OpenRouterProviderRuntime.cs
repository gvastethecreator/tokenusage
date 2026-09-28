using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.OpenRouter;

/// <summary>
/// Reads the saved OpenRouter key's own usage and limit from <c>GET /api/v1/key</c>. Account
/// credits and per-model activity need a management key, so this key-only runtime does not
/// read them.
/// </summary>
public sealed class OpenRouterProviderRuntime : IProviderRuntime
{
    internal const string ProviderIdValue = "openrouter";
    internal const string DisplayNameValue = "OpenRouter";
    private const string NotConfiguredMessage = "OpenRouter is not configured.";
    private const string AuthenticationMessage = "OpenRouter rejected the saved key.";
    private const string PermissionMessage = "OpenRouter did not allow this key to read its usage.";
    private const string TransientMessage = "OpenRouter temporarily failed.";
    private const string ContractMessage = "OpenRouter returned an unexpected response.";

    private static readonly TimeSpan DefaultThrottleRetry = TimeSpan.FromMinutes(5);

    private readonly IOpenRouterKeySource _keySource;
    private readonly IOpenRouterClient _client;

    public OpenRouterProviderRuntime(IOpenRouterKeySource keySource, IOpenRouterClient client)
    {
        _keySource = keySource ?? throw new ArgumentNullException(nameof(keySource));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ProviderDescriptor Descriptor { get; } = new ProviderDescriptor(
        new ProviderId(ProviderIdValue),
        DisplayNameValue,
        isExperimental: true);

    public async Task<ProviderOutcome> RefreshAsync(
        RefreshContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string? apiKey = await _keySource.ReadApiKeyAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ProviderOutcome.NotConfigured(NotConfiguredMessage);
        }

        if (!context.ForceRefresh
            && context.LastGood is ProviderSnapshot lastGood
            && !SnapshotFreshness.IsStale(lastGood, context.Clock, context.StaleAfter))
        {
            return new ProviderOutcome.Success(lastGood);
        }

        DateTimeOffset utcNow = context.Clock.GetUtcNow().ToUniversalTime();
        try
        {
            OpenRouterKeyUsage usage = await _client
                .GetKeyUsageAsync(apiKey, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new ProviderOutcome.Success(OpenRouterSnapshotMapper.Map(usage, utcNow));
        }
        catch (OpenRouterClientException exception)
        {
            return MapException(exception, context.LastGood, utcNow);
        }
    }

    private static ProviderOutcome MapException(
        OpenRouterClientException exception,
        ProviderSnapshot? lastGood,
        DateTimeOffset utcNow)
    {
        switch (exception.Kind)
        {
            case OpenRouterClientErrorKind.Authentication:
                return new ProviderOutcome.NotConfigured(AuthenticationMessage);

            case OpenRouterClientErrorKind.InsufficientPermission:
                return new ProviderOutcome.UnsupportedAccount(PermissionMessage);

            case OpenRouterClientErrorKind.Throttled:
                TimeSpan retryAfter = exception.RetryAfter ?? DefaultThrottleRetry;
                TimeSpan maximumDelay = DateTimeOffset.MaxValue - utcNow;
                return new ProviderOutcome.Throttled(
                    utcNow + (retryAfter > maximumDelay ? maximumDelay : retryAfter),
                    lastGood);

            case OpenRouterClientErrorKind.Transient:
                return new ProviderOutcome.TransientFailure(
                    new ProviderError(ProviderErrorCode.TransientSourceFailure, TransientMessage),
                    lastGood);

            default:
                return new ProviderOutcome.ContractFailure(
                    new ProviderError(ProviderErrorCode.ContractViolation, ContractMessage),
                    lastGood);
        }
    }
}
