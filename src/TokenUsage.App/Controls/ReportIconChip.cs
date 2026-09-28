using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TokenUsage.App.Controls;

/// <summary>
/// A small tinted square with a glyph that marks what a number is. The tone names a fixed
/// meaning (identity tones for kinds of number, Success/Caution only for a real state), and each
/// tone has its own light, dark, and high-contrast resources. The chip is decoration: the text
/// next to it always carries the meaning, so it is hidden from UI Automation.
/// </summary>
public sealed partial class ReportIconChip : Grid
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(ReportIconChip), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(string), typeof(ReportIconChip), new PropertyMetadata("Blue", OnChanged));

    private static readonly Windows.UI.ViewManagement.AccessibilitySettings Accessibility = new();
    private readonly FontIcon _icon = new() { FontSize = 13 };

    public ReportIconChip()
    {
        Width = 26;
        Height = 26;
        CornerRadius = new CornerRadius(7);
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_icon);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ActualThemeChanged += (_, _) => Apply();
        Loaded += (_, _) => Apply();
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Blue, Violet, Amber, Teal, Rose, Success, or Caution.</summary>
    public string Tone
    {
        get => (string)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ReportIconChip)sender).Apply();

    private void Apply()
    {
        _icon.Glyph = Glyph;
        string theme = Accessibility.HighContrast ? "HighContrast"
            : ActualTheme == ElementTheme.Light ? "Light" : "Dark";
        _icon.Foreground = Lookup($"ReportTone{Tone}Brush", theme) ?? Lookup("ReportToneBlueBrush", theme);
        Background = Lookup($"ReportTone{Tone}SoftBrush", theme) ?? Lookup("ReportToneBlueSoftBrush", theme);
    }

    // Theme dictionaries are read directly so the chip follows the report window's own theme,
    // which can differ from the application theme.
    private static Brush? Lookup(string key, string theme) =>
        Application.Current.Resources.ThemeDictionaries.TryGetValue(theme, out object? dictionary)
        && dictionary is ResourceDictionary resources && resources.TryGetValue(key, out object? value)
            ? value as Brush
            : null;
}
