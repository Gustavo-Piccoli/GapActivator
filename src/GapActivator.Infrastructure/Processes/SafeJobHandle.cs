using Microsoft.Win32.SafeHandles;

namespace GapActivator.Infrastructure.Processes;

/// <summary>An owned handle to a Windows job object.</summary>
internal sealed class SafeJobHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
{
    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
