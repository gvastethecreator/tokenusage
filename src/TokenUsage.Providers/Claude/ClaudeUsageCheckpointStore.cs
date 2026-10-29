using System.Security.Cryptography;
using System.Text.Json;
using TokenUsage.Core.Storage;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Claude;

public sealed record ClaudeUsageScanProgress(long BytesRead, long BytesAdvanced,
    int FilesRead, int PendingFiles, int ExcludedRecords, bool HasReadErrors)
{
    public int RetainedUnreadableRecords { get; init; }
    public int DeferredFutureMessages { get; init; }
}

internal sealed class ClaudeUsageCheckpointStore
{
    private readonly VersionedDocumentFile? _document;
    private readonly VersionedDocumentFile? _admission;
    private readonly string _timeZone;
    private readonly Lock _memoryLock = new();
    private ClaudeUsageEventSource.CheckpointState? _memory;
    private long _memoryRevision;

    public ClaudeUsageCheckpointStore(string? path, string timeZone)
    {
        _timeZone = timeZone;
        if (path is null) return;
        _document = new(path, "TokenUsage.ClaudeUsageCheckpoint", TimeProvider.System,
            "Timed out while waiting for the Claude usage checkpoint lock.");
        _admission = new(path + ".admission", "TokenUsage.ClaudeUsageCheckpoint", TimeProvider.System,
            "Timed out while waiting for the Claude usage checkpoint lock.");
    }

    public async Task<(T Result, IUsageReadCheckpoint Checkpoint)> PrepareAsync<T>(
        Func<ClaudeUsageEventSource.CheckpointState, T> read, CancellationToken token)
    {
        if (_document is not null)
            return await _document.RunLockedAsync(() =>
            {
                string? expected = Fingerprint(_document);
                string? admission = Fingerprint(_admission!);
                ClaudeUsageEventSource.CheckpointState state = Load();
                T result = read(state);
                return (result, (IUsageReadCheckpoint)new Pending(this, state, expected, admission, 0));
            }, token).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            lock (_memoryLock)
            {
                token.ThrowIfCancellationRequested();
                var state = Load();
                T result = read(state);
                return (result, (IUsageReadCheckpoint)new Pending(this, state, null, null, _memoryRevision));
            }
        }, token).ConfigureAwait(false);
    }

    private ClaudeUsageEventSource.CheckpointState Load()
    {
        ClaudeUsageEventSource.CheckpointState? state;
        if (_document?.Exists == true)
        {
            using var stream = File.OpenRead(_document.DocumentPath);
            state = JsonSerializer.Deserialize<ClaudeUsageEventSource.CheckpointState>(stream)
                ?? throw new InvalidDataException("Claude checkpoint is empty; the original was preserved.");
        }
        else state = _memory is null ? null : JsonSerializer.Deserialize<ClaudeUsageEventSource.CheckpointState>(
            JsonSerializer.SerializeToUtf8Bytes(_memory));
        if (state is null) return new() { TimeZone = _timeZone };
        if (state.SchemaVersion != 1)
            throw new NotSupportedException("Claude checkpoint schema is unsupported; the original was preserved.");
        if (state.Files is null || state.Messages is null
            || state.Files.Any(pair => !IsHash(pair.Key) || pair.Value is null || pair.Value.Offset < 0
                || pair.Value.ObservedLength < 0 || pair.Value.LineNumber < 0 || pair.Value.UnreadableRecords < 0
                || pair.Value.PrefixLength is < 0 or > 64)
            || state.Messages.Any(pair => !IsHash(pair.Key) || pair.Value is null || pair.Value.EventIdentity != pair.Key
                || pair.Value.MessageId is not null))
            throw new InvalidDataException("Claude checkpoint is invalid; the original was preserved.");
        if (state.Parser != ClaudeUsageEventSource.ParserVersion || state.TimeZone != _timeZone)
            return new() { TimeZone = _timeZone };
        return state;
    }

    private static bool IsHash(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string? Fingerprint(VersionedDocumentFile document)
    {
        if (!document.Exists) return null;
        using var stream = File.OpenRead(document.DocumentPath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class Pending(ClaudeUsageCheckpointStore owner,
        ClaudeUsageEventSource.CheckpointState state, string? expected, string? admission,
        long revision) : IUsageReadCheckpoint
    {
        private bool _committed;
        public async Task PersistAsync(Func<Task<bool>> persist, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(persist);
            if (owner._document is not null)
            {
                await owner._document.RunLockedAsync(() =>
                {
                    if (_committed) return true;
                    if (Fingerprint(owner._document) != expected || Fingerprint(owner._admission!) != admission)
                        throw new IOException("Claude checkpoint changed after this read; retry collection.");
                    cancellationToken.ThrowIfCancellationRequested();
                    owner._admission!.WriteAtomically(Guid.NewGuid().ToByteArray(), 16);
                    if (persist().GetAwaiter().GetResult())
                        owner._document.WriteAtomically(stream => JsonSerializer.Serialize(stream, state));
                    _committed = true;
                    return true;
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
            await Task.Run(() =>
            {
                lock (owner._memoryLock)
                {
                    if (_committed) return;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (revision != owner._memoryRevision)
                        throw new IOException("Claude checkpoint changed after this read; retry collection.");
                    owner._memoryRevision++;
                    if (persist().GetAwaiter().GetResult()) owner._memory = state;
                    _committed = true;
                }
            }, cancellationToken).ConfigureAwait(false);
        }
    }
}
