using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageAttributionExportTests
{
    [Fact]
    public async Task RecheckConsentStripsAttributionWhenDisabledWithoutChangingTotals()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                0.001m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document frozen = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report) with
        {
            Sessions = [new("s1", "attributed", "100", "100", "1", "1")],
            Projects = [new("p1", "attributed", "100", "100", "1", "1")],
        };

        UsageReportSnapshotV2.Document kept = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Enabled(AttributionCapability.CodexSession),
                Disabled(AttributionCapability.CursorSession),
                Enabled(AttributionCapability.CodexProject)));
        Assert.NotNull(kept.Sessions);
        Assert.NotNull(kept.Projects);
        Assert.Equal(frozen.Totals, kept.Totals);

        UsageReportSnapshotV2.Document stripped = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Disabled(AttributionCapability.CodexSession),
                Disabled(AttributionCapability.CursorSession),
                Disabled(AttributionCapability.CodexProject)));
        Assert.Null(stripped.Sessions);
        Assert.Null(stripped.Projects);
        Assert.Equal(frozen.Totals, stripped.Totals);
        Assert.Equal("100", stripped.Totals.Tokens.Input);
    }

    [Fact]
    public async Task RecheckConsentDropsRevokedMcpRowsWhileCommandsRemain()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                0.001m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document frozen = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report,
            operations:
            [
                new UsageOperationRankedRow(
                    "mcp:review-server:list_things:0",
                    UsageOperationKind.Mcp,
                    "list_things",
                    "review-server",
                    1,
                    0,
                    1,
                    0,
                    OutcomesAvailable: true,
                    Capability: AttributionCapability.CodexMcp,
                    ConsentEpoch: 1),
                new UsageOperationRankedRow(
                    "command:null:git:1",
                    UsageOperationKind.Command,
                    "git",
                    null,
                    1,
                    1,
                    0,
                    0,
                    OutcomesAvailable: true,
                    Capability: AttributionCapability.CodexCommands,
                    ConsentEpoch: 1),
            ]);

        UsageReportSnapshotV2.Document after = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Disabled(AttributionCapability.CodexMcp),
                Enabled(AttributionCapability.CodexCommands)));

        Assert.Equal(0, after.Operations?.Count(row => row.Kind == "mcp") ?? 0);
        Assert.Equal("command", Assert.Single(after.Operations!).Kind);
        Assert.Equal("1", after.DerivedActivity?.Unknown);
        Assert.Equal("0", after.DerivedActivity?.Edit ?? "0");
        Assert.Equal(frozen.Totals, after.Totals);
        string json = UsageReportSnapshotV2.Render(frozen, "json");
        Assert.DoesNotContain("codex-mcp", json, StringComparison.Ordinal);
        Assert.DoesNotContain("consentEpoch", json, StringComparison.Ordinal);
        Assert.DoesNotContain("operation_key", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecheckConsentDropsSpawnWhenSkillsRevokedWithoutChangingTotals()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                0.001m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document frozen = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report,
            operations:
            [
                new UsageOperationRankedRow(
                    "spawn:null:reviewer:0",
                    UsageOperationKind.Spawn,
                    "reviewer",
                    null,
                    1,
                    0,
                    0,
                    1,
                    OutcomesAvailable: false,
                    Capability: AttributionCapability.CodexSkills,
                    ConsentEpoch: 1),
                new UsageOperationRankedRow(
                    "mcp:example-server:list_things:1",
                    UsageOperationKind.Mcp,
                    "list_things",
                    "example-server",
                    1,
                    1,
                    0,
                    0,
                    OutcomesAvailable: true,
                    Capability: AttributionCapability.CodexMcp,
                    ConsentEpoch: 1),
            ]);

        UsageReportSnapshotV2.Document after = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Disabled(AttributionCapability.CodexSkills),
                Enabled(AttributionCapability.CodexMcp)));

        Assert.Equal(0, after.Operations?.Count(row => row.Kind == "spawn") ?? 0);
        Assert.Equal("mcp", Assert.Single(after.Operations!).Kind);
        Assert.Equal("0", after.DerivedActivity?.Delegate ?? "0");
        Assert.Equal(frozen.Totals, after.Totals);
        Assert.Equal("100", after.Totals.Tokens.Input);
    }

    [Fact]
    public async Task RecheckConsentZerosWorkflowWhenFilesRevokedWithoutChangingTotals()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                0.001m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document frozen = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report) with
        {
            Workflow = new UsageReportSnapshotV2.WorkflowSnapshot(
                UsageWorkflowIndicators.MethodVersion,
                UsageWorkflowIndicators.FirstEditUnavailableReason,
                "1",
                "0",
                "3",
                "1",
                UsageWorkflowIndicators.CostUnavailable,
                FilesConsentEpoch: 1,
                CommandsConsentEpoch: 1),
        };

        UsageReportSnapshotV2.Document after = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Disabled(AttributionCapability.CodexFiles),
                Enabled(AttributionCapability.CodexCommands)));

        Assert.Equal("0", after.Workflow?.SameFileVerificationSeparated);
        Assert.Equal("0", after.Workflow?.EligibleFileEdits);
        Assert.Equal("1", after.Workflow?.EligibleVerifications);
        Assert.Equal(UsageWorkflowIndicators.FirstEditUnavailableReason, after.Workflow?.FirstEditReason);
        Assert.Equal(frozen.Totals, after.Totals);
    }

    [Fact]
    public async Task RecheckConsentZerosWorkflowWhenFilesEpochIsReplaced()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                0.001m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document frozen = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report,
            operations:
            [
                new UsageOperationRankedRow(
                    "file:hmac:patch:0",
                    UsageOperationKind.File,
                    "patch",
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("file-one"u8)).ToLowerInvariant(),
                    2,
                    2,
                    0,
                    0,
                    OutcomesAvailable: true,
                    Capability: AttributionCapability.CodexFiles,
                    ConsentEpoch: 1),
                new UsageOperationRankedRow(
                    "command:null:test:1",
                    UsageOperationKind.Command,
                    "test",
                    null,
                    1,
                    1,
                    0,
                    0,
                    OutcomesAvailable: true,
                    Capability: AttributionCapability.CodexCommands,
                    ConsentEpoch: 1),
            ]) with
        {
            Workflow = new UsageReportSnapshotV2.WorkflowSnapshot(
                UsageWorkflowIndicators.MethodVersion,
                UsageWorkflowIndicators.FirstEditUnavailableReason,
                "1",
                "0",
                "2",
                "1",
                UsageWorkflowIndicators.CostUnavailable,
                FilesConsentEpoch: 1,
                CommandsConsentEpoch: 1),
        };

        UsageReportSnapshotV2.Document after = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                new AttributionConsent(
                    AttributionCapability.CodexFiles,
                    AttributionConsentState.Enabled,
                    2,
                    new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)),
                new AttributionConsent(
                    AttributionCapability.CodexCommands,
                    AttributionConsentState.Enabled,
                    2,
                    new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero))));

        Assert.Empty(after.Operations ?? []);
        Assert.Equal("0", after.Workflow?.SameFileVerificationSeparated);
        Assert.Equal("0", after.Workflow?.EligibleFileEdits);
        Assert.Equal("0", after.Workflow?.EligibleVerifications);
        Assert.Equal(UsageWorkflowIndicators.FirstEditUnavailableReason, after.Workflow?.FirstEditReason);
        Assert.Equal(frozen.Totals, after.Totals);
        string json = UsageReportSnapshotV2.Render(frozen, "json");
        Assert.DoesNotContain("filesConsentEpoch", json, StringComparison.Ordinal);
        Assert.DoesNotContain("commandsConsentEpoch", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecheckConsentZerosUnstampedWorkflowEvenWhenEnabled()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                0.001m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document frozen = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report) with
        {
            Workflow = new UsageReportSnapshotV2.WorkflowSnapshot(
                UsageWorkflowIndicators.MethodVersion,
                UsageWorkflowIndicators.FirstEditUnavailableReason,
                "1",
                "0",
                "3",
                "1",
                UsageWorkflowIndicators.CostUnavailable),
        };

        UsageReportSnapshotV2.Document after = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Enabled(AttributionCapability.CodexFiles),
                Enabled(AttributionCapability.CodexCommands)));

        Assert.Equal("0", after.Workflow?.SameFileVerificationSeparated);
        Assert.Equal("0", after.Workflow?.EligibleFileEdits);
        Assert.Equal("0", after.Workflow?.EligibleVerifications);
        Assert.Equal(frozen.Totals, after.Totals);
    }

    [Fact]
    public async Task RecheckConsentDropsJointConcurrentWhenEitherWorkflowEpochIsInvalid()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                0.001m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document frozen = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report,
            operations:
            [
                new UsageOperationRankedRow(
                    "file:hmac:patch:0",
                    UsageOperationKind.File,
                    "patch",
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("file-one"u8)).ToLowerInvariant(),
                    4,
                    4,
                    0,
                    0,
                    OutcomesAvailable: true,
                    Capability: AttributionCapability.CodexFiles,
                    ConsentEpoch: 1),
                new UsageOperationRankedRow(
                    "command:null:test:1",
                    UsageOperationKind.Command,
                    "test",
                    null,
                    1,
                    1,
                    0,
                    0,
                    OutcomesAvailable: true,
                    Capability: AttributionCapability.CodexCommands,
                    ConsentEpoch: 1),
            ]) with
        {
            Workflow = new UsageReportSnapshotV2.WorkflowSnapshot(
                UsageWorkflowIndicators.MethodVersion,
                UsageWorkflowIndicators.FirstEditUnavailableReason,
                "1",
                "7",
                "4",
                "1",
                UsageWorkflowIndicators.CostUnavailable,
                FilesConsentEpoch: 1,
                CommandsConsentEpoch: 1,
                ExcludedFileConcurrent: 0,
                ExcludedEditTestConcurrent: 7,
                IncompleteUnproved: "0"),
        };

        UsageReportSnapshotV2.Document commandsRevoked = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Enabled(AttributionCapability.CodexFiles),
                Disabled(AttributionCapability.CodexCommands)));
        Assert.Equal("0", commandsRevoked.Workflow?.SameFileVerificationSeparated);
        Assert.Equal("0", commandsRevoked.Workflow?.ExcludedConcurrent);
        Assert.Equal("4", commandsRevoked.Workflow?.EligibleFileEdits);
        Assert.Equal("0", commandsRevoked.Workflow?.EligibleVerifications);
        Assert.Equal("0", commandsRevoked.Workflow?.IncompleteUnproved);
        Assert.Equal(frozen.Totals, commandsRevoked.Totals);
        Assert.Equal("4", commandsRevoked.DerivedActivity?.Edit);

        UsageReportSnapshotV2.Document commandsNewEpoch = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Enabled(AttributionCapability.CodexFiles),
                new AttributionConsent(
                    AttributionCapability.CodexCommands,
                    AttributionConsentState.Enabled,
                    2,
                    new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero))));
        Assert.Equal("0", commandsNewEpoch.Workflow?.ExcludedConcurrent);
        Assert.Equal("4", commandsNewEpoch.Workflow?.EligibleFileEdits);
        Assert.Equal("0", commandsNewEpoch.Workflow?.EligibleVerifications);

        UsageReportSnapshotV2.Document filesRevoked = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Disabled(AttributionCapability.CodexFiles),
                Enabled(AttributionCapability.CodexCommands)));
        Assert.Equal("0", filesRevoked.Workflow?.ExcludedConcurrent);
        Assert.Equal("0", filesRevoked.Workflow?.EligibleFileEdits);
        Assert.Equal("1", filesRevoked.Workflow?.EligibleVerifications);

        UsageReportSnapshotV2.Document filesNewEpoch = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                new AttributionConsent(
                    AttributionCapability.CodexFiles,
                    AttributionConsentState.Enabled,
                    2,
                    new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)),
                Enabled(AttributionCapability.CodexCommands)));
        Assert.Equal("0", filesNewEpoch.Workflow?.ExcludedConcurrent);
        Assert.Equal("0", filesNewEpoch.Workflow?.EligibleFileEdits);
        Assert.Equal("1", filesNewEpoch.Workflow?.EligibleVerifications);

        UsageReportSnapshotV2.Document bothValid = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Enabled(AttributionCapability.CodexFiles),
                Enabled(AttributionCapability.CodexCommands)));
        Assert.Equal("7", bothValid.Workflow?.ExcludedConcurrent);
        Assert.Equal("1", bothValid.Workflow?.SameFileVerificationSeparated);
        Assert.Equal("4", bothValid.Workflow?.EligibleFileEdits);
        Assert.Equal("1", bothValid.Workflow?.EligibleVerifications);
        Assert.Equal(frozen.Totals, bothValid.Totals);
    }

    [Fact]
    public async Task RecheckConsentDropsRevokedSessionPortionsAndNewEpochs()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                0.001m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document frozen = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report) with
        {
            Sessions =
            [
                new("s1", "assigned", "40", "40", "1", "1", Capability: AttributionCapability.CodexSession.Value, ConsentEpoch: 1),
                new("s2", "assigned", "60", "60", "1", "1", Capability: AttributionCapability.CursorSession.Value, ConsentEpoch: 1),
            ],
        };

        UsageReportSnapshotV2.Document cursorOnly = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                Disabled(AttributionCapability.CodexSession),
                Enabled(AttributionCapability.CursorSession)));
        Assert.Equal("s2", Assert.Single(cursorOnly.Sessions!).ExportId);

        UsageReportSnapshotV2.Document newEpoch = await UsageAttributionExport.RecheckConsentAsync(
            frozen,
            new MapConsent(
                new AttributionConsent(
                    AttributionCapability.CodexSession,
                    AttributionConsentState.Enabled,
                    2,
                    new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)),
                Enabled(AttributionCapability.CursorSession)));
        Assert.Equal("s2", Assert.Single(newEpoch.Sessions!).ExportId);
    }

    private static AttributionConsent Enabled(AttributionCapability capability) =>
        new(capability, AttributionConsentState.Enabled, 1, new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    private static AttributionConsent Disabled(AttributionCapability capability) =>
        new(capability, AttributionConsentState.Disabled, 0, new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    private sealed class MapConsent(params AttributionConsent[] consents) : IAttributionConsentSource
    {
        public Task<AttributionConsent> LoadAsync(
            AttributionCapability capability,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                consents.FirstOrDefault(item => item.Capability.Value == capability.Value)
                ?? new AttributionConsent(
                    capability,
                    AttributionConsentState.Disabled,
                    0,
                    new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)));
    }
}
