using TokenUsage.Platform.Windows.Display;
using TokenUsage.Platform.Windows.Placement;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class ReportWindowPlacementPolicyTests
{
    [Theory]
    [InlineData(0, 0, 3440, 1400, 96, 1080, 250, 1280, 900)]
    [InlineData(5360, 0, 7920, 1440, 144, 5680, 45, 1920, 1350)]
    [InlineData(0, 0, 1024, 720, 96, 16, 16, 992, 688)]
    public void CalculateCentersDpiAwareBoundsInsideTheWorkArea(
        int left,
        int top,
        int right,
        int bottom,
        uint dpi,
        int expectedLeft,
        int expectedTop,
        int expectedWidth,
        int expectedHeight)
    {
        PlatformRect bounds = ReportWindowPlacementPolicy.Calculate(
            new PlatformRect(left, top, right, bottom),
            dpi);

        Assert.Equal(expectedLeft, bounds.Left);
        Assert.Equal(expectedTop, bounds.Top);
        Assert.Equal(expectedWidth, bounds.Width);
        Assert.Equal(expectedHeight, bounds.Height);
    }
}
