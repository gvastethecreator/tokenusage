using WOpenUsage.App.ViewModels.Dashboard;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Layout;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.App.ViewModels;

public sealed record DashboardProviderLayoutRow(
    string ProviderId,
    string Name,
    bool IsVisible,
    bool IsHighlighted,
    bool CanMoveUp,
    bool CanMoveDown,
    string AutomationId,
    string MoveUpAutomationName,
    string MoveDownAutomationName,
    string VisibilityAutomationName,
    string HighlightAutomationName,
    IReadOnlyList<DashboardMetricLayoutRow> Metrics,
    string MetricsAutomationName,
    bool IsMetricsExpanded = false,
    string? ColorHex = null,
    string ColorAutomationName = "Change provider color")
{
    public string MoveUpAutomationId => $"{AutomationId}.MoveUp";
    public string MoveDownAutomationId => $"{AutomationId}.MoveDown";
    public string VisibilityAutomationId => $"{AutomationId}.Visibility";
    public string HighlightAutomationId => $"{AutomationId}.Highlight";
    public string MetricsAutomationId => $"{AutomationId}.Metrics";
    public string ColorAutomationId => $"{AutomationId}.Color";
    public string ColorPickerAutomationId => $"{ColorAutomationId}.Picker";
    public bool HasMetrics => Metrics.Count > 0;
    public DashboardProviderLayoutRow Self => this;
    public IReadOnlyList<DashboardMetricLayoutRow> ExpandedMetrics => IsMetricsExpanded ? Metrics : [];
}

public sealed record DashboardMetricLayoutRow(
    string ProviderId,
    string MetricId,
    string Label,
    bool IsOnDemand,
    bool IsVisible,
    bool IsHighlighted,
    bool CanMoveUp,
    bool CanMoveDown,
    string AutomationId,
    string SectionLabel,
    string MoveUpAutomationName,
    string MoveDownAutomationName,
    string VisibilityAutomationName,
    string HighlightAutomationName,
    string SectionAutomationName)
{
    public string MoveUpAutomationId => $"{AutomationId}.MoveUp";
    public string MoveDownAutomationId => $"{AutomationId}.MoveDown";
    public string VisibilityAutomationId => $"{AutomationId}.Visibility";
    public string HighlightAutomationId => $"{AutomationId}.Highlight";
    public string SectionAutomationId => $"{AutomationId}.Section";
}

public sealed record DashboardProviderActionNameFormats(
    string MoveUp,
    string MoveDown,
    string Visibility,
    string Highlight,
    string Metrics = "Metrics for {0}",
    string Color = "Change color for {0}")
{
    public static DashboardProviderActionNameFormats English { get; } = new(
        "Move {0} up",
        "Move {0} down",
        "Show or hide {0}",
        "Highlight {0}",
        "Metrics for {0}",
        "Change color for {0}");
}

public sealed record DashboardLayoutProjection(
    DashboardLayout Layout,
    DashboardSnapshot Dashboard,
    IReadOnlyList<DashboardProviderLayoutRow> Providers);

public sealed record DashboardSpendSummary(
    string TotalAmount,
    string CompactTotalAmount,
    string AccessibleName);

public static class DashboardLayoutProjector
{
    public static DashboardLayoutProjection Apply(
        DashboardSnapshot dashboard,
        DashboardLayout savedLayout,
        string highlightLabel,
        DashboardProviderActionNameFormats? actionNameFormats = null,
        DashboardMetricActionNameFormats? metricActionNameFormats = null,
        Func<IReadOnlyList<SpendSlice>, DashboardSpendSummary>? spendSummaryFormatter = null)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(savedLayout);
        ArgumentNullException.ThrowIfNull(highlightLabel);
        actionNameFormats ??= DashboardProviderActionNameFormats.English;
        metricActionNameFormats ??= DashboardMetricActionNameFormats.English;
        if (dashboard.Providers is null)
        {
            throw new ArgumentException(
                "Dashboard providers must not be null.",
                nameof(dashboard));
        }
        if (dashboard.SpendSlices is null)
        {
            throw new ArgumentException(
                "Dashboard spend slices must not be null.",
                nameof(dashboard));
        }

