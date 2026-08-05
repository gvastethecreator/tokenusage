using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels;

namespace TokenUsage.App.Views.Options;

public sealed partial class VercelConnectionView : UserControl
{
    private VercelGatewaySettingsViewModel? _viewModel;
    private bool _isInitialized;

    public VercelConnectionView()
    {
        InitializeComponent();
        _isInitialized = true;
    }

    public VercelGatewaySettingsViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value ?? throw new ArgumentNullException(nameof(value));
            if (_isInitialized)
            {
                Bindings.Update();
            }
        }
    }

    public UIElement PrimaryAction => ViewModel?.IsConnectFormVisible is true
        ? VercelApiKeyBox
        : VercelDisconnectButton;

    private void OnVercelApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            ViewModel?.SetApiKeyInputPresence(passwordBox.Password);
        }
    }

    private void OnVercelKeyIdTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            ViewModel?.SetKeyIdInput(textBox.Text);
        }
    }

    private async void OnVercelConnectClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        string apiKey = VercelApiKeyBox.Password;
        string keyId = VercelKeyIdBox.Text;
        Task connection = ViewModel.ConnectAsync(apiKey, keyId);
        VercelApiKeyBox.Password = string.Empty;
        VercelKeyIdBox.Text = string.Empty;
        await connection;
    }
}
