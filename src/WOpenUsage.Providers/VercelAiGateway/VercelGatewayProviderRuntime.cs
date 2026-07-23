using WOpenUsage.Core.Providers;

namespace WOpenUsage.Providers.VercelAiGateway;

public sealed class VercelGatewayConnection
{
    public string ApiKey { get; }

    public string? KeyId { get; }

    public VercelGatewayConnection(string apiKey, string? keyId = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key is required.", nameof(apiKey));
        }

        ApiKey = apiKey;
        if (keyId is not null)
        {
            VercelGatewayKeyIdValidation.Validate(keyId, nameof(keyId));
        }

        KeyId = keyId;
    }
}

public interface IVercelGatewayConnectionSource
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    Task<VercelGatewayConnection?> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed class VercelGatewayProviderRuntime : IProviderRuntime
{
    internal const string ProviderIdValue = "vercel-ai-gateway";
    internal const string DisplayNameValue = "Vercel AI Gateway";
    private const string NotConfiguredMessage = "Vercel AI Gateway is not configured.";
    private const string AuthenticationMessage = "Vercel AI Gateway credentials were rejected.";
    private const string UnsupportedAccountMessage = "Vercel AI Gateway does not support this account.";
    private const string TransientMessage = "Vercel AI Gateway temporarily failed.";
    private const string ContractMessage = "Vercel AI Gateway returned an unexpected response.";
    private const string OverflowMessage = "Vercel AI Gateway report aggregation overflowed.";

    private static readonly TimeSpan DefaultThrottleRetry = TimeSpan.FromMinutes(5);

    private readonly IVercelGatewayConnectionSource _connectionSource;
    private readonly IVercelGatewayReportClient _reportClient;

    public VercelGatewayProviderRuntime(
        IVercelGatewayConnectionSource connectionSource,
        IVercelGatewayReportClient reportClient)
    {
        _connectionSource = connectionSource ?? throw new ArgumentNullException(nameof(connectionSource));
        _reportClient = reportClient ?? throw new ArgumentNullException(nameof(reportClient));
    }

    public ProviderDescriptor Descriptor { get; } = new ProviderDescriptor(
        new ProviderId(ProviderIdValue),
        DisplayNameValue,
        isExperimental: true);

    public async ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool isConfigured = await _connectionSource
            .IsConfiguredAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return isConfigured
            ? new ProviderDetection.Available()
            : new ProviderDetection.Unavailable(NotConfiguredMessage);
    }

    public async Task<ProviderOutcome> RefreshAsync(
        RefreshContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _connectionSource
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (connection is null)
        {
            return new ProviderOutcome.NotConfigured(NotConfiguredMessage);
        }

        DateTimeOffset utcNow = context.Clock.GetUtcNow().ToUniversalTime();
        if (!context.ForceRefresh
            && context.LastGood is ProviderSnapshot lastGood
            && !SnapshotFreshness.IsStale(lastGood, context.Clock, context.StaleAfter))
        {
            return new ProviderOutcome.Success(lastGood);
        }

        var today = DateOnly.FromDateTime(utcNow.UtcDateTime);
        var startDate = today.AddDays(-29);
        var endDate = today;

        try
        {
            var report = await _reportClient
                .GetDailyReportAsync(connection.ApiKey, startDate, endDate, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var mapped = VercelGatewaySnapshotMapper.Map(report, utcNow);

            if (mapped.Warnings.Count > 0)
            {
                return new ProviderOutcome.PartialSuccess(mapped.Snapshot, mapped.Warnings);
            }

            return new ProviderOutcome.Success(mapped.Snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VercelGatewayReportException ex)
        {
            return MapReportException(ex, context.LastGood, utcNow);
        }
        catch (OverflowException)
        {
            return new ProviderOutcome.ContractFailure(
                new ProviderError(ProviderErrorCode.ContractViolation, OverflowMessage),
                context.LastGood);
        }
    }

    private static ProviderOutcome MapReportException(
        VercelGatewayReportException exception,
        ProviderSnapshot? lastGood,
        DateTimeOffset utcNow)
    {
        switch (exception.Kind)
        {
            case VercelGatewayReportErrorKind.Authentication:
                return new ProviderOutcome.NotConfigured(AuthenticationMessage);

            case VercelGatewayReportErrorKind.UnsupportedAccount:
                return new ProviderOutcome.UnsupportedAccount(UnsupportedAccountMessage);

            case VercelGatewayReportErrorKind.Throttled:
                var retryAfter = exception.RetryAfter ?? DefaultThrottleRetry;
                TimeSpan maximumDelay = DateTimeOffset.MaxValue - utcNow;
                DateTimeOffset retryAtUtc = utcNow + (retryAfter > maximumDelay
                    ? maximumDelay
                    : retryAfter);
                return new ProviderOutcome.Throttled(retryAtUtc, lastGood);

            case VercelGatewayReportErrorKind.Transient:
                return new ProviderOutcome.TransientFailure(
                    new ProviderError(ProviderErrorCode.TransientSourceFailure, TransientMessage),
                    lastGood);

            case VercelGatewayReportErrorKind.Contract:
                return new ProviderOutcome.ContractFailure(
                    new ProviderError(ProviderErrorCode.ContractViolation, ContractMessage),
                    lastGood);

            default:
                return new ProviderOutcome.ContractFailure(
                    new ProviderError(ProviderErrorCode.ContractViolation, ContractMessage),
                    lastGood);
        }
    }
}
