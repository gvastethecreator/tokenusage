using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TokenUsage.App.Views.Reports;

public sealed partial class UsageReportPage
{
    private string? _modelReturnId;
    private double _modelReturnOffset;

    private void OnClearExplorerClick(object sender, RoutedEventArgs e) => ViewModel.ClearExplorerFilters();
    private async void OnLoadConfigurationsClick(object sender, RoutedEventArgs e) => await ViewModel.LoadConfigurationsAsync();
    private void OnMoreActivityClick(object sender, RoutedEventArgs e) => ViewModel.ShowMoreActivityWindows();
    private async void OnLoadExplanationDetailClick(object sender, RoutedEventArgs e) => await ViewModel.LoadExplanationDetailAsync();
    private void OnExplanationEvidenceClick(object sender, RoutedEventArgs e)
    {
        ExplanationReturnButton.Visibility = Visibility.Visible;
        OnExplorerEvidenceClick(sender, e);
        ExplanationReturnButton.Focus(FocusState.Programmatic);
    }
    private void OnExplanationReturnClick(object sender, RoutedEventArgs e)
    {
        ExplanationReturnButton.Visibility = Visibility.Collapsed;
        ExplanationBreakdownReturnButton.Visibility = Visibility.Collapsed;
        ExplanationCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
        ExplanationEvidenceButton.Focus(FocusState.Programmatic);
    }
    private void OnExplanationBreakdownClick(object sender, RoutedEventArgs e)
    {
        ExplanationBreakdownReturnButton.Visibility = Visibility.Visible;
        ExplanationBreakdownReturnButton.UpdateLayout();
        ExplanationBreakdownReturnButton.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
        ExplanationBreakdownReturnButton.Focus(FocusState.Programmatic);
    }

    private void OnModelDetailClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string id } row) return;
        _modelReturnId = id;
        _modelReturnOffset = ReportScrollViewer.VerticalOffset;
        ViewModel.OpenModelDetail(id);
        ShowModelDetail();
    }

    private void ShowModelDetail()
    {
        if (!ViewModel.HasModelDetail) return;
        ModelDetailCard.UpdateLayout();
        ModelDetailCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseModelDetailButton.Focus(FocusState.Programmatic);
    }

    private void OnCloseModelDetailClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseModelDetail();
        ReportScrollViewer.UpdateLayout();
        ReportScrollViewer.ChangeView(null, _modelReturnOffset, null, disableAnimation: true);
        ReportScrollViewer.UpdateLayout();
        int index = ViewModel.ModelRows.ToList().FindIndex(row => row.Id == _modelReturnId);
        if (index >= 0 && ModelBreakdownRows.TryGetElement(index) is DependencyObject row
            && Descendants(row).OfType<Button>().FirstOrDefault(button => Equals(button.Tag, _modelReturnId)) is { } button)
            button.Focus(FocusState.Programmatic);
        else ExplorerSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnExplorerEvidenceClick(object sender, RoutedEventArgs e)
    {
        MeasurementDetails.IsExpanded = true;
        MeasurementDetails.UpdateLayout();
        MeasurementDetails.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
        MeasurementDetails.Focus(FocusState.Programmatic);
    }

    private void OnBackToModelClick(object sender, RoutedEventArgs e) => ShowModelDetail();

    private async void OnOpenSessionsClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenSessionsAsync();
        if (!ViewModel.HasSessions) return;
        SessionListCard.UpdateLayout();
        SessionListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseSessionsButton.Focus(FocusState.Programmatic);
    }

    private async void OnOpenDistributionOutlierClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenDistributionOutlierAsync();
        if (!ViewModel.HasSessions) return;
        SessionListCard.UpdateLayout();
        SessionListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseSessionsButton.Focus(FocusState.Programmatic);
    }

    private async void OnOverviewProjectClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string id })
        {
            return;
        }

        await ViewModel.OpenOverviewProjectAsync(id);
        if (!ViewModel.HasProjects)
        {
            return;
        }

        ProjectListCard.UpdateLayout();
        ProjectListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseProjectsButton.Focus(FocusState.Programmatic);
    }

    private async void OnOpenProjectsClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenProjectsAsync();
        if (!ViewModel.HasProjects) return;
        ProjectListCard.UpdateLayout();
        ProjectListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseProjectsButton.Focus(FocusState.Programmatic);
    }

    private async void OnOpenOperationsClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenOperationsAsync();
        if (!ViewModel.HasOperations) return;
        OperationListCard.UpdateLayout();
        OperationListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseOperationsButton.Focus(FocusState.Programmatic);
    }

    private void OnCloseOperationsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseOperations();
        ShowModelDetail();
    }

    private async void OnOpenOperationSessionClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenOperationLinkedSessionAsync();
        if (!ViewModel.HasSessions) return;
        SessionListCard.UpdateLayout();
        SessionListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseSessionsButton.Focus(FocusState.Programmatic);
    }

    private void OnOperationDetailClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string id })
        {
            return;
        }

        ViewModel.OpenOperationDetail(id);
    }

    private void OnCloseProjectsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseProjects();
        ShowModelDetail();
    }

    private async void OnProjectDetailClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Control row)
        {
            return;
        }

        await ViewModel.OpenProjectSessionsAsync(row.Tag as string);
        if (!ViewModel.HasSessions) return;
        SessionListCard.UpdateLayout();
        SessionListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        CloseSessionsButton.Focus(FocusState.Programmatic);
    }

    private void OnCloseSessionsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseSessions();
        if (ViewModel.HasProjects)
        {
            ProjectListCard.UpdateLayout();
            ProjectListCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            CloseProjectsButton.Focus(FocusState.Programmatic);
            return;
        }

        ShowModelDetail();
    }

    private void OnSessionExpandClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Control row)
        {
            return;
        }

        ViewModel.ToggleSessionExpand(row.Tag as string);
    }

    private void OnSessionDetailClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Control row)
        {
            return;
        }

        ViewModel.OpenSessionDetail(row.Tag as string);
    }

    private async void OnSaveProjectAliasClick(object sender, RoutedEventArgs e) =>
        await ViewModel.SaveProjectAliasAsync();

    private async void OnRemapProjectClick(object sender, RoutedEventArgs e) =>
        await ViewModel.RemapProjectToCurrentSelectionAsync();

    private async void OnModelCompareClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenModelComparisonAsync();
        if (IsLoaded && ViewModel.HasExplorerReturn)
        {
            ReportScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
            ExplorerReturnButton.Focus(FocusState.Programmatic);
        }
    }

    private async void OnReturnToExplorerClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ReturnToExplorerAsync();
        if (IsLoaded) ShowModelDetail();
    }
}
