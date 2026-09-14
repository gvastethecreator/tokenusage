namespace TokenUsage.Core.Usage;

public sealed record UsageOverviewObservation(
    UsageEventKey EventKey,
    AgentId AgentId,
    ModelProviderId? Host,
    ModelId ModelId,
    TokenBreakdown Tokens,
    CostKind CostKind,
    decimal? ReportedCostUsd,
    decimal? EstimatedCostUsd,
    OpaqueAttributionKey? SessionKey,
    OpaqueAttributionKey? ParentSessionKey,
    OpaqueAttributionKey? ProjectKey,
    ProjectMappingKind? ProjectMapping,
    ModelId? ObservedModelId,
    string? ReasoningEffort,
    string? ServiceTier)
{
    public bool SessionUnassigned => SessionKey is null;

    public bool ProjectUnassigned => ProjectKey is null;
}
