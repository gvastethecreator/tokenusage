using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WOpenUsage.App.Controls;
using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Surfaces;
using WOpenUsage.Core.Layout;

namespace WOpenUsage.App.Views.Options;

public sealed partial class PersonalizationOptionsView : UserControl
{
    private PersonalizationSurfaceViewModel? _viewModel;
    private bool _isInitialized;

    public PersonalizationOptionsView()
    {
        InitializeComponent();
        _isInitialized = true;
    }

    public PersonalizationSurfaceViewModel? ViewModel
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

    public UIElement PrimaryAction => DashboardLayoutExpander;

    private async void OnDashboardProviderMoveUpClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && sender is FrameworkElement { Tag: string providerId })
        {
            await ViewModel.MoveProviderAsync(providerId, -1);
        }
    }

    private async void OnDashboardLayoutUndoClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.UndoAsync();
        }
    }

    private async void OnDashboardLayoutResetClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ViewModel.ResetTitle,
            Content = ViewModel.ResetBody,
            PrimaryButtonText = ViewModel.ResetConfirm,
            CloseButtonText = ViewModel.ResetCancel,
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(dialog, "DashboardLayoutResetDialog");
        AutomationProperties.SetName(dialog, ViewModel.ResetTitle);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ResetAsync();
        }
    }

    private async void OnDashboardProviderMoveDownClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && sender is FrameworkElement { Tag: string providerId })
        {
            await ViewModel.MoveProviderAsync(providerId, 1);
        }
    }

    private async void OnDashboardProviderVisibilityClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null
            && sender is ToggleButton { Tag: string providerId } toggle)
        {
            await ViewModel.SetProviderVisibleAsync(
                providerId,
                toggle.IsChecked is true);
        }
    }

    private async void OnDashboardProviderHighlightClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null
            && sender is ToggleButton { Tag: string providerId } toggle)
        {
            await ViewModel.SetProviderHighlightedAsync(
                providerId,
                toggle.IsChecked is true);
        }
    }

    private void OnDashboardProviderColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button
            {
                Tag: DashboardProviderLayoutRow row,
                Flyout: Flyout { Content: ColorPicker picker },
            })
        {
            picker.Color = ProviderColorPalette.Parse(
                ProviderColorPalette.GetEffectiveHex(row.ProviderId, row.ColorHex));
        }
    }

    private async void OnDashboardProviderColorFlyoutClosed(object sender, object e)
    {
        if (ViewModel is null
            || sender is not Flyout
            {
                Content: ColorPicker
                {
                    Tag: DashboardProviderLayoutRow row,
                } picker,
            })
        {
            return;
        }

        string selectedColor = ProviderColorPalette.ToHex(picker.Color);
        string currentColor = ProviderColorPalette.GetEffectiveHex(
            row.ProviderId,
            row.ColorHex);
        if (!string.Equals(selectedColor, currentColor, StringComparison.OrdinalIgnoreCase))
        {
            await ViewModel.SetProviderColorAsync(row.ProviderId, selectedColor);
        }
    }

    private async void OnDashboardMetricMoveUpClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null
            && TryGetDashboardMetricTarget(sender, out string providerId, out string metricId))
        {
            await ViewModel.MoveMetricAsync(providerId, metricId, -1);
        }
    }

    private async void OnDashboardMetricMoveDownClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null
            && TryGetDashboardMetricTarget(sender, out string providerId, out string metricId))
        {
            await ViewModel.MoveMetricAsync(providerId, metricId, 1);
        }
    }

    private async void OnDashboardMetricVisibilityClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null
            && sender is ToggleButton toggle
            && TryGetDashboardMetricTarget(sender, out string providerId, out string metricId))
        {
            await ViewModel.SetMetricVisibleAsync(
                providerId,
                metricId,
                toggle.IsChecked is true);
        }
    }

    private async void OnDashboardMetricHighlightClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null
            && sender is ToggleButton toggle
            && TryGetDashboardMetricTarget(sender, out string providerId, out string metricId))
        {
            await ViewModel.SetMetricHighlightedAsync(
                providerId,
                metricId,
                toggle.IsChecked is true);
        }
    }

    private async void OnDashboardMetricSectionClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null
            && sender is ToggleButton toggle
            && TryGetDashboardMetricTarget(sender, out string providerId, out string metricId))
        {
            await ViewModel.SetMetricOnDemandAsync(
                providerId,
                metricId,
                toggle.IsChecked is true);
        }
    }

    private static bool TryGetDashboardMetricTarget(
        object sender,
        out string providerId,
        out string metricId)
    {
        if (sender is ButtonBase
            {
                Tag: string provider,
                CommandParameter: string metric,
            }
            && !string.IsNullOrWhiteSpace(provider)
            && !string.IsNullOrWhiteSpace(metric))
        {
            providerId = provider;
            metricId = metric;
            return true;
        }

        providerId = string.Empty;
        metricId = string.Empty;
        return false;
    }

    private void OnDashboardProviderMetricsExpanding(
        object sender,
        ExpanderExpandingEventArgs e)
    {
        SetDashboardProviderMetricsExpanded(sender, isExpanded: true);
        SetDashboardProviderMetricItems(sender, loadItems: true);
    }

    private void OnDashboardProviderMetricsCollapsed(
        object sender,
        ExpanderCollapsedEventArgs e)
    {
        SetDashboardProviderMetricsExpanded(sender, isExpanded: false);
        SetDashboardProviderMetricItems(sender, loadItems: false);
    }

    private void SetDashboardProviderMetricsExpanded(object sender, bool isExpanded)
    {
        if (ViewModel is not null
            && sender is FrameworkElement { Tag: DashboardProviderLayoutRow row }
            && (isExpanded || ViewModel.Providers.Any(current =>
                ReferenceEquals(current, row))))
        {
            ViewModel.SetProviderMetricsExpanded(row.ProviderId, isExpanded);
        }
    }

    private static void SetDashboardProviderMetricItems(object sender, bool loadItems)
    {
        if (sender is Expander
            {
                Tag: DashboardProviderLayoutRow row,
                Content: ItemsControl items,
            })
        {
            items.ItemsSource = loadItems ? row.Metrics : null;
        }
    }
}
