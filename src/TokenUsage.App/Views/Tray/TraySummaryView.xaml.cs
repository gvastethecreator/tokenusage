using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels.Tray;
using TokenUsage.Core.Appearance;

namespace TokenUsage.App.Views.Tray;

public sealed partial class TraySummaryView : UserControl
{
    private const double ItemWidthDips = 76d;
    private const double EmptyWidthDips = 186d;
    private const double SingleValueHeightDips = 42d;
    private const double ValueLineHeightDips = 18d;
    private const double NameLineHeightDips = 12d;

    private bool _showsProviderName;

    public TraySummaryView()
    {
        InitializeComponent();
    }

    public ObservableCollection<TrayProviderSummary> Items { get; } = [];

    public int ItemCount => Items.Count;

    /// <summary>
    /// Content width the popover window needs. An empty strip still needs room for the
    /// "no provider detected" message.
    /// </summary>
    public double ContentWidthDips => Items.Count == 0
        ? EmptyWidthDips
        : Items.Count * ItemWidthDips;

    /// <summary>
    /// Content height the popover window needs. Height follows how many lines each
    /// provider shows, so a configured popover is never clipped.
    /// </summary>
    public double ContentHeightDips => SingleValueHeightDips
        + (Items.Any(item => item.HasSecondaryValue) ? ValueLineHeightDips : 0d)
        + (_showsProviderName ? NameLineHeightDips : 0d);

    public void Apply(
        IReadOnlyList<TrayProviderSummary> items,
        AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(appearance);
        RequestedTheme = appearance.Theme switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        Items.Clear();
        foreach (TrayProviderSummary item in items.Take(TrayPopoverSettings.MaxProviderCount))
        {
            Items.Add(item);
        }

        _showsProviderName = appearance.TrayPopover.ShowProviderName;
        bool hasProviders = Items.Count > 0;
        ProviderStrip.Visibility = hasProviders ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasProviders ? Visibility.Collapsed : Visibility.Visible;
    }
}
