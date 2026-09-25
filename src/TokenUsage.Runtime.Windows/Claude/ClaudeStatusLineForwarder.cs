using System.Diagnostics;
using System.Text;

namespace TokenUsage.Runtime.Windows.Claude;

/// <summary>
/// Runs the status line command the user had before TokenUsage wrapped it, with the same
/// input Claude Code sent, and returns what it printed. Claude Code on Windows runs these
/// commands through Git Bash, so the forwarder prefers the same shell and falls back to cmd.
/// </summary>
public sealed class ClaudeStatusLineForwarder
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private const int MaximumOutputCharacters = 16 * 1024;

    private readonly Func<string?> _findBash;
    private readonly TimeSpan _timeout;

    public ClaudeStatusLineForwarder(Func<string?>? findBash = null, TimeSpan? timeout = null)
    {
        _findBash = findBash ?? FindGitBash;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<string?> RunAsync(
        string command,
        byte[] standardInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(standardInput);

        string? bash = _findBash();
        var startInfo = new ProcessStartInfo
        {
            FileName = bash ?? Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        if (bash is null)
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                           or InvalidOperationException)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task drainError = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            try
            {
                await process.StandardInput.BaseStream
                    .WriteAsync(standardInput, timeout.Token)
                    .ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The previous command may exit without reading its input.
            }

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await drainError.ConfigureAwait(false);
            string text = await output.ConfigureAwait(false);
            return text.Length > MaximumOutputCharacters ? text[..MaximumOutputCharacters] : text;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or System.ComponentModel.Win32Exception
                                           or NotSupportedException)
        {
        }
    }

    /// <summary>
    /// Claude Code documents <c>CLAUDE_CODE_GIT_BASH_PATH</c> for a Git Bash in a custom place;
    /// otherwise the standard Git for Windows locations are checked.
    /// </summary>
    public static string? FindGitBash()
    {
        string? configured = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        foreach (Environment.SpecialFolder folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                 })
        {
            string root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            string candidate = Path.Combine(root, "Git", "bin", "bash.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string userInstall = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Git",
            "bin",
            "bash.exe");
        return File.Exists(userInstall) ? userInstall : null;
    }
}
