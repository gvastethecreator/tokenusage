using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;

namespace TokenUsage.Platform.Windows.Tests;

internal static class CoordinatorRefresh
{
    public static IAsyncEnumerable<CacheFirstEvent> Run(
        ProviderRefreshRegistration registration,
        TimeProvider clock,
        bool forceRefresh,
        CancellationToken cancellationToken) =>
        new ProviderRefreshHost([registration], clock)
            .RunProviderAsync(new ProviderId("codex"), forceRefresh, cancellationToken);
}
