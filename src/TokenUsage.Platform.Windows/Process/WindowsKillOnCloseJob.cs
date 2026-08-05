using System.ComponentModel;
using System.Runtime.InteropServices;
using TokenUsage.Platform.Windows.Native;

namespace TokenUsage.Platform.Windows.Processes;

internal interface IWindowsProcessJob : IDisposable
{
    void Assign(SafeKernelHandle process);

    void Terminate();
}

internal interface IWindowsProcessJobFactory
{
    IWindowsProcessJob Create();
}

internal sealed class WindowsProcessJobFactory : IWindowsProcessJobFactory
{
    internal static WindowsProcessJobFactory Instance { get; } = new();

    private WindowsProcessJobFactory()
    {
    }

    public IWindowsProcessJob Create() => WindowsKillOnCloseJob.Create();
}

internal sealed class WindowsKillOnCloseJob : IWindowsProcessJob
{
    private readonly SafeKernelHandle _handle;

    private WindowsKillOnCloseJob(SafeKernelHandle handle)
    {
        _handle = handle;
    }

    internal static WindowsKillOnCloseJob Create()
    {
        nint rawHandle = NativeMethods.CreateJobObject(0, name: null);
        if (rawHandle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var handle = new SafeKernelHandle(rawHandle, ownsHandle: true);
        try
        {
            var information = new NativeMethods.JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new NativeMethods.JobObjectBasicLimitInformation
                {
                    LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose,
                },
            };

            if (!NativeMethods.SetInformationJobObject(
                    handle,
                    NativeMethods.JobObjectExtendedLimitInformationClass,
                    ref information,
                    (uint)Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>()))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new WindowsKillOnCloseJob(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Assign(SafeKernelHandle process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!NativeMethods.AssignProcessToJobObject(_handle, process))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Terminate()
    {
        if (!_handle.IsClosed && !_handle.IsInvalid)
        {
            NativeMethods.TerminateJobObject(_handle, exitCode: 1);
        }
    }

    public void Dispose() => _handle.Dispose();
}
