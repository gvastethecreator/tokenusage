using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels.Reports;

namespace TokenUsage.App.Views.Reports;

public sealed partial class UsageReportPage
{
    private Button? _dashboardReturnButton;
    private double _dashboardReturnOffset;

    private void OnDashboardSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDashboardLayout(e.NewSize.Width);

    // Totals, full-width chart, horizontal limits, then the detailed breakdowns.
    // Narrow: one column in the same reading order. Grid spacing applies to empty rows and
    // columns too, so the layout keeps exactly the tracks it uses.
    private void UpdateDashboardLayout(double width)
    {
        bool narrow = width < 820;
        DashboardGrid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        DashboardGrid.ColumnSpacing = narrow ? 0 : 12;
        bool hasAccounts = ViewModel.AccountQuotas.Count > 0;
        bool hasProviderLimits = ViewModel.LimitItems.Count > 0;
        ReportQuotaStrip.ColumnDefinitions[0].Width = hasAccounts ? new GridLength(Math.Max(1, ViewModel.AccountQuotas.Count), GridUnitType.Star) : new GridLength(0);
        ReportQuotaStrip.ColumnDefinitions[1].Width = hasProviderLimits ? new GridLength(Math.Max(1, ViewModel.LimitItems.Count), GridUnitType.Star) : new GridLength(0);
        ReportQuotaStrip.ColumnSpacing = hasAccounts && hasProviderLimits ? 16 : 0;
        var content = ViewModel.ReportContent;
        int nextRow = 0;
        Grid.SetRow(DashboardStatsPanel, nextRow);
        Grid.SetColumnSpan(DashboardStatsPanel, 2);
        if (content.Highlights && ViewModel.HasHighlights) nextRow++;
        bool pair = !narrow && content.Models && content.TokenComposition;
        Grid.SetRow(DashboardModelsCard, nextRow);
        Grid.SetColumn(DashboardModelsCard, 0);
        Grid.SetColumnSpan(DashboardModelsCard, pair ? 1 : 2);
        if (content.Models && !pair) nextRow++;
        Grid.SetRow(DashboardCompositionCard, nextRow);
        Grid.SetColumn(DashboardCompositionCard, pair ? 1 : 0);
        Grid.SetColumnSpan(DashboardCompositionCard, pair ? 1 : 2);
        if (content.TokenComposition) nextRow++;
        Grid.SetRow(DashboardProjectsCard, nextRow);
        Grid.SetColumn(DashboardProjectsCard, 0);
        Grid.SetColumnSpan(DashboardProjectsCard, 2);
        if (content.Projects && ViewModel.HasDashboardProjects) nextRow++;
        Grid.SetRow(DashboardOperationsGrid, nextRow);
        Grid.SetColumnSpan(DashboardOperationsGrid, 2);
        if (content.Tools && ViewModel.HasDashboardOperationsCard) nextRow++;
        DashboardGrid.Visibility = nextRow > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetRowCount(DashboardGrid, Math.Max(1, nextRow));
        UpdateDashboardOperationsLayout(width);
        GlobalCombinedChart.PlotHeight = 250;
        ProviderChartContentRoot.PlotHeight = 250;
    }

    private void OnDashboardOperationsSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateDashboardOperationsLayout(e.NewSize.Width);

    private void UpdateDashboardOperationsLayout(double width)
    {
        bool single = width < 820 || !ViewModel.HasDashboardActivity;
        DashboardOperationsGrid.ColumnDefinitions[1].Width = single ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        DashboardOperationsGrid.ColumnSpacing = single ? 0 : 12;
        Grid.SetColumn(DashboardActivityPreview, single ? 0 : 1);
        Grid.SetRow(DashboardActivityPreview, single ? 1 : 0);
    }

    private static void SetRowCount(Grid grid, int count)
    {
        while (grid.RowDefinitions.Count < count) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        while (grid.RowDefinitions.Count > count) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
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
        DashboardFullBreakdown.Visibility = Visibility.Visible;
        DashboardFullBreakdown.IsExpanded = true;
        DashboardFullBreakdown.UpdateLayout();
        DashboardFullBreakdown.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
        DashboardFullBreakdown.Focus(FocusState.Programmatic);
    }
}
