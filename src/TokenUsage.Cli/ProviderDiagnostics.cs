namespace TokenUsage.Cli;

public delegate Task<ProviderDiagnosticsSnapshot> ProviderDiagnosticsReader(
    CancellationToken cancellationToken);
