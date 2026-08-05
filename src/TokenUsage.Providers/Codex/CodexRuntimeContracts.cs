namespace TokenUsage.Providers.Codex;

public interface ICodexQuotaClient : IAsyncDisposable
{
    Task HandshakeAsync(CancellationToken cancellationToken);

    Task<CodexAccountStatus> ReadAccountStatusAsync(CancellationToken cancellationToken);

    Task<CodexRateLimitsSnapshot> ReadRateLimitsAsync(CancellationToken cancellationToken);

    Task<CodexTokenUsageSnapshot> ReadTokenUsageAsync(CancellationToken cancellationToken);
}

public enum CodexClientAvailability
{
    Available,
    MissingCli,
    UnsupportedVersion,
    Unavailable,
}

public interface ICodexQuotaClientFactory
{
    ValueTask<CodexClientAvailability> DetectAsync(CancellationToken cancellationToken);

    Task<ICodexQuotaClient> CreateAsync(CancellationToken cancellationToken);
}
