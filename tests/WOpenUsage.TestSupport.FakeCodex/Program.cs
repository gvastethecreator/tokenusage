using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

if (args is not ["app-server", "--stdio"])
{
    return 64;
}

Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.Error.WriteLine(new string('x', 3000));
Console.Error.WriteLine("private-account@example.invalid");
Console.Error.WriteLine("sk-test1234");
Console.Error.WriteLine("C:\\Users\\private\\.codex\\auth.json");
Console.Error.WriteLine("Authorization: Bearer test1234");
if (Environment.GetEnvironmentVariable("WOPENUSAGE_FAKE_EXTRA_HANDLE") is string rawHandle
    && long.TryParse(rawHandle, NumberStyles.None, CultureInfo.InvariantCulture, out long handleValue))
{
    bool inherited = FakeNativeMethods.GetHandleInformation((nint)handleValue, out _);
    Console.Error.WriteLine($"extra-handle-inherited={inherited.ToString().ToLowerInvariant()}");
}

await Console.Error.FlushAsync();

if (string.Equals(
    Environment.GetEnvironmentVariable("WOPENUSAGE_FAKE_CODEX_MODE"),
    "quota",
    StringComparison.Ordinal))
{
    await RunQuotaServerAsync();
    return 0;
}

while (await Console.In.ReadLineAsync() is string line)
{
    await Console.Out.WriteLineAsync(line);
    await Console.Out.FlushAsync();
}

await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;

static async Task RunQuotaServerAsync()
{
    while (await Console.In.ReadLineAsync() is string line)
    {
        using JsonDocument request = JsonDocument.Parse(line);
        JsonElement root = request.RootElement;
        if (!root.TryGetProperty("id", out JsonElement id)
            || !root.TryGetProperty("method", out JsonElement methodElement))
        {
            continue;
        }

        string? method = methodElement.GetString();
        object response = method switch
        {
            "initialize" => new { id = id.Clone(), result = new { } },
            "account/read" => new
            {
                id = id.Clone(),
                result = new
                {
                    account = new
                    {
                        type = "chatgpt",
                        email = "private-live@example.invalid",
                        planType = "plus",
                    },
                    requiresOpenaiAuth = true,
                },
            },
            "account/rateLimits/read" => new
            {
                id = id.Clone(),
                result = new
                {
                    rateLimits = new
                    {
                        planType = "plus",
                        primary = new
                        {
                            usedPercent = 42,
                            resetsAt = DateTimeOffset.UtcNow.AddHours(4).ToUnixTimeSeconds(),
                            windowDurationMins = 300,
                        },
                        secondary = new
                        {
                            usedPercent = 18,
                            resetsAt = DateTimeOffset.UtcNow.AddDays(5).ToUnixTimeSeconds(),
                            windowDurationMins = 10080,
                        },
                    },
                },
            },
            "account/usage/read" => new
            {
                id = id.Clone(),
                result = new
                {
                    summary = new
                    {
                        currentStreakDays = 2,
                        privateField = "private-live@example.invalid",
                    },
                    dailyUsageBuckets = new[]
                    {
                        new
                        {
                            startDate = DateOnly.FromDateTime(DateTime.Now).ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture),
                            tokens = 1200,
                        },
                        new
                        {
                            startDate = DateOnly.FromDateTime(DateTime.Now).AddDays(-1).ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture),
                            tokens = 300,
                        },
                    },
                },
            },
            _ => new
            {
                id = id.Clone(),
                error = new { code = -32601, message = "method unavailable" },
            },
        };

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response));
        await Console.Out.FlushAsync();
    }
}

[SuppressMessage(
    "Interoperability",
    "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute'",
    Justification = "The fake child keeps one small test-only Win32 probe.")]
internal static class FakeNativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetHandleInformation(nint handle, out uint flags);
}
