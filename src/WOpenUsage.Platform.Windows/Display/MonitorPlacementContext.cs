using WOpenUsage.Platform.Windows.Placement;

namespace WOpenUsage.Platform.Windows.Display;

public readonly record struct MonitorPlacementContext(
    PlatformRect WorkArea,
    PlatformPoint FallbackAnchor);
