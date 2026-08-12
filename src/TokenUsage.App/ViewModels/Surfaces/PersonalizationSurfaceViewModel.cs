using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Providers;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed partial class PersonalizationSurfaceViewModel : ObservableObject
{
    private readonly DashboardLayoutEditor _editor;
    private readonly Func<string, string> _getString;
    private readonly HashSet<string> _expandedProviders = new(StringComparer.Ordinal);
    private DashboardLayout _layout = DashboardLayout.Empty;
    private bool _hasDashboard;

    public PersonalizationSurfaceViewModel(
        DashboardLayoutEditor editor,
        Func<string, string> getString)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _getString = getString ?? throw new ArgumentNullException(nameof(getString));
        IsBusy = true;
        Initialization = InitializeAsync();
    }

    public event EventHandler? LayoutChanged;

    public Task Initialization { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProviders))]
    [NotifyPropertyChangedFor(nameof(AreAllProvidersHidden))]
    public partial IReadOnlyList<DashboardProviderLayoutRow> Providers { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusVisible))]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    public partial bool IsBusy { get; private set; }

    public bool HasProviders => Providers.Count > 0;

    public bool AreAllProvidersHidden =>
        Providers.Count > 0 && Providers.All(provider => !provider.IsVisible);

    public bool IsStatusVisible => !string.IsNullOrWhiteSpace(StatusText);

    public bool IsEditable => _editor.IsEditable && !IsBusy;

    public bool CanUndo => IsEditable && _editor.CanUndo;

    public string ResetTitle => _getString("DashboardLayoutResetTitle");

    public string ResetBody => _getString("DashboardLayoutResetBody");

    public string ResetConfirm => _getString("DashboardLayoutResetConfirm");

    public string ResetCancel => _getString("DashboardLayoutResetCancel");

    public DashboardSnapshot Apply(DashboardSnapshot dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        _hasDashboard = true;
        DashboardLayoutProjection projection = DashboardLayoutProjector.Apply(
            dashboard,
            _layout,
            _getString("DashboardProviderHighlightedLabel"),
            new DashboardProviderActionNameFormats(
                _getString("DashboardProviderMoveUpAutomationNameFormat"),
                _getString("DashboardProviderMoveDownAutomationNameFormat"),
                _getString("DashboardProviderVisibilityAutomationNameFormat"),
                _getString("DashboardProviderHighlightAutomationNameFormat"),
                _getString("DashboardProviderMetricsAutomationNameFormat"),
                _getString("DashboardProviderColorAutomationNameFormat")),
            new DashboardMetricActionNameFormats(
                _getString("DashboardMetricMoveUpAutomationNameFormat"),
                _getString("DashboardMetricMoveDownAutomationNameFormat"),
                _getString("DashboardMetricVisibilityAutomationNameFormat"),
                _getString("DashboardMetricHighlightAutomationNameFormat"),
                _getString("DashboardMetricAlwaysVisibleSection"),
                _getString("DashboardMetricOnDemandSection"),
                _getString("DashboardMetricMoveToAlwaysVisibleAutomationNameFormat"),
                _getString("DashboardMetricMoveToOnDemandAutomationNameFormat")),
            SummarizeSpend);
        _layout = projection.Layout;
        Providers = projection.Providers
            .Select(row => row with
            {
                IsMetricsExpanded = _expandedProviders.Contains(row.ProviderId),
            })
            .ToArray();
        return projection.Dashboard;
    }

    public LocalUsageCard Apply(LocalUsageCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        LocalUsageCard projected = DashboardLayoutProjector.ApplyToLocalUsage(card, _layout);
        DashboardSpendSummary spend = SummarizeSpend(projected.SpendBreakdown.AgentSlices);
        int providerCount = projected.SpendBreakdown.Models
            .Select(model => model.AgentId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        string summary = string.Format(
            CultureInfo.CurrentCulture,
            _getString("LocalUsageBreakdownSummaryFormat"),
            providerCount,
            projected.SpendBreakdown.Models.Count);
        string accessibleName = string.Format(
            CultureInfo.CurrentCulture,
            _getString("LocalUsageBreakdownAccessibleFormat"),
            spend.TotalAmount,
            summary);

        return projected with
        {
            SpendBreakdown = projected.SpendBreakdown with
            {
                SummaryText = summary,
                TotalText = spend.TotalAmount,
                CompactTotalText = spend.CompactTotalAmount,
                AccessibleName = accessibleName,
            },
        };
    }

    public Task MoveProviderAsync(string providerId, int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return MutateAsync(layout =>
            MoveCurrentProvider(layout, new ProviderId(providerId), offset));
    }

    public Task SetProviderVisibleAsync(string providerId, bool isVisible) =>
        MutateAsync(layout => layout.SetProviderVisible(
            new ProviderId(providerId),
            isVisible));

    public Task SetProviderHighlightedAsync(string providerId, bool isHighlighted) =>
        MutateAsync(layout =>
        {
            var provider = new ProviderId(providerId);
            ProviderLayoutPreference current = layout.Providers.Single(item =>
                item.ProviderId == provider);
            DashboardLayout next = layout.SetProviderHighlighted(provider, isHighlighted);
            if (isHighlighted && !current.IsHighlighted && next.Equals(layout))
            {
                StatusText = _getString("DashboardProviderHighlightLimitReached");
            }

            return next;
        });

    public Task SetProviderColorAsync(string providerId, string colorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
        return MutateAsync(layout => layout.SetProviderColor(
            new ProviderId(providerId),
            colorHex));
    }

    public Task MoveMetricAsync(string providerId, string metricId, int offset)
    {
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return MutateAsync(layout => MoveCurrentMetric(
            layout,
            new ProviderId(providerId),
            new MetricId(metricId),
            offset));
    }

    public Task SetMetricVisibleAsync(
        string providerId,
        string metricId,
        bool isVisible) =>
        MutateAsync(layout => layout.SetMetricVisible(
            new ProviderId(providerId),
            new MetricId(metricId),
            isVisible));

    public Task SetMetricHighlightedAsync(
        string providerId,
        string metricId,
        bool isHighlighted) =>
        MutateAsync(layout =>
        {
            var provider = new ProviderId(providerId);
            var metric = new MetricId(metricId);
            ProviderLayoutPreference currentProvider = layout.Providers.Single(item =>
                item.ProviderId == provider);
            MetricLayoutPreference currentMetric = currentProvider.Metrics.Single(item =>
                item.MetricId == metric);
            DashboardLayout next = layout.SetMetricHighlighted(provider, metric, isHighlighted);
            if (isHighlighted && !currentMetric.IsHighlighted && next.Equals(layout))
            {
                StatusText = _getString("DashboardMetricHighlightLimitReached");
            }

            return next;
        });

    public Task SetMetricOnDemandAsync(
        string providerId,
        string metricId,
        bool isOnDemand) =>
        MutateAsync(layout => layout.SetMetricOnDemand(
            new ProviderId(providerId),
            new MetricId(metricId),
            isOnDemand));

    public Task ResetAsync() => MutateAsync(_ => DashboardLayout.Empty);

    public async Task UndoAsync()
    {
        await Initialization.ConfigureAwait(true);
        if (IsBusy || !_hasDashboard)
        {
            return;
        }

        IsBusy = true;
        try
        {
            DashboardLayoutEditorSaveKind kind = await _editor
                .UndoAsync()
                .ConfigureAwait(true);
            ApplySave(kind);
            if (kind is DashboardLayoutEditorSaveKind.Saved)
            {
                _layout = _editor.Layout;
                LayoutChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(CanUndo));
        }
    }

    public void SetProviderMetricsExpanded(string providerId, bool isExpanded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (isExpanded)
        {
            _expandedProviders.Add(providerId);
        }
        else
        {
            _expandedProviders.Remove(providerId);
        }
    }

    public void MarkReadOnly(string statusText)
    {
        _editor.MarkReadOnly();
        StatusText = statusText;
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanUndo));
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _editor.InitializeAsync().ConfigureAwait(true);
            _layout = _editor.Layout;
            StatusText = _editor.LastLoadKind switch
            {
                DashboardLayoutEditorLoadKind.Corrupt
                    when _editor.QuarantineFileName is string name =>
                    string.Format(
                        CultureInfo.CurrentCulture,
                        _getString("DashboardLayoutRecoveredFormat"),
                        name),
                DashboardLayoutEditorLoadKind.UnsupportedVersion
                    when _editor.UnsupportedSchemaVersion is int version =>
                    string.Format(
                        CultureInfo.CurrentCulture,
                        _getString("DashboardLayoutNewerVersionFormat"),
                        version),
                DashboardLayoutEditorLoadKind.Unavailable =>
                    _getString("DashboardLayoutUnavailable"),
                _ => string.Empty,
            };
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(CanUndo));
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task MutateAsync(Func<DashboardLayout, DashboardLayout> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await Initialization.ConfigureAwait(true);
        if (IsBusy || !_hasDashboard)
        {
            return;
        }

        IsBusy = true;
        try
        {
            DashboardLayout next = mutation(_layout);
            DashboardLayoutEditorSaveKind kind = await _editor
                .MutateAsync(_ => next)
                .ConfigureAwait(true);
            ApplySave(kind);
            if (kind is DashboardLayoutEditorSaveKind.Saved
                or DashboardLayoutEditorSaveKind.Unchanged)
            {
                _layout = _editor.Layout;
                if (kind is DashboardLayoutEditorSaveKind.Saved)
                {
                    StatusText = string.Empty;
                }

                LayoutChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(CanUndo));
        }
    }

    private void ApplySave(DashboardLayoutEditorSaveKind kind)
    {
        StatusText = kind switch
        {
            DashboardLayoutEditorSaveKind.RefusedUnsupportedVersion
                when _editor.UnsupportedSchemaVersion is int version =>
                string.Format(
                    CultureInfo.CurrentCulture,
                    _getString("DashboardLayoutNewerVersionFormat"),
                    version),
            DashboardLayoutEditorSaveKind.Failed =>
                _getString("DashboardLayoutSaveFailed"),
            _ => StatusText,
        };
    }

    private DashboardLayout MoveCurrentProvider(
        DashboardLayout layout,
        ProviderId providerId,
        int offset)
    {
        int currentRowIndex = Providers
            .Select((row, index) => (row, index))
            .Where(item => string.Equals(
                item.row.ProviderId,
                providerId.Value,
                StringComparison.Ordinal))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .Single();
        int targetRowIndex = currentRowIndex + offset;
        if (currentRowIndex < 0 || targetRowIndex < 0 || targetRowIndex >= Providers.Count)
        {
            return layout;
        }

        var targetId = new ProviderId(Providers[targetRowIndex].ProviderId);
        int currentLayoutIndex = FindProviderIndex(layout, providerId);
        int targetLayoutIndex = FindProviderIndex(layout, targetId);
        while (currentLayoutIndex != targetLayoutIndex)
        {
            int step = currentLayoutIndex < targetLayoutIndex ? 1 : -1;
            layout = layout.MoveProvider(providerId, step);
            currentLayoutIndex += step;
        }

        return layout;
    }

    private DashboardLayout MoveCurrentMetric(
        DashboardLayout layout,
        ProviderId providerId,
        MetricId metricId,
        int offset)
    {
        DashboardProviderLayoutRow providerRow = Providers.FirstOrDefault(row =>
            string.Equals(row.ProviderId, providerId.Value, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException(
                $"Provider '{providerId.Value}' is absent from dashboard layout rows.");
        int currentRowIndex = providerRow.Metrics
            .Select((row, index) => (row, index))
            .Where(item => string.Equals(
                item.row.MetricId,
                metricId.Value,
                StringComparison.Ordinal))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .Single();
        int targetRowIndex = currentRowIndex + offset;
        if (currentRowIndex < 0
            || targetRowIndex < 0
            || targetRowIndex >= providerRow.Metrics.Count)
        {
            return layout;
        }

        var targetId = new MetricId(providerRow.Metrics[targetRowIndex].MetricId);
        ProviderLayoutPreference provider = layout.Providers.Single(item =>
            item.ProviderId == providerId);
        int currentLayoutIndex = FindMetricIndex(provider, metricId);
        int targetLayoutIndex = FindMetricIndex(provider, targetId);
        while (currentLayoutIndex != targetLayoutIndex)
        {
            int step = currentLayoutIndex < targetLayoutIndex ? 1 : -1;
            layout = layout.MoveMetric(providerId, metricId, step);
            currentLayoutIndex += step;
        }

        return layout;
    }

    private static int FindProviderIndex(DashboardLayout layout, ProviderId providerId)
    {
        for (int index = 0; index < layout.Providers.Count; index++)
        {
            if (layout.Providers[index].ProviderId == providerId)
            {
                return index;
            }
        }

        throw new KeyNotFoundException(
            $"Provider '{providerId.Value}' is absent from dashboard layout.");
    }

    private static int FindMetricIndex(
        ProviderLayoutPreference provider,
        MetricId metricId)
    {
        for (int index = 0; index < provider.Metrics.Count; index++)
        {
            if (provider.Metrics[index].MetricId == metricId)
            {
                return index;
            }
        }

        throw new KeyNotFoundException(
            $"Metric '{metricId.Value}' is absent from provider '{provider.ProviderId.Value}'.");
    }

    public DashboardSpendSummary SummarizeSpend(IReadOnlyList<SpendSlice> slices)
    {
        if (slices.Count == 0)
        {
            return new DashboardSpendSummary(string.Empty, string.Empty, string.Empty);
        }

        double total = slices.Sum(slice => slice.Amount);
        string totalText = string.Format(
            CultureInfo.CurrentCulture,
            _getString("LocalUsageUsdFormat"),
            total);
        string compactTotalText = string.Format(
            CultureInfo.CurrentCulture,
            _getString("LocalUsageUsdCompactFormat"),
            total);
        string details = string.Join(", ", slices.Select(slice =>
            $"{slice.ProviderName} {slice.LegendAmountText}"));
        string accessibleName = string.Format(
            CultureInfo.CurrentCulture,
            _getString("SampleSpendAccessibleNameFormat"),
            totalText,
            slices.Count,
            details);
        return new DashboardSpendSummary(totalText, compactTotalText, accessibleName);
    }
}
