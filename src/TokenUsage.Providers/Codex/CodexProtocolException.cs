namespace TokenUsage.Providers.Codex;

public class CodexProtocolException : Exception
{
    public CodexProtocolException(string message)
        : base(message)
    {
    }
}

public sealed class CodexRequestTimeoutException : CodexProtocolException
{
    public CodexRequestTimeoutException()
        : base("Codex app-server did not answer before the request timeout.")
    {
    }
}

public sealed class CodexRpcException : CodexProtocolException
{
    public CodexRpcException(long? code)
        : base("Codex app-server rejected the request.")
    {
        Code = code;
    }

    public long? Code { get; }
}
