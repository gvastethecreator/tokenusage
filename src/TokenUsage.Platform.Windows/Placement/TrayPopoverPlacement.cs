namespace TokenUsage.Platform.Windows.Placement;

public static class TrayPopoverPlacement
{
    public static PlatformRect MoveNextToIcon(
        PlatformRect bounds,
        PlatformRect iconBounds,
        FlyoutAnchorEdge anchorEdge,
        int gapPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gapPixels);

        return anchorEdge switch
        {
            FlyoutAnchorEdge.Bottom => WithTop(
                bounds,
                iconBounds.Top - gapPixels - bounds.Height),
            FlyoutAnchorEdge.Top => WithTop(
                bounds,
                iconBounds.Bottom + gapPixels),
            FlyoutAnchorEdge.Left => WithLeft(
                bounds,
                iconBounds.Right + gapPixels),
            FlyoutAnchorEdge.Right => WithLeft(
                bounds,
                iconBounds.Left - gapPixels - bounds.Width),
            _ => bounds,
        };
    }

    private static PlatformRect WithTop(PlatformRect bounds, int top) =>
        new(bounds.Left, top, bounds.Right, top + bounds.Height);

    private static PlatformRect WithLeft(PlatformRect bounds, int left) =>
        new(left, bounds.Top, left + bounds.Width, bounds.Bottom);
}
