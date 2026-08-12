using TokenUsage.Platform.Windows.Native;
using TokenUsage.Platform.Windows.Placement;
using TokenUsage.Platform.Windows.Tray;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class TrayMessageClassifierTests
{
    [Theory]
    [InlineData(NativeMethods.NinSelect)]
    [InlineData(NativeMethods.WmLButtonDown)]
    [InlineData(NativeMethods.WmLButtonUp)]
    [InlineData(NativeMethods.WmLButtonDoubleClick)]
    public void ClassifyPrimaryClickMessagesActivateWithMouse(uint eventCode)
    {
        Assert.Equal(TrayMessageAction.ActivateWithMouse, TrayMessageClassifier.Classify(eventCode));
    }

    [Fact]
    public void ClassifyKeyboardSelectionActivatesWithKeyboard()
    {
        Assert.Equal(
            TrayMessageAction.ActivateWithKeyboard,
            TrayMessageClassifier.Classify(NativeMethods.NinKeySelect));
    }

    [Fact]
    public void ClassifyContextMenuKeepsItsOwnAction()
    {
        Assert.Equal(
            TrayMessageAction.ShowContextMenu,
            TrayMessageClassifier.Classify(NativeMethods.WmContextMenu));
    }

    [Fact]
    public void ClassifyPointerMovementRequestsHoverPreview()
    {
        Assert.Equal(
            TrayMessageAction.PointerMoved,
            TrayMessageClassifier.Classify(NativeMethods.WmMouseMove));
    }

    [Fact]
    public void ClassifyUnrelatedMessageDoesNothing()
    {
        Assert.Equal(TrayMessageAction.None, TrayMessageClassifier.Classify(0x0204));
    }

    [Fact]
    public void RoutingAcceptsVersionFourAndLegacyIconPacking()
    {
        nuint versionFourMessage = (1u << 16) | NativeMethods.WmLButtonDown;

        Assert.True(TrayMessageRoutingPolicy.IsForIcon(0, versionFourMessage, 1));
        Assert.True(TrayMessageRoutingPolicy.IsForIcon(1, NativeMethods.WmLButtonDown, 1));
        Assert.False(TrayMessageRoutingPolicy.IsForIcon(2, NativeMethods.WmLButtonDown, 1));
    }

    [Fact]
    public void RoutingCollapsesMouseMessagesFromOnePhysicalClick()
    {
        Assert.True(TrayMessageRoutingPolicy.ShouldDispatchMouseActivation(long.MinValue, 1_000));
        Assert.False(TrayMessageRoutingPolicy.ShouldDispatchMouseActivation(1_000, 1_050));
        Assert.True(TrayMessageRoutingPolicy.ShouldDispatchMouseActivation(1_000, 1_500));
    }

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(124, 124, true)]
    [InlineData(96, 96, true)]
    [InlineData(94, 100, false)]
    [InlineData(125, 100, false)]
    public void HoverPolicyIncludesConfiguredPadding(int x, int y, bool expected)
    {
        Assert.Equal(
            expected,
            TrayHoverPolicy.IsPointerWithin(
                new PlatformPoint(x, y),
                new PlatformRect(100, 100, 120, 120),
                paddingPixels: 4));
    }
}
