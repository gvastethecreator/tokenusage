using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

public sealed partial class NotificationsOptionsView : UserControl
{
    private NotificationsOptionsViewModel? _viewModel;
    public NotificationsOptionsView() => InitializeComponent();

    public NotificationsOptionsViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value ?? throw new ArgumentNullException(nameof(value));
            Bindings.Update();
        }
    }

    public UIElement PrimaryAction => AlertsMasterToggle;
}
