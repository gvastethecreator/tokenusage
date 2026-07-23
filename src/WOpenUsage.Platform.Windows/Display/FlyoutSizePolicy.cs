using WOpenUsage.Platform.Windows.Placement;

namespace WOpenUsage.Platform.Windows.Display;

public static class FlyoutSizePolicy
{
    public const double WidthDips = 440d;
    public const double MinimumHeightDips = 200d;
    public const double AbsoluteMaximumHeightDips = 720d;
    public const double WorkAreaHeightFraction = 0.85d;
    private const double DefaultDpi = 96d;

    public static double ClampWidthDips(
        double desiredWidthDips,
        PlatformRect workArea,
        uint dpi)
    {
        if (!double.IsFinite(desiredWidthDips) || desiredWidthDips <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredWidthDips),
                desiredWidthDips,
                "Desired width must be finite and greater than zero.");
        }

        ValidateWorkAreaAndDpi(workArea, dpi);
        double workAreaWidthDips = workArea.Width * DefaultDpi / dpi;
        return Math.Min(desiredWidthDips, workAreaWidthDips);
    }

    public static double ClampHeightDips(
        double desiredHeightDips,
        PlatformRect workArea,
        uint dpi)
    {
        if (!double.IsFinite(desiredHeightDips) || desiredHeightDips <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredHeightDips),
                desiredHeightDips,
                "Desired height must be finite and greater than zero.");
        }

        ValidateWorkAreaAndDpi(workArea, dpi);

        var workAreaHeightDips = workArea.Height * DefaultDpi / dpi;
        var maximumHeightDips = Math.Min(
            AbsoluteMaximumHeightDips,
            workAreaHeightDips * WorkAreaHeightFraction);
        var heightWithMinimum = Math.Max(MinimumHeightDips, desiredHeightDips);
        return Math.Min(maximumHeightDips, heightWithMinimum);
    }

    private static void ValidateWorkAreaAndDpi(PlatformRect workArea, uint dpi)
    {
        if (dpi == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be greater than zero.");
        }

        if (workArea.Right <= workArea.Left || workArea.Bottom <= workArea.Top)
        {
            throw new ArgumentException(
                "The work area must have positive width and height.",
                nameof(workArea));
        }
    }
}
