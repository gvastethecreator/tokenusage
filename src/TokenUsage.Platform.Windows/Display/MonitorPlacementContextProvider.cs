using System.ComponentModel;
using System.Runtime.InteropServices;
using WOpenUsage.Platform.Windows.Native;
using WOpenUsage.Platform.Windows.Placement;

namespace WOpenUsage.Platform.Windows.Display;

public static class MonitorPlacementContextProvider
{
    private const uint DefaultDpi = 96;

    public static MonitorPlacementContext Resolve(PlatformRect? trayIconBounds)
    {
        if (trayIconBounds is { } icon
            && icon.Width > 0
            && icon.Height > 0
            && TryResolveFromRect(icon, out var iconContext))
        {
            return iconContext;
        }

        if (NativeMethods.GetCursorPos(out var cursor))
        {
            var cursorPoint = new PlatformPoint(cursor.X, cursor.Y);
            if (TryResolveFromPoint(cursorPoint, out var cursorContext))
            {
                return cursorContext;
            }
        }

        return ResolvePrimaryWorkArea();
    }

    public static uint GetWindowDpi(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        var dpi = NativeMethods.GetDpiForWindow(windowHandle);
        return dpi == 0 ? DefaultDpi : dpi;
    }

    private static bool TryResolveFromRect(
        PlatformRect rect,
        out MonitorPlacementContext context)
    {
        var nativeRect = new NativeMethods.NativeRect(
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom);
        var monitor = NativeMethods.MonitorFromRect(
            ref nativeRect,
            NativeMethods.MonitorDefaultToNearest);
        var anchor = new PlatformPoint(
            rect.Left + (rect.Width / 2),
            rect.Top + (rect.Height / 2));
        return TryResolveMonitor(monitor, anchor, out context);
    }

    private static bool TryResolveFromPoint(
        PlatformPoint point,
        out MonitorPlacementContext context)
    {
        var nativePoint = new NativeMethods.NativePoint(point.X, point.Y);
        var monitor = NativeMethods.MonitorFromPoint(
            nativePoint,
            NativeMethods.MonitorDefaultToNearest);
        return TryResolveMonitor(monitor, point, out context);
    }

    private static bool TryResolveMonitor(
        nint monitor,
        PlatformPoint fallbackAnchor,
        out MonitorPlacementContext context)
    {
        context = default;
        if (monitor == 0)
        {
            return false;
        }

        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.MonitorInfo>()),
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var work = monitorInfo.Work;
        if (work.Right <= work.Left || work.Bottom <= work.Top)
        {
            return false;
        }

        context = new MonitorPlacementContext(
            new PlatformRect(work.Left, work.Top, work.Right, work.Bottom),
            fallbackAnchor);
        return true;
    }

    private static MonitorPlacementContext ResolvePrimaryWorkArea()
    {
        var work = new NativeMethods.NativeRect();
        if (!NativeMethods.SystemParametersInfo(
                NativeMethods.SpiGetWorkArea,
                0,
                ref work,
                0)
            || work.Right <= work.Left
            || work.Bottom <= work.Top)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "The monitor work area could not be resolved.");
        }

        var workArea = new PlatformRect(work.Left, work.Top, work.Right, work.Bottom);
        return new MonitorPlacementContext(
            workArea,
            new PlatformPoint(workArea.Right, workArea.Bottom));
    }
}
