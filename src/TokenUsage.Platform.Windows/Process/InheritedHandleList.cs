using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TokenUsage.Platform.Windows.Native;

namespace TokenUsage.Platform.Windows.Processes;

internal sealed class InheritedHandleList : IDisposable
{
    private nint _attributeList;
    private nint _handleValues;

    private InheritedHandleList(nint attributeList, nint handleValues)
    {
        _attributeList = attributeList;
        _handleValues = handleValues;
    }

    internal nint Pointer => _attributeList;

    internal static InheritedHandleList Create(params SafeFileHandle[] handles)
    {
        ArgumentNullException.ThrowIfNull(handles);
        if (handles.Length == 0 || handles.Any(handle => handle is null || handle.IsInvalid))
        {
            throw new ArgumentException("Inherited handles must contain valid handles.", nameof(handles));
        }

        nuint attributeListSize = 0;
        _ = NativeMethods.InitializeProcThreadAttributeList(
            0,
            attributeCount: 1,
            flags: 0,
            ref attributeListSize);
        if (attributeListSize == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        nint attributeList = Marshal.AllocHGlobal(checked((nint)attributeListSize));
        nint handleValues = 0;
        bool initialized = false;
        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(
                    attributeList,
                    attributeCount: 1,
                    flags: 0,
                    ref attributeListSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            initialized = true;

            int handleBytes = checked(handles.Length * nint.Size);
            handleValues = Marshal.AllocHGlobal(handleBytes);
            for (int index = 0; index < handles.Length; index++)
            {
                Marshal.WriteIntPtr(
                    handleValues,
                    checked(index * nint.Size),
                    handles[index].DangerousGetHandle());
            }

            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    flags: 0,
                    NativeMethods.ProcThreadAttributeHandleList,
                    handleValues,
                    checked((nuint)handleBytes),
                    previousValue: 0,
                    returnSize: 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new InheritedHandleList(attributeList, handleValues);
        }
        catch
        {
            if (initialized)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
            }

            if (handleValues != 0)
            {
                Marshal.FreeHGlobal(handleValues);
            }

            Marshal.FreeHGlobal(attributeList);
            throw;
        }
    }

    public void Dispose()
    {
        nint attributeList = Interlocked.Exchange(ref _attributeList, 0);
        if (attributeList != 0)
        {
            NativeMethods.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
        }

        nint handleValues = Interlocked.Exchange(ref _handleValues, 0);
        if (handleValues != 0)
        {
            Marshal.FreeHGlobal(handleValues);
        }
    }
}
