using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TokenUsage.Core.Storage;

/// <summary>
/// Shared lock / bounded-read / quarantine / atomic-replace protocol for
/// versioned JSON documents under LocalState.
/// </summary>
public sealed class VersionedDocumentFile
{
    public static readonly TimeSpan DefaultMutexTimeout = TimeSpan.FromSeconds(30);

    private readonly string _mutexName;
    private readonly string _lockTimeoutMessage;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _mutexTimeout;

    public VersionedDocumentFile(
        string documentPath,
        string mutexNamePrefix,
        TimeProvider clock,
        string? lockTimeoutMessage = null,
        TimeSpan? mutexTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexNamePrefix);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _mutexTimeout = mutexTimeout ?? DefaultMutexTimeout;
        if (_mutexTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(mutexTimeout));
        }

        DocumentPath = Path.GetFullPath(documentPath);
        if (Path.EndsInDirectorySeparator(DocumentPath)
            || string.IsNullOrWhiteSpace(Path.GetFileName(DocumentPath))
            || Directory.Exists(DocumentPath))
        {
            throw new ArgumentException(
                "The document path must include a file name.",
                nameof(documentPath));
        }

        _mutexName = CreateMutexName(mutexNamePrefix, DocumentPath);
        _lockTimeoutMessage = string.IsNullOrWhiteSpace(lockTimeoutMessage)
            ? "Timed out while waiting for the document lock."
            : lockTimeoutMessage;
    }

    public string DocumentPath { get; }

    public string DocumentDirectory =>
        Path.GetDirectoryName(DocumentPath)
        ?? throw new InvalidOperationException("The document path has no parent directory.");

    public bool Exists => File.Exists(DocumentPath);

    public Task<TResult> RunLockedAsync<TResult>(
        Func<TResult> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var mutex = new Mutex(initiallyOwned: false, _mutexName);
            bool ownsMutex = false;

            try
            {
                try
                {
                    if (cancellationToken.CanBeCanceled)
                    {
                        int signaled = WaitHandle.WaitAny(
                            [mutex, cancellationToken.WaitHandle],
                            _mutexTimeout);
                        if (signaled == 1)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        if (signaled == WaitHandle.WaitTimeout)
                        {
                            throw new TimeoutException(_lockTimeoutMessage);
                        }

                        ownsMutex = true;
                    }
                    else
                    {
                        ownsMutex = mutex.WaitOne(_mutexTimeout);
                        if (!ownsMutex)
                        {
                            throw new TimeoutException(_lockTimeoutMessage);
                        }
                    }
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                cancellationToken.ThrowIfCancellationRequested();
                return operation();
            }
            finally
            {
                if (ownsMutex)
                {
                    mutex.ReleaseMutex();
                }
            }
        }, cancellationToken);
    }

    public byte[] ReadBoundedBytes(int maximumDocumentBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDocumentBytes, 1);
        var file = new FileInfo(DocumentPath);
        if (file.Length is <= 0 || file.Length > maximumDocumentBytes)
        {
            throw new VersionedDocumentFormatException(
                "The document size is outside the allowed range.");
        }

        byte[] bytes = File.ReadAllBytes(DocumentPath);
        if (bytes.Length is <= 0 || bytes.Length > maximumDocumentBytes)
        {
            throw new VersionedDocumentFormatException(
                "The document size is outside the allowed range.");
        }

        return bytes;
    }

    public bool TryReadBoundedBytes(int maximumDocumentBytes, out byte[] bytes)
    {
        bytes = [];
        try
        {
            if (!Exists)
            {
                return false;
            }

            bytes = ReadBoundedBytes(maximumDocumentBytes);
            return true;
        }
        catch (Exception exception) when (exception is VersionedDocumentFormatException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            bytes = [];
            return false;
        }
    }

    public string QuarantineCorrupt()
    {
        string directory = DocumentDirectory;
        Directory.CreateDirectory(directory);
        string fileName = Path.GetFileName(DocumentPath);
        string stamp = _clock.GetUtcNow()
            .ToUniversalTime()
            .ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
        string quarantineFileName = $"{fileName}.corrupt-{stamp}-{Guid.NewGuid():N}";
        string quarantinePath = Path.Combine(directory, quarantineFileName);
        File.Move(DocumentPath, quarantinePath);
        return quarantineFileName;
    }

    public void WriteAtomically(ReadOnlySpan<byte> bytes, int maximumDocumentBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDocumentBytes, 1);
        if (bytes.Length is <= 0 || bytes.Length > maximumDocumentBytes)
        {
            throw new InvalidOperationException(
                $"The document must be between 1 and {maximumDocumentBytes} bytes.");
        }

        string directory = DocumentDirectory;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $"{Path.GetFileName(DocumentPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = 4096,
                    Options = FileOptions.WriteThrough,
                }))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, DocumentPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static ReadOnlyMemory<byte> RemoveUtf8Preamble(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)
            ? bytes.AsMemory(Encoding.UTF8.Preamble.Length)
            : bytes;
    }

    public static bool HasUtf8Preamble(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(Encoding.UTF8.Preamble);

    public static string CreateMutexName(string mutexNamePrefix, string documentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexNamePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        string normalized = Path.GetFullPath(documentPath).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"Local\\{mutexNamePrefix}.{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }
}

public sealed class VersionedDocumentFormatException : Exception
{
    public VersionedDocumentFormatException()
    {
    }

    public VersionedDocumentFormatException(string message)
        : base(message)
    {
    }

    public VersionedDocumentFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
