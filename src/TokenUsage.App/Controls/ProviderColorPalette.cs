using Microsoft.UI.Xaml.Media;
using Windows.UI;
using TokenUsage.Core.Layout;

namespace TokenUsage.App.Controls;

public static class ProviderColorPalette
{
    public static string GetEffectiveHex(string providerId, string? customColorHex) =>
        ProviderColorPreference.Resolve(providerId, customColorHex);

    public static Color Parse(string colorHex)
    {
        string normalized = ProviderColorPreference.Normalize(colorHex)!;
        return Color.FromArgb(
            255,
            Convert.ToByte(normalized.Substring(1, 2), 16),
            Convert.ToByte(normalized.Substring(3, 2), 16),
            Convert.ToByte(normalized.Substring(5, 2), 16));
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static LinearGradientBrush CreateGradient(string colorHex)
    {
        Color color = Parse(colorHex);
        Color dark = Color.FromArgb(
            255,
            (byte)Math.Round(color.R * 0.58),
            (byte)Math.Round(color.G * 0.58),
            (byte)Math.Round(color.B * 0.58));
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = dark, Offset = 0 },
                new GradientStop { Color = color, Offset = 1 },
            },
        };
    }

}
