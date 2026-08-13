#if DEBUG || UI_TEST_FIXTURES
using TokenUsage.Providers.VercelAiGateway;
using TokenUsage.Runtime.Windows.VercelAiGateway;

namespace TokenUsage.App.Composition;

internal sealed class DebugVercelCredentialStore : IVercelGatewayCredentialStore
{
    private string? _apiKey;
    private string? _keyId;

    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_apiKey is not null);
    }

    public Task<VercelGatewayConnection?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _apiKey is null ? null : new VercelGatewayConnection(_apiKey, _keyId));
    }

    public Task SaveAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        cancellationToken.ThrowIfCancellationRequested();
        _apiKey = apiKey;
        _keyId = null;
        return Task.CompletedTask;
    }

    public Task SaveAsync(
        string apiKey,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        cancellationToken.ThrowIfCancellationRequested();
        _apiKey = apiKey;
        _keyId = keyId;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool removed = _apiKey is not null;
        _apiKey = null;
        _keyId = null;
        return Task.FromResult(removed);
    }
}

internal sealed class DebugVercelReportClient : IVercelGatewayReportClient
{
    public Task<VercelGatewayReport> GetDailyReportAsync(
        string apiKey,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VercelGatewayReport(
        [
            new VercelGatewayDailyReportRow(
                endDate,
                TotalCost: 12.5m,
                MarketCost: 11m,
                SurchargeCost: 1m,
                GatewayCost: 0.5m,
                InputTokens: 1000,
                OutputTokens: 250,
                CachedInputTokens: 100,
                CacheCreationInputTokens: 50,
                ReasoningTokens: 25,
                RequestCount: 7),
        ]));
    }
}

internal sealed class DebugVercelQuotaClient : IVercelGatewayQuotaClient
{
    public Task<VercelGatewayQuotaLookupResult> GetQuotaAsync(
        string apiKey,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<VercelGatewayQuotaLookupResult>(
            new VercelGatewayQuotaLookupResult.Found(
                new VercelGatewayQuota(
                    "api_key_id_" + keyId,
                    "tokenusage-ui-test",
                    10m,
                    3.5m,
                    6.5m,
                    VercelGatewayQuotaRefreshPeriod.Monthly,
                    Active: true)));
    }
}
#endif
