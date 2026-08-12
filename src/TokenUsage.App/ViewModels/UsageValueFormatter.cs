using System.Globalization;

namespace TokenUsage.App.ViewModels;

/// <summary>
/// Shared number text for usage surfaces. The dashboard, the report, and the tray all shorten
/// large token counts and money the same way; three copies of these rules used to drift, so a
/// chart and its legend could round the same number differently.
/// </summary>
public static class UsageValueFormatter
{
    /// <summary>
    /// Token counts, shortened past a thousand so a long number never pushes a card wider.
    /// </summary>
    public static string CompactTokens(double value)
    {
        double absolute = Math.Abs(value);
        return absolute switch
        {
            >= 1_000_000_000 => string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.##}B",
                value / 1_000_000_000),
            >= 1_000_000 => string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.#}M",
                value / 1_000_000),
            >= 1_000 => string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.#}K",
                value / 1_000),
            _ => string.Format(CultureInfo.CurrentCulture, "{0:N0}", value),
        };
    }

    /// <summary>
    /// Money for a tight space: whole dollars once the amount reaches a thousand, cents below
    /// it. This is display text, never a value another program parses.
    /// </summary>
    public static string CompactUsd(double amount) => amount >= 1_000
        ? string.Format(CultureInfo.CurrentCulture, "${0:N0}", amount)
        : string.Format(CultureInfo.CurrentCulture, "${0:0.##}", amount);

    /// <summary>
    /// Money in the localized currency layout the resource file defines.
    /// </summary>
    public static string Usd(decimal amount, Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(getString);
        return string.Format(
            CultureInfo.CurrentCulture,
            getString("LocalUsageUsdFormat"),
            amount);
    }

    /// <summary>
    /// A whole count with digit grouping, for a place with room for every digit.
    /// </summary>
    public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// A percentage sign after a value that already counts in percent units, so 50m reads
    /// as "50%". A share between zero and one has to be scaled by the caller.
    /// </summary>
    public static string PercentText(decimal percentValue) => string.Format(
        CultureInfo.CurrentCulture,
        "{0:0.#}%",
        percentValue);
}
