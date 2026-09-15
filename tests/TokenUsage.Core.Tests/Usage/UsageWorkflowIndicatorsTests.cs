using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageWorkflowIndicatorsTests
{
    [Fact]
    public void SameFileVerificationSeparatedPatternCountsOnce()
    {
        OpaqueAttributionKey session = Key("session");
        OpaqueAttributionKey file = Key("file-one");
        UsageWorkflowSummary summary = UsageWorkflowIndicators.Evaluate(
        [
            File(file, At(0), session),
            Test(At(10), session),
            File(file, At(20), session),
        ]);
        Assert.Equal(UsageWorkflowIndicators.MethodVersion, summary.MethodVersion);
        Assert.Equal(UsageWorkflowIndicators.FirstEditUnavailableReason, summary.FirstEditReason);
        Assert.Equal(1, summary.SameFileVerificationSeparated);
        Assert.Equal(0, summary.ExcludedConcurrent);
        Assert.Equal(2, summary.EligibleFileEdits);
        Assert.Equal(1, summary.EligibleVerifications);
        Assert.Equal(file.Value, Assert.Single(summary.SameFileFileIds));
        Assert.Equal(3, summary.ContributingEvents.Count);
        Assert.Equal(UsageOperationKind.File, summary.ContributingEvents[0].Kind);
        Assert.Equal(UsageOperationKind.Command, summary.ContributingEvents[1].Kind);
        Assert.Equal("test", summary.ContributingEvents[1].Tool);
        Assert.Equal(UsageOperationKind.File, summary.ContributingEvents[2].Kind);
        Assert.Equal(UsageWorkflowIndicators.CostUnavailable, summary.CostAvailability);
    }

    [Fact]
    public void PendingVerificationIsNotCompletedBetweenEdits()
    {
        OpaqueAttributionKey session = Key("session");
        OpaqueAttributionKey file = Key("file-one");
        UsageWorkflowSummary pending = UsageWorkflowIndicators.Evaluate(
        [
            File(file, At(0), session, operationKey: "edit-a"),
            Test(At(10), session, pending: true, operationKey: "test-pending"),
            File(file, At(20), session, operationKey: "edit-b"),
        ]);
        Assert.Equal(0, pending.SameFileVerificationSeparated);
        Assert.Equal(0, pending.ExcludedConcurrent);
        Assert.True(pending.IncompleteUnproved > 0);
        Assert.Empty(pending.ContributingEvents);

        UsageWorkflowSummary endedAfterSecond = UsageWorkflowIndicators.Evaluate(
        [
            File(file, At(0), session, operationKey: "edit-a"),
            Test(At(10), session, ended: At(25), operationKey: "test-late"),
            File(file, At(20), session, operationKey: "edit-b"),
        ]);
        Assert.Equal(0, endedAfterSecond.SameFileVerificationSeparated);
        Assert.True(endedAfterSecond.ExcludedConcurrent > 0);
        Assert.Equal(0, endedAfterSecond.ExcludedFileConcurrent);
        Assert.True(endedAfterSecond.ExcludedEditTestConcurrent > 0);

        UsageWorkflowSummary endedBeforeSecond = UsageWorkflowIndicators.Evaluate(
        [
            File(file, At(0), session, operationKey: "edit-a"),
            Test(At(10), session, ended: At(11), operationKey: "test-done"),
            File(file, At(20), session, operationKey: "edit-b"),
        ]);
        Assert.Equal(1, endedBeforeSecond.SameFileVerificationSeparated);
        Assert.Equal(0, endedAfterSecond.SameFileVerificationSeparated);
        Assert.Equal(3, endedBeforeSecond.ContributingEvents.Count);
    }

    [Fact]
    public void PendingEditIsNotCompletedOrdering()
    {
        OpaqueAttributionKey session = Key("session");
        OpaqueAttributionKey file = Key("file-one");
        UsageWorkflowSummary summary = UsageWorkflowIndicators.Evaluate(
        [
            File(file, At(0), session, pending: true, operationKey: "edit-pending"),
            Test(At(10), session, operationKey: "test-done"),
            File(file, At(20), session, operationKey: "edit-b"),
        ]);
        Assert.Equal(0, summary.SameFileVerificationSeparated);
        Assert.True(summary.IncompleteUnproved > 0);
        Assert.Empty(summary.ContributingEvents);
    }

    [Fact]
    public void FailedCompletedVerificationStillSeparatesSameFileEdits()
    {
        OpaqueAttributionKey session = Key("session");
        OpaqueAttributionKey file = Key("file-one");
        UsageWorkflowSummary summary = UsageWorkflowIndicators.Evaluate(
        [
            File(file, At(0), session),
            Test(At(10), session, outcome: UsageOperationOutcome.Error),
            File(file, At(20), session),
        ]);
        Assert.Equal(1, summary.SameFileVerificationSeparated);
        Assert.Equal(UsageOperationOutcome.Error, summary.ContributingEvents[1].Outcome);
    }

    [Fact]
    public void ContributingEvidenceExcludesOtherSessionsAndOutsideIntervalTests()
    {
        OpaqueAttributionKey sessionA = Key("session-a");
        OpaqueAttributionKey sessionB = Key("session-b");
        OpaqueAttributionKey file = Key("file-one");
        UsageWorkflowSummary summary = UsageWorkflowIndicators.Evaluate(
        [
            File(file, At(0), sessionA, operationKey: "a-edit-1"),
            Test(At(10), sessionA, operationKey: "a-test-between"),
            File(file, At(20), sessionA, operationKey: "a-edit-2"),
            Test(At(30), sessionA, operationKey: "a-test-after"),
            File(file, At(0), sessionB, operationKey: "b-edit-1"),
            Test(At(5), sessionB, operationKey: "b-test"),
        ]);
        Assert.Equal(1, summary.SameFileVerificationSeparated);
        Assert.Equal(3, summary.ContributingEvents.Count);
        Assert.All(summary.ContributingEvents, item => Assert.Equal(sessionA.Value, item.SessionKey?.Value));
        Assert.DoesNotContain(summary.ContributingEvents, item => item.OperationKey == "a-test-after");
        Assert.DoesNotContain(summary.ContributingEvents, item => item.OperationKey == "b-test");
        Assert.Equal(file.Value, Assert.Single(summary.SameFileFileIds));
    }

    [Fact]
    public void DifferentFileAfterVerificationIsNotASameFilePattern()
    {
        OpaqueAttributionKey session = Key("session");
        UsageWorkflowSummary summary = UsageWorkflowIndicators.Evaluate(
        [
            File(Key("file-one"), At(0), session),
            Test(At(10), session),
            File(Key("file-two"), At(20), session),
        ]);
        Assert.Equal(0, summary.SameFileVerificationSeparated);
        Assert.Empty(summary.SameFileFileIds);
        Assert.Equal(3, summary.EligibleFileEdits + summary.EligibleVerifications);
    }

    [Fact]
    public void ReadBetweenEditsIsNotVerification()
    {
        OpaqueAttributionKey session = Key("session");
        OpaqueAttributionKey file = Key("file-one");
        UsageWorkflowSummary summary = UsageWorkflowIndicators.Evaluate(
        [
            File(file, At(0), session),
            new UsageOperationTimelineRow(
                UsageOperationKind.Tool,
                "Read",
                null,
                At(10),
                At(11),
                1,
                session,
                UsageOperationOutcome.Success),
            File(file, At(20), session),
        ]);
        Assert.Equal(0, summary.SameFileVerificationSeparated);
        Assert.Equal(0, summary.EligibleVerifications);
    }

    [Fact]
    public void ConcurrentEditAndVerificationAreExcluded()
    {
        OpaqueAttributionKey session = Key("session");
        OpaqueAttributionKey file = Key("file-one");
        DateTimeOffset same = At(10);
        UsageWorkflowSummary summary = UsageWorkflowIndicators.Evaluate(
        [
            File(file, At(0), session),
            File(file, same, session),
            Test(same, session),
        ]);
        Assert.Equal(0, summary.SameFileVerificationSeparated);
        Assert.True(summary.ExcludedConcurrent > 0);
    }

    [Fact]
    public void UnlinkedAndMissingStartStayOutOfPatterns()
    {
        OpaqueAttributionKey file = Key("file-one");
        UsageWorkflowSummary summary = UsageWorkflowIndicators.Evaluate(
        [
            File(file, At(0), session: null),
            Test(At(10), session: null),
            File(file, At(20), session: null),
        ]);
        Assert.Equal(0, summary.SameFileVerificationSeparated);
        Assert.Equal(2, summary.UnlinkedFileEdits);
        Assert.Equal(1, summary.UnlinkedVerifications);
        Assert.Equal(UsageWorkflowIndicators.FirstEditUnavailableReason, summary.FirstEditReason);
    }

    private static UsageOperationTimelineRow File(
        OpaqueAttributionKey file,
        DateTimeOffset started,
        OpaqueAttributionKey? session,
        DateTimeOffset? ended = null,
        string? operationKey = null,
        bool pending = false) =>
        new(
            UsageOperationKind.File,
            "patch",
            file.Value,
            started,
            pending ? null : ended ?? started.AddSeconds(1),
            1,
            session,
            UsageOperationOutcome.Success,
            operationKey);

    private static UsageOperationTimelineRow Test(
        DateTimeOffset started,
        OpaqueAttributionKey? session,
        DateTimeOffset? ended = null,
        string? operationKey = null,
        UsageOperationOutcome outcome = UsageOperationOutcome.Success,
        bool pending = false) =>
        new(
            UsageOperationKind.Command,
            "test",
            null,
            started,
            pending ? null : ended ?? started.AddSeconds(1),
            1,
            session,
            outcome,
            operationKey);

    private static DateTimeOffset At(int seconds) =>
        new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero).AddSeconds(seconds);

    private static OpaqueAttributionKey Key(string seed) =>
        new(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed))).ToLowerInvariant());
}
