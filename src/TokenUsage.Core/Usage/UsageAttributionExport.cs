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
        if (frozen.Sessions is null && frozen.Projects is null && frozen.Operations is null)
        {
            return frozen;
        }

        if (consent is null)
        {
            return frozen with { Sessions = null, Projects = null, Operations = null };
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
        return frozen with
        {
            Sessions = codex.AllowsLinks || cursor.AllowsLinks ? frozen.Sessions : null,
            Projects = projects.AllowsLinks ? frozen.Projects : null,
            Operations = mcp.AllowsLinks || skills.AllowsLinks || commands.AllowsLinks || files.AllowsLinks
                ? frozen.Operations
                : null,
        };
    }
}
