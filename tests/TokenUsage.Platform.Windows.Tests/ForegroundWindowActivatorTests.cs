using TokenUsage.Platform.Windows.Windowing;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class ForegroundWindowActivatorTests
{
    [Fact]
    public void ZeroHandleCannotBeActivated()
    {
        Assert.False(ForegroundWindowActivator.TryActivate(0));
    }
}
