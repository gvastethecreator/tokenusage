namespace TokenUsage.App.ViewModels;

public enum VercelGatewayUiState
{
    Checking,
    NotConfigured,
    Connecting,
    Refreshing,
    Connected,
    Partial,
    Throttled,
    AuthenticationRejected,
    UnsupportedAccount,
    TransientFailure,
    ContractFailure,
    Disconnecting,
    Disconnected,
    CleanupPartial,
    LocalFailure,
}
