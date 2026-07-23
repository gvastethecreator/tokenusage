# Ticket 13D — Dashboard density and window frame

Date: 2026-07-23

## Delivered

- Increased the preferred flyout width from 440 to 480 DIPs.
- Reduced dashboard headings and amounts to 13 DIPs and secondary text to 11 DIPs.
- Tightened gaps between provider blocks while adding clearer space inside quota cards.
- Kept the adaptive 300-DIP layout usable.
- Removed the XAML outline and the remaining Win32 `WS_DLGFRAME` style.
- Reapplied the borderless frame after activation so Windows cannot restore it during window setup.
- Disabled the remaining one-pixel DWM border with `DWMWA_COLOR_NONE` in normal mode.
- Restores a foreground-colored system frame in high contrast and polls system visual settings only while the flyout is visible.
- Replaced the full-width usage-details expander with a 32-DIP icon toggle in each provider header. Its rows still expand below the primary metrics.

Windows 11 documents `DWMWA_BORDER_COLOR` for border color control. Runtime inspection showed that this window also retained `WS_DLGFRAME`; clearing that style and issuing `SWP_FRAMECHANGED` removed the full white frame.

Reference: <https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute>

## Proof

- `FlyoutSizePolicyTests` and `WindowBorderStyleTests`: 10/10 passed on Release x64.
- Packaged Release x64 build with Windows App SDK analyzers: passed.
- Wide runtime capture after review: `.scratch/ui/ticket-13d-density/dashboard-480-final-reviewed.png`.
- Narrow runtime capture: `.scratch/ui/ticket-13d-density/dashboard-300-narrow.png`.
- Borderless runtime capture after the final Win32 fix: `.scratch/ui/ticket-13d-density/dashboard-480-final-reviewed.png`.
- Compact details, closed: `.scratch/ui/border-details-fix/collapsed.png`.
- Compact details, open: `.scratch/ui/border-details-fix/expanded.png`.

The app was launched through `winapp run`; the packaged executable was not started directly.

Independent review found that removing every frame also removed the window boundary in high contrast. The final implementation restores the non-client accessibility frame, uses the system foreground color, and detects live contrast or palette changes within one second while the flyout is open. The operating-system high-contrast switch was not changed during runtime proof.

The first updated Ticket 09C UI run passed 5/6 checks. Its near-limit check expected the English compact value `490K tokens`, while persisted Spanish formatting rendered `490 mil tokens`. The test now checks the semantic value through its stable AutomationId.
The final rerun passed 6/6 after also waiting for the toggle's UIA state instead of reading it in the same frame as the invoke action.
