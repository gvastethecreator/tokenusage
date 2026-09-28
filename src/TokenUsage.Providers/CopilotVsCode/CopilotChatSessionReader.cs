using System.Text.Json;

namespace TokenUsage.Providers.CopilotVsCode;

internal enum CopilotChatSessionReadStatus
{
    Complete,
    Partial,
    UnsupportedSchema,
}

internal sealed record CopilotModelTotal(
    string Model,
    long InputTokens,
    long CachedTokens,
    long OutputTokens);

/// <summary>The allowlisted numeric projection of one chat request. It holds no message text.</summary>
internal sealed class CopilotChatRequest
{
    public string? RequestId { get; set; }

    public long? TimestampMilliseconds { get; set; }

    public string? PickerModel { get; set; }

    public long? PromptTokens { get; set; }

    public long? CompletionTokens { get; set; }

    public double? CopilotCredits { get; set; }

    public IReadOnlyList<CopilotModelTotal>? ModelTotals { get; set; }

    public int? ModelState { get; set; }

    public long? CompletedAtMilliseconds { get; set; }

    public bool HasResult { get; set; }

    public string? ResolvedModel { get; set; }

    public long? MetadataPromptTokens { get; set; }

    public long? MetadataOutputTokens { get; set; }
}

internal sealed class CopilotChatSession
{
    public int? Version { get; set; }

    public string? SessionId { get; set; }

    public List<CopilotChatRequest?> Requests { get; } = [];
}

internal sealed record CopilotChatSessionReadResult(
    CopilotChatSession? Session,
    CopilotChatSessionReadStatus Status);

/// <summary>
/// Replays a VS Code chat session file and keeps only allowlisted request fields.
/// A <c>.jsonl</c> file is VS Code's operation log: one JSON entry per line, where
/// kind 0 holds the whole state, 1 sets a value at a key path, 2 pushes to an array
/// (truncating at <c>i</c> first), and 3 deletes a value. A <c>.json</c> file is an
/// older full snapshot with the same state shape. See
/// https://github.com/microsoft/vscode/blob/1f5367975c88d43d6878f0e6e3f7cb1395e35aef/src/vs/workbench/contrib/chat/common/model/objectMutationLog.ts#L195-L215
/// and the serialized request fields in chatSessionOperationLog.ts#L165-L171.
/// Every other value, including prompts, responses, tool data, references, and
/// paths, is skipped with <see cref="Utf8JsonReader.Skip"/> and never materialized.
/// </summary>
internal static class CopilotChatSessionReader
{
    private const int MaximumIdentifierBytes = 256;
    private const int MaximumRequestIndex = 100_000;
    private const int MaximumModelTotals = 64;
    private static readonly int[] SupportedVersions = [1, 2, 3];

    // Tool results nest prompt nodes deeply; the default depth of 64 is not enough.
    private static readonly JsonReaderOptions Options = new() { MaxDepth = 1024 };

    public static CopilotChatSessionReadResult Read(
        ReadOnlySpan<byte> content,
        bool operationLog,
        int maximumLineBytes)
    {
        if (content.StartsWith("﻿"u8))
        {
            content = content[3..];
        }

        return operationLog
            ? ReadOperationLog(content, maximumLineBytes)
            : ReadSnapshot(content);
    }

    private static CopilotChatSessionReadResult ReadSnapshot(ReadOnlySpan<byte> content)
    {
        CopilotChatSession session;
        try
        {
            var reader = new Utf8JsonReader(content, Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return new CopilotChatSessionReadResult(null, CopilotChatSessionReadStatus.Partial);
            }

            session = ReadSession(ref reader);
        }
        catch (JsonException)
        {
            return new CopilotChatSessionReadResult(null, CopilotChatSessionReadStatus.Partial);
        }

        return Finish(session, CopilotChatSessionReadStatus.Complete);
    }

