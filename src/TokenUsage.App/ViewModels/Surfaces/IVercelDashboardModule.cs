using System.ComponentModel;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;

namespace TokenUsage.App.ViewModels.Surfaces;

public interface IVercelDashboardModule : INotifyPropertyChanged
{
    bool IsBusy { get; }

    bool IsConfigured { get; }

    VercelGatewayUiState State { get; }

    ProviderCard? ProviderCard { get; }

    SpendSlice? SpendSlice { get; }

    void ApplyHostCacheSnapshot(ProviderSnapshot snapshot);

    Task ApplyHostProviderCompletedAsync(
        CacheFirstEvent.ProviderCompleted completed,
        CancellationToken cancellationToken = default);

    ProviderStatusRow CreateStatusRow();
}
