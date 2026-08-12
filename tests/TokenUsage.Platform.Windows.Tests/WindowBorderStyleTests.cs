using TokenUsage.Platform.Windows.Windowing;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class WindowBorderStyleTests
{
    [Fact]
    public void RejectsMissingWindowHandle()
    {
        Assert.False(WindowBorderStyle.TryRemoveNonClientFrame(0));
        Assert.False(WindowBorderStyle.TryRestoreAccessibleFrame(0));
        Assert.False(WindowBorderStyle.TryHideSystemBorder(0));
        Assert.False(WindowBorderStyle.TryMatchSystemBorder(0, 32, 32, 32));
        Assert.False(WindowBorderStyle.TryClipRoundedCorners(0, 12));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void RejectsInvalidRoundedCornerRadius(double radiusDips)
    {
        Assert.False(WindowBorderStyle.TryClipRoundedCorners(1, radiusDips));
    }
}
