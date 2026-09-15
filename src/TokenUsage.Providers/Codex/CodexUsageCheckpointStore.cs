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
                || document.SchemaVersion is not (2 or 3 or SchemaVersion)
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
            if (document.SessionAdmissionEpoch is > 0)
            {
                state.SessionAdmissionEpoch = document.SessionAdmissionEpoch;
                foreach (string key in document.SessionAdmissionEventKeys ?? [])
                {
                    if (OpaqueAttributionKey.IsHexSha256(key))
                    {
                        state.SessionAdmissionEventKeys.Add(key);
                    }
                }

                foreach (string key in document.SessionCommittedEventKeys ?? [])
                {
                    if (OpaqueAttributionKey.IsHexSha256(key))
                    {
                        state.SessionCommittedEventKeys.Add(key);
                    }
                }
            }

            if (document.ProjectAdmissionEpoch is > 0)
            {
                state.ProjectAdmissionEpoch = document.ProjectAdmissionEpoch;
                foreach (string key in document.ProjectAdmissionEventKeys ?? [])
                {
                    if (OpaqueAttributionKey.IsHexSha256(key))
                    {
                        state.ProjectAdmissionEventKeys.Add(key);
                    }
                }

                foreach (string key in document.ProjectCommittedEventKeys ?? [])
                {
                    if (OpaqueAttributionKey.IsHexSha256(key))
                    {
                        state.ProjectCommittedEventKeys.Add(key);
                    }
                }
            }

            foreach (OperationAdmissionV1 admission in document.OperationAdmissions ?? [])
            {
                if (string.IsNullOrWhiteSpace(admission.Capability)
                    || admission.Epoch is not > 0)
                {
                    continue;
                }

                try
                {
                    _ = new AttributionCapability(admission.Capability);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                CodexOperationAdmissionState bucket = state.AdmissionFor(admission.Capability);
                bucket.Epoch = admission.Epoch;
                foreach (string key in admission.Keys ?? [])
                {
                    if (OpaqueAttributionKey.IsHexSha256(key))
                    {
                        bucket.Keys.Add(key);
                    }
                }

                foreach (string key in admission.CommittedKeys ?? [])
                {
                    if (OpaqueAttributionKey.IsHexSha256(key))
                    {
                        bucket.CommittedKeys.Add(key);
                    }
                }
            }
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
                    document.SchemaVersion == 2 ? 0 : file.Offset,
                    file.Model,
                    document.SchemaVersion == 2 ? null : ToTokens(file.Previous),
                    document.SchemaVersion != 2 && file.SawSessionMeta,
                    document.SchemaVersion != 2 && file.ChildReplayPending,
                    document.SchemaVersion == 2 ? null : file.ChildCreatedAtUnixSeconds);
                checkpoint.PreviousTimestamp = document.SchemaVersion == 2 ? null : file.PreviousTimestamp;
                checkpoint.PreviousMeasured = document.SchemaVersion == SchemaVersion ? file.PreviousMeasured : CodexMeasuredComponents.None;
                checkpoint.AuthorityPathHash = file.AuthorityPathHash ?? file.PathHash;
                checkpoint.ReplayThroughUtc = file.ReplayThroughUtc;
                checkpoint.ObservedModel = file.ObservedModel;
                checkpoint.Effort = file.Effort;
                checkpoint.Tier = file.Tier;
                if (OpaqueAttributionKey.IsHexSha256(file.SessionKey ?? string.Empty)
                    && file.AttributionEpoch is > 0)
                {
                    checkpoint.SessionKey = file.SessionKey;
                    checkpoint.AttributionEpoch = file.AttributionEpoch;
                    checkpoint.ParentSessionKey = OpaqueAttributionKey.IsHexSha256(file.ParentSessionKey ?? string.Empty)
                        ? file.ParentSessionKey
                        : null;
                }
                if (OpaqueAttributionKey.IsHexSha256(file.ProjectKey ?? string.Empty)
                    && file.ProjectEpoch is > 0)
                {
                    checkpoint.ProjectKey = file.ProjectKey;
                    checkpoint.ProjectEpoch = file.ProjectEpoch;
                    checkpoint.SawMultipleProjects = file.SawMultipleProjects;
                }
                if (document.SchemaVersion >= 3)
                    checkpoint.Observations.AddRange(file.Observations.Select(item => new CodexNumericObservation(
                        item.Key, item.Timestamp, item.Model, ToTokens(item.Tokens)!, item.Precision, item.IntervalStart, item.ObservedModel, item.Effort, item.Tier,
                        document.SchemaVersion == SchemaVersion ? item.Measured : CodexMeasuredComponents.None,
                        document.SchemaVersion == SchemaVersion ? item.RecordKind
                            : item.Precision == UsageTimePrecision.Interval ? UsageRecordKind.IntervalDelta : UsageRecordKind.Unknown,
                        document.SchemaVersion == SchemaVersion ? item.RepresentationRevision : null,
                        document.SchemaVersion == SchemaVersion ? item.ProjectKey : null,
                        document.SchemaVersion == SchemaVersion ? item.ProjectEpoch : null,
                        document.SchemaVersion == SchemaVersion ? item.ProjectMappingKind : null)));
                foreach (OperationV1 operation in file.Operations ?? [])
                {
                    if (!OpaqueAttributionKey.IsHexSha256(operation.Key)
                        || string.IsNullOrWhiteSpace(operation.Capability)
                        || operation.Quantity < 1
                        || !UsageOperationKindCodec.TryParse(operation.Kind, out _)
                        || !UsageOperationOutcomeCodec.TryParse(operation.Outcome, out _))
                    {
                        continue;
                    }

                    bool unlabeled = string.IsNullOrEmpty(operation.Tool);
                    if (!unlabeled
                        && (!BoundedOperationLabel.TryNormalize(operation.Tool, out _)
                            || (operation.Server is not null
                                && !BoundedOperationLabel.TryNormalize(operation.Server, out _))
                            || (operation.SessionKey is not null
                                && !OpaqueAttributionKey.IsHexSha256(operation.SessionKey))))
                    {
                        continue;
                    }

                    checkpoint.Operations.Add(new CodexOperationObservation(
                        operation.Key,
                        operation.StartedAt,
                        operation.EndedAt,
                        operation.Kind,
                        unlabeled ? string.Empty : operation.Tool,
                        unlabeled ? null : operation.Server,
                        operation.Outcome,
                        operation.Capability,
                        unlabeled ? null : operation.SessionKey,
                        operation.Quantity,
                        operation.LegacyKey));
                }
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
            SessionAdmissionEpoch = state.SessionAdmissionEpoch,
            SessionAdmissionEventKeys = state.SessionAdmissionEventKeys.Order(StringComparer.Ordinal).ToList(),
            SessionCommittedEventKeys = state.SessionCommittedEventKeys.Count == 0
                ? null
                : state.SessionCommittedEventKeys.Order(StringComparer.Ordinal).ToList(),
            ProjectAdmissionEpoch = state.ProjectAdmissionEpoch,
            ProjectAdmissionEventKeys = state.ProjectAdmissionEventKeys.Order(StringComparer.Ordinal).ToList(),
            ProjectCommittedEventKeys = state.ProjectCommittedEventKeys.Count == 0
                ? null
                : state.ProjectCommittedEventKeys.Order(StringComparer.Ordinal).ToList(),
            OperationAdmissions = state.OperationAdmissions.Count == 0
                ? null
                : state.OperationAdmissions
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => new OperationAdmissionV1
                    {
                        Capability = item.Key,
                        Epoch = item.Value.Epoch,
                        Keys = item.Value.Keys.Count == 0
                            ? null
                            : item.Value.Keys.Order(StringComparer.Ordinal).ToList(),
                        CommittedKeys = item.Value.CommittedKeys.Count == 0
                            ? null
                            : item.Value.CommittedKeys.Order(StringComparer.Ordinal).ToList(),
                    })
                    .ToList(),
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
                    SessionKey = item.Value.SessionKey,
                    ParentSessionKey = item.Value.ParentSessionKey,
                    AttributionEpoch = item.Value.AttributionEpoch,
                    ProjectKey = item.Value.ProjectKey,
                    ProjectEpoch = item.Value.ProjectEpoch,
                    SawMultipleProjects = item.Value.SawMultipleProjects,
                    ObservedModel = item.Value.ObservedModel, Effort = item.Value.Effort, Tier = item.Value.Tier,
                    Observations = item.Value.Observations.Select(value => new ObservationV1
                    {
                        Key = value.Key, Timestamp = value.Timestamp, Model = value.Model,
                        Tokens = FromTokens(value.Tokens), Precision = value.Precision, IntervalStart = value.IntervalStart,
                        ObservedModel = value.ObservedModel, Effort = value.Effort, Tier = value.Tier,
                        Measured = value.Measured,
                        RecordKind = value.RecordKind, RepresentationRevision = value.RepresentationRevision,
                        ProjectKey = value.ProjectKey,
                        ProjectEpoch = value.ProjectEpoch,
                        ProjectMappingKind = value.ProjectMappingKind,
                    }).ToList(),
                    Operations = item.Value.Operations.Count == 0
                        ? null
                        : item.Value.Operations.Select(value => new OperationV1
                        {
                            Key = value.Key,
                            StartedAt = value.StartedAt,
                            EndedAt = value.EndedAt,
                            Kind = value.Kind,
                            Tool = value.Tool,
                            Server = value.Server,
                            Outcome = value.Outcome,
                            Capability = value.Capability,
                            SessionKey = value.SessionKey,
                            Quantity = value.Quantity,
                            LegacyKey = value.LegacyKey,
                        }).ToList(),
                })
                .ToList(),
        };
        _document.WriteAtomically(stream => JsonSerializer.Serialize(stream, document, SerializerOptions));
    }

    public void ClearSessionAttribution()
    {
        _document.RunLockedAsync(() =>
        {
            if (!_document.Exists)
            {
                return true;
            }

            CodexUsageCheckpointState state = Load();
            foreach (CodexUsageFileCheckpoint checkpoint in state.Files.Values)
            {
                checkpoint.SessionKey = null;
                checkpoint.ParentSessionKey = null;
                checkpoint.AttributionEpoch = null;
                if (checkpoint.Operations.Count == 0)
                {
                    continue;
                }

                CodexOperationObservation[] stripped = checkpoint.Operations
                    .Select(item => item with { SessionKey = null })
                    .ToArray();
                checkpoint.Operations.Clear();
                checkpoint.Operations.AddRange(stripped);
            }

            Write(state);
            return true;
        }).GetAwaiter().GetResult();
    }

    public void ClearProjectAttribution()
    {
        _document.RunLockedAsync(() =>
        {
            if (!_document.Exists)
            {
                return true;
            }

            CodexUsageCheckpointState state = Load();
            foreach (CodexUsageFileCheckpoint checkpoint in state.Files.Values)
            {
                checkpoint.ProjectKey = null;
                checkpoint.ProjectEpoch = null;
                checkpoint.SawMultipleProjects = false;
                if (checkpoint.Observations.Count == 0)
                {
                    continue;
                }

                CodexNumericObservation[] stripped = checkpoint.Observations
                    .Select(item => item with { ProjectKey = null, ProjectEpoch = null, ProjectMappingKind = null })
                    .ToArray();
                checkpoint.Observations.Clear();
                checkpoint.Observations.AddRange(stripped);
            }

            Write(state);
            return true;
        }).GetAwaiter().GetResult();
    }

    public void ClearOperationAttribution(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        _document.RunLockedAsync(() =>
        {
            if (!_document.Exists)
            {
                return true;
            }

            CodexUsageCheckpointState state = Load();
            CodexOperationAdmissionState bucket = state.AdmissionFor(capability);
            foreach (CodexUsageFileCheckpoint checkpoint in state.Files.Values)
            {
                foreach (CodexOperationObservation operation in checkpoint.Operations)
                {
                    if (operation.Capability == capability)
                    {
                        bucket.Keys.Add(operation.Key);
                    }
                }

                checkpoint.Operations.RemoveAll(item => item.Capability == capability);
            }

            bucket.CommittedKeys.Clear();
            bucket.Epoch = null;

            Write(state);
            return true;
        }).GetAwaiter().GetResult();
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
        public long? SessionAdmissionEpoch { get; init; }
        public List<string>? SessionAdmissionEventKeys { get; init; }
        public List<string>? SessionCommittedEventKeys { get; init; }
        public long? ProjectAdmissionEpoch { get; init; }
        public List<string>? ProjectAdmissionEventKeys { get; init; }
        public List<string>? ProjectCommittedEventKeys { get; init; }
        public List<OperationAdmissionV1>? OperationAdmissions { get; init; }

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
        public string? SessionKey { get; init; }
        public string? ParentSessionKey { get; init; }
        public long? AttributionEpoch { get; init; }
        public string? ProjectKey { get; init; }
        public long? ProjectEpoch { get; init; }
        public bool SawMultipleProjects { get; init; }

        public List<ObservationV1> Observations { get; init; } = [];
        public List<OperationV1>? Operations { get; init; }

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
        public string? ProjectKey { get; init; }
        public long? ProjectEpoch { get; init; }
        public string? ProjectMappingKind { get; init; }
    }

    private sealed class OperationAdmissionV1
    {
        public string Capability { get; init; } = string.Empty;
        public long? Epoch { get; init; }
        public List<string>? Keys { get; init; }
        public List<string>? CommittedKeys { get; init; }
    }

    private sealed class OperationV1
    {
        public string Key { get; init; } = string.Empty;
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? EndedAt { get; init; }
        public string Kind { get; init; } = string.Empty;
        public string Tool { get; init; } = string.Empty;
        public string? Server { get; init; }
        public string Outcome { get; init; } = string.Empty;
        public string Capability { get; init; } = string.Empty;
        public string? SessionKey { get; init; }
        public int Quantity { get; init; } = 1;
        public string? LegacyKey { get; init; }
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
    public long? SessionAdmissionEpoch { get; set; }
    public HashSet<string> SessionAdmissionEventKeys { get; } = new(StringComparer.Ordinal);
    public HashSet<string> SessionCommittedEventKeys { get; } = new(StringComparer.Ordinal);
    public long? ProjectAdmissionEpoch { get; set; }
    public HashSet<string> ProjectAdmissionEventKeys { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ProjectCommittedEventKeys { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, CodexOperationAdmissionState> OperationAdmissions { get; } =
        new(StringComparer.Ordinal);

    public CodexOperationAdmissionState AdmissionFor(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        if (!OperationAdmissions.TryGetValue(capability, out CodexOperationAdmissionState? bucket))
        {
            bucket = new CodexOperationAdmissionState();
            OperationAdmissions[capability] = bucket;
        }

        return bucket;
    }
}

internal sealed class CodexOperationAdmissionState
{
    public long? Epoch { get; set; }
    public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);
    public HashSet<string> CommittedKeys { get; } = new(StringComparer.Ordinal);
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
    public string? SessionKey { get; set; }
    public string? ParentSessionKey { get; set; }
    public long? AttributionEpoch { get; set; }
    public string? ProjectKey { get; set; }
    public long? ProjectEpoch { get; set; }
    public bool SawMultipleProjects { get; set; }

    public List<CodexNumericObservation> Observations { get; } = [];
    public List<CodexOperationObservation> Operations { get; } = [];

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
    UsageRecordKind RecordKind = UsageRecordKind.Unknown, long? RepresentationRevision = null,
    string? ProjectKey = null, long? ProjectEpoch = null, string? ProjectMappingKind = null);

internal sealed record CodexOperationObservation(
    string Key,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Kind,
    string Tool,
    string? Server,
    string Outcome,
    string Capability,
    string? SessionKey,
    int Quantity = 1,
    string? LegacyKey = null);

[Flags]
internal enum CodexMeasuredComponents
{
    None = 0, Input = 1, Output = 2, Reasoning = 4, CacheRead = 8, CacheWrite = 16,
    All = Input | Output | Reasoning | CacheRead | CacheWrite,
}
