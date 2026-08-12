using System.Runtime.InteropServices;
using TokenUsage.Platform.Windows.Native;

namespace TokenUsage.Platform.Windows.Windowing;

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

    public static bool TryHideSystemBorder(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        uint borderColor = NativeMethods.DwmColorNone;
        int result = NativeMethods.DwmSetWindowAttribute(
            windowHandle,
            NativeMethods.DwmWindowAttributeBorderColor,
            ref borderColor,
            sizeof(uint));

        return result >= 0;
    }

    public static bool TryUseSmallRoundedCorners(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        uint preference = NativeMethods.DwmWindowCornerPreferenceRoundSmall;
        int result = NativeMethods.DwmSetWindowAttribute(
            windowHandle,
            NativeMethods.DwmWindowAttributeCornerPreference,
            ref preference,
            sizeof(uint));
        return result >= 0;
    }

    public static bool TryClipRoundedCorners(nint windowHandle, double radiusDips)
    {
        if (windowHandle == 0
            || !double.IsFinite(radiusDips)
            || radiusDips <= 0
            || !NativeMethods.GetClientRect(windowHandle, out var bounds))
        {
            return false;
        }

        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        uint dpi = NativeMethods.GetDpiForWindow(windowHandle);
        double effectiveDpi = dpi == 0 ? 96d : dpi;
        int diameter = Math.Max(
            2,
            (int)Math.Round(
                radiusDips * 2d * effectiveDpi / 96d,
                MidpointRounding.AwayFromZero));
        nint region = NativeMethods.CreateRoundRectRgn(
            0,
            0,
            width + 1,
            height + 1,
            diameter,
            diameter);
        if (region == 0)
        {
            return false;
        }

        if (NativeMethods.SetWindowRgn(windowHandle, region, redraw: true) != 0)
        {
            // Windows owns the region after a successful SetWindowRgn call.
            return true;
        }

        _ = NativeMethods.DeleteObject(region);
        return false;
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
