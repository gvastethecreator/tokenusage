using System.Globalization;

namespace TokenUsage.Cli;

/// <summary>
/// Cost text for command output. This is part of the CLI contract another program reads, so it
/// stays invariant and fixed: six decimal places, and the word "unavailable" when no cost was
/// observed. It is deliberately separate from the localized money text the app shows.
/// </summary>
internal static class CliCostText
{
    internal const string Unavailable = "unavailable";

    internal static string Format(decimal? value) => value is null
        ? Unavailable
        : value.Value.ToString("0.######", CultureInfo.InvariantCulture);
}
