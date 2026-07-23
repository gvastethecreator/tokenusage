namespace WOpenUsage.Cli;

internal enum CliOutputFormat
{
    Human,
    Json,
}

internal static class FormatOnlyCommandParser
{
    internal static bool TryParse(
        IReadOnlyList<string> arguments,
        out CliOutputFormat format,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        format = CliOutputFormat.Human;
        error = string.Empty;
        bool hasFormat = false;

        for (int index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], "--format", StringComparison.Ordinal))
            {
                error = "Unknown command argument.";
                return false;
            }

            if (hasFormat)
            {
                error = "Option '--format' can be set only once.";
                return false;
            }

            if (++index >= arguments.Count || !TryParseFormat(arguments[index], out format))
            {
                error = "Option '--format' must be 'human' or 'json'.";
                return false;
            }

            hasFormat = true;
        }

        return true;
    }

    private static bool TryParseFormat(string value, out CliOutputFormat format)
    {
        if (string.Equals(value, "human", StringComparison.Ordinal))
        {
            format = CliOutputFormat.Human;
            return true;
        }

        if (string.Equals(value, "json", StringComparison.Ordinal))
        {
            format = CliOutputFormat.Json;
            return true;
        }

        format = default;
        return false;
    }
}
