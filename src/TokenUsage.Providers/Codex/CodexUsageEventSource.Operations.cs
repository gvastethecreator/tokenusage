using System.Text.Json;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Codex;

public sealed partial class CodexUsageEventSource
{
    private AttributionConsent? _scanMcpConsent;
    private AttributionConsent? _scanSkillsConsent;
    private AttributionConsent? _scanCommandsConsent;
    private AttributionConsent? _scanFilesConsent;
    public DateOnly? McpAttributionBackfillFrom { get; set; }
    public DateOnly? McpAttributionBackfillTo { get; set; }
    public DateOnly? SkillsAttributionBackfillFrom { get; set; }
    public DateOnly? SkillsAttributionBackfillTo { get; set; }
    public DateOnly? CommandsAttributionBackfillFrom { get; set; }
    public DateOnly? CommandsAttributionBackfillTo { get; set; }
    public DateOnly? FilesAttributionBackfillFrom { get; set; }
    public DateOnly? FilesAttributionBackfillTo { get; set; }

    public void ClearStoredOperationAttribution(AttributionCapability capability) =>
        _checkpointStore?.ClearOperationAttribution(capability.Value);

    private static bool MightBeOperational(ReadOnlySpan<byte> bytes) =>
        bytes.IndexOf("mcp_tool_call"u8) >= 0
        || bytes.IndexOf("item_started"u8) >= 0
        || bytes.IndexOf("item_completed"u8) >= 0
        || bytes.IndexOf("collab_agent_spawn"u8) >= 0
        || bytes.IndexOf("exec_command_begin"u8) >= 0
        || bytes.IndexOf("exec_command_end"u8) >= 0
        || bytes.IndexOf("patch_apply_begin"u8) >= 0
        || bytes.IndexOf("patch_apply_end"u8) >= 0;

    private bool TryObserveOperationalEvent(
        string eventType,
        JsonElement root,
        JsonElement payload,
        CodexUsageFileCheckpoint checkpoint)
    {
        if (_attributionKeys is null
            || !TryGetUtcTimestamp(root, "timestamp", out DateTimeOffset timestamp))
        {
            return eventType is "mcp_tool_call_begin" or "mcp_tool_call_end"
                or "item_started" or "item_completed"
                or "collab_agent_spawn_begin" or "collab_agent_spawn_end"
                or "exec_command_begin" or "exec_command_end"
                or "patch_apply_begin" or "patch_apply_end";
        }

        return eventType switch
        {
            "mcp_tool_call_begin" => ObserveMcp(payload, checkpoint, timestamp, begin: true),
            "mcp_tool_call_end" => ObserveMcp(payload, checkpoint, timestamp, begin: false),
            "item_started" => ObserveItem(payload, checkpoint, timestamp, completed: false),
            "item_completed" => ObserveItem(payload, checkpoint, timestamp, completed: true),
            "collab_agent_spawn_begin" => ObserveSpawn(payload, checkpoint, timestamp, begin: true),
            "collab_agent_spawn_end" => ObserveSpawn(payload, checkpoint, timestamp, begin: false),
            "exec_command_begin" => ObserveCommand(payload, checkpoint, timestamp, begin: true),
            "exec_command_end" => ObserveCommand(payload, checkpoint, timestamp, begin: false),
            "patch_apply_begin" => ObservePatch(payload, checkpoint, timestamp, begin: true),
            "patch_apply_end" => ObservePatch(payload, checkpoint, timestamp, begin: false),
            _ => false,
        };
    }

