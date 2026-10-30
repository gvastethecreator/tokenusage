using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.Services;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

/// <summary>
/// The rarely changed options: screenshots, Claude history recovery, and the dashboard
/// layout. Keeping them here keeps the other tabs short.
/// </summary>
public sealed partial class AdvancedOptionsView : UserControl
{
    private GeneralOptionsViewModel? _general;
    private PersonalizationSurfaceViewModel? _personalization;
    private bool _isInitialized;
    private bool _isShareCaptureFolderPathQueued;

    public AdvancedOptionsView()
    {
        InitializeComponent();
        _isInitialized = true;
        Loaded += OnLoaded;
    }

    public GeneralOptionsViewModel? General
    {
        get => _general;
        set
        {
            _general = value ?? throw new ArgumentNullException(nameof(value));
            UpdateBindings();
        }
    }

    public PersonalizationSurfaceViewModel? Personalization
    {
        get => _personalization;
        set
        {
            _personalization = value ?? throw new ArgumentNullException(nameof(value));
            UpdateBindings();
        }
    }

    public UIElement PrimaryAction => ScreenshotsExpander;

    public UIElement PersonalizationPrimaryAction => PersonalizationView.PrimaryAction;

    private void UpdateBindings()
    {
        if (_isInitialized && _general is not null && _personalization is not null)
        {
            Bindings.Update();
        }
    }

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
