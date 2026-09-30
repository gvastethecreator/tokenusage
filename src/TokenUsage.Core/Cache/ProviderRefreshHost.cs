using System.Runtime.CompilerServices;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Alerts;

namespace TokenUsage.Core.Cache;

/// <summary>
/// Registers provider runtimes against (optionally partitioned) snapshot stores and
/// streams a single cache-first refresh pass for App, CLI, and other hosts.
/// </summary>
public sealed class ProviderRefreshRegistration
{
    public ProviderRefreshRegistration(
        IProviderRuntime provider,
        SnapshotStore store,
        ProviderOperationGate? operationGate = null,
        AlertHost? alertHost = null,
        Func<ProviderSnapshot, CancellationToken, Task>? beforeSnapshotSaveAsync = null,
        ProviderAccountInfo? account = null)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Store = store ?? throw new ArgumentNullException(nameof(store));
        OperationGate = operationGate;
        InstanceKey = store.Scope ?? new ProviderInstanceKey(provider.Descriptor.Id);
        AlertHost = alertHost;
        BeforeSnapshotSaveAsync = beforeSnapshotSaveAsync;
        Account = account;
    }

    public IProviderRuntime Provider { get; }

    public SnapshotStore Store { get; }

    public ProviderOperationGate? OperationGate { get; }

    public ProviderInstanceKey InstanceKey { get; }

    public AlertHost? AlertHost { get; }
    public Func<ProviderSnapshot, CancellationToken, Task>? BeforeSnapshotSaveAsync { get; }
    public ProviderAccountInfo? Account { get; }
}

public sealed class ProviderRefreshHost
{
    private IReadOnlyList<ProviderRefreshRegistration> _registrations;
    private readonly IReadOnlyList<ProviderRefreshRegistration> _baseRegistrations;
    public Func<IReadOnlyList<ProviderRefreshRegistration>, CancellationToken,
        Task<IReadOnlyList<ProviderRefreshRegistration>>>? ResolveRegistrationsAsync { get; set; }
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
            .GroupBy(registration => registration.InstanceKey.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Provider '{duplicate}' appears more than once.",
                nameof(registrations));
        }

        _registrations = Array.AsReadOnly(array);
        _baseRegistrations = _registrations;
    }

    public TimeProvider Clock => _clock;

    public IReadOnlyList<ProviderRefreshRegistration> Registrations => _registrations;

    public IAsyncEnumerable<CacheFirstEvent> RunAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default) =>
        RunResolvedAsync(null, forceRefresh, cancellationToken);

    public IAsyncEnumerable<CacheFirstEvent> RunProviderAsync(
        ProviderId providerId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        if (!_baseRegistrations.Any(item => item.InstanceKey.ProviderId == providerId))
            throw new ArgumentException("The provider is not registered.", nameof(providerId));
        return RunResolvedAsync(providerId, forceRefresh, cancellationToken);
    }

    private async IAsyncEnumerable<CacheFirstEvent> RunResolvedAsync(ProviderId? providerId,
        bool forceRefresh, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await ResolveAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProviderRefreshRegistration> selected = providerId is null ? _registrations
            : _registrations.Where(item => item.InstanceKey.ProviderId == providerId).ToArray();
        await foreach (CacheFirstEvent item in RunRegistrationsAsync(selected, forceRefresh, cancellationToken)
            .ConfigureAwait(false)) yield return item;
    }

    public async Task ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (ResolveRegistrationsAsync is not null)
        {
            IReadOnlyList<ProviderRefreshRegistration> resolved = await ResolveRegistrationsAsync(
                _baseRegistrations, cancellationToken).ConfigureAwait(false);
            if (resolved.Select(item => item.InstanceKey.Value).Distinct(StringComparer.Ordinal).Count() != resolved.Count)
                throw new InvalidOperationException("Provider account registrations must be unique.");
            _registrations = resolved;
        }

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
            .Select(registration => ReadCacheAsync(registration, cancellationToken))
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
                    mergedSnapshots[snapshot.InstanceKey.Value] = snapshot;
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
        yield return new CacheFirstEvent.CachePublished(published,
            _registrations.Select(item => item.InstanceKey.Value).ToHashSet(StringComparer.Ordinal));

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
            registration.OperationGate,
            registration.BeforeSnapshotSaveAsync);
        await foreach (CacheFirstEvent item in partitionRefresh
                           .RunAsync(forceRefresh, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (item is CacheFirstEvent.ProviderCompleted completed)
            {
                return new CacheFirstEvent.ProviderCompleted(completed.ProviderId, completed.Outcome,
                    completed.CacheStatus, registration.InstanceKey);
            }
        }

        throw new InvalidOperationException(
            $"Provider '{registration.Provider.Descriptor.Id.Value}' produced no completion event.");
    }

    private static async Task<SnapshotCacheReadResult> ReadCacheAsync(
        ProviderRefreshRegistration registration, CancellationToken token)
    {
        try { return await registration.Store.LoadAsync(token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            return new SnapshotCacheReadResult.Corrupt(Path.GetFileName(registration.Store.DocumentPath));
        }
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
