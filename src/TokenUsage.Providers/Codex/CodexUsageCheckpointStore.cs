using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Storage;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Codex;

internal sealed class CodexUsageCheckpointStore
{
    private const int SchemaVersion = 2;
    private const int MaximumDocumentBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = 16,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly VersionedDocumentFile _document;

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
            if (document is null
                || document.SchemaVersion != SchemaVersion
                || document.Files is null)
            {
                return QuarantineInvalid();
            }

            var state = new CodexUsageCheckpointState();
            foreach (FileV1 file in document.Files)
            {
                if (string.IsNullOrWhiteSpace(file.SessionIdentity)
                    || string.IsNullOrWhiteSpace(file.PathHash)
                    || file.Offset < 0
                    || string.IsNullOrWhiteSpace(file.Model)
                    || file.Daily is null)
                {
                    return QuarantineInvalid();
                }

                var checkpoint = new CodexUsageFileCheckpoint(
                    file.PathHash,
                    file.Offset,
                    file.Model,
                    ToTokens(file.Previous),
                    file.SawSessionMeta,
                    file.ChildReplayPending,
                    file.ChildCreatedAtUnixSeconds);
                foreach (DailyV1 daily in file.Daily)
                {
                    if (string.IsNullOrWhiteSpace(daily.Model))
                    {
                        return QuarantineInvalid();
                    }

                    checkpoint.Daily[(daily.Date, daily.Model)] = ToTokens(daily.Tokens)
                        ?? throw new InvalidDataException("A daily token counter is missing.");
                }

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
            return QuarantineInvalid();
        }
    }

    private void Write(CodexUsageCheckpointState state)
    {
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
                    Daily = item.Value.Daily
                        .OrderBy(value => value.Key.Date)
                        .ThenBy(value => value.Key.Model, StringComparer.Ordinal)
                        .Select(value => new DailyV1
                        {
                            Date = value.Key.Date,
                            Model = value.Key.Model,
                            Tokens = FromTokens(value.Value)!,
                        })
                        .ToList(),
                })
                .ToList(),
        };
        _document.WriteAtomically(
            JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions),
            MaximumDocumentBytes);
    }

    private CodexUsageCheckpointState QuarantineInvalid()
    {
        if (_document.Exists)
        {
            _document.QuarantineCorrupt();
        }

        return new CodexUsageCheckpointState();
    }

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

        public List<DailyV1> Daily { get; init; } = [];
    }

    private sealed class DailyV1
    {
        public DateOnly Date { get; init; }

        public string Model { get; init; } = string.Empty;

        public TokensV1? Tokens { get; init; }
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
    public string PathHash { get; set; } = pathHash;

    public long Offset { get; set; } = offset;

    public string Model { get; set; } = model;

    public TokenBreakdown? Previous { get; set; } = previous;

    public bool SawSessionMeta { get; set; } = sawSessionMeta;

    public bool ChildReplayPending { get; set; } = childReplayPending;

    public long? ChildCreatedAtUnixSeconds { get; set; } = childCreatedAtUnixSeconds;

    public Dictionary<(DateOnly Date, string Model), TokenBreakdown> Daily { get; } = [];
}
