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
            ["amp"] = "#F34E3F",
            ["claude"] = "#DE7356",
            ["codex"] = "#10A37F",
            ["copilot"] = "#8B5CF6",
            ["cursor"] = "#D7D7D7",
            ["devin"] = "#7C3AED",
            ["grok"] = "#7C5CFC",
            ["goose"] = "#06B6D4",
            ["hermes"] = "#D97706",
            ["mux"] = "#F59E0B",
            ["opencode"] = "#E5488C",
            ["openrouter"] = "#6366F1",
            ["vercel-ai-gateway"] = "#6B7280",
            ["zai"] = "#2D5BFF",
        };

    public static string GetEffectiveHex(string providerId, string? customColorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return ProviderColorPreference.Normalize(customColorHex)
            ?? Defaults.GetValueOrDefault(providerId)
            ?? CreateStableFallback(providerId);
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

    private static string CreateStableFallback(string providerId)
    {
        string[] colors =
        [
            "#14B8A6", "#F97316", "#A855F7", "#06B6D4",
            "#84CC16", "#EC4899", "#EAB308", "#8B5CF6",
            "#22C55E", "#EF4444", "#3B82F6", "#D946EF",
        ];
        uint hash = 2166136261;
        foreach (char character in providerId)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return colors[(int)(hash % (uint)colors.Length)];
    }
}
