using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Storage;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Codex;

internal sealed class CodexUsageCheckpointStore
{
    private const int SchemaVersion = 3;
    private const int MaximumDocumentBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = 16,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly VersionedDocumentFile _document;
    private bool _requiresMigrationBackup;

    public CodexUsageCheckpointStore(string path, TimeProvider clock)
    {
        _document = new VersionedDocumentFile(
            path,
            "TokenUsage.CodexUsageCheckpoint",
            clock,
            "Timed out while waiting for the Codex usage checkpoint lock.");
    }

    public Task<TResult> UpdateAsync<TResult>(
        Func<CodexUsageCheckpointState, TResult> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return _document.RunLockedAsync(() =>
        {
            CodexUsageCheckpointState state = Load();
            TResult result = update(state);
            Write(state);
            return result;
        }, cancellationToken);
    }

    private CodexUsageCheckpointState Load()
    {
        if (!_document.Exists)
        {
            return new CodexUsageCheckpointState();
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaximumDocumentBytes);
            DocumentV1? document = JsonSerializer.Deserialize<DocumentV1>(
                VersionedDocumentFile.RemoveUtf8Preamble(bytes).Span,
                SerializerOptions);
            if (document?.SchemaVersion > SchemaVersion)
                throw new NotSupportedException("Codex checkpoint schema is newer than supported; the original was preserved.");
            if (document is null
                || document.SchemaVersion is not (2 or SchemaVersion)
                || document.Files is null)
            {
                return RejectInvalid();
            }

            _requiresMigrationBackup = document.SchemaVersion < SchemaVersion;
            var state = new CodexUsageCheckpointState();
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
                checkpoint.ReplayThroughUtc = file.ReplayThroughUtc;
                checkpoint.ObservedModel = file.ObservedModel;
                checkpoint.Effort = file.Effort;
                checkpoint.Tier = file.Tier;
                if (document.SchemaVersion == SchemaVersion)
                    checkpoint.Observations.AddRange(file.Observations.Select(item => new CodexNumericObservation(
                        item.Key, item.Timestamp, item.Model, ToTokens(item.Tokens)!, item.Precision, item.IntervalStart, item.ObservedModel, item.Effort, item.Tier)));

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
            string backup = _document.DocumentPath + ".pre-v3";
            if (!File.Exists(backup)) File.Copy(_document.DocumentPath, backup);
            _requiresMigrationBackup = false;
        }
        var document = new DocumentV1
        {
            SchemaVersion = SchemaVersion,
            Files = state.Files
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new FileV1
                {
                    SessionIdentity = item.Key,
                    PathHash = item.Value.PathHash,
                    Offset = item.Value.Offset,
                    Model = item.Value.Model,
                    Previous = FromTokens(item.Value.Previous),
                    SawSessionMeta = item.Value.SawSessionMeta,
                    ChildReplayPending = item.Value.ChildReplayPending,
                    ChildCreatedAtUnixSeconds = item.Value.ChildCreatedAtUnixSeconds,
                    PreviousTimestamp = item.Value.PreviousTimestamp,
                    ReplayThroughUtc = item.Value.ReplayThroughUtc,
                    ObservedModel = item.Value.ObservedModel, Effort = item.Value.Effort, Tier = item.Value.Tier,
                    Observations = item.Value.Observations.Select(value => new ObservationV1
                    {
                        Key = value.Key, Timestamp = value.Timestamp, Model = value.Model,
                        Tokens = FromTokens(value.Tokens), Precision = value.Precision, IntervalStart = value.IntervalStart,
                        ObservedModel = value.ObservedModel, Effort = value.Effort, Tier = value.Tier,
                    }).ToList(),
                })
                .ToList(),
        };
        _document.WriteAtomically(
            JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions),
            MaximumDocumentBytes);
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
        public int SchemaVersion { get; init; }

        public List<FileV1> Files { get; init; } = [];
    }

    private sealed class FileV1
    {
        public string SessionIdentity { get; init; } = string.Empty;

        public string PathHash { get; init; } = string.Empty;

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
        public DateTimeOffset? ReplayThroughUtc { get; init; }

        public List<ObservationV1> Observations { get; init; } = [];

    }

    private sealed class ObservationV1
    {
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
    public DateTimeOffset? ReplayThroughUtc { get; set; }

    public List<CodexNumericObservation> Observations { get; } = [];

    public string PathHash { get; set; } = pathHash;

    public long Offset { get; set; } = offset;

    public string Model { get; set; } = model;

    public TokenBreakdown? Previous { get; set; } = previous;

    public bool SawSessionMeta { get; set; } = sawSessionMeta;

    public bool ChildReplayPending { get; set; } = childReplayPending;

    public long? ChildCreatedAtUnixSeconds { get; set; } = childCreatedAtUnixSeconds;

}

internal sealed record CodexNumericObservation(string Key, DateTimeOffset Timestamp,
    string Model, TokenBreakdown Tokens, UsageTimePrecision Precision, DateTimeOffset? IntervalStart, string? ObservedModel, string? Effort, string? Tier);
