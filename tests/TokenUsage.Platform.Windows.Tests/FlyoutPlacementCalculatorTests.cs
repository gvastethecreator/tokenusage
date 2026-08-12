using TokenUsage.Platform.Windows.Placement;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class FlyoutPlacementCalculatorTests
{
    private static readonly PlatformRect WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void CalculateBottomTaskbarRightAlignsAndUsesWorkAreaBoundary()
    {
        var result = FlyoutPlacementCalculator.Calculate(
            new PlatformRect(1840, 1040, 1880, 1080),
            WorkArea,
            320,
            600,
            96,
            default);

        Assert.Equal(FlyoutAnchorEdge.Bottom, result.AnchorEdge);
        Assert.Equal(new PlatformRect(1560, 440, 1880, 1040), result.Bounds);
        Assert.False(result.SizeConstrained);
    }

    [Fact]
    public void CalculateTopTaskbarPlacesAtWorkAreaTop()
    {
        var result = FlyoutPlacementCalculator.Calculate(
            new PlatformRect(1840, -40, 1880, 0),
            WorkArea,
            320,
            600,
            96,
            default);

        Assert.Equal(FlyoutAnchorEdge.Top, result.AnchorEdge);
        Assert.Equal(new PlatformRect(1560, 0, 1880, 600), result.Bounds);
    }

    [Fact]
    public void CalculateLeftTaskbarUsesWorkAreaLeftAndClampsVertically()
    {
        var result = FlyoutPlacementCalculator.Calculate(
            new PlatformRect(-40, 980, 0, 1020),
            WorkArea,
            320,
            600,
            96,
            default);

        Assert.Equal(FlyoutAnchorEdge.Left, result.AnchorEdge);
        Assert.Equal(new PlatformRect(0, 420, 320, 1020), result.Bounds);
    }

    [Fact]
    public void CalculateRightTaskbarUsesWorkAreaRightAndClampsVertically()
    {
        var result = FlyoutPlacementCalculator.Calculate(
            new PlatformRect(1920, 980, 1960, 1020),
            WorkArea,
            320,
            600,
            96,
            default);

        Assert.Equal(FlyoutAnchorEdge.Right, result.AnchorEdge);
        Assert.Equal(new PlatformRect(1600, 420, 1920, 1020), result.Bounds);
    }

    [Fact]
    public void CalculateMissingIconUsesFallbackAndStaysInsideWorkArea()
    {
        var result = FlyoutPlacementCalculator.Calculate(
            null,
            WorkArea,
            320,
            600,
            96,
            new PlatformPoint(1910, 1030));

        Assert.Equal(FlyoutAnchorEdge.Overflow, result.AnchorEdge);
        Assert.Equal(new PlatformRect(1590, 430, 1910, 1030), result.Bounds);
    }

    [Fact]
    public void CalculateOversizedFlyoutConstrainsBoundsToWorkArea()
    {
        var result = FlyoutPlacementCalculator.Calculate(
            new PlatformRect(1840, 1040, 1880, 1080),
            WorkArea,
            4000,
            4000,
            96,
            default);

        Assert.Equal(WorkArea, result.Bounds);
        Assert.True(result.SizeConstrained);
    }

    [Fact]
    public void CalculateAtHighDpiConvertsDipsBeforePlacement()
    {
        var result = FlyoutPlacementCalculator.Calculate(
            new PlatformRect(2800, 1440, 2860, 1500),
            new PlatformRect(0, 0, 2880, 1440),
            320,
            600,
            144,
            default);

        Assert.Equal(new PlatformRect(2380, 540, 2860, 1440), result.Bounds);
    }

    [Fact]
    public void CalculateNegativeMonitorCoordinatesStayInsideThatWorkArea()
    {
        var result = FlyoutPlacementCalculator.Calculate(
            new PlatformRect(-80, 1040, -40, 1080),
            new PlatformRect(-1920, 0, 0, 1040),
            320,
            600,
            96,
            default);

        Assert.Equal(new PlatformRect(-360, 440, -40, 1040), result.Bounds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void DipsToPixelsInvalidDipsThrows(double dips)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FlyoutPlacementCalculator.DipsToPixels(dips, 96));
    }

    [Fact]
    public void DipsToPixelsTinyPositiveValueReturnsOnePixel()
    {
        Assert.Equal(1, FlyoutPlacementCalculator.DipsToPixels(0.01, 96));
    }

    [Fact]
    public void DipsToPixelsZeroDpiThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FlyoutPlacementCalculator.DipsToPixels(320, 0));
    }

    [Fact]
    public void CalculateInvalidWorkAreaThrows()
    {
        Assert.Throws<ArgumentException>(() =>
            FlyoutPlacementCalculator.Calculate(
                new PlatformRect(0, 0, 1, 1),
                new PlatformRect(0, 0, 0, 10),
                320,
                600,
                96,
                default));
    }

    [Theory]
    [InlineData(FlyoutAnchorEdge.Bottom, 100, 52, 180, 112)]
    [InlineData(FlyoutAnchorEdge.Top, 100, 148, 180, 208)]
    [InlineData(FlyoutAnchorEdge.Left, 228, 100, 308, 160)]
    [InlineData(FlyoutAnchorEdge.Right, 112, 100, 192, 160)]
    public void TrayPopoverMovesNextToTheTrayIcon(
        FlyoutAnchorEdge edge,
        int left,
        int top,
        int right,
        int bottom)
    {
        PlatformRect result = TrayPopoverPlacement.MoveNextToIcon(
            new PlatformRect(100, 100, 180, 160),
            new PlatformRect(200, 120, 220, 140),
            edge,
            gapPixels: 8);

        Assert.Equal(new PlatformRect(left, top, right, bottom), result);
    }

    [Fact]
    public void TrayPopoverKeepsOverflowPlacementUnchanged()
    {
        var bounds = new PlatformRect(100, 100, 180, 160);

        Assert.Equal(
            bounds,
            TrayPopoverPlacement.MoveNextToIcon(
                bounds,
                new PlatformRect(200, 120, 220, 140),
                FlyoutAnchorEdge.Overflow,
                gapPixels: 8));
    }
}
