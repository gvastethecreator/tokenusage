using WOpenUsage.Platform.Windows.Processes;

namespace WOpenUsage.Platform.Windows.Tests;

public sealed class DiagnosticTextSanitizerTests
{
    [Fact]
    public void SanitizerRemovesPrivateLookingValues()
    {
        const string input = "private@example.invalid\n"
            + "sk-test1234\n"
            + "Authorization: Bearer test1234\n"
            + "api_key=private-key-value\n"
            + "C:\\Users\\private\\.codex\\auth.json\n";

        string result = DiagnosticTextSanitizer.Sanitize(input);

        Assert.Contains("[email]", result, StringComparison.Ordinal);
        Assert.Contains("[secret]", result, StringComparison.Ordinal);
        Assert.Contains("[path]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.invalid", result, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test1234", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer test1234", result, StringComparison.Ordinal);
        Assert.DoesNotContain("private-key-value", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Users\\private", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BufferSanitizesValuesSplitAcrossReads()
    {
        var buffer = new SanitizedDiagnosticBuffer(512);

        buffer.Append("private-account@".AsSpan());
        buffer.Append("example.invalid\n".AsSpan());
        buffer.Complete();

        Assert.Equal("[email]\n", buffer.Snapshot());
    }

    [Fact]
    public void BufferDropsOverlongRawLineAndStaysBounded()
    {
        var buffer = new SanitizedDiagnosticBuffer(256);

        buffer.Append(new string('x', 3000).AsSpan());
        buffer.Append("ignored-private-fragment\nnext@example.invalid\n".AsSpan());
        buffer.Complete();
        string result = buffer.Snapshot();

        Assert.True(result.Length <= 256);
        Assert.Contains("[diagnostic line removed]", result, StringComparison.Ordinal);
        Assert.Contains("[email]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored-private-fragment", result, StringComparison.Ordinal);
    }
}