        var catalogProviders = new List<ProviderId>(
            dashboard.Providers.Count + dashboard.SpendSlices.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nameById = new Dictionary<string, string>(StringComparer.Ordinal);
        var metricCatalog = new Dictionary<ProviderId, IReadOnlyList<MetricLayoutCatalogEntry>>();
        var metricSources = new Dictionary<string, IReadOnlyDictionary<string, MetricSource>>(StringComparer.Ordinal);

        foreach (var card in dashboard.Providers)
        {
            if (card is null)
            {
                throw new ArgumentException(
                    "Dashboard providers must not contain null entries.",
                    nameof(dashboard));
            }

            if (!seen.Add(card.ProviderId))
            {
                throw new ArgumentException(
                    $"Duplicate provider id '{card.ProviderId}'.",
                    nameof(dashboard));
            }

            catalogProviders.Add(new ProviderId(card.ProviderId));
            nameById[card.ProviderId] = card.Name;
            (IReadOnlyList<MetricLayoutCatalogEntry> entries, IReadOnlyDictionary<string, MetricSource> sources) =
                CreateMetricCatalog(card, dashboard);
            metricCatalog[catalogProviders[^1]] = entries;
            metricSources[card.ProviderId] = sources;
        }

        var seenSpend = new HashSet<string>(StringComparer.Ordinal);
        foreach (SpendSlice slice in dashboard.SpendSlices)
        {
            if (slice is null)
            {
                throw new ArgumentException(
                    "Dashboard spend slices must not contain null entries.",
                    nameof(dashboard));
            }
            if (!seenSpend.Add(slice.ProviderId))
            {
                throw new ArgumentException(
                    $"Duplicate spend provider id '{slice.ProviderId}'.",
                    nameof(dashboard));
            }
            if (seen.Add(slice.ProviderId))
            {
                var providerId = new ProviderId(slice.ProviderId);
                catalogProviders.Add(providerId);
                nameById[slice.ProviderId] = slice.ProviderName;
                metricCatalog[providerId] = [];
                metricSources[slice.ProviderId] =
                    new Dictionary<string, MetricSource>(StringComparer.Ordinal);
            }
        }

        var layout = savedLayout.ReconcileWithMetricCatalog(catalogProviders, metricCatalog);

        var catalogSet = new HashSet<string>(seen, StringComparer.Ordinal);
        var currentPrefs = new List<ProviderLayoutPreference>();
        foreach (var pref in layout.Providers)
        {
            if (catalogSet.Contains(pref.ProviderId.Value))
            {
                currentPrefs.Add(pref);
            }
        }

        var rows = new List<DashboardProviderLayoutRow>(currentPrefs.Count);
        var cards = new List<ProviderCard>();
        var cardById = new Dictionary<string, ProviderCard>(StringComparer.Ordinal);
        foreach (var card in dashboard.Providers)
        {
            cardById[card.ProviderId] = card;
        }

        for (var i = 0; i < currentPrefs.Count; i++)
        {
            var pref = currentPrefs[i];
            var id = pref.ProviderId.Value;
            var name = nameById[id];
            var automationId = $"DashboardLayout.Provider.{id}";
            ProviderLayoutPreference providerPreference = pref;
            IReadOnlyDictionary<string, MetricSource> providerMetricSources = metricSources[id];
            MetricLayoutPreference[] currentMetrics = providerPreference.Metrics
                .Where(metric => providerMetricSources.ContainsKey(metric.MetricId.Value))
                .ToArray();
            var metricRows = new List<DashboardMetricLayoutRow>(currentMetrics.Length);
            for (var metricIndex = 0; metricIndex < currentMetrics.Length; metricIndex++)
            {
                MetricLayoutPreference metricPreference = currentMetrics[metricIndex];
                MetricSource metricSource = providerMetricSources[metricPreference.MetricId.Value];
                DashboardMetricActionNames metricActionNames = DashboardMetricActionNames.Create(
                    metricSource.Label,
                    metricPreference.IsOnDemand,
                    metricActionNameFormats);

                metricRows.Add(new DashboardMetricLayoutRow(
                    id,
                    metricPreference.MetricId.Value,
                    metricSource.Label,
                    metricPreference.IsOnDemand,
                    metricPreference.IsVisible,
                    metricPreference.IsHighlighted,
                    CanMoveUp: metricIndex > 0,
                    CanMoveDown: metricIndex < currentMetrics.Length - 1,
                    $"{automationId}.Metric.{metricPreference.MetricId.Value}",
                    metricActionNames.SectionLabel,
                    metricActionNames.MoveUp,
                    metricActionNames.MoveDown,
                    metricActionNames.Visibility,
                    metricActionNames.Highlight,
                    metricActionNames.Section));
            }

            string FormatActionName(string format) => string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                format,
                name);
            rows.Add(new DashboardProviderLayoutRow(
                id,
                name,
                pref.IsVisible,
                pref.IsHighlighted,
                CanMoveUp: i > 0,
                CanMoveDown: i < currentPrefs.Count - 1,
                automationId,
                FormatActionName(actionNameFormats.MoveUp),
                FormatActionName(actionNameFormats.MoveDown),
                FormatActionName(actionNameFormats.Visibility),
                FormatActionName(actionNameFormats.Highlight),
                metricRows.AsReadOnly(),
                FormatActionName(actionNameFormats.Metrics),
                ColorHex: pref.ColorHex,
                ColorAutomationName: FormatActionName(actionNameFormats.Color)));

            if (!pref.IsVisible)
            {
                continue;
            }

            if (cardById.TryGetValue(id, out ProviderCard? sourceCard))
            {
                cards.Add(ProjectCard(
                    sourceCard,
                    pref,
                    providerMetricSources,
                    highlightLabel));
            }
        }

