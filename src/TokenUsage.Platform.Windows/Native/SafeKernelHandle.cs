using Microsoft.Win32.SafeHandles;

namespace WOpenUsage.Platform.Windows.Native;

internal sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeKernelHandle()
        : base(ownsHandle: true)
    {
    }

    internal SafeKernelHandle(nint handle, bool ownsHandle)
        : base(ownsHandle)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
