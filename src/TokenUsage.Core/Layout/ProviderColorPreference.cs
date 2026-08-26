using System.Globalization;

namespace TokenUsage.Core.Layout;

public static class ProviderColorPreference
{
    public static string? Normalize(string? colorHex)
    {
        if (colorHex is null)
        {
            return null;
        }

        string value = colorHex.Trim();
        if (value.Length != 7 || value[0] != '#')
        {
            throw new ArgumentException(
                "Provider color must use #RRGGBB format.",
                nameof(colorHex));
        }

        if (!uint.TryParse(
            value.AsSpan(1),
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out _))
        {
            throw new ArgumentException(
                "Provider color must use #RRGGBB format.",
                nameof(colorHex));
        }

        return value.ToUpperInvariant();
    }

    /// <summary>
    /// The color a provider uses when the person has not picked one. Brand colors come first,
    /// and any other provider gets a color derived from its ID so the same tool keeps the same
    /// color across runs and machines.
    /// </summary>
    public static string Resolve(string providerId, string? customColorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return Normalize(customColorHex)
            ?? BrandColors.GetValueOrDefault(providerId)
            ?? DerivedColors[(int)(HashOf(providerId) % (uint)DerivedColors.Length)];
    }

    private static readonly Dictionary<string, string> BrandColors = new(StringComparer.Ordinal)
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
        ["zcode"] = "#4E6BFF",
    };

    private static readonly string[] DerivedColors =
    [
        "#14B8A6", "#F97316", "#A855F7", "#06B6D4",
        "#84CC16", "#EC4899", "#EAB308", "#8B5CF6",
        "#22C55E", "#EF4444", "#3B82F6", "#D946EF",
    ];

    private static uint HashOf(string providerId)
    {
        uint hash = 2166136261;
        foreach (char character in providerId)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return hash;
    }
}
