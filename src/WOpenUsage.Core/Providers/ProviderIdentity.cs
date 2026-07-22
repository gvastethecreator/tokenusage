namespace WOpenUsage.Core.Providers;

public sealed record ProviderId
{
    public ProviderId(string value)
    {
        Value = StableId.Validate(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record MetricId
{
    public MetricId(string value)
    {
        Value = StableId.Validate(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

internal static class StableId
{
    public static string Validate(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Stable IDs cannot start or end with whitespace.", paramName);
        }

        if (!IsAsciiLetterOrDigit(value[0]) || !IsAsciiLetterOrDigit(value[^1]))
        {
            throw new ArgumentException(
                "Stable IDs must start and end with a lowercase ASCII letter or digit.",
                paramName);
        }

        bool previousWasSeparator = false;
        foreach (char character in value)
        {
            bool isSeparator = character is '-' or '.';
            if (!IsAsciiLetterOrDigit(character) && !isSeparator)
            {
                throw new ArgumentException(
                    "Stable IDs may contain lowercase ASCII letters, digits, hyphens, and periods.",
                    paramName);
            }

            if (isSeparator && previousWasSeparator)
            {
                throw new ArgumentException("Stable ID separators cannot repeat.", paramName);
            }

            previousWasSeparator = isSeparator;
        }

        return value;
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';
}
