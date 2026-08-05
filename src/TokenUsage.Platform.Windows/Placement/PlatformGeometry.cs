namespace TokenUsage.Platform.Windows.Placement;

public readonly record struct PlatformPoint(int X, int Y);

public readonly record struct PlatformRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

public enum FlyoutAnchorEdge
{
    Bottom,
    Top,
    Left,
    Right,
    Overflow,
}

public readonly record struct FlyoutPlacementResult(
    PlatformRect Bounds,
    FlyoutAnchorEdge AnchorEdge,
    bool SizeConstrained);
