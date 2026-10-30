using TokenUsage.Providers.Codex;
using TokenUsage.Runtime.Windows.Automation;

namespace TokenUsage.Cli;

public static class LocalProviderDiagnosticsAccess
{
    public static Task<ProviderDiagnosticsSnapshot> ReadAsync(
        string dataDirectory,
        TimeProvider clock,
        CancellationToken cancellationToken = default) =>
        new WindowsProviderDiagnosticsQuery(dataDirectory, clock)
            .ExecuteAsync(cancellationToken);

    internal static Task<ProviderDiagnosticsSnapshot> ReadAsync(
        string dataDirectory,
        ICodexQuotaClientFactory codexFactory,
        Func<string, bool> detectLocalProvider,
        CancellationToken cancellationToken) =>
        new WindowsProviderDiagnosticsQuery(dataDirectory, codexFactory, detectLocalProvider)
            .ExecuteAsync(cancellationToken);
}
