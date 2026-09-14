namespace TokenUsage.Core.Usage;

public enum ProjectMappingKind
{
    Observed = 0,
    UserMapped = 1,
    Ambiguous = 2,
}

public enum ParentChildAccountingKind
{
    Exclusive = 0,
    Inclusive = 1,
    Unknown = 2,
}

public sealed record UsageProjectLink
{
    public UsageProjectLink(
        UsageEventKey eventKey,
        OpaqueAttributionKey? projectKey,
        long consentEpoch,
        ProjectMappingKind mappingKind)
    {
        ArgumentNullException.ThrowIfNull(eventKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consentEpoch);
        if (!Enum.IsDefined(mappingKind))
        {
            throw new ArgumentOutOfRangeException(nameof(mappingKind));
        }

        if (mappingKind is ProjectMappingKind.Observed or ProjectMappingKind.UserMapped
            && projectKey is null)
        {
            throw new ArgumentException(
                "Observed and user-mapped project links require a project key.",
                nameof(projectKey));
        }

        EventKey = eventKey;
        ProjectKey = projectKey;
        ConsentEpoch = consentEpoch;
        MappingKind = mappingKind;
    }

    public UsageEventKey EventKey { get; }

    public OpaqueAttributionKey? ProjectKey { get; }

    public long ConsentEpoch { get; }

    public ProjectMappingKind MappingKind { get; }
}

public sealed record UsageProjectContribution(
    OpaqueAttributionKey? ProjectKey,
    ProjectMappingKind? MappingKind,
    TokenBreakdown SelectedTokens,
    TokenBreakdown ProjectTokens,
    int SelectedEventCount,
    int ProjectEventCount,
    DateTimeOffset? FirstOccurredAtUtc,
    DateTimeOffset? LastOccurredAtUtc,
    bool IsUnassigned,
    bool IsAmbiguous);

public sealed record UsageSessionLineageNode(
    OpaqueAttributionKey SessionKey,
    OpaqueAttributionKey? ParentSessionKey,
    TokenBreakdown ExclusiveTokens,
    TokenBreakdown? InclusiveTokens,
    int ExclusiveEventCount,
    IReadOnlyList<OpaqueAttributionKey> Children);

public static class OpaqueWorkspaceFingerprint
{
    public const int MaximumCwdBytes = 4096;

    public static bool TryFingerprint(string? cwd, out string identifier)
    {
        identifier = string.Empty;
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return false;
        }

        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(cwd.Trim());
        if (utf8.Length is 0 or > MaximumCwdBytes)
        {
            return false;
        }

        identifier = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(utf8))
            .ToLowerInvariant();
        return true;
    }
}

public static class ProjectMappingKindCodec
{
    public static string ToWire(ProjectMappingKind kind) => kind switch
    {
        ProjectMappingKind.Observed => "observed",
        ProjectMappingKind.UserMapped => "user-mapped",
        ProjectMappingKind.Ambiguous => "ambiguous",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParse(string? value, out ProjectMappingKind kind)
    {
        kind = default;
        if (string.Equals(value, "observed", StringComparison.Ordinal))
        {
            kind = ProjectMappingKind.Observed;
            return true;
        }

        if (string.Equals(value, "user-mapped", StringComparison.Ordinal))
        {
            kind = ProjectMappingKind.UserMapped;
            return true;
        }

        if (string.Equals(value, "ambiguous", StringComparison.Ordinal))
        {
            kind = ProjectMappingKind.Ambiguous;
            return true;
        }

        return false;
    }
}
