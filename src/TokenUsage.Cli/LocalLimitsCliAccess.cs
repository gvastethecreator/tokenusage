using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
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

        bool isClaude = string.Equals(providerId, "claude", StringComparison.Ordinal);
        if (providerId is not null
            && !isClaude
            && !IsKnownProvider(providerId))
        {
            return [];
        }

        string root = Path.GetFullPath(dataDirectory);
        var snapshots = new List<ProviderSnapshot>();
        if (!isClaude)
        {
            ProviderRefreshHost host = CreateLiveHost(root, clock);
            snapshots.AddRange(await new LimitsQuery(host)
                .ReadAsync(
                    providerId is null ? null : new ProviderId(providerId),
                    forceRefresh,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        // Claude limits are the last reading its status line wrapper stored; there is no
        // live call to force, so a forced read returns the same reading.
        if ((providerId is null || isClaude)
            && ReadClaude(root, clock) is { } claude)
        {
            snapshots.Add(claude);
        }

        var history = new QuotaResetHistoryStore(
            Path.Combine(root, "history", QuotaResetHistoryStore.DefaultFileName),
            clock);
        foreach (ProviderSnapshot snapshot in snapshots.Where(snapshot => snapshot.ProviderId.Value
                     is "codex" or "claude"))
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

    private static ProviderSnapshot? ReadClaude(string dataDirectory, TimeProvider clock)
    {
        string path = ClaudeRateLimitStore.DefaultPath(dataDirectory);
        if (!File.Exists(path))
        {
            return null;
        }

        ClaudeRateLimitSnapshot? reading = new ClaudeRateLimitStore(path).Load();
        return reading is null
            ? null
            : ClaudeRateLimitSnapshotMapper.Map(reading, clock.GetUtcNow(), TimeZoneInfo.Local.Id);
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
