using TokenUsage.Platform.Windows.Placement;

namespace TokenUsage.Platform.Windows.Display;

public readonly record struct MonitorPlacementContext(
    PlatformRect WorkArea,
    PlatformPoint FallbackAnchor);
