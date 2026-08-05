using TokenUsage.Platform.Windows.Tray;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class TrayIconRecoveryPolicyTests
{
    private const uint TaskbarCreatedMessage = 0xC031;

    [Fact]
    public void UnrelatedMessageDoesNotRequestRecovery()
    {
        Assert.False(TrayIconRecoveryPolicy.ShouldRecover(
            TaskbarCreatedMessage - 1,
            TaskbarCreatedMessage,
            disposed: false));
    }

    [Fact]
    public void TaskbarCreatedRequestsRecovery()
    {
        Assert.True(TrayIconRecoveryPolicy.ShouldRecover(
            TaskbarCreatedMessage,
            TaskbarCreatedMessage,
            disposed: false));
    }

    [Fact]
    public void DisposedHostDoesNotRequestRecovery()
    {
        Assert.False(TrayIconRecoveryPolicy.ShouldRecover(
            TaskbarCreatedMessage,
            TaskbarCreatedMessage,
            disposed: true));
    }

    [Fact]
    public void RepeatedTaskbarCreatedMessagesEachRequestRecovery()
    {
        Assert.True(TrayIconRecoveryPolicy.ShouldRecover(
            TaskbarCreatedMessage,
            TaskbarCreatedMessage,
            disposed: false));
        Assert.True(TrayIconRecoveryPolicy.ShouldRecover(
            TaskbarCreatedMessage,
            TaskbarCreatedMessage,
            disposed: false));
    }
}
