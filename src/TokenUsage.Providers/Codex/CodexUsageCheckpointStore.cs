using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Storage;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Codex;

internal sealed class CodexUsageCheckpointStore
{
    private const int SchemaVersion = 4;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = 16,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly VersionedDocumentFile _document;
    private readonly UsageSourceInstanceId _sourceAuthority;
    private readonly string _timeZone;
    private CodexUsageCheckpointState? _cached;
    private string? _cachedAdmission;

    public CodexUsageCheckpointStore(string path, TimeProvider clock, UsageSourceInstanceId sourceAuthority, string timeZone)
    {
        _sourceAuthority = sourceAuthority;
        _timeZone = timeZone;
        _document = new VersionedDocumentFile(
            path,
            "TokenUsage.CodexUsageCheckpoint",
            clock,
            "Timed out while waiting for the Codex usage checkpoint lock.");
    }

    public Task<(TResult Result, IUsageReadCheckpoint Checkpoint)> PrepareAsync<TResult>(
        Func<CodexUsageCheckpointState, TResult> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return _document.RunLockedAsync(() =>
        {
            using var database = new UsageCheckpointDatabase(_document.DocumentPath + ".sqlite");
            string? admission = database.Read("meta", "admission");
            CodexUsageCheckpointState state = _cached is not null && _cachedAdmission == admission
                ? _cached : Load(database);
            _cached = null; // A prepared read exclusively owns its mutable state until admission.
            state.BeginRead();
            TResult result = update(state);
            return (result, (IUsageReadCheckpoint)new PendingCheckpoint(this, state, admission));
        }, cancellationToken);
    }

    private CodexUsageCheckpointState Load(UsageCheckpointDatabase database)
    {
        string? header = database.Read("meta", "header");
        if (header is null) return LoadLegacy();
        DocumentV1 document = JsonSerializer.Deserialize<DocumentV1>(header, SerializerOptions)
            ?? throw new InvalidDataException("Codex checkpoint header is invalid.");
        document.Files.AddRange(database.ReadRecords("file").Select(row =>
            JsonSerializer.Deserialize<FileV1>(row.Value, SerializerOptions)
            ?? throw new InvalidDataException("Codex checkpoint file is invalid.")));
        var files = document.Files.ToDictionary(file => file.SessionIdentity, StringComparer.Ordinal);
        foreach (var row in database.ReadRecords("observation"))
        {
            int separator = row.Identity.IndexOf('\0');
            if (separator < 0 || !files.TryGetValue(row.Identity[..separator], out FileV1? file))
                throw new InvalidDataException("Codex checkpoint observation has no file.");
            file.Observations.Add(JsonSerializer.Deserialize<ObservationV1>(row.Value, SerializerOptions)
                ?? throw new InvalidDataException("Codex checkpoint observation is invalid."));
        }
        CodexUsageCheckpointState state = LoadDocument(document);
        state.Progress = database.Read("meta", "progress");
        state.PrunedThrough = DateOnly.TryParse(database.Read("meta", "pruned"), out DateOnly day) ? day : null;
        if (document.TimeZone != _timeZone) state.PrunedThrough = null;
        state.Imported = true;
        return state;
    }

