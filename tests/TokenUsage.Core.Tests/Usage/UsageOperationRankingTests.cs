using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageOperationRankingTests
{
    [Fact]
    public async Task RankingHonorsCivilDayUtcBounds()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        OpaqueAttributionKey key = Key("civil-op");
        await repository.UpsertOperationFactsAsync(
        [
            new UsageOperationFact(
                key,
                AttributionCapability.CodexMcp,
                1,
                UsageOperationKind.Mcp,
                "list_things",
                "example-server",
                UsageOperationOutcome.Success,
                new DateTimeOffset(2026, 9, 13, 1, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 13, 1, 0, 1, TimeSpan.Zero),
                sessionKey: null),
        ]);
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone(
            "utc-minus-three",
            TimeSpan.FromHours(-3),
            "UTC-3",
            "UTC-3");
        (DateTimeOffset thirteenthFrom, DateTimeOffset thirteenthTo) = UsageCivilDay.UtcBounds(
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            zone);
        (DateTimeOffset twelfthFrom, DateTimeOffset twelfthTo) = UsageCivilDay.UtcBounds(
            new DateOnly(2026, 9, 12),
            new DateOnly(2026, 9, 12),
            zone);
        IReadOnlyList<UsageOperationRankedRow> thirteenth = await repository.ReadOperationRankingAsync(
            thirteenthFrom,
            thirteenthTo,
            AttributionCapability.CodexMcp,
            1);
        IReadOnlyList<UsageOperationRankedRow> twelfth = await repository.ReadOperationRankingAsync(
            twelfthFrom,
            twelfthTo,
            AttributionCapability.CodexMcp,
            1);
        Assert.Empty(thirteenth);
        Assert.Equal(1, Assert.Single(twelfth).InvocationCount);
    }

    [Fact]
    public async Task HomogeneousSessionKeysExcludeMixedModelsAndConfigurations()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        DateTimeOffset at = new(2026, 9, 13, 15, 0, 0, TimeSpan.Zero);
        UsageEvent mixedModelA = CodexEvent("mixed-a", "model-a", at, reasoningEffort: "low");
        UsageEvent mixedModelB = CodexEvent("mixed-b", "model-b", at, reasoningEffort: "low");
        UsageEvent effortLow = CodexEvent("effort-low", "model-a", at, reasoningEffort: "low");
        UsageEvent effortHigh = CodexEvent("effort-high", "model-a", at, reasoningEffort: "high");
        UsageEvent onlyA = CodexEvent("only-a", "model-a", at, reasoningEffort: "low");
        await repository.IngestAsync([mixedModelA, mixedModelB, effortLow, effortHigh, onlyA]);
        OpaqueAttributionKey mixedSession = Key("session-mixed-models");
        OpaqueAttributionKey mixedEffortSession = Key("session-mixed-effort");
        OpaqueAttributionKey pureA = Key("session-pure-a");
        await repository.ReplaceSessionLinksAsync(
        [
            new UsageSessionLink(mixedModelA.EventKey, mixedSession, parentSessionKey: null, 1),
            new UsageSessionLink(mixedModelB.EventKey, mixedSession, parentSessionKey: null, 1),
            new UsageSessionLink(effortLow.EventKey, mixedEffortSession, parentSessionKey: null, 1),
            new UsageSessionLink(effortHigh.EventKey, mixedEffortSession, parentSessionKey: null, 1),
            new UsageSessionLink(onlyA.EventKey, pureA, parentSessionKey: null, 1),
        ]);
        DateTimeOffset from = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<string> models = await repository.ReadHomogeneousSessionKeysAsync(
            from,
            to,
            new AgentId("codex"),
            AttributionCapability.CodexSession,
            1,
            new ModelId("model-a"));
        Assert.Contains(pureA.Value, models);
        Assert.Contains(mixedEffortSession.Value, models);
        Assert.DoesNotContain(mixedSession.Value, models);
        Assert.Equal(2, models.Count);
        IReadOnlyList<string> low = await repository.ReadHomogeneousSessionKeysAsync(
            from,
            to,
            new AgentId("codex"),
            AttributionCapability.CodexSession,
            1,
            new ModelId("model-a"),
            detail: new UsageDetailSelection([], ["low"], []));
        Assert.Equal(pureA.Value, Assert.Single(low));
        await repository.UpsertOperationFactsAsync(
        [
            Fact("op-mixed", mixedSession, "mixed_tool"),
            Fact("op-pure", pureA, "pure_tool"),
            Fact("op-unlinked", session: null, "unlinked_tool"),
        ]);
        IReadOnlyList<UsageOperationRankedRow> proved = await repository.ReadOperationRankingAsync(
            from,
            to,
            AttributionCapability.CodexMcp,
            1,
            sessionKeys: [pureA.Value],
            restrictToSessionKeys: true);
        IReadOnlyList<UsageOperationRankedRow> mixed = await repository.ReadOperationRankingAsync(
            from,
            to,
            AttributionCapability.CodexMcp,
            1,
            sessionKeys: [pureA.Value],
            restrictToSessionKeys: false);
        Assert.Equal("pure_tool", Assert.Single(proved).Tool);
        Assert.Equal(2, mixed.Sum(row => row.InvocationCount));
        Assert.Contains(mixed, row => row.Tool == "mixed_tool");
        Assert.Contains(mixed, row => row.Tool == "unlinked_tool");
    }

    [Fact]
    public async Task TimelineOmitsOperationKeysAndKeepsSessionOrder()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        OpaqueAttributionKey session = Key("timeline-session");
        DateTimeOffset first = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset second = first.AddSeconds(10);
        await repository.UpsertOperationFactsAsync(
        [
            new UsageOperationFact(
                Key("timeline-file"),
                AttributionCapability.CodexFiles,
                1,
                UsageOperationKind.File,
                "patch",
                Key("file-one").Value,
                UsageOperationOutcome.Success,
                first,
                first.AddSeconds(1),
                session),
            new UsageOperationFact(
                Key("timeline-test"),
                AttributionCapability.CodexCommands,
                1,
                UsageOperationKind.Command,
                "test",
                null,
                UsageOperationOutcome.Success,
                second,
                second.AddSeconds(1),
                session),
        ]);
        DateTimeOffset from = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<UsageOperationTimelineRow> files = await repository.ReadOperationTimelineAsync(
            from,
            to,
            AttributionCapability.CodexFiles,
            1);
        IReadOnlyList<UsageOperationTimelineRow> commands = await repository.ReadOperationTimelineAsync(
            from,
            to,
            AttributionCapability.CodexCommands,
            1);
        Assert.Equal("patch", Assert.Single(files).Tool);
        Assert.Equal(session.Value, Assert.Single(files).SessionKey?.Value);
        Assert.Equal("test", Assert.Single(commands).Tool);
        Assert.Equal(Key("timeline-file").Value, Assert.Single(files).OperationKey);
        UsageWorkflowSummary workflow = UsageWorkflowIndicators.Evaluate(files.Concat(commands).ToArray());
        Assert.Equal(0, workflow.SameFileVerificationSeparated);
        Assert.Equal(1, workflow.EligibleFileEdits);
        Assert.Equal(1, workflow.EligibleVerifications);
    }

    [Fact]
    public async Task PurgeSkillsAndFilesLeavesNumericEvents()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        DateTimeOffset at = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        UsageEvent numeric = CodexEvent("purge-numeric", "model-a", at);
        await repository.IngestAsync([numeric]);
        await repository.UpsertOperationFactsAsync(
        [
            new UsageOperationFact(
                Key("purge-spawn"),
                AttributionCapability.CodexSkills,
                1,
                UsageOperationKind.Spawn,
                "reviewer",
                null,
                UsageOperationOutcome.Unknown,
                at,
                at.AddSeconds(1),
                Key("purge-session")),
            new UsageOperationFact(
                Key("purge-file"),
                AttributionCapability.CodexFiles,
                1,
                UsageOperationKind.File,
                "patch",
                Key("file-one").Value,
                UsageOperationOutcome.Success,
                at,
                at.AddSeconds(1),
                Key("purge-session")),
        ]);
        Assert.Equal(1, await repository.PurgeOperationFactsAsync(AttributionCapability.CodexSkills));
        Assert.Equal(1, await repository.PurgeOperationFactsAsync(AttributionCapability.CodexFiles));
        Assert.Equal(0, await repository.CountOperationFactsAsync(AttributionCapability.CodexSkills));
        Assert.Equal(0, await repository.CountOperationFactsAsync(AttributionCapability.CodexFiles));
        Assert.Equal(10, (await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13))).Sum(row => row.Tokens.Total));
    }

    [Fact]
    public async Task Schema11UpgradeAndLegacyKeyReconcileToOneLogicalCall()
    {
        using var folder = new TemporaryFolder();
        UsageRepository created = await UsageRepository.OpenAsync(folder.DatabasePath);
        DateTimeOffset at = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        UsageEvent numeric = CodexEvent("legacy-numeric", "model-a", at);
        await created.IngestAsync([numeric]);
        OpaqueAttributionKey legacyKey = Key("legacy-call");
        OpaqueAttributionKey namedKey = Key("named-call");
        await using (var setup = new SqliteConnection($"Data Source={folder.DatabasePath};Pooling=False"))
        {
            await setup.OpenAsync();
            setup.CreateFunction("tokenusage_writer_schema", () => 12);
            await using SqliteCommand command = setup.CreateCommand();
            command.CommandText = """
                DELETE FROM schema_migration WHERE version >= 12;
                ALTER TABLE operation_fact DROP COLUMN source_instance;
                INSERT INTO operation_fact (
                    operation_key, capability, consent_epoch, kind, tool, server, outcome,
                    started_at_utc, ended_at_utc, session_key, quantity)
                VALUES (
                    $key, 'codex-mcp', 1, 'mcp', 'list_things', 'example-server', 'unknown',
                    $started, NULL, NULL, 1);
                """;
            command.Parameters.AddWithValue("$key", legacyKey.Value);
            command.Parameters.AddWithValue("$started", at.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync();
        }

        UsageRepository upgraded = await UsageRepository.OpenAsync(folder.DatabasePath);
        Assert.Equal(12, UsageRepository.CurrentSchemaVersion);
        Assert.Equal(1, await upgraded.CountOperationFactsAsync(AttributionCapability.CodexMcp, sourceInstancePresent: false));
        Assert.Equal(0, await upgraded.CountOperationFactsAsync(AttributionCapability.CodexMcp, sourceInstancePresent: true));
        DateTimeOffset from = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(1, (await upgraded.ReadOperationRankingAsync(from, to, AttributionCapability.CodexMcp, 1))
            .Sum(row => row.InvocationCount));
        UsageRepository unchangedRefresh = await UsageRepository.OpenAsync(folder.DatabasePath);
        Assert.Equal(1, await unchangedRefresh.CountOperationFactsAsync(AttributionCapability.CodexMcp, sourceInstancePresent: false));
        Assert.Equal(1, (await unchangedRefresh.ReadOperationRankingAsync(from, to, AttributionCapability.CodexMcp, 1))
            .Sum(row => row.InvocationCount));
        Assert.Equal(10, (await unchangedRefresh.QueryDailyRollupsAsync(
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13))).Sum(row => row.Tokens.Total));

        var source = new UsageSourceInstanceId(new string('b', 64));
        await upgraded.UpsertOperationFactsAsync(
        [
            new UsageOperationFact(
                namedKey,
                AttributionCapability.CodexMcp,
                1,
                UsageOperationKind.Mcp,
                "list_things",
                "example-server",
                UsageOperationOutcome.Success,
                at,
                at.AddSeconds(1),
                sessionKey: null,
                sourceInstance: source,
                legacyOperationKey: legacyKey),
        ]);
        Assert.Equal(1, await upgraded.CountOperationFactsAsync(AttributionCapability.CodexMcp));
        Assert.Equal(1, await upgraded.CountOperationFactsAsync(AttributionCapability.CodexMcp, sourceInstancePresent: true));
        Assert.Equal(0, await upgraded.CountOperationFactsAsync(AttributionCapability.CodexMcp, sourceInstancePresent: false));
        IReadOnlyList<UsageOperationTimelineRow> afterEnd = await upgraded.ReadOperationTimelineAsync(
            from,
            to,
            AttributionCapability.CodexMcp,
            1);
        UsageOperationTimelineRow remaining = Assert.Single(afterEnd);
        Assert.Equal(namedKey.Value, remaining.OperationKey);
        Assert.Equal(at.AddSeconds(1), remaining.EndedAtUtc);

        await upgraded.UpsertOperationFactsAsync(
        [
            new UsageOperationFact(
                namedKey,
                AttributionCapability.CodexMcp,
                1,
                UsageOperationKind.Mcp,
                "list_things",
                "example-server",
                UsageOperationOutcome.Success,
                at,
                at.AddSeconds(2),
                sessionKey: null,
                sourceInstance: source,
                legacyOperationKey: legacyKey),
        ]);
        Assert.Equal(1, await upgraded.CountOperationFactsAsync(AttributionCapability.CodexMcp));
        Assert.Equal(10, (await upgraded.QueryDailyRollupsAsync(
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13))).Sum(row => row.Tokens.Total));
        await upgraded.UpsertOperationFactsAsync(
        [
            new UsageOperationFact(
                namedKey,
                AttributionCapability.CodexMcp,
                1,
                UsageOperationKind.Mcp,
                "list_things",
                "example-server",
                UsageOperationOutcome.Success,
                at,
                at.AddSeconds(2),
                sessionKey: null,
                sourceInstance: source,
                legacyOperationKey: legacyKey),
        ]);
        Assert.Equal(1, await upgraded.CountOperationFactsAsync(AttributionCapability.CodexMcp));
        Assert.Equal(1, (await upgraded.ReadOperationRankingAsync(from, to, AttributionCapability.CodexMcp, 1))
            .Sum(row => row.InvocationCount));

        await upgraded.UpsertOperationFactsAsync(
        [
            new UsageOperationFact(
                Key("other-authority"),
                AttributionCapability.CodexMcp,
                1,
                UsageOperationKind.Mcp,
                "list_things",
                "example-server",
                UsageOperationOutcome.Success,
                at,
                at.AddSeconds(1),
                sessionKey: null,
                sourceInstance: new UsageSourceInstanceId(new string('c', 64))),
        ]);
        Assert.Equal(2, await upgraded.CountOperationFactsAsync(AttributionCapability.CodexMcp));
        Assert.Equal(2, (await upgraded.ReadOperationRankingAsync(from, to, AttributionCapability.CodexMcp, 1))
            .Sum(row => row.InvocationCount));
    }

    [Fact]
    public async Task ProjectAndSessionScopeExcludeOtherLinkedOperations()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        DateTimeOffset at = new(2026, 9, 13, 15, 0, 0, TimeSpan.Zero);
        UsageEvent eventA = CodexEvent("event-a", "model-a", at);
        UsageEvent eventB = CodexEvent("event-b", "model-b", at);
        await repository.IngestAsync([eventA, eventB]);
        OpaqueAttributionKey sessionA = Key("session-a");
        OpaqueAttributionKey sessionB = Key("session-b");
        OpaqueAttributionKey projectA = Key("project-a");
        OpaqueAttributionKey projectB = Key("project-b");
        await repository.ReplaceSessionLinksAsync(
        [
            new UsageSessionLink(eventA.EventKey, sessionA, parentSessionKey: null, 1),
            new UsageSessionLink(eventB.EventKey, sessionB, parentSessionKey: null, 1),
        ]);
        await repository.ReplaceProjectLinksAsync(
        [
            new UsageProjectLink(eventA.EventKey, projectA, 1, ProjectMappingKind.Observed),
            new UsageProjectLink(eventB.EventKey, projectB, 1, ProjectMappingKind.Observed),
        ]);
        await repository.UpsertOperationFactsAsync(
        [
            Fact("op-a", sessionA, "tool_a"),
            Fact("op-b", sessionB, "tool_b"),
            Fact("op-unlinked", session: null, "tool_unlinked"),
        ]);
        DateTimeOffset from = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<string> projectBSessions = await repository.ReadSessionKeysForProjectAsync(
            projectB,
            1,
            1,
            from,
            to);
        Assert.Equal(sessionB.Value, Assert.Single(projectBSessions));
        IReadOnlyList<UsageOperationRankedRow> projectBOps = await repository.ReadOperationRankingAsync(
            from,
            to,
            AttributionCapability.CodexMcp,
            1,
            sessionKeys: projectBSessions,
            restrictToSessionKeys: true);
        Assert.Equal("tool_b", Assert.Single(projectBOps).Tool);
        IReadOnlyList<UsageOperationRankedRow> sessionBOps = await repository.ReadOperationRankingAsync(
            from,
            to,
            AttributionCapability.CodexMcp,
            1,
            sessionKeys: [sessionB.Value],
            restrictToSessionKeys: true);
        Assert.Equal("tool_b", Assert.Single(sessionBOps).Tool);
        IReadOnlyList<UsageOperationRankedRow> unlinked = await repository.ReadOperationRankingAsync(
            from,
            to,
            AttributionCapability.CodexMcp,
            1,
            unlinkedOnly: true);
        Assert.Equal("tool_unlinked", Assert.Single(unlinked).Tool);
        Assert.DoesNotContain(projectBOps, row => row.Tool == "tool_a");
        Assert.DoesNotContain(sessionBOps, row => row.Tool == "tool_unlinked");
    }

    [Fact]
    public async Task MixedProjectSessionOperationsStayInBroaderPopulation()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        DateTimeOffset at = new(2026, 9, 13, 15, 0, 0, TimeSpan.Zero);
        UsageEvent mixedA = CodexEvent("mixed-event-a", "model-a", at);
        UsageEvent mixedB = CodexEvent("mixed-event-b", "model-b", at.AddMinutes(1));
        UsageEvent onlyB = CodexEvent("only-b-event", "model-b", at.AddMinutes(2));
        UsageEvent unassigned = CodexEvent("unassigned-event", "model-a", at.AddMinutes(3));
        UsageEvent incompleteB = CodexEvent("incomplete-b-event", "model-b", at.AddMinutes(4));
        UsageEvent incompleteMissing = CodexEvent("incomplete-missing-event", "model-a", at.AddMinutes(5));
        await repository.IngestAsync(
            [mixedA, mixedB, onlyB, unassigned, incompleteB, incompleteMissing]);
        OpaqueAttributionKey mixedSession = Key("session-mixed-projects");
        OpaqueAttributionKey onlyBSession = Key("session-only-b");
        OpaqueAttributionKey unassignedSession = Key("session-unassigned");
        OpaqueAttributionKey incompleteSession = Key("session-incomplete");
        OpaqueAttributionKey projectA = Key("project-a");
        OpaqueAttributionKey projectB = Key("project-b");
        await repository.ReplaceSessionLinksAsync(
        [
            new UsageSessionLink(mixedA.EventKey, mixedSession, parentSessionKey: null, 1),
            new UsageSessionLink(mixedB.EventKey, mixedSession, parentSessionKey: null, 1),
            new UsageSessionLink(onlyB.EventKey, onlyBSession, parentSessionKey: null, 1),
            new UsageSessionLink(unassigned.EventKey, unassignedSession, parentSessionKey: null, 1),
            new UsageSessionLink(incompleteB.EventKey, incompleteSession, parentSessionKey: null, 1),
            new UsageSessionLink(incompleteMissing.EventKey, incompleteSession, parentSessionKey: null, 1),
        ]);
        await repository.ReplaceProjectLinksAsync(
        [
            new UsageProjectLink(mixedA.EventKey, projectA, 1, ProjectMappingKind.Observed),
            new UsageProjectLink(mixedB.EventKey, projectB, 1, ProjectMappingKind.Observed),
            new UsageProjectLink(onlyB.EventKey, projectB, 1, ProjectMappingKind.Observed),
            new UsageProjectLink(incompleteB.EventKey, projectB, 1, ProjectMappingKind.Observed),
        ]);
        await repository.UpsertOperationFactsAsync(
        [
            Fact("op-a", mixedSession, "tool_a"),
            Fact("op-b", mixedSession, "tool_b"),
            Fact("op-c", onlyBSession, "tool_c"),
            Fact("op-unassigned", unassignedSession, "tool_unassigned"),
            Fact("op-partial", incompleteSession, "tool_partial"),
            Fact("op-incomplete", incompleteSession, "tool_incomplete"),
        ]);
        DateTimeOffset from = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<string> overlapping = await repository.ReadSessionKeysForProjectAsync(
            projectB,
            1,
            1,
            from,
            to);
        IReadOnlyList<string> homogeneous = await repository.ReadHomogeneousSessionKeysForProjectAsync(
            projectB,
            1,
            1,
            from,
            to);
        Assert.Equal(onlyBSession.Value, Assert.Single(homogeneous));
        Assert.Contains(mixedSession.Value, overlapping);
        Assert.Contains(onlyBSession.Value, overlapping);
        Assert.Contains(incompleteSession.Value, overlapping);
        Assert.DoesNotContain(unassignedSession.Value, overlapping);
        HashSet<string> exact = homogeneous.ToHashSet(StringComparer.Ordinal);
        string[] mixedKeys = overlapping.Where(key => !exact.Contains(key)).ToArray();
        IReadOnlyList<UsageOperationRankedRow> exactOps = await repository.ReadOperationRankingAsync(
            from,
            to,
            AttributionCapability.CodexMcp,
            1,
            sessionKeys: homogeneous,
            restrictToSessionKeys: true);
        Assert.Equal("tool_c", Assert.Single(exactOps).Tool);
        IReadOnlyList<UsageOperationRankedRow> mixedOps = await repository.ReadOperationRankingAsync(
            from,
            to,
            AttributionCapability.CodexMcp,
            1,
            sessionKeys: mixedKeys,
            restrictToSessionKeys: true);
        string[] mixedTools = mixedOps.Select(row => row.Tool).OrderBy(tool => tool, StringComparer.Ordinal).ToArray();
        Assert.Equal(["tool_a", "tool_b", "tool_incomplete", "tool_partial"], mixedTools);
        Assert.DoesNotContain(mixedOps, row => row.Tool == "tool_c");
        Assert.DoesNotContain(mixedOps, row => row.Tool == "tool_unassigned");
        IReadOnlyList<UsageOperationTimelineRow> exactTimeline = await repository.ReadOperationTimelineAsync(
            from,
            to,
            AttributionCapability.CodexMcp,
            1,
            sessionKeys: homogeneous,
            restrictToSessionKeys: true);
        Assert.Equal("tool_c", Assert.Single(exactTimeline).Tool);
        UsageDerivedActivitySummary derived = UsageDerivedActivity.Summarize(exactOps);
        Assert.Equal(1, derived.Eligible);
        Assert.Equal(0, derived.Edit);
        UsageWorkflowSummary workflow = UsageWorkflowIndicators.Evaluate(exactTimeline);
        Assert.Equal(0, workflow.SameFileVerificationSeparated);
        UsageReportSnapshotV2.Document snapshot = UsageReportSnapshotV2.Create(
            at,
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            UsageReportQuery.Build(
            [
                new DailyUsageRollup(
                    new DateOnly(2026, 9, 13),
                    "UTC",
                    new AgentId("codex"),
                    new ModelProviderId("openai"),
                    new ModelId("model-b"),
                    new TokenBreakdown(10, 0, 0, 0, 0),
                    0.001m,
                    null,
                    0,
                    0,
                    1,
                    CoverageKind.Complete),
            ]),
            operations: exactOps) with
        {
            MixedOperations = UsageReportSnapshotV2.MapOperations(mixedOps, "m"),
            DerivedActivity = UsageReportSnapshotV2.MapDerivedActivity(exactOps),
            Workflow = UsageReportSnapshotV2.MapWorkflow(workflow),
        };
        Assert.Equal("tool_c", Assert.Single(snapshot.Operations!).Tool);
        Assert.DoesNotContain(snapshot.Operations!, row => row.Tool == "tool_a");
        Assert.Contains(snapshot.MixedOperations!, row => row.Tool == "tool_a");
        Assert.Contains(snapshot.MixedOperations!, row => row.Tool == "tool_b");
        Assert.DoesNotContain(snapshot.MixedOperations!, row => row.Tool == "tool_c");
        Assert.Equal("1", snapshot.DerivedActivity?.Unknown);
        Assert.Equal("1", snapshot.DerivedActivity?.Eligible);
        string csv = UsageReportSnapshotV2.WriteCsv(snapshot);
        Assert.Contains("operation-mixed-or-incomplete-session", csv, StringComparison.Ordinal);
        Assert.Contains("tool_a", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(",edit,4,", csv, StringComparison.Ordinal);
        string json = UsageReportSnapshotV2.Render(snapshot, "json");
        Assert.Contains("mixedOperations", json, StringComparison.Ordinal);
        Assert.Contains("tool_a", json, StringComparison.Ordinal);
        string html = UsageReportSnapshotV2.WriteHtml(snapshot);
        Assert.Contains("operation-mixed-or-incomplete-session", html, StringComparison.Ordinal);
        Assert.Contains("tool_c", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoundedOperationQueriesHonorCancellationAndComplete()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        OpaqueAttributionKey session = Key("bounded-session");
        DateTimeOffset origin = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var facts = new List<UsageOperationFact>(320);
        for (int index = 0; index < 40; index++)
        {
            OpaqueAttributionKey file = Key("file-" + index.ToString(CultureInfo.InvariantCulture));
            facts.Add(new UsageOperationFact(
                Key("edit-a-" + index.ToString(CultureInfo.InvariantCulture)),
                AttributionCapability.CodexFiles,
                1,
                UsageOperationKind.File,
                "patch",
                file.Value,
                UsageOperationOutcome.Success,
                origin.AddSeconds(index * 3),
                origin.AddSeconds(index * 3 + 1),
                session));
            facts.Add(new UsageOperationFact(
                Key("test-" + index.ToString(CultureInfo.InvariantCulture)),
                AttributionCapability.CodexCommands,
                1,
                UsageOperationKind.Command,
                "test",
                null,
                UsageOperationOutcome.Success,
                origin.AddSeconds(index * 3 + 1),
                origin.AddSeconds(index * 3 + 1).AddMilliseconds(500),
                session));
            facts.Add(new UsageOperationFact(
                Key("edit-b-" + index.ToString(CultureInfo.InvariantCulture)),
                AttributionCapability.CodexFiles,
                1,
                UsageOperationKind.File,
                "patch",
                file.Value,
                UsageOperationOutcome.Success,
                origin.AddSeconds(index * 3 + 2),
                origin.AddSeconds(index * 3 + 3),
                session));
        }

        for (int index = 0; index < 200; index++)
        {
            facts.Add(new UsageOperationFact(
                Key("mcp-" + index.ToString(CultureInfo.InvariantCulture)),
                AttributionCapability.CodexMcp,
                1,
                UsageOperationKind.Mcp,
                "list_things",
                "example-server",
                UsageOperationOutcome.Success,
                origin.AddSeconds(index),
                origin.AddSeconds(index + 1),
                session));
        }

        await repository.UpsertOperationFactsAsync(facts);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ReadOperationRankingAsync(
            origin.AddHours(-1),
            origin.AddDays(1),
            AttributionCapability.CodexMcp,
            1,
            cancellationToken: cancelled.Token));
        var timer = Stopwatch.StartNew();
        IReadOnlyList<UsageOperationRankedRow> ranking = await repository.ReadOperationRankingAsync(
            origin.AddHours(-1),
            origin.AddDays(1),
            AttributionCapability.CodexFiles,
            1);
        IReadOnlyList<UsageOperationTimelineRow> files = await repository.ReadOperationTimelineAsync(
            origin.AddHours(-1),
            origin.AddDays(1),
            AttributionCapability.CodexFiles,
            1);
        IReadOnlyList<UsageOperationTimelineRow> commands = await repository.ReadOperationTimelineAsync(
            origin.AddHours(-1),
            origin.AddDays(1),
            AttributionCapability.CodexCommands,
            1);
        UsageDerivedActivitySummary derived = UsageDerivedActivity.Summarize(ranking);
        UsageWorkflowSummary workflow = UsageWorkflowIndicators.Evaluate(files.Concat(commands).ToArray());
        timer.Stop();
        Assert.Equal(80, ranking.Sum(row => row.InvocationCount));
        Assert.Equal(80, derived.Edit);
        Assert.Equal(40, workflow.SameFileVerificationSeparated);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "bounded ranking/derive exceeded hang budget: " + timer.Elapsed);
    }

    private static UsageOperationFact Fact(string seed, OpaqueAttributionKey? session, string tool) =>
        new(
            Key(seed),
            AttributionCapability.CodexMcp,
            1,
            UsageOperationKind.Mcp,
            tool,
            "example-server",
            UsageOperationOutcome.Success,
            new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 13, 15, 0, 1, TimeSpan.Zero),
            session);

    private static UsageEvent CodexEvent(
        string localIdentity,
        string modelId,
        DateTimeOffset occurredAtUtc,
        string? reasoningEffort = null) =>
        new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(localIdentity))).ToLowerInvariant()),
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId(modelId),
            occurredAtUtc,
            "UTC",
            new TokenBreakdown(10, 0, 0, 0, 0),
            CostObservation.ProviderReported(0.001m),
            "fixture/1",
            CoverageKind.Complete,
            UsageTimePrecision.Timestamp,
            reasoningEffort: reasoningEffort);

    private static OpaqueAttributionKey Key(string seed) =>
        new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant());

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "tokenusage-tests", Guid.NewGuid().ToString("N"));

        public TemporaryFolder() => Directory.CreateDirectory(_path);

        public string DatabasePath => Path.Combine(_path, "usage.v1.db");

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
