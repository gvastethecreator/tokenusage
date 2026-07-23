namespace WOpenUsage.Platform.Windows.Tray;

internal static class TrayIconRecoveryPolicy
{
    internal static bool ShouldRecover(
        uint message,
        uint taskbarCreatedMessage,
        bool disposed)
    {
        return !disposed
            && taskbarCreatedMessage != 0
            && message == taskbarCreatedMessage;
    }
}
