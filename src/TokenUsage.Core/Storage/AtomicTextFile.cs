namespace TokenUsage.Core.Storage;

public static class AtomicTextFile
{
    public static async Task WriteAsync(
        string path,
        string payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();
        string staging = path + ".part";
        try
        {
            await File.WriteAllTextAsync(staging, payload, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(staging);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }
}
