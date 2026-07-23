namespace WOpenUsage.Cli;

public enum ProviderCapability
{
    Limits,
    LocalUsage,
}

public enum ProviderDetectionStatus
{
    Detected,
    Missing,
    Unavailable,
}

public enum ProviderDataStatus
{
    Present,
    Absent,
    Unreadable,
    UnsupportedSchema,
}

public enum DoctorCheckStatus
{
    Detected,
    Missing,
    Unavailable,
    Present,
    Absent,
    Unreadable,
    UnsupportedSchema,
}

public sealed record ProviderDiagnostic(
    string Id,
    string Name,
    IReadOnlyList<ProviderCapability> Capabilities,
    ProviderDetectionStatus Detection,
    ProviderDataStatus Data);

public sealed record DoctorCheck(string Id, DoctorCheckStatus Status);

public sealed record ProviderDiagnosticsSnapshot(
    IReadOnlyList<ProviderDiagnostic> Providers,
    IReadOnlyList<DoctorCheck> Checks);
