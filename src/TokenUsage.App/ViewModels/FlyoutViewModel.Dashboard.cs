using System.ComponentModel;

namespace TokenUsage.App.ViewModels;

public partial class FlyoutViewModel
{
    private void OnDashboardPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Dashboard.Scope)
            or nameof(Dashboard.ProviderSummaries)
            or nameof(Dashboard.GlobalHeatmap)
            or nameof(Dashboard.SelectedProviderLimits))
        {
            LayoutRevision++;
        }

        if (string.Equals(
                e.PropertyName,
                nameof(Dashboard.ResultSurface),
                StringComparison.Ordinal))
        {
            _resultSurface = Dashboard.ResultSurface;
            if (!IsOptions)
            {
                SurfaceState = _resultSurface;
            }
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.IsRefreshing), StringComparison.Ordinal)
            || string.Equals(
                e.PropertyName,
                nameof(Dashboard.IsSessionRefreshing),
                StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsRefreshing));
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.RevealToken), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SampleRevealToken));
        }

        if (string.Equals(
                e.PropertyName,
                nameof(Dashboard.IsSampleModeEnabled),
                StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsSampleModeEnabled));
            OnPropertyChanged(nameof(IsSampleContext));
            OnPropertyChanged(nameof(IsLiveLoading));
            OnPropertyChanged(nameof(IsSampleLoading));
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.UnavailableTitle), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(UnavailableTitle));
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.UnavailableBody), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(UnavailableBody));
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.RetryButtonText), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(RetryButtonText));
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.RetryAutomationName), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(RetryAutomationName));
        }
    }
}
