using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

namespace TokenUsage.Core.Usage;

/// <summary>
/// Serializes local collection across the app and CLI. Hooks leave one pending request rather
/// than waiting in another process; an active collector drains it before finishing.
/// </summary>
public static class LocalUsageRefreshGate
{
    public static async Task<T> RunAsync<T>(string databasePath,
        Func<CancellationToken, Task<T>> refresh, CancellationToken cancellationToken = default) =>
        (await Start(databasePath, refresh, waitForTurn: true, cancellationToken).ConfigureAwait(false)).Result!;

    public static async Task<bool> TryRunAsync<T>(string databasePath,
        Func<CancellationToken, Task<T>> refresh, CancellationToken cancellationToken = default) =>
        (await Start(databasePath, refresh, waitForTurn: false, cancellationToken).ConfigureAwait(false)).Ran;

    private static Task<(bool Ran, T? Result)> Start<T>(string databasePath,
        Func<CancellationToken, Task<T>> refresh, bool waitForTurn, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(refresh);
        string fullPath = Path.GetFullPath(databasePath);
        string identity = OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
        string name = "TokenUsage.LocalUsageRefresh."
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        // A mutex is released by the OS if its process dies. Its thread affinity requires
        // keeping acquisition and release on this worker, including across asynchronous I/O.
        return Task.Factory.StartNew(() => Run(name, fullPath + ".refresh-pending", refresh,
            waitForTurn, cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static (bool Ran, T? Result) Run<T>(string name, string pendingPath,
        Func<CancellationToken, Task<T>> refresh, bool waitForTurn, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var running = new Mutex(false, name);
        if (!waitForTurn)
        {
            // Publish before trying ownership. If the previous owner has already finished its
            // final check, we acquire the released mutex and service our own request.
            MarkPending(pendingPath);
        }

        bool ran = false;
        T? result = default;
        OperationCanceledException? canceled = null;
        while (true)
        {
            bool acquired;
            try
            {
                acquired = waitForTurn && !ran && canceled is null
                    ? WaitHandle.WaitAny([running, cancellationToken.WaitHandle]) == 0
                    : running.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            if (!acquired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return (ran, result);
            }

            try
            {
                do
                {
                    bool pendingPass = ConsumePending(pendingPath);
                    CancellationToken passToken = cancellationToken;
                    if (cancellationToken.IsCancellationRequested)
                    {
                        canceled ??= new OperationCanceledException(cancellationToken);
                        if (!pendingPass) break;
                        // A hook already exited after handing us this request. Its collection
                        // must survive cancellation of the panel's own refresh.
                        passToken = CancellationToken.None;
                    }
                    try
                    {
                        result = refresh(passToken).GetAwaiter().GetResult();
                        ran = true;
                    }
                    catch (OperationCanceledException exception) when (passToken.IsCancellationRequested)
                    {
                        canceled = exception;
                        if (pendingPass) MarkPending(pendingPath);
                    }
                    catch
                    {
                        if (pendingPass) MarkPending(pendingPath);
                        throw;
                    }
                }
                while (File.Exists(pendingPath));
            }
            finally
            {
                running.ReleaseMutex();
            }

            // Close the race between the final pending check and releasing ownership. If a
            // new owner got there first, that owner will service the marker instead.
            if (!File.Exists(pendingPath))
            {
                if (canceled is not null) ExceptionDispatchInfo.Capture(canceled).Throw();
                return (ran, result);
            }
        }
    }

    private static bool ConsumePending(string pendingPath)
    {
        try
        {
            // Opening an existing marker and deleting that handle is atomic with respect to
            // absence. A hook that creates the marker after a failed open keeps its request.
            using var pending = new FileStream(pendingPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void MarkPending(string pendingPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        using var pending = new FileStream(pendingPath, FileMode.OpenOrCreate, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
    }
}