    private sealed class PendingCheckpoint(CodexUsageCheckpointStore owner,
        CodexUsageCheckpointState state, string? admission) : IUsageReadCheckpoint
    {
        private bool _committed;
        public async Task PersistAsync(Func<Task<bool>> persist, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(persist);
            await owner._document.RunLockedAsync(() =>
            {
                if (_committed) return true;
                using var database = new UsageCheckpointDatabase(owner._document.DocumentPath + ".sqlite");
                if (database.Read("meta", "admission") != admission)
                    throw new IOException("Codex checkpoint changed after this read; retry collection.");
                cancellationToken.ThrowIfCancellationRequested();
                // Fence other prepared reads even if persistence fails or withholds rows.
                // This file has no offsets or usage: source progress remains unchanged.
                string fence = Guid.NewGuid().ToString("N");
                database.Commit(() => database.Write("meta", "admission", fence));
                // RunLockedAsync owns a thread-affine mutex on a worker thread. Keep that
                // thread until persistence finishes; the callback must not acquire this lock.
                if (persist().GetAwaiter().GetResult())
                {
                    owner.Write(database, state);
                    owner._cached = state;
                    owner._cachedAdmission = fence;
                }
                _committed = true;
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private CodexUsageCheckpointState LoadLegacy()
    {
        if (!_document.Exists)
        {
            return new CodexUsageCheckpointState { SourceAuthority = _sourceAuthority };
        }

        try
        {
            // Observation replay grows with usage, unlike a bounded preferences document.
            // Stream the existing schema rather than imposing a total-history byte limit.
            using var stream = File.OpenRead(_document.DocumentPath);
            DocumentV1? document = JsonSerializer.Deserialize<DocumentV1>(stream, SerializerOptions);
            if (document?.SchemaVersion > SchemaVersion)
                throw new NotSupportedException("Codex checkpoint schema is newer than supported; the original was preserved.");
            if (document is null
                // Schema 2 was never written by a tagged build; 3 was.
                || document.SchemaVersion is not (3 or SchemaVersion)
                || document.Files is null)
            {
                return RejectInvalid();
            }

            return LoadDocument(document);
        }
        catch (Exception exception) when (exception is JsonException
            or VersionedDocumentFormatException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return RejectInvalid();
        }
    }

    private CodexUsageCheckpointState LoadDocument(DocumentV1 document)
    {
            if (document.SchemaVersion > SchemaVersion)
                throw new NotSupportedException("Codex checkpoint schema is newer than supported; the original was preserved.");
            if (document.SchemaVersion is not (3 or SchemaVersion)) return RejectInvalid();
            UsageSourceInstanceId? boundAuthority = document.SchemaVersion == SchemaVersion && document.SourceAuthority is { } authority
                ? new UsageSourceInstanceId(authority) : null;
            if (boundAuthority is not null && boundAuthority != _sourceAuthority)
                throw new InvalidDataException("The Codex checkpoint belongs to another profile; the original was preserved.");
            // Earlier checkpoints did not prove a profile binding. Preserve them as unbound.
            var state = new CodexUsageCheckpointState { SourceAuthority = boundAuthority };
            foreach (FileV1 file in document.Files)
            {
                if (string.IsNullOrWhiteSpace(file.SessionIdentity)
                    || string.IsNullOrWhiteSpace(file.PathHash)
                    || file.Offset < 0
                    || file.PrefixLength is < 0 or > 64
                    || string.IsNullOrWhiteSpace(file.Model)
                    || file.Observations is null || file.Observations.Any(item => item.Tokens is null))
                {
                    return RejectInvalid();
                }

                var checkpoint = new CodexUsageFileCheckpoint(
                    file.PathHash,
                    file.Offset,
                    file.Model,
                    ToTokens(file.Previous),
                    file.SawSessionMeta,
                    file.ChildReplayPending,
                    file.ChildCreatedAtUnixSeconds);
                checkpoint.PreviousTimestamp = file.PreviousTimestamp;
                checkpoint.PreviousMeasured = document.SchemaVersion == SchemaVersion ? file.PreviousMeasured : CodexMeasuredComponents.None;
                checkpoint.AuthorityPathHash = file.AuthorityPathHash ?? file.PathHash;
                checkpoint.ReplayThroughUtc = file.ReplayThroughUtc;
                checkpoint.ObservedModel = file.ObservedModel;
                checkpoint.Effort = file.Effort;
                checkpoint.Tier = file.Tier;
                checkpoint.PrefixLength = file.PrefixLength;
                checkpoint.PrefixHash = file.PrefixHash;
                checkpoint.BoundaryHash = file.BoundaryHash;
                checkpoint.Observations.AddRange(file.Observations.Select(item => new CodexNumericObservation(
                    item.Key, item.Timestamp, item.Model, ToTokens(item.Tokens)!, item.Precision, item.IntervalStart, item.ObservedModel, item.Effort, item.Tier,
                    document.SchemaVersion == SchemaVersion ? item.Measured : CodexMeasuredComponents.None,
                    // Match the metadata retained by the usage database migration.
                    // Older checkpoints did not record a kind, even for timed intervals.
                    document.SchemaVersion == SchemaVersion ? item.RecordKind : UsageRecordKind.Unknown,
                    document.SchemaVersion == SchemaVersion ? item.RepresentationRevision : null)));
                if ((checkpoint.PreviousMeasured & ~CodexMeasuredComponents.All) != 0
                    || checkpoint.Observations.Any(item => (item.Measured & ~CodexMeasuredComponents.All) != 0
                        || !Enum.IsDefined(item.RecordKind) || item.RepresentationRevision is <= 0))
                    return RejectInvalid();

                state.Files[file.SessionIdentity] = checkpoint;
            }

            return state;
    }

    private void Write(UsageCheckpointDatabase database, CodexUsageCheckpointState state)
    {
        // The legacy JSON is an immutable recovery copy; only this database advances.
        database.Commit(() =>
        {
            database.Write("meta", "header", JsonSerializer.Serialize(new DocumentV1
            { SchemaVersion = SchemaVersion, SourceAuthority = state.SourceAuthority?.Value,
                Parser = CodexUsageEventSource.ParserVersion, TimeZone = _timeZone }, SerializerOptions));
            database.Write("meta", "progress", state.Progress);
            database.Write("meta", "pruned", state.PrunedThrough?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            IEnumerable<string> dirty = state.Imported ? state.DirtyFiles : state.Files.Keys;
            foreach (string key in dirty)
            {
                CodexUsageFileCheckpoint file = state.Files[key];
                database.Write("file", key, JsonSerializer.Serialize(new FileV1
                {
                    SessionIdentity = key, PathHash = file.PathHash, AuthorityPathHash = file.AuthorityPathHash,
                    Offset = file.Offset, Model = file.Model, Previous = FromTokens(file.Previous),
                    SawSessionMeta = file.SawSessionMeta, ChildReplayPending = file.ChildReplayPending,
                    ChildCreatedAtUnixSeconds = file.ChildCreatedAtUnixSeconds, PreviousTimestamp = file.PreviousTimestamp,
                    PreviousMeasured = file.PreviousMeasured, ReplayThroughUtc = file.ReplayThroughUtc,
                    ObservedModel = file.ObservedModel, Effort = file.Effort, Tier = file.Tier,
                    PrefixLength = file.PrefixLength, PrefixHash = file.PrefixHash, BoundaryHash = file.BoundaryHash,
                }, SerializerOptions));
            }
            IEnumerable<(string Session, CodexNumericObservation Observation)> additions = state.Imported
                ? state.AddedObservations : state.Files.SelectMany(pair => pair.Value.Observations.Select(item => (pair.Key, item)));
            foreach (var (session, value) in additions)
                database.Write("observation", session + "\0" + value.Key, JsonSerializer.Serialize(new ObservationV1
                {
                    Key = value.Key, Timestamp = value.Timestamp, Model = value.Model,
                    Tokens = FromTokens(value.Tokens), Precision = value.Precision, IntervalStart = value.IntervalStart,
                    ObservedModel = value.ObservedModel, Effort = value.Effort, Tier = value.Tier, Measured = value.Measured,
                    RecordKind = value.RecordKind, RepresentationRevision = value.RepresentationRevision,
                }, SerializerOptions));
            foreach (string key in state.RemovedObservations) database.Write("observation", key, null);
        });
        state.Imported = true;
    }

    private static CodexUsageCheckpointState RejectInvalid() =>
        throw new InvalidDataException("Codex checkpoint is invalid; the original was preserved.");

    private static TokenBreakdown? ToTokens(TokensV1? value)
    {
        if (value is null)
        {
            return null;
        }

        return new TokenBreakdown(
            value.Input,
            value.Output,
            value.Reasoning,
            value.CacheRead,
            value.CacheWrite);
    }

    private static TokensV1? FromTokens(TokenBreakdown? value) => value is null
        ? null
        : new TokensV1
        {
            Input = value.Input,
            Output = value.Output,
            Reasoning = value.Reasoning,
            CacheRead = value.CacheRead,
            CacheWrite = value.CacheWrite,
        };

    private sealed class DocumentV1
    {
        public string? Parser { get; init; }
        public string? TimeZone { get; init; }
        public string? SourceAuthority { get; init; }
        public int SchemaVersion { get; init; }

        public List<FileV1> Files { get; init; } = [];
    }

    private sealed class FileV1
    {
        public int PrefixLength { get; init; }
        public string? PrefixHash { get; init; }
        public string? BoundaryHash { get; init; }
        public string SessionIdentity { get; init; } = string.Empty;

        public string PathHash { get; init; } = string.Empty;
        public string? AuthorityPathHash { get; init; }

        public long Offset { get; init; }

        public string Model { get; init; } = "unknown";

        public TokensV1? Previous { get; init; }

        public bool SawSessionMeta { get; init; }

        public bool ChildReplayPending { get; init; }

        public long? ChildCreatedAtUnixSeconds { get; init; }

        public string? ObservedModel { get; init; }
        public string? Effort { get; init; }
        public string? Tier { get; init; }
        public DateTimeOffset? PreviousTimestamp { get; init; }
        public CodexMeasuredComponents PreviousMeasured { get; init; }
        public DateTimeOffset? ReplayThroughUtc { get; init; }

        public List<ObservationV1> Observations { get; init; } = [];

    }

    private sealed class ObservationV1
    {
        public UsageRecordKind RecordKind { get; init; }
        public long? RepresentationRevision { get; init; }
        public CodexMeasuredComponents Measured { get; init; }
        public string Key { get; init; } = string.Empty;
        public DateTimeOffset Timestamp { get; init; }
        public string Model { get; init; } = "unknown";
        public TokensV1? Tokens { get; init; }
        public string? ObservedModel { get; init; }
        public string? Effort { get; init; }
        public string? Tier { get; init; }
        public UsageTimePrecision Precision { get; init; }
        public DateTimeOffset? IntervalStart { get; init; }
    }

    private sealed class TokensV1
    {
        public long Input { get; init; }

        public long Output { get; init; }

        public long Reasoning { get; init; }

        public long CacheRead { get; init; }

        public long CacheWrite { get; init; }
    }
}

internal sealed class CodexUsageCheckpointState
{
    public bool Imported { get; set; }
    public bool ForceSnapshot { get; set; }
    public string? PreviousProgress { get; private set; }
    public string? Progress { get; set; }
    public DateOnly? PrunedThrough { get; set; }
    public HashSet<string> DirtyFiles { get; } = new(StringComparer.Ordinal);
    public List<(string Session, CodexNumericObservation Observation)> AddedObservations { get; } = [];
    public HashSet<string> RemovedObservations { get; } = new(StringComparer.Ordinal);
    public void BeginRead()
    {
        PreviousProgress = Progress;
        ForceSnapshot = Progress is null;
        DirtyFiles.Clear();
        AddedObservations.Clear();
        RemovedObservations.Clear();
    }

    public UsageSourceInstanceId? SourceAuthority { get; set; }
    public Dictionary<string, CodexUsageFileCheckpoint> Files { get; } =
        new(StringComparer.Ordinal);
}

internal sealed class CodexUsageFileCheckpoint(
    string pathHash,
    long offset,
    string model,
    TokenBreakdown? previous,
    bool sawSessionMeta = false,
    bool childReplayPending = false,
    long? childCreatedAtUnixSeconds = null)
{
    public int PrefixLength { get; set; }
    public string? PrefixHash { get; set; }
    public string? BoundaryHash { get; set; }
    public string ObservationIdentity { get; set; } = string.Empty;
    public string? ObservedModel { get; set; }
    public string? Effort { get; set; }
    public string? Tier { get; set; }
    public DateTimeOffset? PreviousTimestamp { get; set; }
    public CodexMeasuredComponents PreviousMeasured { get; set; }
    public DateTimeOffset? ReplayThroughUtc { get; set; }

    public List<CodexNumericObservation> Observations { get; } = [];

    public string PathHash { get; set; } = pathHash;
    public string AuthorityPathHash { get; set; } = pathHash;

    public long Offset { get; set; } = offset;

    public string Model { get; set; } = model;

    public TokenBreakdown? Previous { get; set; } = previous;

    public bool SawSessionMeta { get; set; } = sawSessionMeta;

    public bool ChildReplayPending { get; set; } = childReplayPending;

    public long? ChildCreatedAtUnixSeconds { get; set; } = childCreatedAtUnixSeconds;

}

internal sealed record CodexNumericObservation(string Key, DateTimeOffset Timestamp,
    string Model, TokenBreakdown Tokens, UsageTimePrecision Precision, DateTimeOffset? IntervalStart, string? ObservedModel, string? Effort, string? Tier,
    CodexMeasuredComponents Measured = CodexMeasuredComponents.None,
    UsageRecordKind RecordKind = UsageRecordKind.Unknown, long? RepresentationRevision = null);

[Flags]
internal enum CodexMeasuredComponents
{
    None = 0, Input = 1, Output = 2, Reasoning = 4, CacheRead = 8, CacheWrite = 16,
    All = Input | Output | Reasoning | CacheRead | CacheWrite,
}
