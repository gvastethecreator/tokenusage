using WOpenUsage.Platform.Windows.Windowing;

namespace WOpenUsage.Platform.Windows.Tests;

public sealed class ForegroundWindowActivatorTests
{
    [Fact]
    public void ZeroHandleCannotBeActivated()
    {
        Assert.False(ForegroundWindowActivator.TryActivate(0));
    }
}
