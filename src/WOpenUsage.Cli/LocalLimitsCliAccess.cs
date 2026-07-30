using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Runtime.Windows.Providers;

namespace WOpenUsage.Cli;

public static class LocalLimitsCliAccess
{
    public static async Task<IReadOnlyList<ProviderSnapshot>> ReadAsync(
        string dataDirectory,
        string? providerId,
        bool forceRefresh,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (providerId is not null
            && !IsKnownProvider(providerId))
        {
            return [];
        }

        string root = Path.GetFullPath(dataDirectory);
        ProviderRefreshHost host = CreateLiveHost(root, clock);
        return await new LimitsQuery(host)
            .ReadAsync(
                providerId is null ? null : new ProviderId(providerId),
                forceRefresh,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static ProviderRefreshHost CreateLiveHost(string dataDirectory, TimeProvider clock)
    {
        return WindowsProviderCatalog.CreateComposition(
            dataDirectory,
            clock).RefreshHost;
    }

    internal static Task<IReadOnlyList<ProviderSnapshot>> SelectForceResultAsync(
        IAsyncEnumerable<CacheFirstEvent> events,
        string? providerId,
        CancellationToken cancellationToken) =>
        LimitsQuery.SelectForceResultAsync(
            events,
            providerId is null ? null : new ProviderId(providerId),
            cancellationToken);

    // Back-compat for existing tests that call the codex-only selector.
    internal static Task<IReadOnlyList<ProviderSnapshot>> SelectForceResultAsync(
        IAsyncEnumerable<CacheFirstEvent> events,
        CancellationToken cancellationToken) =>
        SelectForceResultAsync(events, providerId: "codex", cancellationToken);

    private static bool IsKnownProvider(string providerId) =>
        WindowsProviderCatalog.Entries.Any(entry =>
            entry.Capabilities.Contains(ProviderCapability.Limits)
            && string.Equals(entry.Id.Value, providerId, StringComparison.Ordinal));

}
