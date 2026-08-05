namespace WOpenUsage.Platform.Windows.Placement;

public static class FlyoutPlacementCalculator
{
    private const double DefaultDpi = 96d;

    public static FlyoutPlacementResult Calculate(
        PlatformRect? trayIconBounds,
        PlatformRect workArea,
        double targetWidthDips,
        double targetHeightDips,
        uint dpi,
        PlatformPoint fallbackAnchor)
    {
        Validate(workArea, targetWidthDips, targetHeightDips, dpi);

        var requestedWidth = DipsToPixels(targetWidthDips, dpi);
        var requestedHeight = DipsToPixels(targetHeightDips, dpi);
        var width = Math.Min(requestedWidth, workArea.Width);
        var height = Math.Min(requestedHeight, workArea.Height);
        var sizeConstrained = width != requestedWidth || height != requestedHeight;
        var anchorEdge = InferAnchorEdge(trayIconBounds, workArea);

        var fallbackX = Math.Clamp(fallbackAnchor.X, workArea.Left, workArea.Right);
        var fallbackY = Math.Clamp(fallbackAnchor.Y, workArea.Top, workArea.Bottom);
        var anchor = trayIconBounds ?? new PlatformRect(
            fallbackX,
            fallbackY,
            fallbackX,
            fallbackY);

        var (rawX, rawY) = anchorEdge switch
        {
            FlyoutAnchorEdge.Bottom => (anchor.Right - width, workArea.Bottom - height),
            FlyoutAnchorEdge.Top => (anchor.Right - width, workArea.Top),
            FlyoutAnchorEdge.Left => (workArea.Left, anchor.Bottom - height),
            FlyoutAnchorEdge.Right => (workArea.Right - width, anchor.Bottom - height),
            _ => PlaceNearOverflowAnchor(anchor, workArea, width, height),
        };

        var x = Math.Clamp(rawX, workArea.Left, workArea.Right - width);
        var y = Math.Clamp(rawY, workArea.Top, workArea.Bottom - height);

        return new FlyoutPlacementResult(
            new PlatformRect(x, y, x + width, y + height),
            anchorEdge,
            sizeConstrained);
    }

    public static int DipsToPixels(double dips, uint dpi)
    {
        if (!double.IsFinite(dips) || dips <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dips), dips, "DIPs must be finite and greater than zero.");
        }

        if (dpi == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be greater than zero.");
        }

        var pixels = checked((int)Math.Round(
            dips * dpi / DefaultDpi,
            MidpointRounding.AwayFromZero));
        return Math.Max(1, pixels);
    }

    private static FlyoutAnchorEdge InferAnchorEdge(PlatformRect? trayIconBounds, PlatformRect workArea)
    {
        if (trayIconBounds is not { } icon)
        {
            return FlyoutAnchorEdge.Overflow;
        }

        if (icon.Top >= workArea.Bottom)
        {
            return FlyoutAnchorEdge.Bottom;
        }

        if (icon.Bottom <= workArea.Top)
        {
            return FlyoutAnchorEdge.Top;
        }

        if (icon.Right <= workArea.Left)
        {
            return FlyoutAnchorEdge.Left;
        }

        if (icon.Left >= workArea.Right)
        {
            return FlyoutAnchorEdge.Right;
        }

        return FlyoutAnchorEdge.Overflow;
    }

    private static (int X, int Y) PlaceNearOverflowAnchor(
        PlatformRect anchor,
        PlatformRect workArea,
        int width,
        int height)
    {
        var x = anchor.Right - width;
        var spaceAbove = anchor.Top - workArea.Top;
        var spaceBelow = workArea.Bottom - anchor.Bottom;

        if (spaceAbove >= height || spaceAbove >= spaceBelow)
        {
            return (x, anchor.Top - height);
        }

        return (x, anchor.Bottom);
    }

    private static void Validate(
        PlatformRect workArea,
        double targetWidthDips,
        double targetHeightDips,
        uint dpi)
    {
        if (workArea.Right <= workArea.Left || workArea.Bottom <= workArea.Top)
        {
            throw new ArgumentException("The work area must have positive width and height.", nameof(workArea));
        }

        _ = DipsToPixels(targetWidthDips, dpi);
        _ = DipsToPixels(targetHeightDips, dpi);
    }
}
