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

if (Environment.GetEnvironmentVariable("WOPENUSAGE_FAKE_PATH_MARKER") is string markerPath
    && !string.IsNullOrWhiteSpace(markerPath))
{
    await File.WriteAllTextAsync(markerPath, Environment.ProcessPath ?? string.Empty);
}

DateTimeOffset fakeNowUtc = DateTimeOffset.TryParse(
    Environment.GetEnvironmentVariable("WOPENUSAGE_FAKE_NOW_UTC"),
    CultureInfo.InvariantCulture,
    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
    out DateTimeOffset parsedFakeNow)
    ? parsedFakeNow
    : DateTimeOffset.UtcNow;

string mode = Environment.GetEnvironmentVariable("WOPENUSAGE_FAKE_CODEX_MODE") ?? string.Empty;
if (string.Equals(mode, "quota", StringComparison.Ordinal))
{
    await RunQuotaServerAsync(fakeNowUtc);
    return 0;
}

if (string.Equals(mode, "timeout", StringComparison.Ordinal))
{
    _ = await Console.In.ReadLineAsync();
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

if (string.Equals(mode, "crash", StringComparison.Ordinal))
{
    _ = await Console.In.ReadLineAsync();
    return 13;
}

if (string.Equals(mode, "contract", StringComparison.Ordinal))
{
    await RunContractServerAsync();
    return 0;
}

while (await Console.In.ReadLineAsync() is string line)
{
    await Console.Out.WriteLineAsync(line);
    await Console.Out.FlushAsync();
}

await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;

static async Task RunQuotaServerAsync(DateTimeOffset fakeNowUtc)
{
    DateOnly localDate = DateOnly.FromDateTime(fakeNowUtc.ToLocalTime().DateTime);
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
                            resetsAt = fakeNowUtc.AddHours(4).ToUnixTimeSeconds(),
                            windowDurationMins = 300,
                        },
                        secondary = new
                        {
                            usedPercent = 18,
                            resetsAt = fakeNowUtc.AddDays(5).ToUnixTimeSeconds(),
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
                            startDate = localDate.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture),
                            tokens = 1200,
                        },
                        new
                        {
                            startDate = localDate.AddDays(-1).ToString(
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

static async Task RunContractServerAsync()
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

        object response = string.Equals(
            methodElement.GetString(),
            "initialize",
            StringComparison.Ordinal)
            ? new { id = id.Clone(), result = new { } }
            : new
            {
                id = id.Clone(),
                error = new { code = -32601, message = "method unavailable" },
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
