using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Cli;

internal static class ProviderDiagnosticsValidator
{
    private static readonly string[] ExpectedChecks = WindowsProviderCatalog.Entries
        .SelectMany(entry => entry.DetectionCheckId is null
            ? [entry.DataCheckId!]
            : new[] { entry.DetectionCheckId, entry.DataCheckId! })
        .Append("usage-db")
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

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
