using TokenUsage.Platform.Windows.Native;

namespace TokenUsage.Platform.Windows.Windowing;

public static class ForegroundWindowInspector
{
    public static bool IsForeground(nint windowHandle) =>
        windowHandle != 0 && NativeMethods.GetForegroundWindow() == windowHandle;
}
