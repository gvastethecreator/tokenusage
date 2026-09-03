using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

public sealed partial class ProviderStatusView : UserControl
{
    private ProviderStatusSurfaceViewModel? _viewModel;
    private bool _isInitialized;

    public ProviderStatusView()
    {
        InitializeComponent();
        _isInitialized = true;
    }

    public ProviderStatusSurfaceViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = value ?? throw new ArgumentNullException(nameof(value));
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            if (_isInitialized)
            {
                Bindings.Update();
            }
        }
    }

    public UIElement PrimaryAction => ProviderStatusRefreshButton;

    private void OnProviderStatusMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.IsAdditionalProvidersExpanded =
                !ViewModel.IsAdditionalProvidersExpanded;
        }
    }

    private void OnCredentialEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProviderCredentialEditor editor)
        {
            editor.Host = ViewModel;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(
                e.PropertyName,
                nameof(ProviderStatusSurfaceViewModel.FocusedProviderId),
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(ViewModel?.FocusedProviderId))
        {
            return;
        }

        string providerId = ViewModel.FocusedProviderId;
        _ = DispatcherQueue.TryEnqueue(() => FocusProviderRow(providerId));
    }

    private void FocusProviderRow(string providerId)
    {
        string? automationId = ViewModel?.Providers.FirstOrDefault(provider => string.Equals(
            provider.ProviderId,
            providerId,
            StringComparison.Ordinal))?.AutomationId;
        if (automationId is null)
        {
            return;
        }

        FrameworkElement? row = FindDescendant<FrameworkElement>(this, element => string.Equals(
            AutomationProperties.GetAutomationId(element),
            automationId,
            StringComparison.Ordinal));
        row?.StartBringIntoView();
        FindDescendant<Control>(row, control => control.IsTabStop)?.Focus(FocusState.Programmatic);
    }

    private static T? FindDescendant<T>(
        DependencyObject? root,
        Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && predicate(match))
            {
                return match;
            }

            T? nested = FindDescendant(VisualTreeHelper.GetChild(root, index), predicate);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
