using System.Globalization;
using TokenUsage.Core.Updates;

namespace TokenUsage.Cli;

internal static class UpdateWorkerCommand
{
    internal static async Task<int> RunAsync(string[] args, TextWriter standardError)
    {
        if (args.Length != 2 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
            || processId <= 0)
        {
            return 2;
        }

        try
        {
            return await PortableUpdateInstaller.RunWorkerAsync(args[0], processId).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            await standardError.WriteLineAsync("The portable update could not start. Open TokenUsage to retry.").ConfigureAwait(false);
            return 1;
        }
    }
}
