using System.Text;

namespace TokenUsage.App.Localization;

internal static class AppLanguageRestartArguments
{
    public static string Create(IEnumerable<string> launchArguments) => string.Join(
        " ",
        launchArguments
            .Where(IsPreservedDebugArgument)
            .Select(Quote));

    private static bool IsPreservedDebugArgument(string argument) =>
        argument.StartsWith("--test-", StringComparison.OrdinalIgnoreCase)
        || argument.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase);

    private static string Quote(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        if (!argument.Any(character => char.IsWhiteSpace(character) || character == '\"'))
        {
            return argument;
        }

        var result = new StringBuilder(argument.Length + 2);
        result.Append('\"');
        int slashCount = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                slashCount++;
                continue;
            }

            if (character == '\"')
            {
                result.Append('\\', (slashCount * 2) + 1);
                result.Append(character);
                slashCount = 0;
                continue;
            }

            result.Append('\\', slashCount);
            result.Append(character);
            slashCount = 0;
        }

        result.Append('\\', slashCount * 2);
        result.Append('\"');
        return result.ToString();
    }
}
