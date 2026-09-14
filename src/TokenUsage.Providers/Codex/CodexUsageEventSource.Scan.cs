using System.Globalization;
using System.Text.Json;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.LocalScan;

namespace TokenUsage.Providers.Codex;

public sealed partial class CodexUsageEventSource
{
    private ScanResult ScanCore(
        CodexUsageCheckpointState checkpoints,
        CancellationToken cancellationToken)
    {
        string[] roots = SessionRoots().Where(Directory.Exists).ToArray();
        if (roots.Length == 0)
            return new ScanResult(UsageSourceReadStatus.NoData, UsageSourceIssueKind.RootUnavailable)
                { SourceInstance = checkpoints.SourceAuthority };

        var state = new LocalScanState(_budget);
        SessionFile[] files = FindSessionFiles(roots, state, cancellationToken);
        // Compare original paths before scanning can update replay locations. A copied
        // checkpoint must not acquire this profile's authority on a later replay.
        var foundPaths = files.Select(file => Hash(Path.GetFullPath(file.Path).ToUpperInvariant()))
            .ToHashSet(StringComparer.Ordinal);
        bool canBindAuthority = checkpoints.SourceAuthority is null
            && checkpoints.Files.Count > 0
            && checkpoints.Files.Values.All(file => foundPaths.Contains(file.AuthorityPathHash));
        DateOnly recentFrom = RecentFrom();
        long initialBytesRemaining = MaximumInitialRecentScanBytes;
        CaptureAdmissionWatermarks(checkpoints);
        foreach (SessionFile file in files.OrderByDescending(TryGetLastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.TryConsumeFile()) break;
            ScanRecentUsage(file, checkpoints, recentFrom, ref initialBytesRemaining, state, cancellationToken);
        }

        // Missing source files do not retire numeric observations; the retention horizon does.
        PruneCheckpointDays(checkpoints, recentFrom);
        if (canBindAuthority && !state.IsPartial && checkpoints.Files.Values.Any(file => file.Observations.Count > 0))
            checkpoints.SourceAuthority = SourceAuthority;
        return CreateScanResult(checkpoints, state);
    }

    private void ScanRecentUsage(
        SessionFile file,
        CodexUsageCheckpointState checkpoints,
        DateOnly recentFrom,
        ref long initialBytesRemaining,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(file.Path);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                state.MarkPartial();
                return;
            }

            string pathHash = Hash(Path.GetFullPath(file.Path).ToUpperInvariant());
            bool hasCheckpoint = checkpoints.Files.TryGetValue(
                file.SessionIdentity,
                out CodexUsageFileCheckpoint? checkpoint);
            if (!hasCheckpoint
                || !string.Equals(checkpoint!.PathHash, pathHash, StringComparison.Ordinal)
                || checkpoint.Offset > info.Length)
            {
                if (info.LastWriteTimeUtc < StartOfDayUtc(recentFrom))
                {
                    // Keep earlier numeric observations even if the source was moved or trimmed.
                    return;
                }

                if (info.Length > initialBytesRemaining)
                {
                    state.MarkPartial();
                    return;
                }

                initialBytesRemaining -= info.Length;
                if (checkpoint is null)
                    checkpoint = new CodexUsageFileCheckpoint(pathHash, offset: 0, "unknown", previous: null);
                else
                {
                    checkpoint.PathHash = pathHash;
                    checkpoint.Offset = 0;
                    checkpoint.ReplayThroughUtc = checkpoint.PreviousTimestamp;
                }
                checkpoints.Files[file.SessionIdentity] = checkpoint;
            }

            checkpoint.ObservationIdentity = file.SessionIdentity;
            ApplyScanConsentToCheckpoint(checkpoint);
            FillOpaqueSessionKeys(file.Path, checkpoint, cancellationToken);
            StampCheckpointedProjectObservations(checkpoint);
            if (checkpoint.Offset == info.Length)
            {
                return;
            }

