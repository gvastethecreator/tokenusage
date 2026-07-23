namespace WOpenUsage.Cli;

internal static class ProviderDiagnosticsValidator
{
    private static readonly IReadOnlyDictionary<string, ExpectedProvider> ExpectedProviders =
        new Dictionary<string, ExpectedProvider>(StringComparer.Ordinal)
        {
            ["claude"] = new("Claude", ProviderCapability.LocalUsage),
            ["codex"] = new("Codex", ProviderCapability.Limits),
            ["grok"] = new("Grok Build", ProviderCapability.LocalUsage),
            ["opencode"] = new("OpenCode", ProviderCapability.LocalUsage),
        };

    private static readonly string[] ExpectedChecks =
    [
        "codex-cache",
        "codex-cli",
        "local-usage-claude",
        "local-usage-grok",
        "local-usage-opencode",
        "usage-db",
    ];

    internal static ProviderDiagnostic[] ValidateProviders(ProviderDiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Providers);
        if (snapshot.Providers.Any(provider => provider is null)
            || snapshot.Providers.Count != ExpectedProviders.Count)
        {
            throw new InvalidDataException("The provider catalog is incomplete.");
        }

        ProviderDiagnostic[] ordered = snapshot.Providers
            .OrderBy(provider => provider.Id, StringComparer.Ordinal)
            .ToArray();
        if (!ordered.Select(provider => provider.Id).SequenceEqual(
                ExpectedProviders.Keys.OrderBy(id => id, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidDataException("The provider catalog has unexpected IDs.");
        }

        foreach (ProviderDiagnostic provider in ordered)
        {
            ExpectedProvider expected = ExpectedProviders[provider.Id];
            if (!string.Equals(provider.Name, expected.Name, StringComparison.Ordinal)
                || provider.Capabilities is null
                || provider.Capabilities.Count != 1
                || provider.Capabilities[0] != expected.Capability
                || !Enum.IsDefined(provider.Detection)
                || !Enum.IsDefined(provider.Data))
            {
                throw new InvalidDataException("The provider catalog has an invalid descriptor.");
            }
        }

        return ordered;
    }

    internal static DoctorCheck[] ValidateChecks(ProviderDiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Checks);
        if (snapshot.Checks.Any(check => check is null)
            || snapshot.Checks.Count != ExpectedChecks.Length)
        {
            throw new InvalidDataException("The doctor report is incomplete.");
        }

        DoctorCheck[] ordered = snapshot.Checks
            .OrderBy(check => check.Id, StringComparer.Ordinal)
            .ToArray();
        if (!ordered.Select(check => check.Id).SequenceEqual(
                ExpectedChecks,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException("The doctor report has unexpected checks.");
        }

        if (ordered.Any(check => !Enum.IsDefined(check.Status)))
        {
            throw new InvalidDataException("The doctor report has an invalid status.");
        }

        return ordered;
    }

    private sealed record ExpectedProvider(string Name, ProviderCapability Capability);
}
