using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

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

while (await Console.In.ReadLineAsync() is string line)
{
    await Console.Out.WriteLineAsync(line);
    await Console.Out.FlushAsync();
}

await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;

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
