using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.Codex;

public sealed class CodexProviderRuntime : IProviderRuntime
{
    private const string MissingCliReason = "Codex CLI is not installed or could not be found.";
    private const string UnsupportedVersionReason = "The installed Codex CLI does not support the required account methods.";
    private const string UnavailableReason = "Codex app-server is unavailable.";
    private const string NeedsLoginReason = "Sign in with ChatGPT through Codex to read quota.";
    private const string UnsupportedAccountReason = "The active Codex authentication does not provide ChatGPT quota.";
    private const string MissingRateLimitsReason = "The active ChatGPT account did not report Codex quota windows.";
    private const string TimeoutReason = "Codex app-server timed out while reading quota.";
    private const string RejectedReason = "Codex app-server could not return quota right now.";
    private const string ContractReason = "Codex app-server returned an unsupported response.";
    private const string UsageUnavailableReason =
        "Codex daily usage is unavailable; quota remains current.";

    private readonly ICodexQuotaClientFactory _clientFactory;
    private readonly TimeZoneInfo _timeZone;
    private readonly string _timeZoneId;

    public CodexProviderRuntime(
        ICodexQuotaClientFactory clientFactory,
        string? timeZoneId = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _timeZoneId = timeZoneId ?? TimeZoneInfo.Local.Id;
        ArgumentException.ThrowIfNullOrWhiteSpace(_timeZoneId);
        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException("The Codex time zone is invalid.", nameof(timeZoneId), exception);
        }
    }

    public ProviderDescriptor Descriptor { get; } =
        new(new ProviderId("codex"), "Codex");

    public async ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CodexClientAvailability availability =
            await _clientFactory.DetectAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return availability switch
        {
            CodexClientAvailability.Available => new ProviderDetection.Available(),
            CodexClientAvailability.MissingCli =>
                new ProviderDetection.Unavailable(MissingCliReason),
            CodexClientAvailability.UnsupportedVersion =>
                new ProviderDetection.Unavailable(UnsupportedVersionReason),
            CodexClientAvailability.Unavailable =>
                new ProviderDetection.Unavailable(UnavailableReason),
            _ => throw new InvalidOperationException("Unknown Codex client availability."),
        };
    }

    public async Task<ProviderOutcome> RefreshAsync(
        RefreshContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        CodexClientAvailability availability =
            await _clientFactory.DetectAsync(cancellationToken).ConfigureAwait(false);
        switch (availability)
        {
            case CodexClientAvailability.MissingCli:
                return context.LastGood is null
                    ? new ProviderOutcome.NotConfigured(MissingCliReason)
                    : TransientFailure(MissingCliReason, context.LastGood);
            case CodexClientAvailability.UnsupportedVersion:
                return ContractFailure(UnsupportedVersionReason, context.LastGood);
            case CodexClientAvailability.Unavailable:
                return TransientFailure(UnavailableReason, context.LastGood);
            case CodexClientAvailability.Available:
                break;
            default:
                throw new InvalidOperationException("Unknown Codex client availability.");
        }

        try
        {
            await using ICodexQuotaClient client =
                await _clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
            await client.HandshakeAsync(cancellationToken).ConfigureAwait(false);
            CodexAccountStatus account =
                await client.ReadAccountStatusAsync(cancellationToken).ConfigureAwait(false);

            ProviderOutcome? accountOutcome = MapAccountStatus(account);
            if (accountOutcome is not null)
            {
                return accountOutcome;
            }

            CodexRateLimitsSnapshot rateLimits =
                await client.ReadRateLimitsAsync(cancellationToken).ConfigureAwait(false);
            CodexRateLimitsSnapshot effectiveRateLimits = ApplyAccountPlan(rateLimits, account.PlanType);
            DateTimeOffset observedAtUtc = context.Clock.GetUtcNow().ToUniversalTime();
            CodexSnapshotMappingResult mapping =
                CodexRateLimitsSnapshotMapper.Map(
                    effectiveRateLimits,
                    observedAtUtc,
                    _timeZoneId);

            return mapping switch
            {
                CodexSnapshotMappingResult.Available available =>
                    await ReadUsageAsync(
                        client,
                        available.Snapshot,
                        cancellationToken).ConfigureAwait(false),
                CodexSnapshotMappingResult.NoRateLimits =>
                    new ProviderOutcome.UnsupportedAccount(MissingRateLimitsReason),
                _ => throw new InvalidOperationException("Unknown Codex snapshot mapping result."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CodexRequestTimeoutException)
        {
            return TransientFailure(TimeoutReason, context.LastGood);
        }
        catch (CodexClientUnavailableException)
        {
            return TransientFailure(UnavailableReason, context.LastGood);
        }
        catch (CodexRpcException exception) when (exception.Code == -32601)
        {
            return ContractFailure(UnsupportedVersionReason, context.LastGood);
        }
        catch (CodexRpcException)
        {
            return TransientFailure(RejectedReason, context.LastGood);
        }
        catch (CodexProtocolException)
        {
            return ContractFailure(ContractReason, context.LastGood);
        }
    }

    private async Task<ProviderOutcome> ReadUsageAsync(
        ICodexQuotaClient client,
        ProviderSnapshot quotaSnapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            CodexTokenUsageSnapshot usage =
                await client.ReadTokenUsageAsync(cancellationToken).ConfigureAwait(false);
            ProviderSnapshot snapshot =
                CodexUsageSnapshotMapper.AppendUsage(quotaSnapshot, usage, _timeZone);
            return new ProviderOutcome.Success(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is CodexRequestTimeoutException
                or CodexClientUnavailableException
                or CodexRpcException
                or CodexProtocolException
                or OperationCanceledException)
        {
            ProviderSnapshot partialSnapshot =
                CodexUsageSnapshotMapper.MarkUsageUnavailable(quotaSnapshot);
            return new ProviderOutcome.PartialSuccess(
                partialSnapshot,
                [new ProviderWarning(ProviderWarningCode.MissingMetric, UsageUnavailableReason)]);
        }
    }

    private static ProviderOutcome? MapAccountStatus(CodexAccountStatus account) =>
        account.Kind switch
        {
            CodexAccountKind.ChatGpt => null,
            CodexAccountKind.None when account.RequiresOpenAiAuth =>
                new ProviderOutcome.NotConfigured(NeedsLoginReason),
            CodexAccountKind.None =>
                new ProviderOutcome.UnsupportedAccount(UnsupportedAccountReason),
            CodexAccountKind.ApiKey or CodexAccountKind.AmazonBedrock or CodexAccountKind.Other =>
                new ProviderOutcome.UnsupportedAccount(UnsupportedAccountReason),
            _ => throw new InvalidOperationException("Unknown Codex account kind."),
        };

    private static CodexRateLimitsSnapshot ApplyAccountPlan(
        CodexRateLimitsSnapshot rateLimits,
        string? accountPlanType)
    {
        if (rateLimits.RateLimits.PlanType is not null || accountPlanType is null)
        {
            return rateLimits;
        }

        return new CodexRateLimitsSnapshot(
            rateLimits.RateLimits with { PlanType = accountPlanType },
            rateLimits.RateLimitsByLimitId,
            rateLimits.ResetCredits);
    }

    private static ProviderOutcome.TransientFailure TransientFailure(
        string message,
        ProviderSnapshot? lastGood) =>
        new(
            new ProviderError(ProviderErrorCode.TransientSourceFailure, message),
            lastGood);

    private static ProviderOutcome.ContractFailure ContractFailure(
        string message,
        ProviderSnapshot? lastGood) =>
        new(
            new ProviderError(ProviderErrorCode.ContractViolation, message),
            lastGood);
}
