namespace TokenUsage.Providers.Copilot;

public static class CopilotAccountName
{
    public const int MaximumLength = 39;

    public static string Validate(string? value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The GitHub account name must not be empty.",
                parameterName);
        }

        string trimmed = value.Trim();
        if (!IsValid(trimmed))
        {
            throw new ArgumentException(
                "The GitHub account name is not a public login.",
                parameterName);
        }

        return trimmed;
    }

    public static bool IsValid(string value)
    {
        if (value.Length is < 1 or > MaximumLength)
        {
            return false;
        }

        if (value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        foreach (char character in value)
        {
            if (character is not (>= 'A' and <= 'Z')
                and not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-')
            {
                return false;
            }
        }

        return true;
    }
}