    private static CopilotChatSessionReadResult ReadOperationLog(
        ReadOnlySpan<byte> content,
        int maximumLineBytes)
    {
        CopilotChatSession? session = null;
        CopilotChatSessionReadStatus status = CopilotChatSessionReadStatus.Complete;
        int start = 0;
        while (start < content.Length)
        {
            int relativeEnd = content[start..].IndexOf((byte)'\n');
            bool terminated = relativeEnd >= 0;
            int end = terminated ? start + relativeEnd : content.Length;
            ReadOnlySpan<byte> line = content[start..end].Trim(" \t\r"u8);
            start = end + 1;
            if (line.IsEmpty)
            {
                continue;
            }

            if (line.Length > maximumLineBytes)
            {
                status = CopilotChatSessionReadStatus.Partial;
                continue;
            }

            switch (ApplyEntry(line, ref session))
            {
                case EntryOutcome.Applied:
                    break;
                case EntryOutcome.Unsupported:
                    return new CopilotChatSessionReadResult(null, CopilotChatSessionReadStatus.UnsupportedSchema);
                case EntryOutcome.Invalid when !terminated:
                    // VS Code appends whole lines. An unterminated last line is a write in progress.
                    break;
                default:
                    status = CopilotChatSessionReadStatus.Partial;
                    break;
            }
        }

        return session is null
            ? new CopilotChatSessionReadResult(null, status)
            : Finish(session, status);
    }

    private static CopilotChatSessionReadResult Finish(
        CopilotChatSession session,
        CopilotChatSessionReadStatus status) =>
        session.Version is { } version && SupportedVersions.Contains(version)
            ? new CopilotChatSessionReadResult(session, status)
            : new CopilotChatSessionReadResult(null, CopilotChatSessionReadStatus.UnsupportedSchema);

    private enum EntryOutcome
    {
        Applied,
        Invalid,
        Unsupported,
    }

    private enum PathRoot
    {
        Other,
        Requests,
        Version,
        SessionId,
    }

    private enum RequestField
    {
        Other,
        RequestId,
        Timestamp,
        ModelId,
        PromptTokens,
        CompletionTokens,
        CopilotCredits,
        ModelTotals,
        ModelState,
        Result,
    }

    /// <summary>The first three key-path segments, compared without materializing them.</summary>
    private struct EntryPath
    {
        public int Length;
        public PathRoot Root;
        public int? Index;
        public RequestField Field;
    }

