using System.Runtime.InteropServices;
using TokenUsage.Platform.Windows.Native;
using TokenUsage.Platform.Windows.Placement;

namespace TokenUsage.Platform.Windows.Windowing;

public static class NonActivatingWindowStyle
{
    public static bool TryApply(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        nint current = NativeMethods.GetWindowLongPtr(
            windowHandle,
            NativeMethods.GwlExtendedStyle);
        if (current == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            return false;
        }

        nint required = AddRequiredStyles(current);
        if (required == current)
        {
            return true;
        }

        Marshal.SetLastPInvokeError(0);
        nint previous = NativeMethods.SetWindowLongPtr(
            windowHandle,
            NativeMethods.GwlExtendedStyle,
            required);
        return previous != 0 || Marshal.GetLastPInvokeError() == 0;
    }

    public static bool TryShowAt(nint windowHandle, PlatformRect bounds)
    {
        if (windowHandle == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        return NativeMethods.SetWindowPos(
            windowHandle,
            NativeMethods.HwndTopmost,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    internal static nint AddRequiredStyles(nint current) =>
        current | (nint)(NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
}
