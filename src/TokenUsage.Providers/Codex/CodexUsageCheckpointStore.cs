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
    private readonly VersionedDocumentFile _admission;
    private readonly UsageSourceInstanceId _sourceAuthority;
    private bool _requiresMigrationBackup;

    public CodexUsageCheckpointStore(string path, TimeProvider clock, UsageSourceInstanceId sourceAuthority)
    {
        _sourceAuthority = sourceAuthority;
        _document = new VersionedDocumentFile(
            path,
            "TokenUsage.CodexUsageCheckpoint",
            clock,
            "Timed out while waiting for the Codex usage checkpoint lock.");
        _admission = new VersionedDocumentFile(path + ".admission", "TokenUsage.CodexUsageCheckpoint",
            clock, "Timed out while waiting for the Codex usage admission lock.");
    }

    public Task<(TResult Result, IUsageReadCheckpoint Checkpoint)> PrepareAsync<TResult>(
        Func<CodexUsageCheckpointState, TResult> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return _document.RunLockedAsync(() =>
        {
            string? expected = Fingerprint();
            string? admission = AdmissionFingerprint();
            CodexUsageCheckpointState state = Load();
            TResult result = update(state);
            return (result, (IUsageReadCheckpoint)new PendingCheckpoint(this, state, expected, admission));
        }, cancellationToken);
    }

    private string? Fingerprint()
    {
        if (!_document.Exists) return null;
        using var stream = File.OpenRead(_document.DocumentPath);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private string? AdmissionFingerprint() => _admission.Exists
        ? Convert.ToHexString(_admission.ReadBoundedBytes(16)) : null;

    private sealed class PendingCheckpoint(CodexUsageCheckpointStore owner,
        CodexUsageCheckpointState state, string? expected, string? admission) : IUsageReadCheckpoint
    {
        private bool _committed;
        public async Task PersistAsync(Func<Task<bool>> persist, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(persist);
            await owner._document.RunLockedAsync(() =>
            {
                if (_committed) return true;
                if (owner.Fingerprint() != expected || owner.AdmissionFingerprint() != admission)
                    throw new IOException("Codex checkpoint changed after this read; retry collection.");
                cancellationToken.ThrowIfCancellationRequested();
                // Fence other prepared reads even if persistence fails or withholds rows.
                // This file has no offsets or usage: source progress remains unchanged.
                owner._admission.WriteAtomically(Guid.NewGuid().ToByteArray(), 16);
                // RunLockedAsync owns a thread-affine mutex on a worker thread. Keep that
                // thread until persistence finishes; the callback must not acquire this lock.
                if (persist().GetAwaiter().GetResult()) owner.Write(state);
                _committed = true;
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private CodexUsageCheckpointState Load()
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

            _requiresMigrationBackup = document.SchemaVersion < SchemaVersion;
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
                    || string.IsNullOrWhiteSpace(file.Model))
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
        catch (Exception exception) when (exception is JsonException
            or VersionedDocumentFormatException
            or InvalidDataException
            or ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            return RejectInvalid();
        }
    }

    private void Write(CodexUsageCheckpointState state)
    {
        if (_requiresMigrationBackup && _document.Exists)
        {
            string backup = _document.DocumentPath + ".pre-v4";
            if (!File.Exists(backup)) File.Copy(_document.DocumentPath, backup);
            _requiresMigrationBackup = false;
        }
        var document = new DocumentV1
        {
            SchemaVersion = SchemaVersion,
            SourceAuthority = state.SourceAuthority?.Value,
            Files = state.Files
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new FileV1
                {
                    SessionIdentity = item.Key,
                    PathHash = item.Value.PathHash,
                    AuthorityPathHash = item.Value.AuthorityPathHash,
                    Offset = item.Value.Offset,
                    Model = item.Value.Model,
                    Previous = FromTokens(item.Value.Previous),
                    SawSessionMeta = item.Value.SawSessionMeta,
                    ChildReplayPending = item.Value.ChildReplayPending,
                    ChildCreatedAtUnixSeconds = item.Value.ChildCreatedAtUnixSeconds,
                    PreviousTimestamp = item.Value.PreviousTimestamp,
                    PreviousMeasured = item.Value.PreviousMeasured,
                    ReplayThroughUtc = item.Value.ReplayThroughUtc,
                    ObservedModel = item.Value.ObservedModel, Effort = item.Value.Effort, Tier = item.Value.Tier,
                    Observations = item.Value.Observations.Select(value => new ObservationV1
                    {
                        Key = value.Key, Timestamp = value.Timestamp, Model = value.Model,
                        Tokens = FromTokens(value.Tokens), Precision = value.Precision, IntervalStart = value.IntervalStart,
                        ObservedModel = value.ObservedModel, Effort = value.Effort, Tier = value.Tier,
                        Measured = value.Measured,
                        RecordKind = value.RecordKind, RepresentationRevision = value.RepresentationRevision,
                    }).ToList(),
                })
                .ToList(),
        };
        _document.WriteAtomically(stream => JsonSerializer.Serialize(stream, document, SerializerOptions));
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
        public string? SourceAuthority { get; init; }
        public int SchemaVersion { get; init; }

        public List<FileV1> Files { get; init; } = [];
    }

    private sealed class FileV1
    {
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
