namespace WOpenUsage.Cli;

public delegate Task<ProviderDiagnosticsSnapshot> ProviderDiagnosticsReader(
    CancellationToken cancellationToken);
