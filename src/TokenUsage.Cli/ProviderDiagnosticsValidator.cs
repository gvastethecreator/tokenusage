using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Cli;

internal static class ProviderDiagnosticsValidator
{
    private static readonly IReadOnlyDictionary<string, WindowsProviderCatalogEntry>
        ExpectedProviders = WindowsProviderCatalog.Entries.ToDictionary(
            entry => entry.Id.Value,
            StringComparer.Ordinal);

    private static readonly string[] ExpectedChecks = WindowsProviderCatalog.Entries
        .SelectMany(entry => entry.DetectionCheckId is null
            ? [entry.DataCheckId]
            : new[] { entry.DetectionCheckId, entry.DataCheckId })
        .Append("usage-db")
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

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
            WindowsProviderCatalogEntry expected = ExpectedProviders[provider.Id];
            if (!string.Equals(provider.Name, expected.DisplayName, StringComparison.Ordinal)
                || provider.Capabilities is null
                || !provider.Capabilities.SequenceEqual(expected.Capabilities)
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
}
