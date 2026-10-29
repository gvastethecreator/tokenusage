using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TokenUsage.App.ViewModels.Reports;

namespace TokenUsage.App.Views.Reports;

public sealed partial class UsageReportPage
{
    private bool _syncingCompactSort;

    private void OnCompactSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCompactSort
            || sender is not ComboBox { SelectedItem: UsageReportCompactSortOption option })
        {
            return;
        }

        ViewModel.SelectCompactSort(option);
    }

    private void OnCompactSortDirectionClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleCompactSortDirection();
    }

    private void SyncCompactSortCombo()
    {
        if (UsageReportCompactSortCombo is null)
        {
            return;
        }

        _syncingCompactSort = true;
        try
        {
            UsageReportCompactSortCombo.SelectedItem = ViewModel.SelectedCompactSort;
        }
        finally
        {
            _syncingCompactSort = false;
        }
    }

    private void OnTableRowPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Control row) VisualStateManager.GoToState(row, "PointerOver", false);
    }

    private void OnTableRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Control row) VisualStateManager.GoToState(row, "Normal", false);
    }
}
