namespace TokenUsage.Core.Usage;

public sealed record UsageOperationTimelineRow(
    UsageOperationKind Kind,
    string Tool,
    string? Server,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int Quantity,
    OpaqueAttributionKey? SessionKey,
    UsageOperationOutcome Outcome,
    string? OperationKey = null);

public sealed record UsageWorkflowEvidenceEvent(
    UsageOperationKind Kind,
    string Tool,
    string? Server,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    OpaqueAttributionKey? SessionKey,
    string? OperationKey,
    UsageOperationOutcome Outcome);

public sealed record UsageWorkflowSummary(
    string MethodVersion,
    string FirstEditReason,
    int SameFileVerificationSeparated,
    int ExcludedConcurrent,
    int EligibleFileEdits,
    int EligibleVerifications,
    int UnlinkedFileEdits,
    int UnlinkedVerifications,
    string CostAvailability,
    IReadOnlyList<string> SameFileFileIds,
    IReadOnlyList<UsageWorkflowEvidenceEvent> ContributingEvents,
    int ExcludedFileConcurrent = 0,
    int ExcludedEditTestConcurrent = 0,
    int IncompleteUnproved = 0)
{
    public static UsageWorkflowSummary Empty { get; } = new(
        UsageWorkflowIndicators.MethodVersion,
        UsageWorkflowIndicators.FirstEditUnavailableReason,
        0,
        0,
        0,
        0,
        0,
        0,
        UsageWorkflowIndicators.CostUnavailable,
        [],
        []);
}

public static class UsageWorkflowIndicators
{
    public const string MethodVersion = "workflow-indicators/v1";
    public const string FirstEditUnavailableReason = "no-proved-task-start";
    public const string CostUnavailable = "unavailable";

    public static bool IsFileEdit(UsageOperationKind kind) => kind == UsageOperationKind.File;

    public static bool IsVerification(UsageOperationKind kind, string tool) =>
        kind == UsageOperationKind.Command && string.Equals(tool, "test", StringComparison.Ordinal);

    public static UsageWorkflowSummary Evaluate(IReadOnlyList<UsageOperationTimelineRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int fileEdits = 0;
        int verifications = 0;
        int unlinkedFiles = 0;
        int unlinkedVerifications = 0;
        foreach (UsageOperationTimelineRow row in rows)
        {
            int quantity = Math.Max(1, row.Quantity);
            if (IsFileEdit(row.Kind))
            {
                fileEdits += quantity;
                if (row.SessionKey is null)
                {
                    unlinkedFiles += quantity;
                }
            }
            else if (IsVerification(row.Kind, row.Tool))
            {
                verifications += quantity;
                if (row.SessionKey is null)
                {
                    unlinkedVerifications += quantity;
                }
            }
        }

        int patterns = 0;
        int excludedFile = 0;
        int excludedEditTest = 0;
        int incomplete = 0;
        var contributingIds = new HashSet<string>(StringComparer.Ordinal);
        var contributingEvents = new List<UsageWorkflowEvidenceEvent>();
        var seenEvidence = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<string, UsageOperationTimelineRow> session in rows
            .Where(row => row.SessionKey is not null)
            .GroupBy(row => row.SessionKey!.Value, StringComparer.Ordinal))
        {
            SessionEvaluation evaluated = EvaluateSession(session.ToArray());
            patterns += evaluated.Patterns;
            excludedFile += evaluated.ExcludedFile;
            excludedEditTest += evaluated.ExcludedEditTest;
            incomplete += evaluated.Incomplete;
            foreach (string fileId in evaluated.FileIds)
            {
                contributingIds.Add(fileId);
            }

            foreach (UsageWorkflowEvidenceEvent evidence in evaluated.Events)
            {
                if (seenEvidence.Add(EvidenceIdentity(evidence)))
                {
                    contributingEvents.Add(evidence);
                }
            }
        }

