using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Runtime.Windows.Codex;
using TokenUsage.Runtime.Windows.VercelAiGateway;

namespace TokenUsage.Platform.Windows.Tests;

internal static class CoordinatorRefresh
{
    public static IAsyncEnumerable<CacheFirstEvent> Run(
        CodexRefreshCoordinator coordinator,
        bool forceRefresh,
        CancellationToken cancellationToken) =>
        new ProviderRefreshHost([coordinator.CreateRegistration()], coordinator.Clock)
            .RunProviderAsync(new ProviderId("codex"), forceRefresh, cancellationToken);

    public static IAsyncEnumerable<CacheFirstEvent> Run(
        VercelGatewayRefreshCoordinator coordinator,
        bool forceRefresh,
        CancellationToken cancellationToken) =>
        new ProviderRefreshHost([coordinator.CreateRegistration()], coordinator.Clock)
            .RunProviderAsync(
                new ProviderId("vercel-ai-gateway"),
                forceRefresh,
                cancellationToken);
}
