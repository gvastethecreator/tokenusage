using TokenUsage.Platform.Windows.Placement;

namespace TokenUsage.Platform.Windows.Tray;

public enum TrayMenuCommand
{
    None = 0,
    Update = 1,
    Settings = 2,
    Exit = 3,
}

public enum TrayActivationKind
{
    Mouse,
    Keyboard,
}

public sealed record TrayMenuLabels(string Update, string Settings, string Exit);

public sealed class TrayActivatedEventArgs(TrayActivationKind kind) : EventArgs
{
    public TrayActivationKind Kind { get; } = kind;
}

public sealed class TrayHoveredEventArgs(PlatformRect iconBounds) : EventArgs
{
    public PlatformRect IconBounds { get; } = iconBounds;
}
