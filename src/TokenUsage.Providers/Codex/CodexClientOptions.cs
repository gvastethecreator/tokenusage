namespace TokenUsage.Providers.Codex;

public sealed class CodexClientOptions
{
    public const int DefaultMaximumLineBytes = 256 * 1024;

    public CodexClientOptions(
        string clientName,
        string clientVersion,
        string? clientTitle = null,
        TimeSpan? requestTimeout = null,
        int maximumLineBytes = DefaultMaximumLineBytes)
    {
        ClientName = RequireIdentifier(clientName, nameof(clientName));
        ClientVersion = RequireIdentifier(clientVersion, nameof(clientVersion));
        ClientTitle = RequireOptionalTitle(clientTitle, nameof(clientTitle));

        RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestTimeout),
                "Request timeout must be between zero and five minutes.");
        }

        if (maximumLineBytes is < 256 or > 4 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumLineBytes),
                "Maximum line length must be between 256 bytes and 4 MiB.");
        }

        MaximumLineBytes = maximumLineBytes;
    }

    public string ClientName { get; }

    public string ClientVersion { get; }

    public string? ClientTitle { get; }

    public TimeSpan RequestTimeout { get; }

    public int MaximumLineBytes { get; }

    private static string RequireIdentifier(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        if (value.Length > 64
            || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                "Client identifiers must use 1-64 ASCII letters, digits, dots, dashes, or underscores.",
                paramName);
        }

        return value;
    }

    private static string? RequireOptionalTitle(string? value, string paramName)
    {
        if (value is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        if (value.Length > 64 || value.Any(character => char.IsControl(character)))
        {
            throw new ArgumentException(
                "Client title must contain 1-64 printable characters.",
                paramName);
        }

        return value;
    }
}
