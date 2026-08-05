using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace TokenUsage.App.Controls;

public sealed partial class ProviderColorSwatch : UserControl
{
    private readonly AccessibilitySettings _accessibilitySettings = new();

    public static readonly DependencyProperty ColorHexProperty =
        DependencyProperty.Register(
            nameof(ColorHex),
            typeof(string),
            typeof(ProviderColorSwatch),
            new PropertyMetadata(null, OnAppearanceChanged));

    public static readonly DependencyProperty ProviderIdProperty =
        DependencyProperty.Register(
            nameof(ProviderId),
            typeof(string),
            typeof(ProviderColorSwatch),
            new PropertyMetadata(string.Empty, OnAppearanceChanged));

    public ProviderColorSwatch()
    {
        InitializeComponent();
        ActualThemeChanged += OnActualThemeChanged;
        Loaded += OnLoaded;
    }

    public string? ColorHex
    {
        get => (string?)GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    public string ProviderId
    {
        get => (string)GetValue(ProviderIdProperty);
        set => SetValue(ProviderIdProperty, value);
    }

    private static void OnAppearanceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((ProviderColorSwatch)dependencyObject).UpdateFill();

    private void OnActualThemeChanged(FrameworkElement sender, object args) => UpdateFill();

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateFill();

    private void UpdateFill()
    {
        if (Swatch is null)
        {
            return;
        }

        if (!_accessibilitySettings.HighContrast && !string.IsNullOrWhiteSpace(ColorHex))
        {
            Swatch.Fill = ProviderColorPalette.CreateGradient(ColorHex);
            return;
        }

        Swatch.Fill = ProviderId switch
        {
            "antigravity" => AntigravityBrushProxy.Background,
            "claude" => ClaudeBrushProxy.Background,
            "codex" => CodexBrushProxy.Background,
            "grok" => GrokBrushProxy.Background,
            "opencode" => OpenCodeBrushProxy.Background,
            _ => FallbackBrushProxy.Background,
        };
    }
}
