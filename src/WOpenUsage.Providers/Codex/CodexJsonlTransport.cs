using System.Buffers;

namespace WOpenUsage.Providers.Codex;

internal sealed class CodexJsonlTransport : IAsyncDisposable
{
    private static readonly byte[] NewLine = [(byte)'\n'];
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly int _maximumLineBytes;
    private readonly byte[] _readBuffer;
    private int _readOffset;
    private int _readCount;

    public CodexJsonlTransport(
        Stream input,
        Stream output,
        int maximumLineBytes,
        bool leaveOpen)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        if (!input.CanRead)
        {
            throw new ArgumentException("Input stream must be readable.", nameof(input));
        }

        if (!output.CanWrite)
        {
            throw new ArgumentException("Output stream must be writable.", nameof(output));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLineBytes);
        _maximumLineBytes = maximumLineBytes;
        _leaveOpen = leaveOpen;
        _readBuffer = new byte[Math.Min(4096, maximumLineBytes + 1)];
    }

    public async ValueTask WriteLineAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length > _maximumLineBytes)
        {
            throw new CodexProtocolException("Codex app-server request exceeded the JSONL line limit.");
        }

        try
        {
            await _output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            throw new CodexProtocolException("Codex app-server JSONL output failed.");
        }
    }

    public async ValueTask<byte[]> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new ArrayBufferWriter<byte>(Math.Min(_maximumLineBytes, 4096));

        while (true)
        {
            if (_readOffset == _readCount)
            {
                _readOffset = 0;
                try
                {
                    _readCount = await _input.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    throw new CodexProtocolException("Codex app-server JSONL input failed.");
                }

                if (_readCount == 0)
                {
                    throw new CodexProtocolException(
                        line.WrittenCount == 0
                            ? "Codex app-server closed the JSONL stream."
                            : "Codex app-server closed a truncated JSONL message.");
                }
            }

            ReadOnlySpan<byte> available = _readBuffer.AsSpan(_readOffset, _readCount - _readOffset);
            int newLineOffset = available.IndexOf((byte)'\n');
            int bytesToCopy = newLineOffset >= 0 ? newLineOffset : available.Length;
            if (line.WrittenCount + bytesToCopy > _maximumLineBytes)
            {
                throw new CodexProtocolException("Codex app-server response exceeded the JSONL line limit.");
            }

            if (bytesToCopy > 0)
            {
                available[..bytesToCopy].CopyTo(line.GetSpan(bytesToCopy));
                line.Advance(bytesToCopy);
            }

            _readOffset += bytesToCopy;
            if (newLineOffset < 0)
            {
                continue;
            }

            _readOffset++;
            ReadOnlySpan<byte> written = line.WrittenSpan;
            int resultLength = written.Length > 0 && written[^1] == (byte)'\r'
                ? written.Length - 1
                : written.Length;
            return written[..resultLength].ToArray();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_leaveOpen)
        {
            return;
        }

        try
        {
            await _input.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (!ReferenceEquals(_input, _output))
            {
                await _output.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
