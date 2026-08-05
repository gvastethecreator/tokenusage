using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TokenUsage.Platform.Windows.Native;

namespace TokenUsage.Platform.Windows.Processes;

public sealed class CodexAppServerProcess : IAsyncDisposable
{
    private const uint FailedResumeThread = uint.MaxValue;
    private const uint ForcedExitCode = 1;

    private readonly SafeKernelHandle _processHandle;
    private readonly IWindowsProcessJob _job;
    private readonly FileStream _stderrStream;
    private readonly CancellationTokenSource _stderrCancellation = new();
    private readonly SanitizedDiagnosticBuffer _diagnostics;
    private readonly CodexAppServerProcessOptions _options;
    private readonly Task _stderrPump;
    private int _disposeStarted;

    private CodexAppServerProcess(
        int processId,
        SafeKernelHandle processHandle,
        IWindowsProcessJob job,
        FileStream clientInput,
        FileStream clientOutput,
        FileStream stderrStream,
        CodexAppServerProcessOptions options)
    {
        ProcessId = processId;
        _processHandle = processHandle;
        _job = job;
        ClientInput = clientInput;
        ClientOutput = clientOutput;
        _stderrStream = stderrStream;
        _options = options;
        _diagnostics = new SanitizedDiagnosticBuffer(options.MaximumDiagnosticCharacters);
        _stderrPump = DrainStderrAsync();
    }

    public int ProcessId { get; }

    /// <summary>Readable child stdout. Pass this as the Codex client's input stream.</summary>
    public Stream ClientInput { get; }

    /// <summary>Writable child stdin. Pass this as the Codex client's output stream.</summary>
    public Stream ClientOutput { get; }

    public string SanitizedDiagnosticSnapshot => _diagnostics.Snapshot();

    public static CodexAppServerProcess Start(
        CodexExecutableResolution.Resolved executable,
        CodexAppServerProcessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return StartCore(
            executable,
            options ?? new CodexAppServerProcessOptions(),
            WindowsProcessJobFactory.Instance,
            processCreated: null);
    }

    internal static CodexAppServerProcess StartCore(
        CodexExecutableResolution.Resolved executable,
        CodexAppServerProcessOptions options,
        IWindowsProcessJobFactory jobFactory,
        Action<int>? processCreated)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(jobFactory);

        if (!CodexExecutableResolver.TryNormalizeExecutable(
                executable.ExecutablePath,
                out string executablePath))
        {
            throw new CodexAppServerProcessException(
                CodexAppServerProcessError.InvalidExecutable,
                "The resolved Codex executable is unavailable or unsafe.");
        }

        IWindowsProcessJob job;
        try
        {
            job = jobFactory.Create();
        }
        catch (Exception exception) when (IsExpectedNativeFailure(exception))
        {
            throw new CodexAppServerProcessException(
                CodexAppServerProcessError.JobSetupFailed,
                "Windows could not create a safe Codex process job.",
                GetNativeErrorCode(exception));
        }

        SafeFileHandle? parentStdinWrite = null;
        SafeFileHandle? childStdinRead = null;
        SafeFileHandle? parentStdoutRead = null;
        SafeFileHandle? childStdoutWrite = null;
        SafeFileHandle? parentStderrRead = null;
        SafeFileHandle? childStderrWrite = null;
        SafeKernelHandle? processHandle = null;
        SafeKernelHandle? threadHandle = null;
        FileStream? clientInput = null;
        FileStream? clientOutput = null;
        FileStream? stderrStream = null;
        CodexAppServerProcess? result = null;
        CodexAppServerProcessError failureStage = CodexAppServerProcessError.StartFailed;

