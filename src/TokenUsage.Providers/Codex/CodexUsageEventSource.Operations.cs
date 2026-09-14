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

        OpaqueAttributionKey key = _attributionKeys!.Derive(OpaqueKeyDomains.CodexMcp, "codex", callId);
        UsageOperationOutcome outcome = begin ? UsageOperationOutcome.Unknown : ReadMcpOutcome(payload);
        UpsertOperation(
            checkpoint,
            key.Value,
            AttributionCapability.CodexMcp.Value,
            UsageOperationKindCodec.ToWire(UsageOperationKind.Mcp),
            boundedTool,
            boundedServer,
            UsageOperationOutcomeCodec.ToWire(outcome),
            timestamp,
            begin ? null : timestamp);
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

            OpaqueAttributionKey key = _attributionKeys!.Derive(OpaqueKeyDomains.CodexMcp, "codex", id);
            UsageOperationOutcome outcome = completed ? ReadItemOutcome(item) : UsageOperationOutcome.Unknown;
            UpsertOperation(
                checkpoint,
                key.Value,
                AttributionCapability.CodexMcp.Value,
                UsageOperationKindCodec.ToWire(UsageOperationKind.Tool),
                boundedTool,
                server: null,
                UsageOperationOutcomeCodec.ToWire(outcome),
                timestamp,
                completed ? timestamp : null);
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

            OpaqueAttributionKey key = _attributionKeys!.Derive(OpaqueKeyDomains.CodexSkills, "codex", spawnId!);
            UpsertOperation(
                checkpoint,
                key.Value,
                AttributionCapability.CodexSkills.Value,
                UsageOperationKindCodec.ToWire(UsageOperationKind.Spawn),
                role,
                server: null,
                UsageOperationOutcomeCodec.ToWire(
                    completed ? ReadItemOutcome(item) : UsageOperationOutcome.Unknown),
                timestamp,
                completed ? timestamp : null);
            return true;
        }

        if (string.Equals(itemType, "CommandExecution", StringComparison.Ordinal)
            && TryGetString(item, "id", out string? commandId)
            && item.TryGetProperty("command", out JsonElement commandElement))
        {
            string family = CodexCommandFamily.Classify(ReadStringList(commandElement));
            OpaqueAttributionKey key = _attributionKeys!.Derive(OpaqueKeyDomains.CodexCommands, "codex", commandId);
            UpsertOperation(
                checkpoint,
                key.Value,
                AttributionCapability.CodexCommands.Value,
                UsageOperationKindCodec.ToWire(UsageOperationKind.Command),
                family,
                server: null,
                UsageOperationOutcomeCodec.ToWire(
                    completed ? ReadItemOutcome(item) : UsageOperationOutcome.Unknown),
                timestamp,
                completed ? timestamp : null);
            return true;
        }

        if (string.Equals(itemType, "FileChange", StringComparison.Ordinal)
            && TryGetString(item, "id", out string? fileItemId)
            && item.TryGetProperty("changes", out JsonElement changes)
            && changes.ValueKind == JsonValueKind.Object)
        {
            ObserveFileKeys(checkpoint, timestamp, fileItemId, changes, completed);
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

        OpaqueAttributionKey key = _attributionKeys!.Derive(OpaqueKeyDomains.CodexSkills, "codex", callId);
        UpsertOperation(
            checkpoint,
            key.Value,
            AttributionCapability.CodexSkills.Value,
            UsageOperationKindCodec.ToWire(UsageOperationKind.Spawn),
            role,
            server: null,
            UsageOperationOutcomeCodec.ToWire(begin ? UsageOperationOutcome.Unknown : UsageOperationOutcome.Success),
            timestamp,
            begin ? null : timestamp);
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
        OpaqueAttributionKey key = _attributionKeys!.Derive(OpaqueKeyDomains.CodexCommands, "codex", callId);
        UsageOperationOutcome outcome = begin
            ? UsageOperationOutcome.Unknown
            : ReadCommandOutcome(payload);
        UpsertOperation(
            checkpoint,
            key.Value,
            AttributionCapability.CodexCommands.Value,
            UsageOperationKindCodec.ToWire(UsageOperationKind.Command),
            family,
            server: null,
            UsageOperationOutcomeCodec.ToWire(outcome),
            timestamp,
            begin ? null : timestamp);
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

        ObserveFileKeys(checkpoint, timestamp, callId, changes, completed: !begin);
        return true;
    }

    private void ObserveFileKeys(
        CodexUsageFileCheckpoint checkpoint,
        DateTimeOffset timestamp,
        string callId,
        JsonElement changes,
        bool completed)
    {
        foreach (JsonProperty change in changes.EnumerateObject())
        {
            string normalized = change.Name.Replace('\\', '/').Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            OpaqueAttributionKey fileId = _attributionKeys!.Derive(
                OpaqueKeyDomains.CodexFiles,
                "codex",
                normalized);
            OpaqueAttributionKey key = _attributionKeys.Derive(
                OpaqueKeyDomains.CodexFiles,
                "codex",
                callId + "\u001f" + fileId.Value);
            UpsertOperation(
                checkpoint,
                key.Value,
                AttributionCapability.CodexFiles.Value,
                UsageOperationKindCodec.ToWire(UsageOperationKind.File),
                "patch",
                fileId.Value,
                UsageOperationOutcomeCodec.ToWire(
                    completed ? UsageOperationOutcome.Success : UsageOperationOutcome.Unknown),
                timestamp,
                completed ? timestamp : null);
        }
    }

        private static void UpsertOperation(
        CodexUsageFileCheckpoint checkpoint,
        string key,
        string capability,
        string kind,
        string tool,
        string? server,
        string outcome,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt)
    {
        string? sessionKey = OpaqueAttributionKey.IsHexSha256(checkpoint.SessionKey ?? string.Empty)
            ? checkpoint.SessionKey
            : null;
        int index = checkpoint.Operations.FindIndex(item => item.Key == key);
        var observation = new CodexOperationObservation(
            key,
            startedAt,
            endedAt,
            kind,
            tool,
            server,
            outcome,
            capability,
            sessionKey);
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
            Tool = tool,
            Server = server ?? previous.Server,
            SessionKey = sessionKey ?? previous.SessionKey,
            StartedAt = previous.StartedAt == default ? startedAt : previous.StartedAt,
        };
    }

    private static UsageOperationOutcome ReadMcpOutcome(JsonElement payload)
    {
        if (payload.TryGetProperty("error", out JsonElement error)
            && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            return UsageOperationOutcome.Error;
        }

        if (!payload.TryGetProperty("result", out JsonElement result))
        {
            return UsageOperationOutcome.Success;
        }

        if (result.ValueKind == JsonValueKind.Object
            && (result.TryGetProperty("Err", out _) || result.TryGetProperty("err", out _)))
        {
            return UsageOperationOutcome.Error;
        }

        return UsageOperationOutcome.Success;
    }

    private static UsageOperationOutcome ReadItemOutcome(JsonElement item)
    {
        if (item.TryGetProperty("error", out JsonElement error)
            && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            return UsageOperationOutcome.Error;
        }

        if (item.TryGetProperty("success", out JsonElement success)
            && success.ValueKind is JsonValueKind.False)
        {
            return UsageOperationOutcome.Error;
        }

        if (TryGetString(item, "status", out string? status)
            && status is "failed" or "Failed" or "declined" or "Declined")
        {
            return UsageOperationOutcome.Error;
        }

        return UsageOperationOutcome.Success;
    }

    private static UsageOperationOutcome ReadCommandOutcome(JsonElement payload)
    {
        if (TryGetString(payload, "status", out string? status)
            && status is "failed" or "declined")
        {
            return UsageOperationOutcome.Error;
        }

        if (payload.TryGetProperty("exit_code", out JsonElement exit)
            && exit.TryGetInt32(out int code)
            && code != 0)
        {
            return UsageOperationOutcome.Error;
        }

        return UsageOperationOutcome.Success;
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
                    operation.Quantity));
                bucket.CommittedKeys.Add(operation.Key);
            }
        }
    }
}
