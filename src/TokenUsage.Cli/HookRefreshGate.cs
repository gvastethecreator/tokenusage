using System.Security.Cryptography;
using System.Text;

namespace TokenUsage.Cli;

/// <summary>
/// Stop hooks fire after every agent turn, often from many sessions at once. One refresh runs at
/// a time; a hook that arrives meanwhile asks for one more pass and exits, so processes do not
/// pile up and the latest turn is still collected.
/// </summary>
internal sealed class HookRefreshGate(string name)
{
    public static HookRefreshGate ForDataDirectory(string dataDirectory) => new("TokenUsage.HookRefresh."
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).ToUpperInvariant())))[..16]);

    /// <summary>Returns false when another process was already refreshing and will run again.</summary>
    public async Task<bool> RunAsync(Func<Task> refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        // A semaphore, unlike a mutex, can be released from whichever thread the refresh resumes on.
        using var running = new Semaphore(1, 1, name + ".Running");
        using var pending = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Pending");
        bool ran = false;
        while (true)
        {
            if (!running.WaitOne(0))
            {
                pending.Set();
                return ran;
            }

            try
            {
                do await refresh().ConfigureAwait(false);
                while (pending.WaitOne(0));
            }
            finally
            {
                running.Release();
            }

            ran = true;
            // A request that arrived between the last check and the release still gets its pass.
            if (!pending.WaitOne(0)) return true;
        }
    }
}