        try
        {
            (parentStdinWrite, childStdinRead) = CreatePipe(parentReads: false);
            (parentStdoutRead, childStdoutWrite) = CreatePipe(parentReads: true);
            (parentStderrRead, childStderrWrite) = CreatePipe(parentReads: true);

            using InheritedHandleList inheritedHandles = InheritedHandleList.Create(
                childStdinRead,
                childStdoutWrite,
                childStderrWrite);
            var startupInfo = new NativeMethods.StartupInfoEx
            {
                StartupInfo = new NativeMethods.StartupInfo
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.StartupInfoEx>(),
                    Flags = NativeMethods.StartfUseStdHandles,
                    StandardInput = childStdinRead.DangerousGetHandle(),
                    StandardOutput = childStdoutWrite.DangerousGetHandle(),
                    StandardError = childStderrWrite.DangerousGetHandle(),
                },
                AttributeList = inheritedHandles.Pointer,
            };
            char[] commandLine = ($"\"{executablePath}\" app-server --stdio\0").ToCharArray();
            string workingDirectory = Path.GetDirectoryName(executablePath)
                ?? throw new InvalidOperationException("Codex executable directory is unavailable.");

            if (!NativeMethods.CreateProcessWithExtendedStartupInfo(
                    executablePath,
                    commandLine,
                    0,
                    0,
                    inheritHandles: true,
                    NativeMethods.CreateSuspended
                        | NativeMethods.CreateNoWindow
                        | NativeMethods.ExtendedStartupInfoPresent,
                    0,
                    workingDirectory,
                    ref startupInfo,
                    out NativeMethods.ProcessInformation processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            processHandle = new SafeKernelHandle(processInformation.Process, ownsHandle: true);
            threadHandle = new SafeKernelHandle(processInformation.Thread, ownsHandle: true);
            int processId = checked((int)processInformation.ProcessId);
            processCreated?.Invoke(processId);

            childStdinRead.Dispose();
            childStdinRead = null;
            childStdoutWrite.Dispose();
            childStdoutWrite = null;
            childStderrWrite.Dispose();
            childStderrWrite = null;

            failureStage = CodexAppServerProcessError.JobSetupFailed;
            job.Assign(processHandle);

            failureStage = CodexAppServerProcessError.StartFailed;
            clientInput = new FileStream(
                parentStdoutRead,
                FileAccess.Read,
                bufferSize: 4096,
                isAsync: false);
            parentStdoutRead = null;
            clientOutput = new FileStream(
                parentStdinWrite,
                FileAccess.Write,
                bufferSize: 4096,
                isAsync: false);
            parentStdinWrite = null;
            stderrStream = new FileStream(
                parentStderrRead,
                FileAccess.Read,
                bufferSize: 1024,
                isAsync: false);
            parentStderrRead = null;

            if (NativeMethods.ResumeThread(threadHandle) == FailedResumeThread)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            threadHandle.Dispose();
            threadHandle = null;

            result = new CodexAppServerProcess(
                processId,
                processHandle,
                job,
                clientInput,
                clientOutput,
                stderrStream,
                options);
            processHandle = null;
            clientInput = null;
            clientOutput = null;
            stderrStream = null;
            job = null!;
            return result;
        }
        catch (CodexAppServerProcessException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedNativeFailure(exception))
        {
            string message = failureStage == CodexAppServerProcessError.JobSetupFailed
                ? "Windows could not assign Codex to its safe process job."
                : "Windows could not start Codex app-server.";
            throw new CodexAppServerProcessException(
                failureStage,
                message,
                GetNativeErrorCode(exception));
        }
        finally
        {
            childStdinRead?.Dispose();
            childStdoutWrite?.Dispose();
            childStderrWrite?.Dispose();
            parentStdinWrite?.Dispose();
            parentStdoutRead?.Dispose();
            parentStderrRead?.Dispose();
            threadHandle?.Dispose();

            if (result is null)
            {
                TryTerminate(processHandle, job);
                clientOutput?.Dispose();
                clientInput?.Dispose();
                stderrStream?.Dispose();
                processHandle?.Dispose();
                job.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        bool exited = false;
        try
        {
            try
            {
                await ClientOutput.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }

            exited = await WaitForExitAsync(_options.GracefulShutdownTimeout).ConfigureAwait(false);
            if (!exited)
            {
                _job.Terminate();
                _job.Dispose();
                exited = await WaitForExitAsync(_options.ForcedShutdownTimeout).ConfigureAwait(false);
            }

            if (!exited)
            {
                NativeMethods.TerminateProcess(_processHandle, ForcedExitCode);
                exited = await WaitForExitAsync(_options.ForcedShutdownTimeout).ConfigureAwait(false);
            }
        }
        finally
        {
            _job.Dispose();
            _stderrCancellation.Cancel();
            await _stderrStream.DisposeAsync().ConfigureAwait(false);
            await IgnoreStderrPumpFailureAsync().ConfigureAwait(false);
            await ClientInput.DisposeAsync().ConfigureAwait(false);
            _stderrCancellation.Dispose();
            _processHandle.Dispose();
        }

        if (!exited)
        {
            throw new CodexAppServerProcessException(
                CodexAppServerProcessError.ShutdownFailed,
                "Codex app-server did not stop within the shutdown limit.");
        }
    }

    internal bool IsRunning =>
        !_processHandle.IsClosed
        && NativeMethods.WaitForSingleObject(_processHandle, 0) == NativeMethods.WaitTimeout;

    private async Task DrainStderrAsync()
    {
        char[] buffer = new char[512];
        using var reader = new StreamReader(
            _stderrStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 512,
            leaveOpen: true);

        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(
                        buffer.AsMemory(),
                        _stderrCancellation.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                _diagnostics.Append(buffer.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException) when (_stderrCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
        finally
        {
            _diagnostics.Complete();
        }
    }

    private async Task IgnoreStderrPumpFailureAsync()
    {
        try
        {
            await _stderrPump.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private Task<bool> WaitForExitAsync(TimeSpan timeout) =>
        Task.Run(() =>
        {
            uint milliseconds = checked((uint)Math.Ceiling(timeout.TotalMilliseconds));
            uint result = NativeMethods.WaitForSingleObject(_processHandle, milliseconds);
            return result switch
            {
                NativeMethods.WaitObject0 => true,
                NativeMethods.WaitTimeout => false,
                _ => throw new CodexAppServerProcessException(
                    CodexAppServerProcessError.ShutdownFailed,
                    "Windows could not observe the Codex process state."),
            };
        });

    private static (SafeFileHandle Parent, SafeFileHandle Child) CreatePipe(bool parentReads)
    {
        var attributes = new NativeMethods.SecurityAttributes
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.SecurityAttributes>(),
            InheritHandle = true,
        };

        if (!NativeMethods.CreatePipe(
                out SafeFileHandle readPipe,
                out SafeFileHandle writePipe,
                ref attributes,
                size: 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        SafeFileHandle parent = parentReads ? readPipe : writePipe;
        SafeFileHandle child = parentReads ? writePipe : readPipe;
        try
        {
            if (!NativeMethods.SetHandleInformation(
                    parent,
                    NativeMethods.HandleFlagInherit,
                    flags: 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return (parent, child);
        }
        catch
        {
            readPipe.Dispose();
            writePipe.Dispose();
            throw;
        }
    }

    private static void TryTerminate(
        SafeKernelHandle? processHandle,
        IWindowsProcessJob job)
    {
        try
        {
            job.Terminate();
        }
        catch (Exception exception) when (IsExpectedNativeFailure(exception))
        {
        }

        if (processHandle is not null && !processHandle.IsInvalid && !processHandle.IsClosed)
        {
            NativeMethods.TerminateProcess(processHandle, ForcedExitCode);
            _ = NativeMethods.WaitForSingleObject(processHandle, 2000);
        }
    }

    private static bool IsExpectedNativeFailure(Exception exception) =>
        exception is Win32Exception
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or ObjectDisposedException
            or OverflowException;

    private static int? GetNativeErrorCode(Exception exception) =>
        exception is Win32Exception native ? native.NativeErrorCode : null;
}