            using var stream = new FileStream(
                file.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                1024 * 1024,
                FileOptions.SequentialScan);
            stream.Seek(checkpoint.Offset, SeekOrigin.Begin);
            ScanRecentLines(
                stream,
                checkpoint,
                recentFrom,
                state,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or ArgumentException
                                           or OverflowException
                                           or System.Security.SecurityException)
        {
            state.MarkPartial();
        }
    }

    private void ScanRecentLines(
        FileStream stream,
        CodexUsageFileCheckpoint checkpoint,
        DateOnly recentFrom,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024 * 1024];
        using var line = new MemoryStream(capacity: Math.Min(state.MaximumLineBytes, 64 * 1024));
        bool oversized = false;
        long absoluteOffset = stream.Position;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                break;
            }

            int segmentStart = 0;
            while (segmentStart < bytesRead)
            {
                int newline = Array.IndexOf(buffer, (byte)'\n', segmentStart, bytesRead - segmentStart);
                int segmentEnd = newline >= 0 ? newline : bytesRead;
                AppendRecentLineSegment(
                    line,
                    buffer.AsSpan(segmentStart, segmentEnd - segmentStart),
                    state.MaximumLineBytes,
                    ref oversized);
                if (newline < 0)
                {
                    break;
                }

                if (!oversized)
                {
                    ReadOnlyMemory<byte> utf8 = line.GetBuffer().AsMemory(0, checked((int)line.Length));
                    if (!utf8.IsEmpty && utf8.Span[^1] == (byte)'\r')
                    {
                        utf8 = utf8[..^1];
                    }

                    ProcessRecentLine(utf8, checkpoint, recentFrom, state);
                }
                else if (!IsNonUsageRecord(line.GetBuffer().AsSpan(0, checked((int)line.Length))))
                {
                    state.MarkPartial();
                }

                checkpoint.Offset = checked(absoluteOffset + newline + 1L);
                line.SetLength(0);
                oversized = false;
                segmentStart = newline + 1;
            }

            absoluteOffset = checked(absoluteOffset + bytesRead);
        }
    }

    private static bool IsNonUsageRecord(ReadOnlySpan<byte> prefix)
    {
        // Read only the envelope, not message text which can itself mention token_count.
        // A truncated or unrecognized envelope is not evidence that a line is irrelevant.
        try
        {
            var reader = new Utf8JsonReader(prefix, isFinalBlock: false, state: default);
            bool eventMessage = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1
                    && reader.ValueTextEquals("type") && reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.String) return false;
                    if (reader.ValueTextEquals("turn_context") || reader.ValueTextEquals("session_meta")) return false;
                    eventMessage = reader.ValueTextEquals("event_msg");
                    if (!eventMessage) return true;
                }
                if (eventMessage && reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 2
                    && reader.ValueTextEquals("type") && reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.String) return false;
                    return !reader.ValueTextEquals("token_count")
                        && !reader.ValueTextEquals("task_started")
                        && !reader.ValueTextEquals("mcp_tool_call_begin")
                        && !reader.ValueTextEquals("mcp_tool_call_end")
                        && !reader.ValueTextEquals("item_started")
                        && !reader.ValueTextEquals("item_completed")
                        && !reader.ValueTextEquals("collab_agent_spawn_begin")
                        && !reader.ValueTextEquals("collab_agent_spawn_end")
                        && !reader.ValueTextEquals("exec_command_begin")
                        && !reader.ValueTextEquals("exec_command_end")
                        && !reader.ValueTextEquals("patch_apply_begin")
                        && !reader.ValueTextEquals("patch_apply_end");
                }
            }
        }
        catch (JsonException) { }
        return false;
    }

    private static void AppendRecentLineSegment(
        MemoryStream line,
        ReadOnlySpan<byte> segment,
        int maximumLineBytes,
        ref bool oversized)
    {
        if (oversized || segment.IsEmpty)
        {
            return;
        }

        int remaining = maximumLineBytes - checked((int)line.Length);
        if (segment.Length <= remaining)
        {
            line.Write(segment);
            return;
        }

        if (remaining > 0)
        {
            line.Write(segment[..remaining]);
        }

        oversized = true;
    }

    private void ProcessRecentLine(
        ReadOnlyMemory<byte> utf8,
        CodexUsageFileCheckpoint checkpoint,
        DateOnly recentFrom,
        LocalScanState state)
    {
        ReadOnlySpan<byte> bytes = utf8.Span;
        bool mightBeContext = bytes.IndexOf("turn_context"u8) >= 0;
        bool mightBeUsage = bytes.IndexOf("token_count"u8) >= 0;
        bool mightBeSessionMeta = bytes.IndexOf("session_meta"u8) >= 0;
        bool mightBeTaskStarted = bytes.IndexOf("task_started"u8) >= 0;
        bool mightBeOperational = MightBeOperational(bytes);
        if (!mightBeContext && !mightBeUsage && !mightBeSessionMeta && !mightBeTaskStarted && !mightBeOperational)
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8);
            JsonElement root = document.RootElement;
            if (!TryGetString(root, "type", out string? recordType)
                || !root.TryGetProperty("payload", out JsonElement payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                state.UnsupportedSchema = true;
                state.MarkPartial();
                return;
            }

            if (string.Equals(recordType, "session_meta", StringComparison.Ordinal))
            {
                ObserveSessionMeta(root, payload, checkpoint);
                return;
            }

            if (string.Equals(recordType, "turn_context", StringComparison.Ordinal))
            {
                if (TryGetString(payload, "model", out string? model))
                {
                    checkpoint.Model = NormalizeModel(model) ?? "unknown";
                    checkpoint.ObservedModel = TokenUsage.Providers.Pricing.ModelIdentity.Sanitize(model);
                }

                checkpoint.Effort = TryGetString(payload, "effort", out string? effort)
                    && effort is "none" or "minimal" or "low" or "medium" or "high" or "xhigh" or "max" or "ultra" ? effort : null;
                checkpoint.Tier = TryGetString(payload, "service_tier", out string? tier)
                    && tier is "standard" or "fast" or "batch" or "flex" or "priority" ? tier : null;
                ApplyProjectFromPayload(payload, checkpoint);
                return;
            }

            if (!string.Equals(recordType, "event_msg", StringComparison.Ordinal)
                || !TryGetString(payload, "type", out string? eventType))
            {
                return;
            }

            if (string.Equals(eventType, "task_started", StringComparison.Ordinal))
            {
                ObserveTaskStarted(root, payload, checkpoint);
                return;
            }

            if (TryObserveOperationalEvent(eventType!, root, payload, checkpoint))
            {
                return;
            }

            if (!string.Equals(eventType, "token_count", StringComparison.Ordinal)
                || !payload.TryGetProperty("info", out JsonElement info)
                || info.ValueKind is JsonValueKind.Null)
            {
                return;
            }

            if (info.ValueKind != JsonValueKind.Object
                || !TryGetUtcTimestamp(root, "timestamp", out DateTimeOffset timestamp))
            {
                state.UnsupportedSchema = true;
                state.MarkPartial();
                return;
            }

            if (checkpoint.ReplayThroughUtc is { } replayThrough && timestamp <= replayThrough) return;

            bool hasCumulative = info.TryGetProperty(
                                     "total_token_usage",
                                     out JsonElement cumulativeElement)
                                 && cumulativeElement.ValueKind == JsonValueKind.Object;
            bool hasLast = info.TryGetProperty(
                               "last_token_usage",
                               out JsonElement lastElement)
                           && lastElement.ValueKind == JsonValueKind.Object;
            TokenBreakdown? current = null;
            TokenBreakdown? last = null;
            bool cumulativeIsValid = hasCumulative
                && TryReadTokenBreakdown(cumulativeElement, out current);
            bool lastIsValid = hasLast && TryReadTokenBreakdown(lastElement, out last);
            CodexMeasuredComponents currentMeasured = cumulativeIsValid ? ReadMeasuredComponents(cumulativeElement) : CodexMeasuredComponents.None;
            CodexMeasuredComponents lastMeasured = lastIsValid ? ReadMeasuredComponents(lastElement) : CodexMeasuredComponents.None;
            if (!cumulativeIsValid && !lastIsValid)
            {
                state.UnsupportedSchema = true;
                state.MarkPartial();
                return;
            }

            if (checkpoint.ChildReplayPending)
            {
                if (cumulativeIsValid)
                {
                    checkpoint.Previous = current;
                    checkpoint.PreviousMeasured = currentMeasured;
                }
                else
                {
                    state.MarkPartial();
                }

                return;
            }

            DateTimeOffset? previousTimestamp = checkpoint.PreviousTimestamp;
            TokenBreakdown? previous = checkpoint.Previous;
            CodexMeasuredComponents measured = currentMeasured & checkpoint.PreviousMeasured;
            if (previous is null) measured = currentMeasured;
            else if (current is not null)
            {
                if (InputComponentsRegressed(current, previous))
                    measured &= ~(CodexMeasuredComponents.Input | CodexMeasuredComponents.CacheRead | CodexMeasuredComponents.CacheWrite);
                if (OutputComponentsRegressed(current, previous))
                    measured &= ~(CodexMeasuredComponents.Output | CodexMeasuredComponents.Reasoning);
            }
            TokenBreakdown delta;
            if (cumulativeIsValid)
            {
                if (IsStaleRegression(current!, last, checkpoint.Previous, lastIsValid))
                {
                    return;
                }

                delta = ComputeTurnDelta(current!, last, checkpoint.Previous, lastIsValid);
                checkpoint.Previous = current;
                checkpoint.PreviousMeasured = currentMeasured;
            }
            else
            {
                delta = last!;
                state.MarkPartial();
            }

            checkpoint.PreviousTimestamp = timestamp;
            if (delta.Total == 0)
            {
                return;
            }

            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_groupingTimeZoneId);
            DateOnly date = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(timestamp, timeZone).DateTime);
            if (date < recentFrom)
            {
                return;
            }

            bool isTurnObservation = lastIsValid && delta == last;
            if (isTurnObservation) measured = lastMeasured;
            bool isInitialSnapshot = cumulativeIsValid && previous is null && !lastIsValid;
            UsageTimePrecision precision = isInitialSnapshot ? UsageTimePrecision.Unknown
                : isTurnObservation ? UsageTimePrecision.Timestamp
                : previousTimestamp is { } previousTime && previousTime <= timestamp
                    ? UsageTimePrecision.Interval : UsageTimePrecision.Unknown;
            UsageRecordKind kind = isInitialSnapshot ? UsageRecordKind.Snapshot
                : !isTurnObservation && cumulativeIsValid && previous is not null ? UsageRecordKind.IntervalDelta
                : UsageRecordKind.Unknown;
            string observedModel = isTurnObservation ? checkpoint.Model : "unknown";
            string observationKey = Hash($"codex-observation\0{checkpoint.ObservationIdentity}\0{timestamp:O}\0{checkpoint.Offset}");
            string? projectKey = checkpoint.ProjectKey;
            long? projectEpoch = checkpoint.ProjectEpoch;
            string? mapping = projectKey is not null
                ? ProjectMappingKindCodec.ToWire(ProjectMappingKind.Observed)
                : checkpoint.SawMultipleProjects && _scanProjectConsent is { State: AttributionConsentState.Enabled }
                    ? ProjectMappingKindCodec.ToWire(ProjectMappingKind.Ambiguous)
                    : null;
            if (mapping is not null && projectEpoch is null && _scanProjectConsent is { State: AttributionConsentState.Enabled })
            {
                projectEpoch = _scanProjectConsent.Epoch;
            }

            checkpoint.Observations.Add(new CodexNumericObservation(observationKey, timestamp,
                observedModel, delta, precision, precision == UsageTimePrecision.Interval ? previousTimestamp : null,
                isTurnObservation ? checkpoint.ObservedModel : null,
                isTurnObservation ? checkpoint.Effort : null, isTurnObservation ? checkpoint.Tier : null,
                measured, kind, RepresentationRevision: 1,
                ProjectKey: projectKey,
                ProjectEpoch: projectEpoch,
                ProjectMappingKind: mapping));

        }
        catch (Exception exception) when (exception is JsonException
                                           or ArgumentException
                                           or InvalidOperationException
                                           or OverflowException)
        {
            state.UnsupportedSchema = true;
            state.MarkPartial();
        }
    }

    private void ObserveSessionMeta(
        JsonElement root,
        JsonElement payload,
        CodexUsageFileCheckpoint checkpoint)
    {
        if (checkpoint.SawSessionMeta)
        {
            return;
        }

        checkpoint.SawSessionMeta = true;
        ApplySessionIdentity(payload, checkpoint);
        ApplyProjectFromPayload(payload, checkpoint);
        if (!IsChildSessionMeta(payload))
        {
            return;
        }

        checkpoint.ChildReplayPending = true;
        checkpoint.ChildCreatedAtUnixSeconds = TryGetUtcTimestamp(
            root,
            "timestamp",
            out DateTimeOffset createdAt)
            ? createdAt.ToUnixTimeSeconds()
            : null;
    }

    private void CaptureAdmissionWatermarks(CodexUsageCheckpointState checkpoints)
    {
        if (_scanConsent is { State: AttributionConsentState.Enabled }
            && checkpoints.SessionAdmissionEpoch != _scanConsent.Epoch)
        {
            checkpoints.SessionAdmissionEventKeys.Clear();
            checkpoints.SessionCommittedEventKeys.Clear();
            foreach (CodexUsageFileCheckpoint file in checkpoints.Files.Values)
            {
                foreach (CodexNumericObservation observation in file.Observations)
                {
                    checkpoints.SessionAdmissionEventKeys.Add(observation.Key);
                }
            }

            checkpoints.SessionAdmissionEpoch = _scanConsent.Epoch;
        }

        if (_scanProjectConsent is { State: AttributionConsentState.Enabled }
            && checkpoints.ProjectAdmissionEpoch != _scanProjectConsent.Epoch)
        {
            checkpoints.ProjectAdmissionEventKeys.Clear();
            checkpoints.ProjectCommittedEventKeys.Clear();
            foreach (CodexUsageFileCheckpoint file in checkpoints.Files.Values)
            {
                foreach (CodexNumericObservation observation in file.Observations)
                {
                    checkpoints.ProjectAdmissionEventKeys.Add(observation.Key);
                }
            }

            checkpoints.ProjectAdmissionEpoch = _scanProjectConsent.Epoch;
        }

        CaptureOperationAdmissionWatermarks(checkpoints);
    }

    private void ApplyScanConsentToCheckpoint(CodexUsageFileCheckpoint checkpoint)
    {
        if (_scanConsent is not { State: AttributionConsentState.Enabled })
        {
            checkpoint.SessionKey = null;
            checkpoint.ParentSessionKey = null;
            checkpoint.AttributionEpoch = null;
        }

        if (_scanProjectConsent is { State: AttributionConsentState.Enabled })
        {
            return;
        }

        checkpoint.ProjectKey = null;
        checkpoint.ProjectEpoch = null;
        checkpoint.SawMultipleProjects = false;
        if (checkpoint.Observations.Exists(item => item.ProjectKey is not null || item.ProjectMappingKind is not null))
        {
            CodexNumericObservation[] stripped = checkpoint.Observations
                .Select(item => item with { ProjectKey = null, ProjectEpoch = null, ProjectMappingKind = null })
                .ToArray();
            checkpoint.Observations.Clear();
            checkpoint.Observations.AddRange(stripped);
        }
    }

    private void FillOpaqueSessionKeys(
        string path,
        CodexUsageFileCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        if (_attributionKeys is null)
        {
            return;
        }

        bool sessionReady = _scanConsent is not { State: AttributionConsentState.Enabled }
            || (checkpoint.SessionKey is not null && checkpoint.AttributionEpoch == _scanConsent.Epoch);
        bool projectReady = _scanProjectConsent is not { State: AttributionConsentState.Enabled }
            || checkpoint.ProjectKey is not null;
        if (sessionReady && projectReady)
        {
            return;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.SequentialScan);
            byte[] buffer = new byte[64 * 1024];
            using var line = new MemoryStream(capacity: 16 * 1024);
            long scanned = 0;
            const long maximumBytes = 8L * 1024 * 1024;
            while (scanned < maximumBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    return;
                }

                int segmentStart = 0;
                while (segmentStart < bytesRead)
                {
                    int newline = Array.IndexOf(buffer, (byte)'\n', segmentStart, bytesRead - segmentStart);
                    int segmentEnd = newline >= 0 ? newline : bytesRead;
                    line.Write(buffer, segmentStart, segmentEnd - segmentStart);
                    scanned += segmentEnd - segmentStart;
                    if (newline < 0)
                    {
                        break;
                    }

                    ReadOnlyMemory<byte> utf8 = line.GetBuffer().AsMemory(0, checked((int)line.Length));
                    if (!utf8.IsEmpty && utf8.Span[^1] == (byte)'\r')
                    {
                        utf8 = utf8[..^1];
                    }

                    line.SetLength(0);
                    if (utf8.Span.IndexOf("session_meta"u8) >= 0
                        && TryApplySessionMetaBytes(utf8, checkpoint))
                    {
                        return;
                    }

                    segmentStart = newline + 1;
                    if (scanned >= maximumBytes)
                    {
                        return;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or ArgumentException
                                           or JsonException)
        {
            // Missing or unreadable session_meta leaves the file Unassigned.
        }
    }

    private bool TryApplySessionMetaBytes(ReadOnlyMemory<byte> utf8, CodexUsageFileCheckpoint checkpoint)
    {
        using JsonDocument document = JsonDocument.Parse(utf8);
        JsonElement root = document.RootElement;
        return TryGetString(root, "type", out string? recordType)
            && string.Equals(recordType, "session_meta", StringComparison.Ordinal)
            && root.TryGetProperty("payload", out JsonElement payload)
            && payload.ValueKind == JsonValueKind.Object
            && (ApplySessionIdentity(payload, checkpoint) | ApplyProjectSideEffect(payload, checkpoint));
    }

    private void StampCheckpointedProjectObservations(CodexUsageFileCheckpoint checkpoint)
    {
        if (_scanProjectConsent is not { State: AttributionConsentState.Enabled }
            || checkpoint.ProjectEpoch is not > 0
            || checkpoint.Observations.Count == 0
            || !checkpoint.Observations.Exists(item => item.ProjectMappingKind is null))
        {
            return;
        }

        bool ambiguous = checkpoint.SawMultipleProjects || checkpoint.ProjectKey is null;
        CodexNumericObservation[] stamped = checkpoint.Observations
            .Select(item => item.ProjectMappingKind is not null
                ? item
                : item with
                {
                    ProjectKey = ambiguous ? null : checkpoint.ProjectKey,
                    ProjectEpoch = checkpoint.ProjectEpoch,
                    ProjectMappingKind = ProjectMappingKindCodec.ToWire(
                        ambiguous ? ProjectMappingKind.Ambiguous : ProjectMappingKind.Observed),
                })
            .ToArray();
        checkpoint.Observations.Clear();
        checkpoint.Observations.AddRange(stamped);
    }

    private bool ApplyProjectSideEffect(JsonElement payload, CodexUsageFileCheckpoint checkpoint)
    {
        ApplyProjectFromPayload(payload, checkpoint);
        return checkpoint.ProjectKey is not null;
    }

    private bool ApplySessionIdentity(JsonElement payload, CodexUsageFileCheckpoint checkpoint)
    {
        if (_scanConsent is not { State: AttributionConsentState.Enabled }
            || _attributionKeys is null)
        {
            return false;
        }

        if (!TryGetString(payload, "id", out string? nativeId)
            || !OpaqueNativeId.TryNormalize(nativeId, out string identifier))
        {
            return false;
        }

        checkpoint.SessionKey = _attributionKeys.Derive(OpaqueKeyDomains.CodexSession, AgentId.Value, identifier).Value;
        checkpoint.AttributionEpoch = _scanConsent.Epoch;
        string? parentNative = null;
        if (TryGetString(payload, "parent_thread_id", out string? parent)
            && OpaqueNativeId.TryNormalize(parent, out string parentId))
        {
            parentNative = parentId;
        }
        else if (TryGetString(payload, "forked_from_id", out string? forked)
            && OpaqueNativeId.TryNormalize(forked, out string forkedId))
        {
            parentNative = forkedId;
        }

        checkpoint.ParentSessionKey = parentNative is null
            ? null
            : _attributionKeys.Derive(OpaqueKeyDomains.CodexParent, AgentId.Value, parentNative).Value;
        return true;
    }

    private void ApplyProjectFromPayload(JsonElement payload, CodexUsageFileCheckpoint checkpoint)
    {
        if (_scanProjectConsent is not { State: AttributionConsentState.Enabled }
            || _attributionKeys is null
            || !TryGetString(payload, "cwd", out string? cwd)
            || !OpaqueWorkspaceFingerprint.TryFingerprint(cwd, out string fingerprint))
        {
            return;
        }

        string opaque = _attributionKeys.Derive(OpaqueKeyDomains.CodexProject, AgentId.Value, fingerprint).Value;
        if (checkpoint.ProjectKey is not null
            && !string.Equals(checkpoint.ProjectKey, opaque, StringComparison.Ordinal))
        {
            checkpoint.SawMultipleProjects = true;
        }

        checkpoint.ProjectKey = opaque;
        checkpoint.ProjectEpoch = _scanProjectConsent.Epoch;
    }

    private static void ObserveTaskStarted(
        JsonElement root,
        JsonElement payload,
        CodexUsageFileCheckpoint checkpoint)
    {
        if (!checkpoint.ChildReplayPending
            || !TryGetNonNegativeDouble(payload, "started_at", out double startedAt))
        {
            return;
        }

        long? threshold = checkpoint.ChildCreatedAtUnixSeconds;
        if (threshold is null
            && TryGetUtcTimestamp(root, "timestamp", out DateTimeOffset lineTimestamp))
        {
            threshold = lineTimestamp.ToUnixTimeSeconds();
        }

        if (threshold is null || startedAt < threshold.Value)
        {
            return;
        }

        checkpoint.ChildReplayPending = false;
        checkpoint.ChildCreatedAtUnixSeconds = null;
    }

    private static bool IsChildSessionMeta(JsonElement payload)
    {
        if (HasNonNullValue(payload, "forked_from_id")
            || HasNonNullValue(payload, "parent_thread_id"))
        {
            return true;
        }

        if (TryGetString(payload, "thread_source", out string? threadSource)
            && string.Equals(threadSource, "subagent", StringComparison.Ordinal))
        {
            return true;
        }

        return payload.TryGetProperty("source", out JsonElement source)
               && source.ValueKind == JsonValueKind.Object
               && HasNonNullValue(source, "subagent");
    }

    private static bool HasNonNullValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        return value.ValueKind != JsonValueKind.String
               || !string.IsNullOrWhiteSpace(value.GetString());
    }

    private static bool TryGetNonNegativeDouble(
        JsonElement element,
        string propertyName,
        out double value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out JsonElement property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetDouble(out value)
               && double.IsFinite(value)
               && value >= 0;
    }

    private static TokenBreakdown ComputeTurnDelta(
        TokenBreakdown current,
        TokenBreakdown? last,
        TokenBreakdown? previous,
        bool lastIsValid)
    {
        if (previous is null)
        {
            // A resumed session carries prior totals. last_token_usage is the
            // current turn; the cumulative snapshot is not a new charge.
            return lastIsValid ? last! : current;
        }

        if (TotalInput(current) < TotalInput(previous)
            || TotalOutput(current) < TotalOutput(previous))
        {
            // A real session reset starts a new cumulative. last_token_usage
            // is that turn. Stale replicas never reach this branch.
            return lastIsValid ? last! : new TokenBreakdown(0, 0, 0, 0, 0);
        }

        if (lastIsValid && (InputComponentsRegressed(current, previous) || OutputComponentsRegressed(current, previous))
            && TotalInput(last!) == TotalInput(current) - TotalInput(previous)
            && TotalOutput(last!) == TotalOutput(current) - TotalOutput(previous))
            return last!;
        return Difference(current, previous);
    }

    private static bool IsStaleRegression(
        TokenBreakdown current,
        TokenBreakdown? last,
        TokenBreakdown? previous,
        bool lastIsValid)
    {
        if (previous is null)
        {
            return false;
        }

        if (TotalInput(current) >= TotalInput(previous)
            && TotalOutput(current) >= TotalOutput(previous))
        {
            return false;
        }

        long previousSum = SumOf(previous);
        long currentSum = SumOf(current);
        long lastSum = lastIsValid ? SumOf(last!) : 0;
        return previousSum > 0
            && (currentSum * 100 >= previousSum * 98
                || currentSum + lastSum * 2 >= previousSum);
    }

    private static long SumOf(TokenBreakdown value) =>
        checked(TotalInput(value) + TotalOutput(value));

    private DateTime StartOfDayUtc(DateOnly date)
    {
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_groupingTimeZoneId);
        DateTime local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private static bool CanSubtract(TokenBreakdown current, TokenBreakdown previous) =>
        TotalInput(current) >= TotalInput(previous)
        && TotalOutput(current) >= TotalOutput(previous);

    private static TokenBreakdown Difference(
        TokenBreakdown current,
        TokenBreakdown? previous)
    {
        if (previous is null
            || TotalInput(current) < TotalInput(previous)
            || TotalOutput(current) < TotalOutput(previous))
        {
            return current;
        }

        // A reclassification can decrease an exclusive component while the inclusive
        // total grows. Preserve that total, with an unknown split, rather than adding
        // independently clamped positive changes and inventing extra usage.
        bool inputRegressed = InputComponentsRegressed(current, previous);
        bool outputRegressed = OutputComponentsRegressed(current, previous);
        return new TokenBreakdown(
            inputRegressed ? TotalInput(current) - TotalInput(previous) : current.Input - previous.Input,
            outputRegressed ? TotalOutput(current) - TotalOutput(previous) : current.Output - previous.Output,
            outputRegressed ? 0 : current.Reasoning - previous.Reasoning,
            inputRegressed ? 0 : current.CacheRead - previous.CacheRead,
            inputRegressed ? 0 : current.CacheWrite - previous.CacheWrite);
    }

    private static bool InputComponentsRegressed(TokenBreakdown current, TokenBreakdown previous) =>
        current.Input < previous.Input || current.CacheRead < previous.CacheRead || current.CacheWrite < previous.CacheWrite;

    private static bool OutputComponentsRegressed(TokenBreakdown current, TokenBreakdown previous) =>
        current.Output < previous.Output || current.Reasoning < previous.Reasoning;

    private static long TotalInput(TokenBreakdown value) => checked(
        value.Input + value.CacheRead + value.CacheWrite);

    private static long TotalOutput(TokenBreakdown value) => checked(
        value.Output + value.Reasoning);

    private void PruneCheckpointDays(
        CodexUsageCheckpointState checkpoints,
        DateOnly recentFrom)
    {
        foreach (CodexUsageFileCheckpoint checkpoint in checkpoints.Files.Values)
        {
            checkpoint.Observations.RemoveAll(item => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
                item.Timestamp, TimeZoneInfo.FindSystemTimeZoneById(_groupingTimeZoneId)).DateTime) < recentFrom);
            checkpoint.Operations.RemoveAll(item => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
                item.StartedAt, TimeZoneInfo.FindSystemTimeZoneById(_groupingTimeZoneId)).DateTime) < recentFrom);
        }
    }

    private ScanResult CreateScanResult(
        CodexUsageCheckpointState checkpoints,
        LocalScanState state)
    {
        bool legacyRepresentation = checkpoints.Files.Values.Any(file => file.Observations.Any(item => item.RepresentationRevision is null));
        UsageSourceReadStatus status = state.IsPartial || legacyRepresentation
            ? UsageSourceReadStatus.Partial
            : !checkpoints.Files.Values.Any(file => file.Observations.Count > 0)
                ? UsageSourceReadStatus.NoData
                : UsageSourceReadStatus.Complete;
        UsageSourceIssueKind? issue = status switch
        {
            UsageSourceReadStatus.Partial when state.UnsupportedSchema =>
                UsageSourceIssueKind.UnsupportedSchema,
            UsageSourceReadStatus.Partial when legacyRepresentation => UsageSourceIssueKind.UnresolvedHistory,
            UsageSourceReadStatus.Partial => UsageSourceIssueKind.PartialScan,
            UsageSourceReadStatus.NoData => UsageSourceIssueKind.Empty,
            _ => null,
        };
        UsageSessionLink[] sessionLinks = checkpoints.Files.Values
            .Where(file => file.SessionKey is not null
                && file.AttributionEpoch is > 0
                && OpaqueAttributionKey.IsHexSha256(file.SessionKey)
                && (file.ParentSessionKey is null || OpaqueAttributionKey.IsHexSha256(file.ParentSessionKey)))
            .SelectMany(file => file.Observations
                .Where(item => AttributionAdmission.AdmitsAutomaticLink(
                    _scanConsent,
                    item.Timestamp,
                    item.Key,
                    checkpoints.SessionAdmissionEventKeys,
                    SessionAttributionBackfillFrom,
                    SessionAttributionBackfillTo,
                    checkpoints.SessionCommittedEventKeys))
                .Select(item => new UsageSessionLink(
                new UsageEventKey(item.Key),
                new OpaqueAttributionKey(file.SessionKey!),
                file.ParentSessionKey is null ? null : new OpaqueAttributionKey(file.ParentSessionKey),
                file.AttributionEpoch!.Value)))
            .DistinctBy(link => link.EventKey.Value)
            .ToArray();
        UsageProjectLink[] projectLinks = checkpoints.Files.Values
            .SelectMany(file => file.Observations
                .Where(item => item.ProjectEpoch is > 0
                    && item.ProjectMappingKind is not null
                    && (item.ProjectKey is null || OpaqueAttributionKey.IsHexSha256(item.ProjectKey))
                    && AttributionAdmission.AdmitsAutomaticLink(
                        _scanProjectConsent,
                        item.Timestamp,
                        item.Key,
                        checkpoints.ProjectAdmissionEventKeys,
                        ProjectAttributionBackfillFrom,
                        ProjectAttributionBackfillTo,
                        checkpoints.ProjectCommittedEventKeys))
                .Select(item => new UsageProjectLink(
                    new UsageEventKey(item.Key),
                    item.ProjectKey is null ? null : new OpaqueAttributionKey(item.ProjectKey),
                    item.ProjectEpoch!.Value,
                    ProjectMappingKindCodec.TryParse(item.ProjectMappingKind, out ProjectMappingKind kind)
                        ? kind
                        : ProjectMappingKind.Ambiguous)))
            .DistinctBy(link => link.EventKey.Value)
            .ToArray();
        foreach (UsageSessionLink link in sessionLinks)
        {
            checkpoints.SessionCommittedEventKeys.Add(link.EventKey.Value);
        }

        foreach (UsageProjectLink link in projectLinks)
        {
            checkpoints.ProjectCommittedEventKeys.Add(link.EventKey.Value);
        }

        IReadOnlyList<UsageOperationFact> operations = AdmitOperations(checkpoints);

        return new ScanResult(
            status,
            issue ?? UsageSourceIssueKind.None)
        {
            SourceInstance = checkpoints.SourceAuthority,
            Observations = checkpoints.Files.Values.SelectMany(item => item.Observations)
                .Select(item => CreateObservationEvent(item, checkpoints.SourceAuthority))
                .DistinctBy(item => item.EventKey).OrderBy(item => item.OccurredAtUtc).ToArray(),
            SessionLinks = sessionLinks,
            ProjectLinks = projectLinks,
            Operations = operations,
        };
    }
}
