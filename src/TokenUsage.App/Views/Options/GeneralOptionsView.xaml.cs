using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

public sealed partial class GeneralOptionsView : UserControl
{
    private GeneralOptionsViewModel? _viewModel;
    private bool _isInitialized;

    public GeneralOptionsView()
    {
        InitializeComponent();
        var resources = new ResourceLoader();
        foreach ((ToggleSwitch toggle, string key) in new[]
        {
            (DataCollectionBackgroundToggle, "DataCollectionBackgroundDescription.Text"),
            (ClaudeLimitsToggle, "ClaudeLimitsDescription.Text"),
        })
        {
            string description = resources.GetString(key.Replace('.', '/'));
            AutomationProperties.SetHelpText(toggle, description);
            ToolTipService.SetToolTip(toggle, new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
            });
        }

        _isInitialized = true;
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
}
