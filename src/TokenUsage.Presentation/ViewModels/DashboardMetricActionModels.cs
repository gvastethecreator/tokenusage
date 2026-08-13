using System.Globalization;

namespace TokenUsage.App.ViewModels;

public sealed record DashboardMetricActionNameFormats(
    string MoveUp,
    string MoveDown,
    string Visibility,
    string Highlight,
    string AlwaysVisibleSection,
    string OnDemandSection,
    string MoveToAlwaysVisible,
    string MoveToOnDemand)
{
    public static DashboardMetricActionNameFormats English { get; } = new(
        "Move {0} up",
        "Move {0} down",
        "Show or hide {0}",
        "Highlight {0}",
        "Always visible",
        "On demand",
        "Always show {0}",
        "Show {0} on demand");
}
public sealed record DashboardMetricActionNames(
    string SectionLabel,
    string MoveUp,
    string MoveDown,
    string Visibility,
    string Highlight,
    string Section)
{
    public static DashboardMetricActionNames Create(
        string label,
        bool isOnDemand,
        DashboardMetricActionNameFormats? formats = null)
    {
        ArgumentNullException.ThrowIfNull(label);

        formats ??= DashboardMetricActionNameFormats.English;
        var culture = CultureInfo.CurrentCulture;

        var sectionLabel = isOnDemand
            ? formats.OnDemandSection
            : formats.AlwaysVisibleSection;

        var section = isOnDemand
            ? string.Format(culture, formats.MoveToAlwaysVisible, label)
            : string.Format(culture, formats.MoveToOnDemand, label);

        return new DashboardMetricActionNames(
            sectionLabel,
            string.Format(culture, formats.MoveUp, label),
            string.Format(culture, formats.MoveDown, label),
            string.Format(culture, formats.Visibility, label),
            string.Format(culture, formats.Highlight, label),
            section);
    }
}
