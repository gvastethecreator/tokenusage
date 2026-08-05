using System.Text.RegularExpressions;

namespace TokenUsage.Platform.Windows.Processes;

internal static partial class DiagnosticTextSanitizer
{
    private const string RedactedPath = "[path]";
    private const string RedactedEmail = "[email]";
    private const string RedactedSecret = "[secret]";

    internal static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string sanitized = WindowsPathRegex().Replace(value, RedactedPath);
        sanitized = EmailRegex().Replace(sanitized, RedactedEmail);
        sanitized = BearerRegex().Replace(sanitized, RedactedSecret);
        sanitized = SecretAssignmentRegex().Replace(sanitized, RedactedSecret);
        return TokenRegex().Replace(sanitized, RedactedSecret);
    }

    [GeneratedRegex(
        @"(?i)(?:[a-z]:\\|\\\\)[^\r\n]*",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(
        @"(?i)\b[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9-]+(?:\.[a-z0-9-]+)+\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(
        @"(?i)\bbearer\s+[a-z0-9._~+/-]{8,}",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(
        @"(?i)\b(?:token|secret|api[_-]?key)\s*[:=]\s*[^\s;,]+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(
        @"(?i)\b(?:sk|xox[baprs])-[a-z0-9_-]{8,}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex TokenRegex();
}
