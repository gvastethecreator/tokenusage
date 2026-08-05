namespace TokenUsage.Platform.Windows.Processes;

public enum CodexAppServerProcessError
{
    InvalidExecutable,
    JobSetupFailed,
    StartFailed,
    ShutdownFailed,
}

public sealed class CodexAppServerProcessException : Exception
{
    internal CodexAppServerProcessException(
        CodexAppServerProcessError error,
        string message,
        int? nativeErrorCode = null)
        : base(message)
    {
        Error = error;
        NativeErrorCode = nativeErrorCode;
    }

    public CodexAppServerProcessError Error { get; }

    public int? NativeErrorCode { get; }
}
