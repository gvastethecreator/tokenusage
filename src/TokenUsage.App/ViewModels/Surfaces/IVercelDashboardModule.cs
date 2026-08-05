using System.ComponentModel;
using WOpenUsage.App.ViewModels.Dashboard;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.App.ViewModels.Surfaces;

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
