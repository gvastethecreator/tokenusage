namespace TokenUsage.Core.Updates;

public sealed record PortableUpdatePlan(
    string PlanPath,
    string WorkerExecutablePath,
    int ParentProcessId);

public sealed record PortableUpdateResult(
    string Status,
    string Message,
    DateTimeOffset CompletedAtUtc);