    private bool ObserveMcp(
        JsonElement payload,
        CodexUsageFileCheckpoint checkpoint,
        DateTimeOffset timestamp,
        bool begin)
    {
        if (!TryGetString(payload, "call_id", out string? callId)
            || !payload.TryGetProperty("invocation", out JsonElement invocation)
            || invocation.ValueKind != JsonValueKind.Object
            || !TryGetString(invocation, "server", out string? server)
            || !TryGetString(invocation, "tool", out string? tool)
            || !BoundedOperationLabel.TryNormalize(server, out string boundedServer)
            || !BoundedOperationLabel.TryNormalize(tool, out string boundedTool))
        {
            return true;
        }

        (string key, string? legacy) = OperationKeyPair(OpaqueKeyDomains.CodexMcp, callId);
        UsageOperationOutcome outcome = begin ? UsageOperationOutcome.Unknown : ReadMcpOutcome(payload);
        UpsertOperation(
            checkpoint,
            key,
            AttributionCapability.CodexMcp.Value,
            UsageOperationKindCodec.ToWire(UsageOperationKind.Mcp),
            boundedTool,
            boundedServer,
            UsageOperationOutcomeCodec.ToWire(outcome),
            timestamp,
            begin ? null : timestamp,
            legacy);
        return true;
    }