        return new UsageWorkflowSummary(
            MethodVersion,
            FirstEditUnavailableReason,
            patterns,
            excludedFile + excludedEditTest,
            fileEdits,
            verifications,
            unlinkedFiles,
            unlinkedVerifications,
            CostUnavailable,
            contributingIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            contributingEvents,
            excludedFile,
            excludedEditTest,
            incomplete);
    }

    public static bool MatchesEvidence(UsageWorkflowEvidenceEvent evidence, UsageOperationKind kind, string tool, string? server, string? sessionKey)
    {
        if (evidence.Kind != kind
            || !string.Equals(evidence.Tool, tool, StringComparison.Ordinal)
            || !string.Equals(evidence.Server, server, StringComparison.Ordinal))
        {
            return false;
        }

        string? evidenceSession = evidence.SessionKey?.Value;
        return string.Equals(evidenceSession, sessionKey, StringComparison.Ordinal);
    }

    private static SessionEvaluation EvaluateSession(IReadOnlyList<UsageOperationTimelineRow> rows)
    {
        List<UsageOperationTimelineRow> edits = Expand(rows.Where(row => IsFileEdit(row.Kind)))
            .Where(row => row.Server is { Length: 64 })
            .ToList();
        List<UsageOperationTimelineRow> tests = Expand(rows.Where(row => IsVerification(row.Kind, row.Tool)))
            .ToList();
        int excludedFile = 0;
        int excludedEditTest = 0;
        int incomplete = 0;
        foreach (UsageOperationTimelineRow edit in edits)
        {
            foreach (UsageOperationTimelineRow test in tests)
            {
                if (!HasProvedEnd(edit) || !HasProvedEnd(test))
                {
                    if (CouldOverlap(edit, test))
                    {
                        incomplete++;
                    }

                    continue;
                }

                if (Concurrent(edit, test))
                {
                    excludedEditTest++;
                }
            }
        }

        int patterns = 0;
        var fileIds = new List<string>();
        var events = new List<UsageWorkflowEvidenceEvent>();
        foreach (string file in edits.Select(row => row.Server!).Distinct(StringComparer.Ordinal))
        {
            List<UsageOperationTimelineRow> fileEdits = edits
                .Where(row => string.Equals(row.Server, file, StringComparison.Ordinal))
                .OrderBy(row => row.StartedAtUtc)
                .ThenBy(row => row.EndedAtUtc ?? DateTimeOffset.MaxValue)
                .ToList();
            bool counted = false;
            for (int index = 0; index < fileEdits.Count - 1; index++)
            {
                UsageOperationTimelineRow first = fileEdits[index];
                UsageOperationTimelineRow second = fileEdits[index + 1];
                if (!HasProvedEnd(first) || !HasProvedEnd(second))
                {
                    incomplete++;
                    continue;
                }

                if (Concurrent(first, second))
                {
                    excludedFile++;
                    continue;
                }

                UsageOperationTimelineRow? between = tests.FirstOrDefault(test =>
                    HasProvedEnd(test)
                    && StrictlyBefore(first, test)
                    && StrictlyBefore(test, second));
                if (between is null)
                {
                    if (tests.Any(test =>
                        !HasProvedEnd(test)
                        && test.StartedAtUtc >= first.EndedAtUtc
                        && test.StartedAtUtc < second.StartedAtUtc))
                    {
                        incomplete++;
                    }

                    continue;
                }

                patterns++;
                counted = true;
                events.Add(ToEvidence(first));
                events.Add(ToEvidence(between));
                events.Add(ToEvidence(second));
            }

            if (counted)
            {
                fileIds.Add(file);
            }
        }

        return new SessionEvaluation(patterns, excludedFile, excludedEditTest, incomplete, fileIds, events);
    }

    private static IEnumerable<UsageOperationTimelineRow> Expand(IEnumerable<UsageOperationTimelineRow> rows)
    {
        foreach (UsageOperationTimelineRow row in rows)
        {
            int quantity = Math.Max(1, row.Quantity);
            for (int index = 0; index < quantity; index++)
            {
                yield return row with { Quantity = 1 };
            }
        }
    }

    private static bool HasProvedEnd(UsageOperationTimelineRow row) => row.EndedAtUtc is not null;

    private static bool StrictlyBefore(UsageOperationTimelineRow left, UsageOperationTimelineRow right) =>
        left.EndedAtUtc is { } leftEnd
        && leftEnd <= right.StartedAtUtc
        && left.StartedAtUtc < right.StartedAtUtc;

    private static bool Concurrent(UsageOperationTimelineRow left, UsageOperationTimelineRow right) =>
        HasProvedEnd(left)
        && HasProvedEnd(right)
        && !StrictlyBefore(left, right)
        && !StrictlyBefore(right, left);

    private static bool CouldOverlap(UsageOperationTimelineRow left, UsageOperationTimelineRow right) =>
        !StrictlyBefore(left, right) && !StrictlyBefore(right, left);

    private static UsageWorkflowEvidenceEvent ToEvidence(UsageOperationTimelineRow row) =>
        new(
            row.Kind,
            row.Tool,
            row.Server,
            row.StartedAtUtc,
            row.EndedAtUtc,
            row.SessionKey,
            row.OperationKey,
            row.Outcome);

    private static string EvidenceIdentity(UsageWorkflowEvidenceEvent evidence) =>
        string.Join(
            '\u001f',
            evidence.OperationKey ?? "",
            evidence.Kind,
            evidence.Tool,
            evidence.Server ?? "",
            evidence.SessionKey?.Value ?? "",
            evidence.StartedAtUtc.UtcDateTime.ToString("o"));

    private sealed record SessionEvaluation(
        int Patterns,
        int ExcludedFile,
        int ExcludedEditTest,
        int Incomplete,
        IReadOnlyList<string> FileIds,
        IReadOnlyList<UsageWorkflowEvidenceEvent> Events);
}
