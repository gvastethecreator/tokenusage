using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels.Reports;

namespace TokenUsage.App.Views.Reports;

public sealed partial class UsageReportPage
{
    private Button? _dashboardReturnButton;
    private double _dashboardReturnOffset;

    private void OnDashboardSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDashboardLayout(e.NewSize.Width);

    private void OnDashboardExpandChartClick(object sender, RoutedEventArgs e) => UpdateDashboardLayout(DashboardGrid.ActualWidth);

    private void UpdateDashboardLayout(double width)
    {
        bool narrow = width < 820;
        bool expanded = DashboardExpandChart.IsChecked == true;
        DashboardGrid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumnSpan(DashboardTrendCard, !narrow && expanded ? 2 : 1);
        Grid.SetColumnSpan(DashboardProviderTrendCard, !narrow && expanded ? 2 : 1);
        Grid.SetColumn(DashboardCompositionCard, narrow || expanded ? 0 : 1);
        Grid.SetColumnSpan(DashboardCompositionCard, !narrow && expanded ? 2 : 1);
        Grid.SetRow(DashboardCompositionCard, narrow || expanded ? 1 : 0);
        Grid.SetColumn(DashboardModelsCard, 0);
        Grid.SetRow(DashboardModelsCard, narrow || expanded ? 2 : 1);
        Grid.SetColumn(DashboardProjectsCard, narrow ? 0 : 1);
        Grid.SetRow(DashboardProjectsCard, narrow ? 3 : expanded ? 2 : 1);
        int operationsRow = narrow ? 4 : expanded ? 3 : 2;
        Grid.SetRow(DashboardOperationsGrid, operationsRow);
        Grid.SetColumnSpan(DashboardOperationsGrid, 2);
        DashboardOperationsGrid.ColumnDefinitions[1].Width = narrow || !ViewModel.HasDashboardActivity
            ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(DashboardActivityPreview, narrow ? 0 : 1);
        Grid.SetRow(DashboardActivityPreview, narrow ? 1 : 0);
        GlobalCombinedChart.PlotHeight = expanded ? 310 : 190;
        ProviderChartContentRoot.PlotHeight = expanded ? 250 : 190;
    }

    private void OnDashboardOperationsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 820;
        DashboardOperationsGrid.ColumnDefinitions[1].Width = narrow || !ViewModel.HasDashboardActivity
            ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(DashboardActivityPreview, narrow ? 0 : 1);
        Grid.SetRow(DashboardActivityPreview, narrow ? 1 : 0);
    }

    private void OnDashboardOperationKindClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            ViewModel.SetDashboardShowsMcp(tag == "mcp");
        }
    }

    private async void OnDashboardOperationsViewAllClick(object sender, RoutedEventArgs e)
    {
        RememberDashboardOrigin(sender);
        await ViewModel.OpenDashboardOperationsAsync();
        if (!ViewModel.HasOperations)
        {
            return;
        }

        OperationListCard.UpdateLayout();
        OperationListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseOperationsButton.Focus(FocusState.Programmatic);
    }

    private async void OnDashboardOperationClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string id })
        {
            return;
        }

        RememberDashboardOrigin(sender);
        await ViewModel.OpenDashboardOperationsAsync();
        if (!ViewModel.HasOperations)
        {
            return;
        }

        ViewModel.OpenOperationDetail(id);
        OperationListCard.UpdateLayout();
        OperationListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseOperationsButton.Focus(FocusState.Programmatic);
    }

    private async void OnDashboardActivityClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string category })
        {
            return;
        }

        RememberDashboardOrigin(sender);
        await ViewModel.OpenDashboardOperationsAsync();
        if (!ViewModel.HasOperations)
        {
            return;
        }

        ViewModel.SelectDerivedActivity(category);
        OperationListCard.UpdateLayout();
        OperationListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseOperationsButton.Focus(FocusState.Programmatic);
    }

    private void RememberDashboardOrigin(object sender)
    {
        _dashboardReturnButton = sender as Button;
        _dashboardReturnOffset = ReportScrollViewer.VerticalOffset;
    }

    private bool RestoreDashboardOrigin()
    {
        if (_dashboardReturnButton is not { IsLoaded: true } button) return false;
        _dashboardReturnButton = null;
        ReportScrollViewer.UpdateLayout();
        ReportScrollViewer.ChangeView(null, _dashboardReturnOffset, null, disableAnimation: true);
        button.Focus(FocusState.Programmatic);
        return true;
    }

    private void OnDashboardDetailsCollapsed(Expander sender, ExpanderCollapsedEventArgs args) => RestoreDashboardOrigin();

    private void OnDashboardModelClick(object sender, RoutedEventArgs e)
    {
        RememberDashboardOrigin(sender);
        OnModelDetailClick(sender, e);
    }

    private void OnDashboardProjectClick(object sender, RoutedEventArgs e)
    {
        RememberDashboardOrigin(sender);
        OnOverviewProjectClick(sender, e);
    }

    private void OnDashboardViewAllClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out UsageReportBreakdown breakdown)) return;
        RememberDashboardOrigin(sender);
        ViewModel.SetBreakdown(breakdown);
        DashboardFullBreakdown.IsExpanded = true;
        DashboardFullBreakdown.UpdateLayout();
        DashboardFullBreakdown.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
        DashboardFullBreakdown.Focus(FocusState.Programmatic);
    }
}
