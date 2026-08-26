namespace TokenUsage.Runtime.Windows;

/// <summary>
/// The command string refresh-trigger hooks run. Provider hook payloads can
/// carry conversation content, so the trigger detaches immediately and the
/// CLI drains and discards stdin. The wrapper works under cmd and POSIX
/// shells alike.
/// </summary>
public static class HookTriggerCommand
{
    public const string DetachedRefresh =
        "powershell.exe -NoProfile -NonInteractive -Command "
        + "\"Start-Process -WindowStyle Hidden tokenusage -ArgumentList 'hook','stop'\"";
}
