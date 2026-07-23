using WOpenUsage.Platform.Windows.Windowing;

namespace WOpenUsage.Platform.Windows.Tests;

public sealed class WindowBorderStyleTests
{
    [Fact]
    public void RejectsMissingWindowHandle()
    {
        Assert.False(WindowBorderStyle.TryRemoveNonClientFrame(0));
        Assert.False(WindowBorderStyle.TryRestoreAccessibleFrame(0));
        Assert.False(WindowBorderStyle.TryMatchSystemBorder(0, 32, 32, 32));
    }
}
