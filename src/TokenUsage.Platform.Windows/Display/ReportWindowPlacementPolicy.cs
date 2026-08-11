using TokenUsage.Platform.Windows.Placement;

namespace TokenUsage.Platform.Windows.Display;

public static class ReportWindowPlacementPolicy
{
    public const double WidthDips = 1280d;
    public const double HeightDips = 900d;
    public const double WorkAreaMarginDips = 16d;
    private const double DefaultDpi = 96d;

    public static PlatformRect Calculate(PlatformRect workArea, uint dpi)
    {
        if (dpi == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be greater than zero.");
        }

        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            throw new ArgumentException(
                "The work area must have positive width and height.",
                nameof(workArea));
        }

        int margin = DipsToPixels(WorkAreaMarginDips, dpi);
        int availableWidth = Math.Max(1, workArea.Width - (margin * 2));
        int availableHeight = Math.Max(1, workArea.Height - (margin * 2));
        int width = Math.Min(DipsToPixels(WidthDips, dpi), availableWidth);
        int height = Math.Min(DipsToPixels(HeightDips, dpi), availableHeight);
        int left = workArea.Left + ((workArea.Width - width) / 2);
        int top = workArea.Top + ((workArea.Height - height) / 2);

        return new PlatformRect(left, top, left + width, top + height);
    }

    private static int DipsToPixels(double dips, uint dpi) =>
        Math.Max(1, (int)Math.Round(dips * dpi / DefaultDpi));
}
