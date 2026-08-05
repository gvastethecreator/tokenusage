using WOpenUsage.Platform.Windows.Native;

namespace WOpenUsage.Platform.Windows.Windowing;

public static class ForegroundWindowActivator
{
    public static bool TryActivate(nint windowHandle) =>
        windowHandle != 0 && NativeMethods.SetForegroundWindow(windowHandle);
}
