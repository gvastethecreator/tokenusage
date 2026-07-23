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
    string HighlightAutomationName)
{
    public string MoveUpAutomationId => $"{AutomationId}.MoveUp";
    public string MoveDownAutomationId => $"{AutomationId}.MoveDown";
    public string VisibilityAutomationId => $"{AutomationId}.Visibility";
    public string HighlightAutomationId => $"{AutomationId}.Highlight";
}

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
        }

        var emptyMetrics = new Dictionary<ProviderId, IReadOnlyList<MetricId>>();
        foreach (var id in catalogProviders)
        {
            emptyMetrics[id] = Array.Empty<MetricId>();
        }

        var layout = savedLayout.Reconcile(catalogProviders, emptyMetrics);

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
                FormatActionName(actionNameFormats.Highlight)));

            if (!pref.IsVisible)
            {
                continue;
            }

            var source = cardById[id];
            cards.Add(source with
            {
                IsHighlighted = pref.IsHighlighted,
                HighlightLabel = pref.IsHighlighted ? highlightLabel : string.Empty,
            });
        }

        var projectedDashboard = dashboard with { Providers = cards.AsReadOnly() };
        return new DashboardLayoutProjection(
            layout,
            projectedDashboard,
            rows.AsReadOnly());
    }
}
