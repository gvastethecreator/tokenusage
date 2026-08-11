using Microsoft.UI.Xaml.Media;
using Windows.UI;
using TokenUsage.Core.Layout;

namespace TokenUsage.App.Controls;

public static class ProviderColorPalette
{
    private static readonly IReadOnlyDictionary<string, string> Defaults =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["antigravity"] = "#4285F4",
            ["claude"] = "#DE7356",
            ["codex"] = "#10A37F",
            ["cursor"] = "#D7D7D7",
            ["grok"] = "#7C5CFC",
            ["opencode"] = "#E5488C",
            ["vercel-ai-gateway"] = "#6B7280",
        };

    public static string GetEffectiveHex(string providerId, string? customColorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return ProviderColorPreference.Normalize(customColorHex)
            ?? Defaults.GetValueOrDefault(providerId)
            ?? "#6B7280";
    }

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
