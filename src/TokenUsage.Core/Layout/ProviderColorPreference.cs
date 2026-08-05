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
}
