using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

public sealed partial class UpdateOptionsView : UserControl
{
    private UpdateOptionsViewModel? _viewModel;
    private bool _subscribed;

    public UpdateOptionsView()
    {
        InitializeComponent();
        Loaded += (_, _) => SubscribeToStatus();
        Unloaded += (_, _) => UnsubscribeFromStatus();
    }

    public UpdateOptionsViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            UnsubscribeFromStatus();
            _viewModel = value;
            Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
            Bindings.Update();
            if (IsLoaded) SubscribeToStatus();
        }
    }

    private void SubscribeToStatus()
    {
        if (_subscribed || _viewModel is null) return;
        _viewModel.PropertyChanged += OnStatusChanged;
        _subscribed = true;
    }

    private void UnsubscribeFromStatus()
    {
        if (!_subscribed || _viewModel is null) return;
        _viewModel.PropertyChanged -= OnStatusChanged;
        _subscribed = false;
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(UpdateOptionsViewModel.StatusText)
            && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            FrameworkElementAutomationPeer.CreatePeerForElement(UpdateStatus)
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
