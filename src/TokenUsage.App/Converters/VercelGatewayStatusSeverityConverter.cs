using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using TokenUsage.App.ViewModels;

namespace TokenUsage.App.Converters;

public sealed class VercelGatewayStatusSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is VercelGatewayUiState state
            ? Map(state)
            : InfoBarSeverity.Informational;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static InfoBarSeverity Map(VercelGatewayUiState state) => state switch
    {
        VercelGatewayUiState.Connected or VercelGatewayUiState.Disconnected =>
            InfoBarSeverity.Success,
        VercelGatewayUiState.Partial or VercelGatewayUiState.Throttled
            or VercelGatewayUiState.CleanupPartial => InfoBarSeverity.Warning,
        VercelGatewayUiState.AuthenticationRejected
            or VercelGatewayUiState.UnsupportedAccount
            or VercelGatewayUiState.TransientFailure
            or VercelGatewayUiState.ContractFailure
            or VercelGatewayUiState.LocalFailure => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };
}
