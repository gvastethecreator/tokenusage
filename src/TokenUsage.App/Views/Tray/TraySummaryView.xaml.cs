using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels.Tray;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Layout;

namespace TokenUsage.App.Views.Tray;

public sealed partial class TraySummaryView : UserControl
{
    public TraySummaryView()
    {
        InitializeComponent();
    }

    public ObservableCollection<TrayProviderSummary> Items { get; } = [];

    public int ItemCount => Items.Count;

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
        foreach (TrayProviderSummary item in items.Take(DashboardLayout.MaxHighlightedProviders))
        {
            Items.Add(item);
        }
    }
}
