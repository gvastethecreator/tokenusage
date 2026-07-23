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
    IReadOnlyList<DashboardMetricLayoutRow> Metrics)
{
    public string MoveUpAutomationId => $"{AutomationId}.MoveUp";
    public string MoveDownAutomationId => $"{AutomationId}.MoveDown";
    public string VisibilityAutomationId => $"{AutomationId}.Visibility";
    public string HighlightAutomationId => $"{AutomationId}.Highlight";
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
    string AutomationId);

public sealed record DashboardProviderActionNameFormats(
    string MoveUp,
    string MoveDown,
    string Visibility,
    string Highlight)
{
    public static DashboardProviderActionNameFormats English { get; } = new(
        "Move {0} up",
        "Move {0} down",
        "Show or hide {0}",
        "Highlight {0}");
}

public sealed record DashboardLayoutProjection(
    DashboardLayout Layout,
    SampleDashboardSnapshot Dashboard,
    IReadOnlyList<DashboardProviderLayoutRow> Providers);

public static class DashboardLayoutProjector
{
    public static DashboardLayoutProjection Apply(
        SampleDashboardSnapshot dashboard,
        DashboardLayout savedLayout,
        string highlightLabel,
        DashboardProviderActionNameFormats? actionNameFormats = null)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(savedLayout);
        ArgumentNullException.ThrowIfNull(highlightLabel);
        actionNameFormats ??= DashboardProviderActionNameFormats.English;
        if (dashboard.Providers is null)
        {
            throw new ArgumentException(
                "Dashboard providers must not be null.",
                nameof(dashboard));
        }

        var catalogProviders = new List<ProviderId>(dashboard.Providers.Count);
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
        var cards = new List<SampleProviderCard>();
        var cardById = new Dictionary<string, SampleProviderCard>(StringComparer.Ordinal);
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

                metricRows.Add(new DashboardMetricLayoutRow(
                    id,
                    metricPreference.MetricId.Value,
                    metricSource.Label,
                    metricPreference.IsOnDemand,
                    metricPreference.IsVisible,
                    metricPreference.IsHighlighted,
                    CanMoveUp: metricIndex > 0,
                    CanMoveDown: metricIndex < currentMetrics.Length - 1,
                    $"{automationId}.Metric.{metricPreference.MetricId.Value}"));
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
                metricRows.AsReadOnly()));

            if (!pref.IsVisible)
            {
                continue;
            }

            cards.Add(ProjectCard(
                cardById[id],
                pref,
                providerMetricSources,
                highlightLabel));
        }

        var projectedDashboard = dashboard with { Providers = cards.AsReadOnly() };
        return new DashboardLayoutProjection(
            layout,
            projectedDashboard,
            rows.AsReadOnly());
    }

    private static (IReadOnlyList<MetricLayoutCatalogEntry> Entries, IReadOnlyDictionary<string, MetricSource> Sources)
        CreateMetricCatalog(SampleProviderCard card, SampleDashboardSnapshot dashboard)
    {
        if (card.Windows is null || card.Metrics is null || card.SecondaryMetricItems is null)
        {
            throw new ArgumentException("Dashboard metric collections must not be null.", nameof(dashboard));
        }

        var entries = new List<MetricLayoutCatalogEntry>();
        var sources = new Dictionary<string, MetricSource>(StringComparer.Ordinal);

        void Add(string layoutMetricId, string label, SampleQuotaWindow? window, SampleMetric? metric, bool isOnDemand)
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

        foreach (SampleQuotaWindow window in card.Windows)
        {
            if (window is null)
            {
                throw new ArgumentException("Dashboard quota collections must not contain null entries.", nameof(dashboard));
            }

            Add(window.LayoutMetricId, window.Title, window, null, isOnDemand: false);
        }

        foreach (SampleMetric metric in card.Metrics)
        {
            if (metric is null)
            {
                throw new ArgumentException("Dashboard metric collections must not contain null entries.", nameof(dashboard));
            }

            Add(metric.LayoutMetricId, metric.Label, null, metric, isOnDemand: false);
        }

        foreach (SampleMetric metric in card.SecondaryMetricItems)
        {
            if (metric is null)
            {
                throw new ArgumentException("Dashboard metric collections must not contain null entries.", nameof(dashboard));
            }

            Add(metric.LayoutMetricId, metric.Label, null, metric, isOnDemand: true);
        }

        foreach (SampleQuotaWindow window in card.SecondaryWindowItems)
        {
            if (window is null)
            {
                throw new ArgumentException("Dashboard quota collections must not contain null entries.", nameof(dashboard));
            }

            Add(window.LayoutMetricId, window.Title, window, null, isOnDemand: true);
        }

        return (entries.AsReadOnly(), sources);
    }

    private static SampleProviderCard ProjectCard(
        SampleProviderCard source,
        ProviderLayoutPreference preference,
        IReadOnlyDictionary<string, MetricSource> sources,
        string highlightLabel)
    {
        var windows = new List<SampleQuotaWindow>();
        var secondaryWindows = new List<SampleQuotaWindow>();
        var metrics = new List<SampleMetric>();
        var secondaryMetrics = new List<SampleMetric>();
        var orderedPrimaryMetrics = new List<SampleDashboardMetricItem>();
        var orderedOnDemandMetrics = new List<SampleDashboardMetricItem>();

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
                SampleQuotaWindow projected = window with
                {
                    IsHighlighted = metricPreference.IsHighlighted,
                    HighlightLabel = metricHighlightLabel,
                };
                (metricPreference.IsOnDemand ? secondaryWindows : windows).Add(projected);
                (metricPreference.IsOnDemand ? orderedOnDemandMetrics : orderedPrimaryMetrics)
                    .Add(SampleDashboardMetricItem.FromWindow(projected));
            }
            else if (sourceMetric.Metric is { } metric)
            {
                SampleMetric projected = metric with
                {
                    IsHighlighted = metricPreference.IsHighlighted,
                    HighlightLabel = metricHighlightLabel,
                };
                (metricPreference.IsOnDemand ? secondaryMetrics : metrics).Add(projected);
                (metricPreference.IsOnDemand ? orderedOnDemandMetrics : orderedPrimaryMetrics)
                    .Add(SampleDashboardMetricItem.FromMetric(projected));
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
        };
    }

    private sealed record MetricSource(
        string Label,
        SampleQuotaWindow? Window,
        SampleMetric? Metric);
}
