using System.Runtime.InteropServices;
using WOpenUsage.Platform.Windows.Native;

namespace WOpenUsage.Platform.Windows.Windowing;

public static class WindowBorderStyle
{
    public static bool TryRemoveNonClientFrame(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        nint style = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlStyle);
        if (style == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            return false;
        }

        nint borderlessStyle = (nint)(style.ToInt64() & ~NativeMethods.NonClientFrameStyleMask);
        if (borderlessStyle != style)
        {
            Marshal.SetLastPInvokeError(0);
            nint previousStyle = NativeMethods.SetWindowLongPtr(
                windowHandle,
                NativeMethods.GwlStyle,
                borderlessStyle);
            if (previousStyle == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                return false;
            }
        }

        return NativeMethods.SetWindowPos(
            windowHandle,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoSize
                | NativeMethods.SwpNoMove
                | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate
                | NativeMethods.SwpFrameChanged);
    }

    public static bool TryMatchSystemBorder(
        nint windowHandle,
        byte red,
        byte green,
        byte blue)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        uint borderColor = red | ((uint)green << 8) | ((uint)blue << 16);
        int result = NativeMethods.DwmSetWindowAttribute(
            windowHandle,
            NativeMethods.DwmWindowAttributeBorderColor,
            ref borderColor,
            sizeof(uint));

        return result >= 0;
    }

    public static bool TryRestoreAccessibleFrame(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        nint style = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlStyle);
        if (style == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            return false;
        }

        nint accessibleStyle = (nint)(style.ToInt64() | NativeMethods.AccessibleNonClientFrameStyle);
        if (accessibleStyle != style)
        {
            Marshal.SetLastPInvokeError(0);
            nint previousStyle = NativeMethods.SetWindowLongPtr(
                windowHandle,
                NativeMethods.GwlStyle,
                accessibleStyle);
            if (previousStyle == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                return false;
            }
        }

        return NativeMethods.SetWindowPos(
            windowHandle,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoSize
                | NativeMethods.SwpNoMove
                | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate
                | NativeMethods.SwpFrameChanged);
    }

}
