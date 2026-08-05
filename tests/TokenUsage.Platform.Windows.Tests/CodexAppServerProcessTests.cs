using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using WOpenUsage.Platform.Windows.Native;
using WOpenUsage.Platform.Windows.Processes;

namespace WOpenUsage.Platform.Windows.Tests;

public sealed class CodexAppServerProcessTests
{
    private const string ExtraHandleEnvironmentVariable = "WOPENUSAGE_FAKE_EXTRA_HANDLE";
    private static readonly CodexAppServerProcessOptions TestOptions = new(
        gracefulShutdownTimeout: TimeSpan.FromMilliseconds(50),
        forcedShutdownTimeout: TimeSpan.FromSeconds(2),
        maximumDiagnosticCharacters: 1024);

    [Fact]
    public async Task FakeAppServerEchoesUtf8OverExpectedStreamPolarity()
    {
        await using CodexAppServerProcess process = StartFake();
        using var reader = new StreamReader(
            process.ClientInput,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        using var writer = new StreamWriter(
            process.ClientOutput,
            new UTF8Encoding(false),
            leaveOpen: true)
        {
            AutoFlush = true,
        };
        const string payload = "quota-ñ-東京";

        await writer.WriteLineAsync(payload);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string? response = await reader.ReadLineAsync(timeout.Token);

        Assert.Equal(payload, response);
        Assert.True(process.IsRunning);
    }

    [Fact]
    public async Task DisposeKillsChildThatStaysAliveAfterStdinCloses()
    {
        CodexAppServerProcess process = StartFake();
        int processId = process.ProcessId;
        Assert.True(IsProcessRunning(processId));

        await process.DisposeAsync();

        await AssertProcessStoppedAsync(processId);
        Assert.False(process.IsRunning);
        await process.DisposeAsync();
    }

    [Fact]
    public async Task DiagnosticSnapshotIsBoundedAndSanitizedBeforeExposure()
    {
        await using CodexAppServerProcess process = StartFake();

        string diagnostics = await WaitForDiagnosticsAsync(process);

        Assert.True(diagnostics.Length <= TestOptions.MaximumDiagnosticCharacters);
        Assert.Contains("[diagnostic line removed]", diagnostics, StringComparison.Ordinal);
        Assert.Contains("[email]", diagnostics, StringComparison.Ordinal);
        Assert.Contains("[secret]", diagnostics, StringComparison.Ordinal);
        Assert.Contains("[path]", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("private-account@example.invalid", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test1234", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("Users\\private", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer test1234", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChildInheritsOnlyTheThreeExplicitStandardHandles()
    {
        var attributes = new NativeMethods.SecurityAttributes
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.SecurityAttributes>(),
            InheritHandle = true,
        };
        Assert.True(NativeMethods.CreatePipe(
            out Microsoft.Win32.SafeHandles.SafeFileHandle extraRead,
            out Microsoft.Win32.SafeHandles.SafeFileHandle extraWrite,
            ref attributes,
            size: 0));
        using (extraRead)
        using (extraWrite)
        {
            string? previous = Environment.GetEnvironmentVariable(ExtraHandleEnvironmentVariable);
            Environment.SetEnvironmentVariable(
                ExtraHandleEnvironmentVariable,
                extraRead.DangerousGetHandle().ToInt64().ToString(CultureInfo.InvariantCulture));
            try
            {
                await using CodexAppServerProcess process = StartFake();
                string diagnostics = await WaitForDiagnosticTextAsync(
                    process,
                    "extra-handle-inherited=false");

                Assert.Contains(
                    "extra-handle-inherited=false",
                    diagnostics,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "extra-handle-inherited=true",
                    diagnostics,
                    StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable(ExtraHandleEnvironmentVariable, previous);
            }
        }
    }

    [Fact]
    public async Task JobAssignmentFailureStopsSuspendedChildAndReturnsFixedError()
    {
        int processId = 0;
        CodexAppServerProcessException error = Assert.Throws<CodexAppServerProcessException>(() =>
            CodexAppServerProcess.StartCore(
                FakeResolution(),
                TestOptions,
                new ThrowingJobFactory(),
                createdProcessId => processId = createdProcessId));

        Assert.Equal(CodexAppServerProcessError.JobSetupFailed, error.Error);
        Assert.Equal("Windows could not assign Codex to its safe process job.", error.Message);
        Assert.True(processId > 0);
        await AssertProcessStoppedAsync(processId);
    }

    [Fact]
    public void MissingExecutableReturnsFixedErrorWithoutPathDisclosure()
    {
        string privatePath = Path.Combine(
            Path.GetTempPath(),
            "private-user-sentinel",
            "codex.exe");
        var resolution = new CodexExecutableResolution.Resolved(privatePath);

        CodexAppServerProcessException error = Assert.Throws<CodexAppServerProcessException>(() =>
            CodexAppServerProcess.Start(resolution, TestOptions));

        Assert.Equal(CodexAppServerProcessError.InvalidExecutable, error.Error);
        Assert.Equal("The resolved Codex executable is unavailable or unsafe.", error.Message);
        Assert.DoesNotContain("private-user-sentinel", error.ToString(), StringComparison.Ordinal);
    }

    private static CodexAppServerProcess StartFake()
    {
        try
        {
            return CodexAppServerProcess.Start(FakeResolution(), TestOptions);
        }
        catch (CodexAppServerProcessException error)
        {
            string nativeCode = error.NativeErrorCode is int code
                ? code.ToString(CultureInfo.InvariantCulture)
                : "none";
            Assert.Fail($"Fake Codex start failed: {error.Error}, native={nativeCode}.");
            throw;
        }
    }

    private static CodexExecutableResolution.Resolved FakeResolution()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "FakeCodex", "codex.exe");
        Assert.True(File.Exists(path), $"Fake Codex executable is missing: {path}");
        return new CodexExecutableResolution.Resolved(path);
    }

    private static async Task<string> WaitForDiagnosticsAsync(CodexAppServerProcess process)
        => await WaitForDiagnosticTextAsync(process, "[path]");

    private static async Task<string> WaitForDiagnosticTextAsync(
        CodexAppServerProcess process,
        string expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            string snapshot = process.SanitizedDiagnosticSnapshot;
            if (snapshot.Contains(expected, StringComparison.Ordinal))
            {
                return snapshot;
            }

            await Task.Delay(20, timeout.Token);
        }

        throw new TimeoutException("Fake Codex diagnostics did not arrive.");
    }

    private static async Task AssertProcessStoppedAsync(int processId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (IsProcessRunning(processId) && !timeout.IsCancellationRequested)
        {
            await Task.Delay(20, timeout.Token);
        }

        Assert.False(IsProcessRunning(processId));
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class ThrowingJobFactory : IWindowsProcessJobFactory
    {
        public IWindowsProcessJob Create() => new ThrowingJob();
    }

    private sealed class ThrowingJob : IWindowsProcessJob
    {
        public void Assign(SafeKernelHandle process) =>
            throw new InvalidOperationException("Synthetic assignment failure.");

        public void Terminate()
        {
        }

        public void Dispose()
        {
        }
    }
}
