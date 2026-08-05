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
    }
}
