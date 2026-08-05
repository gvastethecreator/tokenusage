using TokenUsage.Platform.Windows.Display;
using TokenUsage.Platform.Windows.Placement;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class FlyoutSizePolicyTests
{
    [Fact]
    public void UsesRoomForDashboardHierarchy()
    {
        Assert.Equal(480, FlyoutSizePolicy.WidthDips);
    }

    [Fact]
    public void ClampWidthUsesDesiredWidthWhenItFits()
    {
        double width = FlyoutSizePolicy.ClampWidthDips(
            480,
            new PlatformRect(0, 0, 1920, 1080),
            144);

        Assert.Equal(480, width);
    }

    [Fact]
    public void ClampWidthFitsNarrowHighDpiWorkArea()
    {
        double width = FlyoutSizePolicy.ClampWidthDips(
            480,
            new PlatformRect(0, 0, 600, 1000),
            192);

        Assert.Equal(300, width);
    }

    [Fact]
    public void ClampHeightUsesDesiredHeightInsideRange()
    {
        var height = FlyoutSizePolicy.ClampHeightDips(
            320,
            new PlatformRect(0, 0, 1920, 1040),
            96);

        Assert.Equal(320, height);
    }

    [Fact]
    public void ClampHeightEnforcesMinimumHeight()
    {
        var height = FlyoutSizePolicy.ClampHeightDips(
            50,
            new PlatformRect(0, 0, 1920, 1040),
            96);

        Assert.Equal(200, height);
    }

    [Fact]
    public void ClampHeightEnforcesAbsoluteMaximumHeight()
    {
        var height = FlyoutSizePolicy.ClampHeightDips(
            900,
            new PlatformRect(0, 0, 1920, 2000),
            96);

        Assert.Equal(720, height);
    }

    [Fact]
    public void ClampHeightEnforcesWorkAreaFraction()
    {
        var height = FlyoutSizePolicy.ClampHeightDips(
            500,
            new PlatformRect(0, 0, 800, 400),
            96);

        Assert.Equal(340, height);
    }

    [Fact]
    public void ClampHeightLetsSmallScreenMaximumOverrideMinimum()
    {
        var height = FlyoutSizePolicy.ClampHeightDips(
            50,
            new PlatformRect(0, 0, 800, 180),
            96);

        Assert.Equal(153, height);
    }

    [Fact]
    public void ClampHeightUsesPhysicalWorkAreaAtHighDpi()
    {
        var height = FlyoutSizePolicy.ClampHeightDips(
            800,
            new PlatformRect(0, 0, 2880, 1440),
            144);

        Assert.Equal(720, height);
    }
}
