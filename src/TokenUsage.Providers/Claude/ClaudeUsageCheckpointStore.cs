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
    private readonly string _timeZone;
    private readonly Lock _memoryLock = new();
    private ClaudeUsageEventSource.CheckpointState? _memory;
    private ClaudeUsageEventSource.CheckpointState? _cached;
    private string? _cachedAdmission;
    private long _memoryRevision;

    public ClaudeUsageCheckpointStore(string? path, string timeZone)
    {
        _timeZone = timeZone;
        if (path is not null)
            _document = new(path, "TokenUsage.ClaudeUsageCheckpoint", TimeProvider.System,
                "Timed out while waiting for the Claude usage checkpoint lock.");
    }

    public async Task<(T Result, IUsageReadCheckpoint Checkpoint)> PrepareAsync<T>(
        Func<ClaudeUsageEventSource.CheckpointState, T> read, CancellationToken token)
    {
        if (_document is not null)
            return await _document.RunLockedAsync(() =>
            {
                using var database = new UsageCheckpointDatabase(_document.DocumentPath + ".sqlite");
                string? admission = database.Read("meta", "admission");
                var state = _cached is not null && _cachedAdmission == admission ? _cached : Load(database);
                _cached = null;
                state.BeginRead();
                T result = read(state);
                return (result, (IUsageReadCheckpoint)new Pending(this, state, admission, 0));
            }, token).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            lock (_memoryLock)
            {
                token.ThrowIfCancellationRequested();
                var state = _memory?.Clone() ?? new() { TimeZone = _timeZone };
                state.BeginRead();
                T result = read(state);
                return (result, (IUsageReadCheckpoint)new Pending(this, state, null, _memoryRevision));
            }
        }, token).ConfigureAwait(false);
    }

    private ClaudeUsageEventSource.CheckpointState Load(UsageCheckpointDatabase database)
    {
        ClaudeUsageEventSource.CheckpointState? state;
        string? header = database.Read("meta", "header");
        if (header is not null)
        {
            state = JsonSerializer.Deserialize<ClaudeUsageEventSource.CheckpointState>(header)
                ?? throw new InvalidDataException("Claude checkpoint is empty; the original was preserved.");
            foreach (var row in database.ReadRecords("file"))
                state.Files.Add(row.Identity, JsonSerializer.Deserialize<ClaudeUsageEventSource.FileCheckpoint>(row.Value)
                    ?? throw new InvalidDataException("Claude checkpoint file is invalid."));
            foreach (var row in database.ReadRecords("message"))
                state.Messages.Add(row.Identity, JsonSerializer.Deserialize<ClaudeUsageEventSource.Candidate>(row.Value)
                    ?? throw new InvalidDataException("Claude checkpoint message is invalid."));
            state.Progress = database.Read("meta", "progress");
            state.Imported = true;
        }
        else if (_document?.Exists == true)
        {
            using var stream = File.OpenRead(_document.DocumentPath);
            state = JsonSerializer.Deserialize<ClaudeUsageEventSource.CheckpointState>(stream)
                ?? throw new InvalidDataException("Claude checkpoint is empty; the original was preserved.");
        }
        else state = new() { TimeZone = _timeZone };
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
            return new() { TimeZone = _timeZone, ResetImported = state.Imported };
        foreach (var pair in state.Messages.Where(pair => pair.Value.AwaitingClock))
            state.AwaitingMessages.Add(pair.Key, pair.Value);
        return state;
    }

    private static void Write(UsageCheckpointDatabase database, ClaudeUsageEventSource.CheckpointState state)
    {
        database.Commit(() =>
        {
            if (state.ResetImported)
            {
                foreach (var row in database.ReadRecords("file").ToArray()) database.Write("file", row.Identity, null);
                foreach (var row in database.ReadRecords("message").ToArray()) database.Write("message", row.Identity, null);
            }
            database.Write("meta", "header", JsonSerializer.Serialize(new ClaudeUsageEventSource.CheckpointState
            { TimeZone = state.TimeZone, NextFileHash = state.NextFileHash, PrunedThrough = state.PrunedThrough }));
            database.Write("meta", "progress", state.Progress);
            IEnumerable<string> files = state.Imported ? state.DirtyFiles : state.Files.Keys;
            foreach (string key in files)
                database.Write("file", key, JsonSerializer.Serialize(state.Files[key]));
            IEnumerable<string> messages = state.Imported ? state.DirtyMessages : state.Messages.Keys;
            foreach (string key in messages)
                database.Write("message", key, JsonSerializer.Serialize(state.Messages[key]));
            foreach (string key in state.RemovedMessages) database.Write("message", key, null);
        });
        state.Imported = true;
        state.ResetImported = false;
    }

    private static bool IsHash(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed class Pending(ClaudeUsageCheckpointStore owner,
        ClaudeUsageEventSource.CheckpointState state, string? admission, long revision) : IUsageReadCheckpoint
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
                    using var database = new UsageCheckpointDatabase(owner._document.DocumentPath + ".sqlite");
                    if (database.Read("meta", "admission") != admission)
                        throw new IOException("Claude checkpoint changed after this read; retry collection.");
                    cancellationToken.ThrowIfCancellationRequested();
                    string fence = Guid.NewGuid().ToString("N");
                    database.Commit(() => database.Write("meta", "admission", fence));
                    if (persist().GetAwaiter().GetResult())
                    {
                        Write(database, state);
                        owner._cached = state;
                        owner._cachedAdmission = fence;
                    }
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
