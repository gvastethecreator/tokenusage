using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.VercelAiGateway;

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

/// <summary>
/// Reads the saved AI Gateway key's spend (Custom Reporting, scoped to that key), the team's
/// credit balance, and the key's budget when a key ID is saved. Only the report is paid
/// ($5 per 1,000 queries), so an unforced refresh reuses a snapshot for at least an hour.
/// The credit balance and the budget are best-effort: their failure never hides the report.
/// </summary>
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
    private const string QuotaWarningMessage = "Vercel AI Gateway key budget is unavailable.";
    private const string CreditsWarningMessage = "Vercel AI Gateway credit balance is unavailable.";
    private const string ReportPlanWarningMessage =
        "Vercel AI Gateway Custom Reporting is not available for this plan.";

    /// <summary>
    /// Minimum age before an unforced refresh queries the paid report again.
    /// </summary>
    public static readonly TimeSpan ReportStaleAfter = TimeSpan.FromHours(1);

    private static readonly TimeSpan DefaultThrottleRetry = TimeSpan.FromMinutes(5);

    private readonly IVercelGatewayConnectionSource _connectionSource;
    private readonly IVercelGatewayReportClient _reportClient;
    private readonly IVercelGatewayQuotaClient _quotaClient;
    private readonly IVercelGatewayCreditsClient _creditsClient;

    public VercelGatewayProviderRuntime(
        IVercelGatewayConnectionSource connectionSource,
        IVercelGatewayReportClient reportClient,
        IVercelGatewayQuotaClient quotaClient,
        IVercelGatewayCreditsClient creditsClient)
    {
        _connectionSource = connectionSource ?? throw new ArgumentNullException(nameof(connectionSource));
        _reportClient = reportClient ?? throw new ArgumentNullException(nameof(reportClient));
        _quotaClient = quotaClient ?? throw new ArgumentNullException(nameof(quotaClient));
        _creditsClient = creditsClient ?? throw new ArgumentNullException(nameof(creditsClient));
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

        var connection = await _connectionSource
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (connection is null)
        {
            return new ProviderOutcome.NotConfigured(NotConfiguredMessage);
        }

        DateTimeOffset utcNow = context.Clock.GetUtcNow().ToUniversalTime();
        TimeSpan staleAfter = context.StaleAfter > ReportStaleAfter
            ? context.StaleAfter
            : ReportStaleAfter;
        if (!context.ForceRefresh
            && context.LastGood is ProviderSnapshot lastGood
            && !SnapshotFreshness.IsStale(lastGood, context.Clock, staleAfter))
        {
            return new ProviderOutcome.Success(lastGood);
        }

        var today = DateOnly.FromDateTime(utcNow.UtcDateTime);
        DateOnly startDate = UsagePeriodPolicy.RollingDisplayStart(today);
        var endDate = today;

        var warnings = new List<ProviderWarning>();
        VercelGatewayCredits? credits = null;
        ProviderCapabilityState creditsState;
        try
        {
            credits = await _creditsClient
                .GetCreditsAsync(connection.ApiKey, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            creditsState = ProviderCapabilityState.Available;
        }
        catch (VercelGatewayCreditsException exception)
            when (exception.Kind == VercelGatewayCreditsErrorKind.Authentication)
        {
            return new ProviderOutcome.NotConfigured(AuthenticationMessage);
        }
        catch (VercelGatewayCreditsException)
        {
            creditsState = ProviderCapabilityState.Degraded;
            warnings.Add(new ProviderWarning(
                ProviderWarningCode.SourceDegraded,
                CreditsWarningMessage));
        }

        try
        {
            VercelGatewayReport? report;
            try
            {
                report = await _reportClient
                    .GetDailyReportAsync(connection.ApiKey, startDate, endDate, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (VercelGatewayReportException exception)
                when (exception.Kind == VercelGatewayReportErrorKind.UnsupportedAccount
                    && credits is not null)
            {
                // Hobby and Pro-trial plans cannot query Custom Reporting, but every plan has
                // a credit balance. Keep the balance instead of hiding the whole provider.
                report = null;
                warnings.Add(new ProviderWarning(
                    ProviderWarningCode.SourceDegraded,
                    ReportPlanWarningMessage));
            }

            cancellationToken.ThrowIfCancellationRequested();

            VercelGatewayQuotaLookupResult? quotaResult = null;
            ProviderCapabilityState quotaState = ProviderCapabilityState.NotRequested;
            if (connection.KeyId is not null)
            {
                try
                {
                    quotaResult = await _quotaClient
                        .GetQuotaAsync(
                            connection.ApiKey,
                            connection.KeyId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    quotaState = quotaResult switch
                    {
                        VercelGatewayQuotaLookupResult.Found =>
                            ProviderCapabilityState.Available,
                        VercelGatewayQuotaLookupResult.NoBudget =>
                            ProviderCapabilityState.NotConfigured,
                        _ => ProviderCapabilityState.Degraded,
                    };
                }
                catch (VercelGatewayQuotaException)
                {
                    // The budget endpoint is not in the public REST reference. Any failure
                    // degrades only the budget and keeps the rest of the snapshot.
                    quotaState = ProviderCapabilityState.Degraded;
                    warnings.Add(new ProviderWarning(
                        ProviderWarningCode.SourceDegraded,
                        QuotaWarningMessage));
                }
            }

            var mapped = VercelGatewaySnapshotMapper.Map(
                report,
                new VercelGatewaySnapshotMapper.SupplementalReadings(
                    credits,
                    creditsState,
                    quotaResult,
                    quotaState),
                utcNow,
                warnings);

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