        var colorsByProvider = currentPrefs.ToDictionary(
            preference => preference.ProviderId.Value,
            preference => preference.ColorHex,
            StringComparer.Ordinal);
        var spendByProvider = dashboard.SpendSlices.ToDictionary(
            slice => slice.ProviderId,
            StringComparer.Ordinal);
        SpendSlice[] spendSlices = currentPrefs
            .Where(preference => preference.IsVisible
                && spendByProvider.ContainsKey(preference.ProviderId.Value))
            .Select(preference => spendByProvider[preference.ProviderId.Value] with
            {
                ColorHex = colorsByProvider.GetValueOrDefault(preference.ProviderId.Value),
            })
            .ToArray();
        DashboardSpendSummary? visibleSpendSummary = spendSummaryFormatter?.Invoke(spendSlices);
        var projectedDashboard = dashboard with
        {
            Providers = cards.AsReadOnly(),
            SpendSlices = spendSlices,
            TotalSpendAmount = visibleSpendSummary?.TotalAmount ?? dashboard.TotalSpendAmount,
            CompactTotalSpendAmount = visibleSpendSummary?.CompactTotalAmount
                ?? dashboard.CompactTotalSpendAmount,
            SpendAccessibleName = visibleSpendSummary?.AccessibleName
                ?? dashboard.SpendAccessibleName,
        };
        return new DashboardLayoutProjection(
            layout,
            projectedDashboard,
            rows.AsReadOnly());
    }

    public static LocalUsageCard ApplyToLocalUsage(
        LocalUsageCard card,
        DashboardLayout layout)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(layout);

        Dictionary<string, (ProviderLayoutPreference Preference, int Rank)> preferences = layout.Providers
            .Select((preference, rank) => (preference, rank))
            .ToDictionary(
                item => item.preference.ProviderId.Value,
                item => (item.preference, item.rank),
                StringComparer.Ordinal);

        bool IsVisible(string providerId) =>
            !preferences.TryGetValue(providerId, out var entry) || entry.Preference.IsVisible;

        int Rank(string providerId) => preferences.TryGetValue(providerId, out var entry)
            ? entry.Rank
            : int.MaxValue;

        SpendSlice[] slices = card.SpendBreakdown.AgentSlices
            .Select((slice, index) => (slice, index))
            .Where(item => IsVisible(item.slice.ProviderId))
            .OrderBy(item => Rank(item.slice.ProviderId))
            .ThenBy(item => item.index)
            .Select(item => item.slice with
            {
                ColorHex = preferences.TryGetValue(item.slice.ProviderId, out var entry)
                    ? entry.Preference.ColorHex
                    : item.slice.ColorHex,
            })
            .ToArray();
        LocalUsageModelRow[] models = card.SpendBreakdown.Models
            .Select((model, index) => (model, index))
            .Where(item => IsVisible(item.model.AgentId))
            .OrderBy(item => Rank(item.model.AgentId))
            .ThenBy(item => item.index)
            .Select(item => item.model)
            .ToArray();

        return card with
        {
            SpendBreakdown = card.SpendBreakdown with
            {
                AgentSlices = slices,
                Models = models,
            },
        };
    }

    private static (IReadOnlyList<MetricLayoutCatalogEntry> Entries, IReadOnlyDictionary<string, MetricSource> Sources)
        CreateMetricCatalog(ProviderCard card, DashboardSnapshot dashboard)
    {
        if (card.Windows is null || card.Metrics is null || card.SecondaryMetricItems is null)
        {
            throw new ArgumentException("Dashboard metric collections must not be null.", nameof(dashboard));
        }

        var entries = new List<MetricLayoutCatalogEntry>();
        var sources = new Dictionary<string, MetricSource>(StringComparer.Ordinal);

        void Add(string layoutMetricId, string label, QuotaWindow? window, DashboardMetric? metric, bool isOnDemand)
        {
            if (string.IsNullOrWhiteSpace(layoutMetricId))
            {
                throw new ArgumentException(
                    $"Provider '{card.ProviderId}' contains a metric without a layout id.",
                    nameof(dashboard));
            }

            var metricId = new MetricId(layoutMetricId);
            if (!sources.TryAdd(layoutMetricId, new MetricSource(label, window, metric)))
            {
                throw new ArgumentException(
                    $"Duplicate metric id '{layoutMetricId}' for provider '{card.ProviderId}'.",
                    nameof(dashboard));
            }

            entries.Add(new MetricLayoutCatalogEntry(metricId, isOnDemand));
        }

        foreach (QuotaWindow window in card.Windows)
        {
            if (window is null)
            {
                throw new ArgumentException("Dashboard quota collections must not contain null entries.", nameof(dashboard));
            }

            Add(window.LayoutMetricId, window.Title, window, null, isOnDemand: false);
        }

        foreach (DashboardMetric metric in card.Metrics)
        {
            if (metric is null)
            {
                throw new ArgumentException("Dashboard metric collections must not contain null entries.", nameof(dashboard));
            }

            Add(metric.LayoutMetricId, metric.Label, null, metric, isOnDemand: false);
        }

        foreach (DashboardMetric metric in card.SecondaryMetricItems)
        {
            if (metric is null)
            {
                throw new ArgumentException("Dashboard metric collections must not contain null entries.", nameof(dashboard));
            }

            Add(metric.LayoutMetricId, metric.Label, null, metric, isOnDemand: true);
        }

        foreach (QuotaWindow window in card.SecondaryWindowItems)
        {
            if (window is null)
            {
                throw new ArgumentException("Dashboard quota collections must not contain null entries.", nameof(dashboard));
            }

            Add(window.LayoutMetricId, window.Title, window, null, isOnDemand: true);
        }

        return (entries.AsReadOnly(), sources);
    }

    private static ProviderCard ProjectCard(
        ProviderCard source,
        ProviderLayoutPreference preference,
        IReadOnlyDictionary<string, MetricSource> sources,
        string highlightLabel)
    {
        var windows = new List<QuotaWindow>();
        var secondaryWindows = new List<QuotaWindow>();
        var metrics = new List<DashboardMetric>();
        var secondaryMetrics = new List<DashboardMetric>();
        var orderedPrimaryMetrics = new List<DashboardMetricItem>();
        var orderedOnDemandMetrics = new List<DashboardMetricItem>();

        foreach (MetricLayoutPreference metricPreference in preference.Metrics)
        {
            if (!metricPreference.IsVisible
                || !sources.TryGetValue(metricPreference.MetricId.Value, out MetricSource? sourceMetric))
            {
                continue;
            }

            string metricHighlightLabel = metricPreference.IsHighlighted ? highlightLabel : string.Empty;
            if (sourceMetric.Window is { } window)
            {
                QuotaWindow projected = window with
                {
                    IsHighlighted = metricPreference.IsHighlighted,
                    HighlightLabel = metricHighlightLabel,
                };
                (metricPreference.IsOnDemand ? secondaryWindows : windows).Add(projected);
                (metricPreference.IsOnDemand ? orderedOnDemandMetrics : orderedPrimaryMetrics)
                    .Add(DashboardMetricItem.FromWindow(projected));
            }
            else if (sourceMetric.Metric is { } metric)
            {
                DashboardMetric projected = metric with
                {
                    IsHighlighted = metricPreference.IsHighlighted,
                    HighlightLabel = metricHighlightLabel,
                };
                (metricPreference.IsOnDemand ? secondaryMetrics : metrics).Add(projected);
                (metricPreference.IsOnDemand ? orderedOnDemandMetrics : orderedPrimaryMetrics)
                    .Add(DashboardMetricItem.FromMetric(projected));
            }
        }

        return source with
        {
            Windows = windows.AsReadOnly(),
            Metrics = metrics.AsReadOnly(),
            SecondaryMetrics = secondaryMetrics.AsReadOnly(),
            SecondaryWindows = secondaryWindows.AsReadOnly(),
            OrderedPrimaryMetrics = orderedPrimaryMetrics.AsReadOnly(),
            OrderedOnDemandMetrics = orderedOnDemandMetrics.AsReadOnly(),
            IsHighlighted = preference.IsHighlighted,
            HighlightLabel = preference.IsHighlighted ? highlightLabel : string.Empty,
            ProviderColorHex = preference.ColorHex,
        };
    }

    private sealed record MetricSource(
        string Label,
        QuotaWindow? Window,
        DashboardMetric? Metric);
}
