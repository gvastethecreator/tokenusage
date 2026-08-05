using TokenUsage.Platform.Windows.Native;

namespace TokenUsage.Platform.Windows.Windowing;

public static class ForegroundWindowActivator
{
    public static bool TryActivate(nint windowHandle) =>
        windowHandle != 0 && NativeMethods.SetForegroundWindow(windowHandle);
}
