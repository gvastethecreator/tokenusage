using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Cli;

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
        IReadOnlyList<ProviderSnapshot> snapshots = await new LimitsQuery(host)
            .ReadAsync(providerId is null ? null : new ProviderId(providerId), forceRefresh, cancellationToken)
            .ConfigureAwait(false);

        var history = new QuotaResetHistoryStore(
            Path.Combine(root, "history", QuotaResetHistoryStore.DefaultFileName),
            clock, isCodexHistoryFrozen: () => new ProviderAccountSelectionStore(root, new ProviderId("codex")).WasActivated);
        foreach (ProviderSnapshot snapshot in snapshots.Where(snapshot => snapshot.InstanceKey.AccountKey is null
                     && (snapshot.ProviderId.Value is "codex" or "claude")))
        {
            try
            {
                await history.ObserveAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or TimeoutException
                or InvalidOperationException
                or System.Security.SecurityException)
            {
                // Limits remain useful even if supplementary reset history cannot be written.
            }
        }

        return snapshots;
    }

    internal static ProviderRefreshHost CreateLiveHost(string dataDirectory, TimeProvider clock)
    {
        return WindowsProviderCatalog.CreateComposition(
            dataDirectory,
            clock, options: new WindowsProviderCompositionOptions(CodexActiveAccountOnly: true)).RefreshHost;
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
