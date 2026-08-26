using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.Services;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

public sealed partial class GeneralOptionsView : UserControl
{
    private GeneralOptionsViewModel? _viewModel;
    private bool _isInitialized;
    private bool _isShareCaptureFolderPathQueued;

    public GeneralOptionsView()
    {
        InitializeComponent();
        _isInitialized = true;
        Loaded += OnLoaded;
    }

    public GeneralOptionsViewModel? ViewModel
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

    public UIElement PrimaryAction => CloseWhenInactiveToggle;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isShareCaptureFolderPathQueued)
        {
            return;
        }

        _isShareCaptureFolderPathQueued = DispatcherQueue.TryEnqueue(
            LoadShareCaptureFolderPathAsync);
    }

    private async void LoadShareCaptureFolderPathAsync()
    {
        try
        {
            ShareCaptureFolderPath.Text = await ShareCaptureService.GetDestinationPathAsync();
        }
        finally
        {
            _isShareCaptureFolderPathQueued = false;
        }
    }

    private async void OnShareCaptureFolderBrowseClicked(object sender, RoutedEventArgs e)
    {
        if (App.Window is not MainWindow window)
        {
            return;
        }

        using IDisposable guard = window.SuppressDeactivateHide();
        string? path = await ShareCaptureService.PickDestinationAsync(
            App.WindowHandle);
        if (!string.IsNullOrWhiteSpace(path))
        {
            ShareCaptureFolderPath.Text = path;
        }
    }

    private async void OnShareCaptureFolderResetClicked(object sender, RoutedEventArgs e) =>
        ShareCaptureFolderPath.Text = await ShareCaptureService.ResetDestinationAsync();

}
