using TokenUsage.Platform.Windows.Native;
using TokenUsage.Platform.Windows.Placement;

namespace TokenUsage.Platform.Windows.Tray;

internal enum TrayMessageAction
{
    None,
    ActivateWithMouse,
    ActivateWithKeyboard,
    PointerMoved,
    ShowContextMenu,
}

internal static class TrayMessageClassifier
{
    public static TrayMessageAction Classify(uint eventCode) => eventCode switch
    {
        NativeMethods.NinSelect => TrayMessageAction.ActivateWithMouse,
        NativeMethods.WmLButtonDown => TrayMessageAction.ActivateWithMouse,
        NativeMethods.WmLButtonUp => TrayMessageAction.ActivateWithMouse,
        NativeMethods.WmLButtonDoubleClick => TrayMessageAction.ActivateWithMouse,
        NativeMethods.NinKeySelect => TrayMessageAction.ActivateWithKeyboard,
        NativeMethods.WmMouseMove => TrayMessageAction.PointerMoved,
        NativeMethods.WmContextMenu => TrayMessageAction.ShowContextMenu,
        _ => TrayMessageAction.None,
    };
}

internal static class TrayHoverPolicy
{
    public static bool IsPointerWithin(
        PlatformPoint pointer,
        PlatformRect bounds,
        int paddingPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(paddingPixels);
        return pointer.X >= bounds.Left - paddingPixels
            && pointer.X <= bounds.Right + paddingPixels
            && pointer.Y >= bounds.Top - paddingPixels
            && pointer.Y <= bounds.Bottom + paddingPixels;
    }
}

internal static class TrayMessageRoutingPolicy
{
    private const long DuplicateMouseMessageWindowMilliseconds = 500;

    public static bool IsForIcon(nuint wParam, nuint packedMessage, uint expectedIconId)
    {
        uint packedIconId = (uint)((packedMessage >> 16) & 0xFFFF);
        return packedIconId == expectedIconId
            || (packedIconId == 0 && unchecked((uint)wParam) == expectedIconId);
    }

    public static bool ShouldDispatchMouseActivation(long lastTick, long currentTick) =>
        lastTick == long.MinValue
        || currentTick - lastTick >= DuplicateMouseMessageWindowMilliseconds;
}
