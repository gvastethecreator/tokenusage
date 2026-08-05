using System.Text;

namespace WOpenUsage.Providers.Tests.Codex;

internal sealed class ScriptedCodexJsonlPeer : IDisposable
{
    private readonly MemoryStream _responses;
    private readonly MemoryStream _requests = new();

    public ScriptedCodexJsonlPeer(params string[] responseLines)
    {
        ArgumentNullException.ThrowIfNull(responseLines);
        string script = responseLines.Length == 0
            ? string.Empty
            : string.Join('\n', responseLines) + "\n";
        _responses = new MemoryStream(Encoding.UTF8.GetBytes(script), writable: false);
    }

    public WOpenUsage.Providers.Codex.CodexAppServerClient CreateClient(
        WOpenUsage.Providers.Codex.CodexClientOptions? options = null) =>
        new(
            _responses,
            _requests,
            options ?? CreateDefaultOptions(),
            leaveOpen: true);

    public IReadOnlyList<string> GetRequestLines()
    {
        string content = Encoding.UTF8.GetString(_requests.ToArray());
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    public void Dispose()
    {
        _responses.Dispose();
        _requests.Dispose();
    }

    public static WOpenUsage.Providers.Codex.CodexClientOptions CreateDefaultOptions(
        TimeSpan? timeout = null,
        int maximumLineBytes = 4096) =>
        new(
            "wopenusage",
            "0.1.0",
            "WOpenUsage",
            timeout ?? TimeSpan.FromSeconds(2),
            maximumLineBytes);
}

internal sealed class BlockingReadStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
