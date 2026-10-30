using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Codex;
using TokenUsage.Runtime.Windows.Accounts;

namespace TokenUsage.Runtime.Windows.Codex;

/// <summary>
/// Reads a code-swap profile's quota through the official local app-server with that profile's
/// <c>CODEX_HOME</c>. The inventory is read again afterwards, so a profile that changed identity
/// during the query is rejected.
/// </summary>
internal sealed class CodexSwapSource(TimeProvider clock) : ISwapAccountSource
{
    public ProviderId Provider { get; } = new("codex");

    public string DisplayName => "Codex";

    public bool KeepsGlobalReadingForActiveAccountOnly => false;

    public async Task<IReadOnlyList<SwapAccount>> ReadAsync(IOpaqueKeyDeriver keys, CancellationToken token) =>
        await new CodexSwapDiscovery(keys).ReadAsync(token).ConfigureAwait(false);

    public async Task<ProviderOutcome> RefreshAsync(SwapAccount account,
        Func<CancellationToken, Task<IReadOnlyList<SwapAccount>>> reread, RefreshContext context, CancellationToken token)
    {
        var profile = (CodexSwapAccount)account;
        var options = new CodexClientOptions("tokenusage", "0.1.0", "TokenUsage",
            expectedHome: profile.Home, expectedEmail: profile.Email);
        var runtime = new CodexProviderRuntime(new CodexAppServerQuotaClientFactory(clock, options, profile.Home),
            includeUsage: false);
        ProviderOutcome outcome = await runtime.RefreshAsync(context, token).ConfigureAwait(false);
        if (outcome is not ProviderOutcome.Success) return outcome;
        IReadOnlyList<SwapAccount> after = await reread(token).ConfigureAwait(false);
        return after.SingleOrDefault(item => item.InstanceKey == account.InstanceKey)?.Fingerprint == account.Fingerprint
            ? outcome
            : new ProviderOutcome.TransientFailure(new ProviderError(ProviderErrorCode.TransientSourceFailure,
                "The account changed identity during the query."), context.LastGood);
    }
}
