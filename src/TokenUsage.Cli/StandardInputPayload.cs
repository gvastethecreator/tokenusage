namespace TokenUsage.Cli;

/// <summary>
/// Reads the payload a hook or status line host pipes in. Hosts write it at once, but one that
/// is killed can leave the pipe open in another process, so the read also ends at a deadline
/// instead of waiting for an end of stream that never comes.
/// </summary>
internal static class StandardInputPayload
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Returns what arrived before the end of stream or the deadline. A payload over
    /// <paramref name="maximumBytes"/> is drained so the writer does not block, and returns empty.
    /// </summary>
    public static async Task<byte[]> ReadAsync(Stream? input, int maximumBytes, CancellationToken cancellationToken)
    {
        if (input is null) return [];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(DefaultTimeout);
        Task expired = Task.Delay(Timeout.Infinite, deadline.Token);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        bool oversized = false;
        try
        {
            while (true)
            {
                // Console and pipe reads ignore cancellation once blocked, so the deadline is
                // raced; an abandoned read ends with the process.
                Task<int> read = input.ReadAsync(chunk, deadline.Token).AsTask();
                if (await Task.WhenAny(read, expired).ConfigureAwait(false) != read) break;
                int count = await read.ConfigureAwait(false);
                if (count == 0) break;
                oversized |= buffer.Length + count > maximumBytes;
                if (!oversized) buffer.Write(chunk, 0, count);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
                                           or NotSupportedException
                                           || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
        }

        cancellationToken.ThrowIfCancellationRequested();
        return oversized ? [] : buffer.ToArray();
    }
}