    private static EntryOutcome ApplyEntry(ReadOnlySpan<byte> line, ref CopilotChatSession? session)
    {
        try
        {
            if (!TryReadEnvelope(line, out int kind, out EntryPath path, out int? truncateAt, out bool hasValue))
            {
                return EntryOutcome.Invalid;
            }

            if (kind is < 0 or > 3)
            {
                return EntryOutcome.Unsupported;
            }

            if (kind == 0)
            {
                if (!hasValue)
                {
                    return EntryOutcome.Invalid;
                }

                var reader = PositionAtValue(line);
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    return EntryOutcome.Invalid;
                }

                session = ReadSession(ref reader);
                return EntryOutcome.Applied;
            }

            if (session is null)
            {
                return EntryOutcome.Invalid;
            }

            return kind switch
            {
                1 => ApplySet(line, session, path, hasValue),
                2 => ApplyPush(line, session, path, truncateAt, hasValue),
                _ => ApplyDelete(session, path),
            };
        }
        catch (JsonException)
        {
            return EntryOutcome.Invalid;
        }
        catch (InvalidOperationException)
        {
            return EntryOutcome.Invalid;
        }
    }

    private static bool TryReadEnvelope(
        ReadOnlySpan<byte> line,
        out int kind,
        out EntryPath path,
        out int? truncateAt,
        out bool hasValue)
    {
        kind = -1;
        path = default;
        truncateAt = null;
        hasValue = false;
        bool hasKind = false;
        var reader = new Utf8JsonReader(line, Options);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("kind"u8))
            {
                reader.Read();
                if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out kind))
                {
                    return false;
                }

                hasKind = true;
            }
            else if (reader.ValueTextEquals("k"u8))
            {
                reader.Read();
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    return false;
                }

                path = ReadPath(ref reader);
            }
            else if (reader.ValueTextEquals("i"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int index) && index >= 0)
                {
                    truncateAt = index;
                }
                else if (reader.TokenType != JsonTokenType.Null)
                {
                    return false;
                }
            }
            else if (reader.ValueTextEquals("v"u8))
            {
                hasValue = true;
                reader.Skip();
            }
            else
            {
                reader.Skip();
            }
        }

        return hasKind && reader.TokenType == JsonTokenType.EndObject;
    }

    private static EntryPath ReadPath(ref Utf8JsonReader reader)
    {
        var path = new EntryPath();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int position = path.Length++;
            if (position == 0 && reader.TokenType == JsonTokenType.String)
            {
                path.Root = reader.ValueTextEquals("requests"u8) ? PathRoot.Requests
                    : reader.ValueTextEquals("version"u8) ? PathRoot.Version
                    : reader.ValueTextEquals("sessionId"u8) ? PathRoot.SessionId
                    : PathRoot.Other;
            }
            else if (position == 1 && reader.TokenType == JsonTokenType.Number
                && reader.TryGetInt32(out int index) && index >= 0)
            {
                path.Index = index;
            }
            else if (position == 2 && reader.TokenType == JsonTokenType.String)
            {
                path.Field = ReadRequestField(ref reader);
            }
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }

        return path;
    }

    private static RequestField ReadRequestField(ref Utf8JsonReader reader) =>
        reader.ValueTextEquals("requestId"u8) ? RequestField.RequestId
        : reader.ValueTextEquals("timestamp"u8) ? RequestField.Timestamp
        : reader.ValueTextEquals("modelId"u8) ? RequestField.ModelId
        : reader.ValueTextEquals("promptTokens"u8) ? RequestField.PromptTokens
        : reader.ValueTextEquals("completionTokens"u8) ? RequestField.CompletionTokens
        : reader.ValueTextEquals("copilotCredits"u8) ? RequestField.CopilotCredits
        : reader.ValueTextEquals("modelTotals"u8) ? RequestField.ModelTotals
        : reader.ValueTextEquals("modelState"u8) ? RequestField.ModelState
        : reader.ValueTextEquals("result"u8) ? RequestField.Result
        : RequestField.Other;

    private static Utf8JsonReader PositionAtValue(ReadOnlySpan<byte> line)
    {
        var reader = new Utf8JsonReader(line, Options);
        reader.Read();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("v"u8))
            {
                reader.Read();
                return reader;
            }

            reader.Skip();
        }

        throw new JsonException();
    }

    private static EntryOutcome ApplySet(
        ReadOnlySpan<byte> line,
        CopilotChatSession session,
        EntryPath path,
        bool hasValue)
    {
        if (!hasValue)
        {
            return EntryOutcome.Invalid;
        }

        switch (path.Root)
        {
            case PathRoot.Version when path.Length == 1:
            {
                var reader = PositionAtValue(line);
                session.Version = ReadInt32(ref reader);
                return EntryOutcome.Applied;
            }
            case PathRoot.SessionId when path.Length == 1:
            {
                var reader = PositionAtValue(line);
                session.SessionId = ReadIdentifier(ref reader);
                return EntryOutcome.Applied;
            }
            case PathRoot.Requests when path.Length == 1:
            {
                var reader = PositionAtValue(line);
                session.Requests.Clear();
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    ReadRequestArray(ref reader, session.Requests);
                }

                return EntryOutcome.Applied;
            }
            case PathRoot.Requests when path.Length == 2:
            {
                if (path.Index is not { } index || index > MaximumRequestIndex)
                {
                    return EntryOutcome.Invalid;
                }

                var reader = PositionAtValue(line);
                CopilotChatRequest? request = reader.TokenType == JsonTokenType.StartObject
                    ? ReadRequest(ref reader)
                    : null;
                while (session.Requests.Count <= index)
                {
                    session.Requests.Add(null);
                }

                session.Requests[index] = request;
                return EntryOutcome.Applied;
            }
            case PathRoot.Requests when path.Length == 3 && path.Field != RequestField.Other:
            {
                if (path.Index is not { } index
                    || index >= session.Requests.Count
                    || session.Requests[index] is not { } request)
                {
                    return EntryOutcome.Invalid;
                }

                var reader = PositionAtValue(line);
                ReadRequestField(ref reader, request, path.Field);
                return EntryOutcome.Applied;
            }
            default:
                return EntryOutcome.Applied;
        }
    }

    private static EntryOutcome ApplyPush(
        ReadOnlySpan<byte> line,
        CopilotChatSession session,
        EntryPath path,
        int? truncateAt,
        bool hasValue)
    {
        if (path.Root != PathRoot.Requests || path.Length != 1)
        {
            return EntryOutcome.Applied;
        }

        if (truncateAt is { } index && index < session.Requests.Count)
        {
            session.Requests.RemoveRange(index, session.Requests.Count - index);
        }

        if (!hasValue)
        {
            return EntryOutcome.Applied;
        }

        var reader = PositionAtValue(line);
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            ReadRequestArray(ref reader, session.Requests);
        }

        return EntryOutcome.Applied;
    }

    private static EntryOutcome ApplyDelete(CopilotChatSession session, EntryPath path)
    {
        switch (path.Root)
        {
            case PathRoot.Version when path.Length == 1:
                session.Version = null;
                break;
            case PathRoot.SessionId when path.Length == 1:
                session.SessionId = null;
                break;
            case PathRoot.Requests when path.Length == 1:
                session.Requests.Clear();
                break;
            case PathRoot.Requests when path.Length == 2
                && path.Index is { } index && index < session.Requests.Count:
                session.Requests[index] = null;
                break;
            case PathRoot.Requests when path.Length == 3
                && path.Index is { } index && index < session.Requests.Count
                && session.Requests[index] is { } request:
                ClearRequestField(request, path.Field);
                break;
        }

        return EntryOutcome.Applied;
    }

    private static CopilotChatSession ReadSession(ref Utf8JsonReader reader)
    {
        var session = new CopilotChatSession();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("version"u8))
            {
                reader.Read();
                session.Version = ReadInt32(ref reader);
            }
            else if (reader.ValueTextEquals("sessionId"u8))
            {
                reader.Read();
                session.SessionId = ReadIdentifier(ref reader);
            }
            else if (reader.ValueTextEquals("requests"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    ReadRequestArray(ref reader, session.Requests);
                }
                else
                {
                    reader.Skip();
                }
            }
            else
            {
                reader.Skip();
            }
        }

        return session;
    }

    private static void ReadRequestArray(ref Utf8JsonReader reader, List<CopilotChatRequest?> requests)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (requests.Count > MaximumRequestIndex)
            {
                reader.Skip();
                continue;
            }

            requests.Add(reader.TokenType == JsonTokenType.StartObject ? ReadRequest(ref reader) : null);
            reader.Skip();
        }
    }

    private static CopilotChatRequest ReadRequest(ref Utf8JsonReader reader)
    {
        var request = new CopilotChatRequest();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            RequestField field = ReadRequestField(ref reader);
            if (field == RequestField.Other)
            {
                reader.Skip();
                continue;
            }

            reader.Read();
            ReadRequestField(ref reader, request, field);
        }

        return request;
    }

    /// <summary>Reads the value the reader is positioned on into one allowlisted field.</summary>
    private static void ReadRequestField(ref Utf8JsonReader reader, CopilotChatRequest request, RequestField field)
    {
        switch (field)
        {
            case RequestField.RequestId:
                request.RequestId = ReadIdentifier(ref reader);
                break;
            case RequestField.Timestamp:
                request.TimestampMilliseconds = ReadCount(ref reader);
                break;
            case RequestField.ModelId:
                request.PickerModel = ReadIdentifier(ref reader);
                break;
            case RequestField.PromptTokens:
                request.PromptTokens = ReadCount(ref reader);
                break;
            case RequestField.CompletionTokens:
                request.CompletionTokens = ReadCount(ref reader);
                break;
            case RequestField.CopilotCredits:
                request.CopilotCredits = ReadCredits(ref reader);
                break;
            case RequestField.ModelTotals:
                request.ModelTotals = ReadModelTotals(ref reader);
                break;
            case RequestField.ModelState:
                ReadModelState(ref reader, request);
                break;
            case RequestField.Result:
                ReadResult(ref reader, request);
                break;
            default:
                reader.Skip();
                break;
        }
    }

    private static void ClearRequestField(CopilotChatRequest request, RequestField field)
    {
        switch (field)
        {
            case RequestField.RequestId:
                request.RequestId = null;
                break;
            case RequestField.Timestamp:
                request.TimestampMilliseconds = null;
                break;
            case RequestField.ModelId:
                request.PickerModel = null;
                break;
            case RequestField.PromptTokens:
                request.PromptTokens = null;
                break;
            case RequestField.CompletionTokens:
                request.CompletionTokens = null;
                break;
            case RequestField.CopilotCredits:
                request.CopilotCredits = null;
                break;
            case RequestField.ModelTotals:
                request.ModelTotals = null;
                break;
            case RequestField.ModelState:
                request.ModelState = null;
                request.CompletedAtMilliseconds = null;
                break;
            case RequestField.Result:
                request.HasResult = false;
                request.ResolvedModel = null;
                request.MetadataPromptTokens = null;
                request.MetadataOutputTokens = null;
                break;
        }
    }

    private static void ReadModelState(ref Utf8JsonReader reader, CopilotChatRequest request)
    {
        request.ModelState = null;
        request.CompletedAtMilliseconds = null;
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("value"u8))
            {
                reader.Read();
                request.ModelState = ReadInt32(ref reader);
            }
            else if (reader.ValueTextEquals("completedAt"u8))
            {
                reader.Read();
                request.CompletedAtMilliseconds = ReadCount(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }
    }

    private static void ReadResult(ref Utf8JsonReader reader, CopilotChatRequest request)
    {
        request.HasResult = reader.TokenType == JsonTokenType.StartObject;
        request.ResolvedModel = null;
        request.MetadataPromptTokens = null;
        request.MetadataOutputTokens = null;
        if (!request.HasResult)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (!reader.ValueTextEquals("metadata"u8))
            {
                reader.Skip();
                continue;
            }

            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("resolvedModel"u8))
                {
                    reader.Read();
                    request.ResolvedModel = ReadIdentifier(ref reader);
                }
                else if (reader.ValueTextEquals("promptTokens"u8))
                {
                    reader.Read();
                    request.MetadataPromptTokens = ReadCount(ref reader);
                }
                else if (reader.ValueTextEquals("outputTokens"u8))
                {
                    reader.Read();
                    request.MetadataOutputTokens = ReadCount(ref reader);
                }
                else
                {
                    reader.Skip();
                }
            }
        }
    }

    private static List<CopilotModelTotal>? ReadModelTotals(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return null;
        }

        var totals = new List<CopilotModelTotal>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject || totals.Count >= MaximumModelTotals)
            {
                reader.Skip();
                continue;
            }

            string? model = null;
            long? input = null;
            long? cached = null;
            long? output = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("model"u8))
                {
                    reader.Read();
                    model = ReadIdentifier(ref reader);
                }
                else if (reader.ValueTextEquals("inputTokens"u8))
                {
                    reader.Read();
                    input = ReadCount(ref reader);
                }
                else if (reader.ValueTextEquals("cachedTokens"u8))
                {
                    reader.Read();
                    cached = ReadCount(ref reader);
                }
                else if (reader.ValueTextEquals("outputTokens"u8))
                {
                    reader.Read();
                    output = ReadCount(ref reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            if (model is not null && input is not null && output is not null)
            {
                totals.Add(new CopilotModelTotal(model, input.Value, cached ?? 0, output.Value));
            }
        }

        return totals.Count == 0 ? null : totals;
    }

    /// <summary>Only short identifier strings are materialized; anything longer is not an id.</summary>
    private static string? ReadIdentifier(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }

        if (reader.HasValueSequence || reader.ValueSpan.Length is 0 or > MaximumIdentifierBytes)
        {
            return null;
        }

        string value = reader.GetString()!.Trim();
        return value.Length == 0 || value.Any(char.IsControl) ? null : value;
    }

    private static int? ReadInt32(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value))
        {
            return value;
        }

        reader.Skip();
        return null;
    }

    private static double? ReadCredits(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number
            && reader.TryGetDouble(out double credits)
            && double.IsFinite(credits)
            && credits >= 0)
        {
            return credits;
        }

        reader.Skip();
        return null;
    }

    private static long? ReadCount(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            reader.Skip();
            return null;
        }

        if (reader.TryGetInt64(out long whole))
        {
            return whole >= 0 ? whole : null;
        }

        return reader.TryGetDouble(out double real) && double.IsFinite(real) && real >= 0 && real < long.MaxValue
            ? (long)real
            : null;
    }
}