    private bool ObserveItem(
        JsonElement payload,
        CodexUsageFileCheckpoint checkpoint,
        DateTimeOffset timestamp,
        bool completed)
    {
        if (!payload.TryGetProperty("item", out JsonElement item)
            || item.ValueKind != JsonValueKind.Object
            || !TryGetString(item, "type", out string? itemType))
        {
            return true;
        }

        if (string.Equals(itemType, "DynamicToolCall", StringComparison.Ordinal))
        {
            if (!TryGetString(item, "id", out string? id)
                || !TryGetString(item, "tool", out string? tool)
                || !CodexAdmittedDynamicTools.IsAllowlisted(tool)
                || !BoundedOperationLabel.TryNormalize(tool, out string boundedTool))
            {
                return true;
            }

            (string key, string? legacy) = OperationKeyPair(OpaqueKeyDomains.CodexMcp, id);
            UsageOperationOutcome outcome = completed ? ReadItemOutcome(item) : UsageOperationOutcome.Unknown;
            UpsertOperation(
                checkpoint,
                key,
                AttributionCapability.CodexMcp.Value,
                UsageOperationKindCodec.ToWire(UsageOperationKind.Tool),
                boundedTool,
                server: null,
                UsageOperationOutcomeCodec.ToWire(outcome),
                timestamp,
                completed ? timestamp : null,
                legacy);
            return true;
        }

        if (string.Equals(itemType, "CollabAgentToolCall", StringComparison.Ordinal)
            && TryGetString(item, "id", out string? spawnId)
            && TryGetString(item, "tool", out string? spawnTool)
            && (string.Equals(spawnTool, "spawn_agent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(spawnTool, "SpawnAgent", StringComparison.Ordinal)))
        {
            if (!BoundedOperationLabel.TryNormalize(spawnTool, out string role))
            {
                role = "spawn";
            }

            (string key, string? legacy) = OperationKeyPair(OpaqueKeyDomains.CodexSkills, spawnId!);
            UpsertOperation(
                checkpoint,
                key,
                AttributionCapability.CodexSkills.Value,
                UsageOperationKindCodec.ToWire(UsageOperationKind.Spawn),
                role,
                server: null,
                UsageOperationOutcomeCodec.ToWire(
                    completed ? ReadItemOutcome(item) : UsageOperationOutcome.Unknown),
                timestamp,
                completed ? timestamp : null,
                legacy);
            return true;
        }

        if (string.Equals(itemType, "CommandExecution", StringComparison.Ordinal)
            && TryGetString(item, "id", out string? commandId)
            && item.TryGetProperty("command", out JsonElement commandElement))
        {
            string family = CodexCommandFamily.Classify(ReadStringList(commandElement));
            (string key, string? legacy) = OperationKeyPair(OpaqueKeyDomains.CodexCommands, commandId);
            UpsertOperation(
                checkpoint,
                key,
                AttributionCapability.CodexCommands.Value,
                UsageOperationKindCodec.ToWire(UsageOperationKind.Command),
                family,
                server: null,
                UsageOperationOutcomeCodec.ToWire(
                    completed ? ReadItemOutcome(item) : UsageOperationOutcome.Unknown),
                timestamp,
                completed ? timestamp : null,
                legacy);
            return true;
        }

        if (string.Equals(itemType, "FileChange", StringComparison.Ordinal)
            && TryGetString(item, "id", out string? fileItemId)
            && item.TryGetProperty("changes", out JsonElement changes)
            && changes.ValueKind == JsonValueKind.Object)
        {
            ObserveFileKeys(checkpoint, timestamp, fileItemId, changes, completed, completed ? ReadItemOutcome(item) : UsageOperationOutcome.Unknown);
            return true;
        }

        return true;
    }

    private bool ObserveSpawn(
        JsonElement payload,
        CodexUsageFileCheckpoint checkpoint,
        DateTimeOffset timestamp,
        bool begin)
    {
        if (!TryGetString(payload, "call_id", out string? callId))
        {
            return true;
        }

        string role = "spawn";
        if (TryGetString(payload, begin ? "agent_role" : "new_agent_role", out string? named)
            && BoundedOperationLabel.TryNormalize(named, out string boundedRole))
        {
            role = boundedRole;
        }

        (string key, string? legacy) = OperationKeyPair(OpaqueKeyDomains.CodexSkills, callId);
        UpsertOperation(
            checkpoint,
            key,
            AttributionCapability.CodexSkills.Value,
            UsageOperationKindCodec.ToWire(UsageOperationKind.Spawn),
            role,
            server: null,
            UsageOperationOutcomeCodec.ToWire(begin ? UsageOperationOutcome.Unknown : ReadItemOutcome(payload)),
            timestamp,
            begin ? null : timestamp,
            legacy);
        return true;
    }

    private bool ObserveCommand(
        JsonElement payload,
        CodexUsageFileCheckpoint checkpoint,
        DateTimeOffset timestamp,
        bool begin)
    {
        if (!TryGetString(payload, "call_id", out string? callId)
            || !payload.TryGetProperty("command", out JsonElement commandElement))
        {
            return true;
        }

        string family = CodexCommandFamily.Classify(ReadStringList(commandElement));
        (string key, string? legacy) = OperationKeyPair(OpaqueKeyDomains.CodexCommands, callId);
        UsageOperationOutcome outcome = begin
            ? UsageOperationOutcome.Unknown
            : ReadCommandOutcome(payload);
        UpsertOperation(
            checkpoint,
            key,
            AttributionCapability.CodexCommands.Value,
            UsageOperationKindCodec.ToWire(UsageOperationKind.Command),
            family,
            server: null,
            UsageOperationOutcomeCodec.ToWire(outcome),
            timestamp,
            begin ? null : timestamp,
            legacy);
        return true;
    }

    private bool ObservePatch(
        JsonElement payload,
        CodexUsageFileCheckpoint checkpoint,
        DateTimeOffset timestamp,
        bool begin)
    {
        if (!TryGetString(payload, "call_id", out string? callId)
            || !payload.TryGetProperty("changes", out JsonElement changes)
            || changes.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        ObserveFileKeys(
            checkpoint,
            timestamp,
            callId,
            changes,
            completed: !begin,
            !begin ? ReadItemOutcome(payload) : UsageOperationOutcome.Unknown);
        return true;
    }

    private void ObserveFileKeys(
        CodexUsageFileCheckpoint checkpoint,
        DateTimeOffset timestamp,
        string callId,
        JsonElement changes,
        bool completed,
        UsageOperationOutcome outcome)
    {
        foreach (JsonProperty change in changes.EnumerateObject())
        {
            string normalized = change.Name.Replace('\\', '/').Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            string namedFile = _attributionKeys!.Derive(
                OpaqueKeyDomains.CodexFiles,
                SourceAuthority.Value,
                normalized).Value;
            string unnamedFile = _attributionKeys.Derive(
                OpaqueKeyDomains.CodexFiles,
                OpaqueKeyDomains.LegacyUnnamedSource,
                normalized).Value;
            (string key, string? legacy) = OperationKeyPair(
                OpaqueKeyDomains.CodexFiles,
                callId + "\u001f" + namedFile);
            if (!string.Equals(namedFile, unnamedFile, StringComparison.Ordinal))
            {
                legacy = _attributionKeys.Derive(
                    OpaqueKeyDomains.CodexFiles,
                    OpaqueKeyDomains.LegacyUnnamedSource,
                    callId + "\u001f" + unnamedFile).Value;
            }

            UpsertOperation(
                checkpoint,
                key,
                AttributionCapability.CodexFiles.Value,
                UsageOperationKindCodec.ToWire(UsageOperationKind.File),
                "patch",
                namedFile,
                UsageOperationOutcomeCodec.ToWire(outcome),
                timestamp,
                completed ? timestamp : null,
                legacy);
        }
    }

    private void UpsertOperation(
        CodexUsageFileCheckpoint checkpoint,
        string key,
        string capability,
        string kind,
        string tool,
        string? server,
        string outcome,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt,
        string? legacyKey = null)
    {
        bool retainLabels = AllowsOperationLabels(capability);
        bool retainSession = _scanConsent is { State: AttributionConsentState.Enabled }
            && OpaqueAttributionKey.IsHexSha256(checkpoint.SessionKey ?? string.Empty);
        string storedTool = retainLabels ? tool : string.Empty;
        string? storedServer = retainLabels ? server : null;
        string? sessionKey = retainLabels && retainSession ? checkpoint.SessionKey : null;
        int index = checkpoint.Operations.FindIndex(item => item.Key == key);
        var observation = new CodexOperationObservation(
            key,
            startedAt,
            endedAt,
            kind,
            storedTool,
            storedServer,
            outcome,
            capability,
            sessionKey,
            LegacyKey: legacyKey);
        if (index < 0)
        {
            checkpoint.Operations.Add(observation);
            return;
        }

        CodexOperationObservation previous = checkpoint.Operations[index];
        checkpoint.Operations[index] = previous with
        {
            EndedAt = endedAt ?? previous.EndedAt,
            Outcome = outcome == UsageOperationOutcomeCodec.ToWire(UsageOperationOutcome.Unknown)
                ? previous.Outcome
                : outcome,
            Tool = retainLabels
                ? (string.IsNullOrEmpty(tool) ? previous.Tool : tool)
                : string.Empty,
            Server = retainLabels ? storedServer ?? previous.Server : null,
            SessionKey = retainLabels && retainSession
                ? sessionKey ?? previous.SessionKey
                : null,
            StartedAt = previous.StartedAt == default ? startedAt : previous.StartedAt,
            LegacyKey = legacyKey ?? previous.LegacyKey,
        };
    }

    private (string Key, string? LegacyKey) OperationKeyPair(string domain, string identifier)
    {
        string key = _attributionKeys!.Derive(domain, SourceAuthority.Value, identifier).Value;
        string unnamed = _attributionKeys.Derive(domain, OpaqueKeyDomains.LegacyUnnamedSource, identifier).Value;
        return (key, string.Equals(key, unnamed, StringComparison.Ordinal) ? null : unnamed);
    }

    private bool AllowsOperationLabels(string capability) =>
        ConsentFor(capability) is { State: AttributionConsentState.Enabled };

    private AttributionConsent? ConsentFor(string capability)
    {
        if (capability == AttributionCapability.CodexMcp.Value)
        {
            return _scanMcpConsent;
        }

        if (capability == AttributionCapability.CodexSkills.Value)
        {
            return _scanSkillsConsent;
        }

        if (capability == AttributionCapability.CodexCommands.Value)
        {
            return _scanCommandsConsent;
        }

        if (capability == AttributionCapability.CodexFiles.Value)
        {
            return _scanFilesConsent;
        }

        return null;
    }

    internal void StripDisabledOperationLabels(CodexUsageFileCheckpoint checkpoint)
    {
        bool sessionAllowed = _scanConsent is { State: AttributionConsentState.Enabled };
        for (int index = 0; index < checkpoint.Operations.Count; index++)
        {
            CodexOperationObservation operation = checkpoint.Operations[index];
            bool labels = AllowsOperationLabels(operation.Capability);
            if (labels && sessionAllowed)
            {
                continue;
            }

            checkpoint.Operations[index] = operation with
            {
                Tool = labels ? operation.Tool : string.Empty,
                Server = labels ? operation.Server : null,
                SessionKey = labels && sessionAllowed ? operation.SessionKey : null,
            };
        }
    }

    internal bool ShouldReplayOperationalEvents() =>
        IsEnabledBackfill(_scanMcpConsent, McpAttributionBackfillFrom, McpAttributionBackfillTo)
        || IsEnabledBackfill(_scanSkillsConsent, SkillsAttributionBackfillFrom, SkillsAttributionBackfillTo)
        || IsEnabledBackfill(_scanCommandsConsent, CommandsAttributionBackfillFrom, CommandsAttributionBackfillTo)
        || IsEnabledBackfill(_scanFilesConsent, FilesAttributionBackfillFrom, FilesAttributionBackfillTo);

    private static bool IsEnabledBackfill(
        AttributionConsent? consent,
        DateOnly? from,
        DateOnly? to) =>
        consent is { State: AttributionConsentState.Enabled } && from is not null && to is not null;

    private static UsageOperationOutcome ReadMcpOutcome(JsonElement payload)
    {
        if (payload.TryGetProperty("error", out JsonElement error)
            && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            return UsageOperationOutcome.Error;
        }

        if (!payload.TryGetProperty("result", out JsonElement result)
            || result.ValueKind != JsonValueKind.Object)
        {
            return UsageOperationOutcome.Unknown;
        }

        if (result.TryGetProperty("Err", out _) || result.TryGetProperty("err", out _))
        {
            return UsageOperationOutcome.Error;
        }

        if (TryReadStructuredIsError(result, out bool isError))
        {
            return isError ? UsageOperationOutcome.Error : UsageOperationOutcome.Success;
        }

        return UsageOperationOutcome.Unknown;
    }

    private static bool TryReadStructuredIsError(JsonElement result, out bool isError)
    {
        isError = false;
        JsonElement ok;
        if (result.TryGetProperty("Ok", out ok) || result.TryGetProperty("ok", out ok))
        {
            if (ok.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (ok.TryGetProperty("isError", out JsonElement flag)
                && flag.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                isError = flag.GetBoolean();
                return true;
            }
        }

        return false;
    }

    private static UsageOperationOutcome ReadItemOutcome(JsonElement item)
    {
        if (item.TryGetProperty("error", out JsonElement error)
            && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            return UsageOperationOutcome.Error;
        }

        if (item.TryGetProperty("success", out JsonElement success))
        {
            if (success.ValueKind is JsonValueKind.False)
            {
                return UsageOperationOutcome.Error;
            }

            if (success.ValueKind is JsonValueKind.True)
            {
                return UsageOperationOutcome.Success;
            }
        }

        if (TryGetString(item, "status", out string? status))
        {
            if (status is "failed" or "Failed" or "declined" or "Declined")
            {
                return UsageOperationOutcome.Error;
            }

            if (status is "completed" or "Completed" or "success" or "Success")
            {
                return UsageOperationOutcome.Success;
            }
        }

        return UsageOperationOutcome.Unknown;
    }

    private static UsageOperationOutcome ReadCommandOutcome(JsonElement payload)
    {
        if (TryGetString(payload, "status", out string? status)
            && status is "failed" or "declined")
        {
            return UsageOperationOutcome.Error;
        }

        if (payload.TryGetProperty("exit_code", out JsonElement exit)
            && exit.TryGetInt32(out int code))
        {
            return code == 0 ? UsageOperationOutcome.Success : UsageOperationOutcome.Error;
        }

        return UsageOperationOutcome.Unknown;
    }

    private static List<string> ReadStringList(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private void CaptureOperationAdmissionWatermarks(CodexUsageCheckpointState checkpoints)
    {
        CaptureOperationWatermark(checkpoints, _scanMcpConsent);
        CaptureOperationWatermark(checkpoints, _scanSkillsConsent);
        CaptureOperationWatermark(checkpoints, _scanCommandsConsent);
        CaptureOperationWatermark(checkpoints, _scanFilesConsent);
    }

    private static void CaptureOperationWatermark(
        CodexUsageCheckpointState checkpoints,
        AttributionConsent? consent)
    {
        if (consent is not { State: AttributionConsentState.Enabled })
        {
            return;
        }

        CodexOperationAdmissionState bucket = checkpoints.AdmissionFor(consent.Capability.Value);
        if (bucket.Epoch == consent.Epoch)
        {
            return;
        }

        string[] preserved = [.. bucket.Keys];
        bucket.Keys.Clear();
        bucket.CommittedKeys.Clear();
        foreach (CodexUsageFileCheckpoint file in checkpoints.Files.Values)
        {
            foreach (CodexOperationObservation operation in file.Operations)
            {
                if (operation.Capability == consent.Capability.Value)
                {
                    bucket.Keys.Add(operation.Key);
                }
            }
        }

        foreach (string key in preserved)
        {
            bucket.Keys.Add(key);
        }

        bucket.Epoch = consent.Epoch;
    }

    private List<UsageOperationFact> AdmitOperations(CodexUsageCheckpointState checkpoints)
    {
        var admitted = new List<UsageOperationFact>();
        AdmitCapability(
            checkpoints,
            _scanMcpConsent,
            McpAttributionBackfillFrom,
            McpAttributionBackfillTo,
            admitted);
        AdmitCapability(
            checkpoints,
            _scanSkillsConsent,
            SkillsAttributionBackfillFrom,
            SkillsAttributionBackfillTo,
            admitted);
        AdmitCapability(
            checkpoints,
            _scanCommandsConsent,
            CommandsAttributionBackfillFrom,
            CommandsAttributionBackfillTo,
            admitted);
        AdmitCapability(
            checkpoints,
            _scanFilesConsent,
            FilesAttributionBackfillFrom,
            FilesAttributionBackfillTo,
            admitted);
        return admitted;
    }

    private static void AdmitCapability(
        CodexUsageCheckpointState checkpoints,
        AttributionConsent? consent,
        DateOnly? backfillFrom,
        DateOnly? backfillTo,
        List<UsageOperationFact> admitted)
    {
        if (consent is not { State: AttributionConsentState.Enabled } || consent.Epoch <= 0)
        {
            return;
        }

        CodexOperationAdmissionState bucket = checkpoints.AdmissionFor(consent.Capability.Value);
        foreach (CodexUsageFileCheckpoint file in checkpoints.Files.Values)
        {
            foreach (CodexOperationObservation operation in file.Operations)
            {
                if (operation.Capability != consent.Capability.Value
                    || !UsageOperationKindCodec.TryParse(operation.Kind, out UsageOperationKind kind)
                    || !UsageOperationOutcomeCodec.TryParse(operation.Outcome, out UsageOperationOutcome outcome)
                    || !OpaqueAttributionKey.IsHexSha256(operation.Key)
                    || !BoundedOperationLabel.TryNormalize(operation.Tool, out string tool))
                {
                    continue;
                }

                if (!AttributionAdmission.AdmitsAutomaticLink(
                    consent,
                    operation.StartedAt,
                    operation.Key,
                    bucket.Keys,
                    backfillFrom,
                    backfillTo,
                    bucket.CommittedKeys))
                {
                    continue;
                }

                string? server = operation.Server;
                if (server is not null && !BoundedOperationLabel.TryNormalize(server, out server))
                {
                    continue;
                }

                admitted.Add(new UsageOperationFact(
                    new OpaqueAttributionKey(operation.Key),
                    consent.Capability,
                    consent.Epoch,
                    kind,
                    tool,
                    server,
                    outcome,
                    operation.StartedAt,
                    operation.EndedAt,
                    OpaqueAttributionKey.IsHexSha256(operation.SessionKey ?? string.Empty)
                        ? new OpaqueAttributionKey(operation.SessionKey!)
                        : null,
                    operation.Quantity,
                    checkpoints.SourceAuthority,
                    OpaqueAttributionKey.IsHexSha256(operation.LegacyKey ?? string.Empty)
                        ? new OpaqueAttributionKey(operation.LegacyKey!)
                        : null));
                bucket.CommittedKeys.Add(operation.Key);
            }
        }
    }
}
