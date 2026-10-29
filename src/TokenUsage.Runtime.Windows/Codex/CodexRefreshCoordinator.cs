using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Codex;

namespace TokenUsage.Runtime.Windows.Codex;

public static class CodexRefreshCoordinator
{
    public static ProviderRefreshRegistration CreateRegistration(
        string cacheDirectory,
        TimeProvider clock,
        ICodexQuotaClientFactory clientFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(clientFactory);

        string cachePath = Path.Combine(
            Path.GetFullPath(cacheDirectory),
            SnapshotStore.DefaultFileName);
        return new ProviderRefreshRegistration(
            new ResilientProviderRuntime(new CodexProviderRuntime(clientFactory)),
            new SnapshotStore(cachePath, clock));
    }
}
