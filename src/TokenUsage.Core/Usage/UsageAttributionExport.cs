using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public static class UsageAttributionExport
{
    public static async Task<(IReadOnlyList<UsageSessionContribution> Sessions, IReadOnlyList<UsageProjectContribution> Projects)>
        ReadAsync(
            string databasePath,
            IAttributionConsentSource consent,
            DateOnly fromInclusive,
            DateOnly toInclusive,
            AgentId? agentId,
            UsageDetailSelection? detail = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(consent);
        UsageRepository repository = await UsageRepository.OpenReadOnlyAsync(databasePath, cancellationToken)
            .ConfigureAwait(false);
        var sessions = new List<UsageSessionContribution>();
        AgentId[] agents = agentId is { } selected ? [selected] : [];
        if (agents.Length == 0)
        {
            AttributionConsent codex = await consent
                .LoadAsync(AttributionCapability.CodexSession, cancellationToken)
                .ConfigureAwait(false);
            sessions.AddRange(await repository.ReadSessionContributionsAsync(
                fromInclusive,
                toInclusive,
                codex.ActiveLinkEpoch,
                new AgentId("codex"),
                capability: AttributionCapability.CodexSession,
                detail: detail,
                cancellationToken: cancellationToken).ConfigureAwait(false));
            AttributionConsent cursor = await consent
                .LoadAsync(AttributionCapability.CursorSession, cancellationToken)
                .ConfigureAwait(false);
            sessions.AddRange(await repository.ReadSessionContributionsAsync(
                fromInclusive,
                toInclusive,
                cursor.ActiveLinkEpoch,
                new AgentId("cursor"),
                capability: AttributionCapability.CursorSession,
                detail: detail,
                cancellationToken: cancellationToken).ConfigureAwait(false));
        }
        else
        {
            foreach (AgentId agent in agents)
            {
                AttributionCapability capability = string.Equals(agent.Value, "cursor", StringComparison.Ordinal)
                    ? AttributionCapability.CursorSession
                    : AttributionCapability.CodexSession;
                AttributionConsent loaded = await consent.LoadAsync(capability, cancellationToken)
                    .ConfigureAwait(false);
                sessions.AddRange(await repository.ReadSessionContributionsAsync(
                    fromInclusive,
                    toInclusive,
                    loaded.ActiveLinkEpoch,
                    agent,
                    capability: capability,
                    detail: detail,
                    cancellationToken: cancellationToken).ConfigureAwait(false));
            }
        }

        AttributionConsent projects = await consent
            .LoadAsync(AttributionCapability.CodexProject, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<UsageProjectContribution> projectRows = await repository.ReadProjectContributionsAsync(
            fromInclusive,
            toInclusive,
            projects.ActiveLinkEpoch,
            agentId,
            detail: detail,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return (sessions, projectRows);
    }

    public static async Task<UsageReportSnapshotV2.Document> RecheckConsentAsync(
        UsageReportSnapshotV2.Document frozen,
        IAttributionConsentSource? consent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frozen);
        if (frozen.Sessions is null
            && frozen.Projects is null
            && frozen.Operations is null
            && frozen.MixedOperations is null
            && frozen.DerivedActivity is null
            && frozen.Workflow is null)
        {
            return frozen;
        }

        if (consent is null)
        {
            return frozen with
            {
                Sessions = null,
                Projects = null,
                Operations = null,
                MixedOperations = null,
                DerivedActivity = null,
                Workflow = null,
            };
        }

        AttributionConsent codex = await consent
            .LoadAsync(AttributionCapability.CodexSession, cancellationToken)
            .ConfigureAwait(false);
        AttributionConsent cursor = await consent
            .LoadAsync(AttributionCapability.CursorSession, cancellationToken)
            .ConfigureAwait(false);
        AttributionConsent projects = await consent
            .LoadAsync(AttributionCapability.CodexProject, cancellationToken)
            .ConfigureAwait(false);
        AttributionConsent mcp = await consent
            .LoadAsync(AttributionCapability.CodexMcp, cancellationToken)
            .ConfigureAwait(false);
        AttributionConsent skills = await consent
            .LoadAsync(AttributionCapability.CodexSkills, cancellationToken)
            .ConfigureAwait(false);
        AttributionConsent commands = await consent
            .LoadAsync(AttributionCapability.CodexCommands, cancellationToken)
            .ConfigureAwait(false);
        AttributionConsent files = await consent
            .LoadAsync(AttributionCapability.CodexFiles, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<UsageReportSnapshotV2.OperationPopulationRow>? operations =
            RecheckOperations(frozen.Operations, mcp, skills, commands, files);
        IReadOnlyList<UsageReportSnapshotV2.OperationPopulationRow>? mixedOperations =
            RecheckOperations(frozen.MixedOperations, mcp, skills, commands, files);
        return frozen with
        {
            Sessions = RecheckRows(frozen.Sessions, codex, cursor),
            Projects = RecheckRows(frozen.Projects, projects),
            Operations = operations,
            MixedOperations = mixedOperations,
            DerivedActivity = UsageReportSnapshotV2.MapDerivedActivity(operations),
            Workflow = RecheckWorkflow(frozen.Workflow, files, commands),
        };
    }

    private static List<UsageReportSnapshotV2.AttributionPopulationRow>? RecheckRows(
        IReadOnlyList<UsageReportSnapshotV2.AttributionPopulationRow>? rows,
        params AttributionConsent[] consents)
    {
        if (rows is null)
        {
            return null;
        }

        var kept = new List<UsageReportSnapshotV2.AttributionPopulationRow>(rows.Count);
        foreach (UsageReportSnapshotV2.AttributionPopulationRow row in rows)
        {
            AttributionConsent? live = MatchConsent(row.Capability, consents);
            if (live is null)
            {
                if (!consents.Any(item => item.AllowsLinks))
                {
                    continue;
                }

                if (consents.Length == 1)
                {
                    live = consents[0];
                }
                else
                {
                    kept.Add(row);
                    continue;
                }
            }

            if (!live.AllowsLinks)
            {
                continue;
            }

            if (row.ConsentEpoch is { } epoch and > 0 && !live.AcceptsEpoch(epoch))
            {
                continue;
            }

            kept.Add(row);
        }

        return kept.Count == 0 ? null : kept;
    }

    private static List<UsageReportSnapshotV2.OperationPopulationRow>? RecheckOperations(
        IReadOnlyList<UsageReportSnapshotV2.OperationPopulationRow>? rows,
        AttributionConsent mcp,
        AttributionConsent skills,
        AttributionConsent commands,
        AttributionConsent files)
    {
        if (rows is null)
        {
            return null;
        }

        var kept = new List<UsageReportSnapshotV2.OperationPopulationRow>(rows.Count);
        foreach (UsageReportSnapshotV2.OperationPopulationRow row in rows)
        {
            AttributionConsent live = ResolveOperationConsent(row, mcp, skills, commands, files);
            if (!live.AllowsLinks)
            {
                continue;
            }

            if (row.ConsentEpoch is { } epoch and > 0 && !live.AcceptsEpoch(epoch))
            {
                continue;
            }

            kept.Add(row);
        }

        return kept.Count == 0 ? null : kept;
    }

    private static UsageReportSnapshotV2.WorkflowSnapshot? RecheckWorkflow(
        UsageReportSnapshotV2.WorkflowSnapshot? workflow,
        AttributionConsent files,
        AttributionConsent commands)
    {
        if (workflow is null)
        {
            return null;
        }

        bool filesOk = AcceptsStampedEpoch(files, workflow.FilesConsentEpoch);
        bool commandsOk = AcceptsStampedEpoch(commands, workflow.CommandsConsentEpoch);
        if (filesOk && commandsOk)
        {
            return workflow;
        }

        int fileFile = workflow.ExcludedFileConcurrent ?? -1;
        int editTest = workflow.ExcludedEditTestConcurrent ?? -1;
        bool hasSplits = fileFile >= 0 || editTest >= 0;
        string excluded = !hasSplits
            ? "0"
            : Integer(filesOk ? Math.Max(0, fileFile) : 0);

        return workflow with
        {
            SameFileVerificationSeparated = "0",
            ExcludedConcurrent = excluded,
            EligibleFileEdits = filesOk ? workflow.EligibleFileEdits : "0",
            EligibleVerifications = commandsOk ? workflow.EligibleVerifications : "0",
            IncompleteUnproved = "0",
            ExcludedFileConcurrent = filesOk ? workflow.ExcludedFileConcurrent : 0,
            ExcludedEditTestConcurrent = 0,
        };
    }

    private static string Integer(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool AcceptsStampedEpoch(AttributionConsent live, long? epoch) =>
        epoch is { } stamped and > 0 && live.AcceptsEpoch(stamped);

    private static AttributionConsent? MatchConsent(
        string? capability,
        IReadOnlyList<AttributionConsent> consents)
    {
        if (string.IsNullOrWhiteSpace(capability))
        {
            return null;
        }

        return consents.FirstOrDefault(item => item.Capability.Value == capability);
    }

    private static AttributionConsent ResolveOperationConsent(
        UsageReportSnapshotV2.OperationPopulationRow row,
        AttributionConsent mcp,
        AttributionConsent skills,
        AttributionConsent commands,
        AttributionConsent files)
    {
        if (!string.IsNullOrWhiteSpace(row.Capability))
        {
            if (row.Capability == AttributionCapability.CodexMcp.Value)
            {
                return mcp;
            }

            if (row.Capability == AttributionCapability.CodexSkills.Value)
            {
                return skills;
            }

            if (row.Capability == AttributionCapability.CodexCommands.Value)
            {
                return commands;
            }

            if (row.Capability == AttributionCapability.CodexFiles.Value)
            {
                return files;
            }
        }

        return row.Kind switch
        {
            "mcp" or "tool" => mcp,
            "spawn" or "skill" => skills,
            "command" => commands,
            "file" => files,
            _ => mcp,
        };
    }
}
