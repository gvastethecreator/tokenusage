using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TokenUsage.App.ViewModels.Reports;

namespace TokenUsage.App.Views.Reports;

public sealed partial class UsageReportPage
{
    private double _breakdownReturnOffset;

    private void OnBreakdownLayoutSizeChanged(object sender, SizeChangedEventArgs e) => UpdateBreakdownLayout();

    private void UpdateBreakdownLayout()
    {
        if (BreakdownLayout is null || BreakdownDetailColumn is null) return;
        bool lateral = BreakdownLayout.ActualWidth >= 1100;
        bool selected = ViewModel.HasBreakdownDetail;
        BreakdownDetailColumn.Width = new GridLength(lateral && selected ? 360 : 0);
        Grid.SetColumn(BreakdownDetail, lateral ? 1 : 0);
        Grid.SetRow(BreakdownDetail, !lateral && _isCapturing ? 1 : 0);
        BreakdownTable.Visibility = !lateral && selected && !_isCapturing ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnBreakdownRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        if (!ViewModel.HasBreakdownDetail) _breakdownReturnOffset = ReportScrollViewer.VerticalOffset;
        await ViewModel.SelectBreakdownRowAsync(id);
        UpdateBreakdownLayout();
        BreakdownDetail.UpdateLayout();
        BreakdownDetail.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseBreakdownDetailButton.Focus(FocusState.Programmatic);
    }

    private void OnCloseBreakdownDetailClick(object sender, RoutedEventArgs e) => CloseBreakdownDetail();
    private void OnBreakdownEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        CloseBreakdownDetail();
        args.Handled = true;
    }

    private void CloseBreakdownDetail()
    {
        string? id = ViewModel.SelectedBreakdownRow?.Id;
        ViewModel.CloseBreakdownDetail();
        UpdateBreakdownLayout();
        ReportScrollViewer.UpdateLayout();
        ReportScrollViewer.ChangeView(null, _breakdownReturnOffset, null, disableAnimation: true);
        int index = ViewModel.BreakdownRows.ToList().FindIndex(row => row.Id == id);
        if (index >= 0 && (BreakdownRowsView.TryGetElement(index) ?? BreakdownRowsView.GetOrCreateElement(index)) is Control row)
            row.Focus(FocusState.Programmatic);
    }

    private async void OnBreakdownEvidenceClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedBreakdownRow?.EvidenceId is not { } id) return;
        _dashboardReturnButton = null;
        if (ViewModel.Breakdown == UsageReportBreakdown.Model)
        {
            _modelReturnId = id;
            _modelReturnOffset = ReportScrollViewer.VerticalOffset;
            ViewModel.OpenModelDetail(id);
            ShowModelDetail();
        }
        else if (ViewModel.Breakdown == UsageReportBreakdown.Project)
        {
            _overviewProjectReturnId = id;
            await ViewModel.OpenOverviewProjectAsync(id);
            if (!ViewModel.HasProjects) return;
            ProjectListCard.UpdateLayout();
            ProjectListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            CloseProjectsButton.Focus(FocusState.Programmatic);
        }
    }
}
