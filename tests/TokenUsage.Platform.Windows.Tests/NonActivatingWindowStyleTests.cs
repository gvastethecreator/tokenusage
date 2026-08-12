using TokenUsage.Platform.Windows.Native;
using TokenUsage.Platform.Windows.Windowing;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class NonActivatingWindowStyleTests
{
    [Fact]
    public void AddRequiredStylesPreservesExistingFlags()
    {
        nint current = new(0x00000100);

        nint result = NonActivatingWindowStyle.AddRequiredStyles(current);

        Assert.NotEqual(0, result & current);
        Assert.NotEqual(0, result & (nint)NativeMethods.WsExNoActivate);
        Assert.NotEqual(0, result & (nint)NativeMethods.WsExToolWindow);
    }
}
