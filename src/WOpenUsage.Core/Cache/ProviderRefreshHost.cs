using System.Runtime.CompilerServices;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Cache;

/// <summary>
/// Registers provider runtimes against (optionally partitioned) snapshot stores and
/// streams a single cache-first refresh pass for App, CLI, and other hosts.
/// </summary>
public sealed class ProviderRefreshRegistration
{
    public ProviderRefreshRegistration(
        IProviderRuntime provider,
        SnapshotStore store,
        ProviderOperationGate? operationGate = null)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Store = store ?? throw new ArgumentNullException(nameof(store));
        OperationGate = operationGate;
    }

    public IProviderRuntime Provider { get; }

    public SnapshotStore Store { get; }

    public ProviderOperationGate? OperationGate { get; }
}

public sealed class ProviderRefreshHost
{
    private readonly IReadOnlyList<ProviderRefreshRegistration> _registrations;
    private readonly TimeProvider _clock;

    public ProviderRefreshHost(
        IEnumerable<ProviderRefreshRegistration> registrations,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        ProviderRefreshRegistration[] array = registrations.ToArray();
        if (array.Length == 0 || array.Any(registration => registration is null))
        {
            throw new ArgumentException(
                "At least one provider registration is required.",
                nameof(registrations));
        }

        string? duplicate = array
            .GroupBy(registration => registration.Provider.Descriptor.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Provider '{duplicate}' appears more than once.",
                nameof(registrations));
        }

        _registrations = Array.AsReadOnly(array);
    }

    public TimeProvider Clock => _clock;

    public IReadOnlyList<ProviderRefreshRegistration> Registrations => _registrations;

    public IAsyncEnumerable<CacheFirstEvent> RunAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default) =>
        RunRegistrationsAsync(_registrations, forceRefresh, cancellationToken);

    public IAsyncEnumerable<CacheFirstEvent> RunProviderAsync(
        ProviderId providerId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        ProviderRefreshRegistration registration = _registrations.FirstOrDefault(candidate =>
            candidate.Provider.Descriptor.Id == providerId)
            ?? throw new KeyNotFoundException(
                $"Provider '{providerId.Value}' is not registered with this refresh host.");
        return RunRegistrationsAsync([registration], forceRefresh, cancellationToken);
    }

    private async IAsyncEnumerable<CacheFirstEvent> RunRegistrationsAsync(
        IReadOnlyList<ProviderRefreshRegistration> registrations,
        bool forceRefresh,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var mergedSnapshots = new Dictionary<string, ProviderSnapshot>(StringComparer.Ordinal);
        var loadedAny = false;
        SnapshotCacheReadResult? firstEmptyOrCorrupt = null;

        Task<SnapshotCacheReadResult>[] cacheReads = registrations
            .Select(registration => registration.Store.LoadAsync(cancellationToken))
            .ToArray();
        SnapshotCacheReadResult[] readResults = await Task
            .WhenAll(cacheReads)
            .ConfigureAwait(false);
        foreach (SnapshotCacheReadResult readResult in readResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (readResult is SnapshotCacheReadResult.Loaded loaded)
            {
                loadedAny = true;
                foreach (ProviderSnapshot snapshot in loaded.Snapshots)
                {
                    mergedSnapshots[snapshot.ProviderId.Value] = snapshot;
                }
            }
            else
            {
                firstEmptyOrCorrupt ??= readResult;
            }
        }

        SnapshotCacheReadResult published = loadedAny
            ? new SnapshotCacheReadResult.Loaded(
                mergedSnapshots.Values
                    .OrderBy(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
                    .ToArray())
            : firstEmptyOrCorrupt ?? new SnapshotCacheReadResult.Empty();
        yield return new CacheFirstEvent.CachePublished(published);

        using var refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var pending = registrations
            .Select(registration => RefreshRegistrationAsync(
                registration,
                forceRefresh,
                refreshCancellation.Token))
            .ToList();

        try
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task<CacheFirstEvent.ProviderCompleted> completedTask = await Task
                    .WhenAny(pending)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                pending.Remove(completedTask);
                yield return await completedTask.ConfigureAwait(false);
            }
        }
        finally
        {
            refreshCancellation.Cancel();
            await ObservePendingAsync(pending).ConfigureAwait(false);
        }
    }

    private async Task<CacheFirstEvent.ProviderCompleted> RefreshRegistrationAsync(
        ProviderRefreshRegistration registration,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        CacheFirstRefresh partitionRefresh = new(
            registration.Store,
            [registration.Provider],
            _clock,
            registration.OperationGate);
        await foreach (CacheFirstEvent item in partitionRefresh
                           .RunAsync(forceRefresh, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (item is CacheFirstEvent.ProviderCompleted completed)
            {
                return completed;
            }
        }

        throw new InvalidOperationException(
            $"Provider '{registration.Provider.Descriptor.Id.Value}' produced no completion event.");
    }

    private static async Task ObservePendingAsync(
        List<Task<CacheFirstEvent.ProviderCompleted>> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The active iteration already owns the first failure or cancellation.
            // Await every sibling here so no provider task escapes unobserved.
        }
    }
}
