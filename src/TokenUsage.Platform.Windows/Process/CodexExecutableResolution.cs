namespace TokenUsage.Platform.Windows.Processes;

public abstract record CodexExecutableResolution
{
    private CodexExecutableResolution()
    {
    }

    public sealed record Resolved : CodexExecutableResolution
    {
        internal Resolved(string executablePath)
        {
            ExecutablePath = executablePath;
        }

        public string ExecutablePath { get; }
    }

    public sealed record Missing : CodexExecutableResolution;

    public sealed record InvalidOverride : CodexExecutableResolution;
}
