namespace TokenUsage.Providers.Codex;

public sealed class CodexClientUnavailableException : Exception
{
    public CodexClientUnavailableException()
        : base("Codex app-server is unavailable.")
    {
    }
}
