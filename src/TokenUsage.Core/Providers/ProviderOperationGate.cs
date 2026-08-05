using System.Diagnostics.CodeAnalysis;

namespace WOpenUsage.Core.Providers;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WaitAsync never creates SemaphoreSlim's optional wait handle; app-lifetime disposal could race active leases.")]
public sealed class ProviderOperationGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async ValueTask<IAsyncDisposable> EnterAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            SemaphoreSlim? ownedSemaphore = Interlocked.Exchange(ref _semaphore, null);
            ownedSemaphore?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
