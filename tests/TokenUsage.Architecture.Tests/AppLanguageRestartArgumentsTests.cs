using WOpenUsage.App.Localization;

namespace WOpenUsage.Architecture.Tests;

public sealed class AppLanguageRestartArgumentsTests
{
    [Fact]
    public void CreateKeepsEveryDebugHarnessArgumentAndTheme()
    {
        string arguments = AppLanguageRestartArguments.Create(
        [
            "--test-show-flyout",
            "--test-claude-config=C:\\fixture path",
            "--test-grok-home=C:\\grok",
            "--test-opencode-data=C:\\open code\\",
            "--theme=dark",
            "--ignored=production",
        ]);

        Assert.Equal(
            "--test-show-flyout \"--test-claude-config=C:\\fixture path\" --test-grok-home=C:\\grok \"--test-opencode-data=C:\\open code\\\\\" --theme=dark",
            arguments);
    }

    [Fact]
    public void CreateEscapesQuotesInPreservedArguments()
    {
        string arguments = AppLanguageRestartArguments.Create(["--test-claude-config=C:\\a\"b"]);

        Assert.Equal("\"--test-claude-config=C:\\a\\\"b\"", arguments);
    }
}
