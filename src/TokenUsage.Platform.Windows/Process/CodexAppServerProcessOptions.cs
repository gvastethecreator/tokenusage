namespace TokenUsage.Platform.Windows.Processes;

public sealed class CodexAppServerProcessOptions
{
    public CodexAppServerProcessOptions(
        TimeSpan? gracefulShutdownTimeout = null,
        TimeSpan? forcedShutdownTimeout = null,
        int maximumDiagnosticCharacters = 4096,
        string? codexHome = null)
    {
        GracefulShutdownTimeout = ValidateTimeout(
            gracefulShutdownTimeout ?? TimeSpan.FromMilliseconds(500),
            nameof(gracefulShutdownTimeout));
        ForcedShutdownTimeout = ValidateTimeout(
            forcedShutdownTimeout ?? TimeSpan.FromSeconds(2),
            nameof(forcedShutdownTimeout));

        if (maximumDiagnosticCharacters is < 256 or > 64 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDiagnosticCharacters),
                "Diagnostic capacity must be between 256 and 65536 characters.");
        }

        MaximumDiagnosticCharacters = maximumDiagnosticCharacters;
        if (codexHome is not null && (!Path.IsPathFullyQualified(codexHome) || !Directory.Exists(codexHome)))
            throw new ArgumentException("The account home must be an existing absolute directory.", nameof(codexHome));
        CodexHome = codexHome;
    }

    public TimeSpan GracefulShutdownTimeout { get; }

    public TimeSpan ForcedShutdownTimeout { get; }

    public int MaximumDiagnosticCharacters { get; }

    public string? CodexHome { get; }

    private static TimeSpan ValidateTimeout(TimeSpan value, string paramName)
    {
        if (value < TimeSpan.FromMilliseconds(10) || value > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                "Process shutdown timeouts must be between 10 milliseconds and 30 seconds.");
        }

        return value;
    }
}
