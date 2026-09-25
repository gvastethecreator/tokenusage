using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TokenUsage.App.Services;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

public sealed partial class GeneralOptionsView : UserControl
{
    private GeneralOptionsViewModel? _viewModel;
    private bool _isInitialized;
    private bool _isShareCaptureFolderPathQueued;
    private readonly DispatcherTimer _descriptionTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private TextBlock? _pendingDescriptionLabel;
    private TextBlock? _openDescriptionLabel;

    public GeneralOptionsView()
    {
        InitializeComponent();
        foreach ((TextBlock label, ToggleSwitch toggle) in new[]
        {
            (DataCollectionBackgroundLabel, DataCollectionBackgroundToggle),
            (ClaudeStatusLineLabel, ClaudeStatusLineToggle),
            (CodexSessionAttributionLabel, CodexSessionAttributionToggle),
            (CodexProjectAttributionLabel, CodexProjectAttributionToggle),
            (CursorSessionAttributionLabel, CursorSessionAttributionToggle),
            (CodexMcpAttributionLabel, CodexMcpAttributionToggle),
            (CodexSkillsAttributionLabel, CodexSkillsAttributionToggle),
            (CodexCommandsAttributionLabel, CodexCommandsAttributionToggle),
            (CodexFilesAttributionLabel, CodexFilesAttributionToggle),
        })
        {
            label.PointerEntered += OnDescriptionLabelPointerEntered;
            label.PointerExited += OnDescriptionLabelPointerExited;
            if (label.Tag is ToolTip { Content: TextBlock description })
            {
                AutomationProperties.SetLabeledBy(toggle, label);
                AutomationProperties.SetHelpText(toggle, description.Text);
            }
        }

        _descriptionTimer.Tick += OnDescriptionTimerTick;
        _isInitialized = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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

    private void OnDescriptionLabelPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        CloseDescriptionToolTip();
        _pendingDescriptionLabel = (TextBlock)sender;
        _descriptionTimer.Start();
    }

    private void OnDescriptionLabelPointerExited(object sender, PointerRoutedEventArgs e) =>
        CloseDescriptionToolTip();

    private void OnDescriptionTimerTick(object? sender, object e)
    {
        _descriptionTimer.Stop();
        if (_pendingDescriptionLabel is not { Tag: ToolTip toolTip } label)
        {
            return;
        }

        _pendingDescriptionLabel = null;
        _openDescriptionLabel = label;
        ToolTipService.SetToolTip(label, toolTip);
        toolTip.IsOpen = true;
    }

    private void CloseDescriptionToolTip()
    {
        _descriptionTimer.Stop();
        _pendingDescriptionLabel = null;
        if (_openDescriptionLabel is { Tag: ToolTip toolTip } label)
        {
            toolTip.IsOpen = false;
            ToolTipService.SetToolTip(label, null);
        }

        _openDescriptionLabel = null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => CloseDescriptionToolTip();

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

    private async void OnAttributionBackfillRunClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        await ViewModel.RunAttributionBackfillAsync();
    }

}
